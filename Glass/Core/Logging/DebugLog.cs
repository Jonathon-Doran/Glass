using System;
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Glass.Core.Logging;

///////////////////////////////////////////////////////////////////////////////////////////////
// LogChannel
//
// Identifies the logical source of a log message.  Each channel can be
// independently routed to any combination of sinks.  Channels can be
// enabled or disabled without destroying their routing configuration.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum LogChannel
{
    General,
    ISXGlass,
    Pipes,
    Video,
    Sessions,
    Profiles,
    Input,
    Database,
    LowNetwork,
    Network,
    Opcodes,
    Inference,
    InferenceDebug,
    Fields,
    Count
}

///////////////////////////////////////////////////////////////////////////////////////////////
// LogSink
//
// Identifies a logical output destination.  Multiple IHandleLogMessages
// implementations can be registered under the same sink ID.  When a
// message is routed to a sink, all handlers registered under that sink
// are invoked.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum LogSink
{
    GlassConsole,
    InferenceDebugTab,
    InferenceTab,
    GlassDebugLogfile,
    InferenceLogfile,
    Aux1LogFile,                // spare log file
    Aux2LogFile,                // spare log file
    InferenceDebugLogfile,
    Count
}

///////////////////////////////////////////////////////////////////////////////////////////////
// DebugLog
//
// Unified logging system for Glass and Inference.  Messages are written to
// a named channel.  Each channel has a bitmask of sinks it is routed to
// and an independent enabled/disabled flag.  Each sink has zero or more
// handlers that receive the message text.
//
// Routing table:     _routing[channel] = bitmask of sinks
// Enabled table:     _enabled = bitmask of channels that are active
// Handler table:     _handlers[sink] = snapshot array of IHandleLogMessages
//
// The Write hot path performs no locking.  Handler lists are replaced
// atomically (copy-on-write) so that Write can iterate a stable snapshot
// while AddHandler/RemoveHandler modify the list safely.
//
// Each handler is responsible for its own thread safety (file locks,
// Dispatcher.Invoke, etc.).  DebugLog does not lock around dispatch.
//
// DebugLog prepends a timestamp to every message before dispatching.
///////////////////////////////////////////////////////////////////////////////////////////////
public static class DebugLog
{
    private static readonly int ChannelCount = (int)LogChannel.Count;
    private static readonly int SinkCount = (int)LogSink.Count;

    private static readonly ulong[] _routing = new ulong[ChannelCount];
    private static ulong _enabled = 0;
    private static volatile bool _shutdown = false;

    private static readonly List<IHandleLogMessages>[] _handlers = new List<IHandleLogMessages>[SinkCount];


    // Lock for handler registration and removal only.  Never held during dispatch.
    private static readonly object _registrationLock = new object();

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Static constructor
    //
    // Initializes the handler snapshot array with empty arrays for each sink.
    // Enables all channels by default.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    static DebugLog()
    {
        for (int i = 0; i < SinkCount; i++)
        {
            _handlers[i] = new List<IHandleLogMessages>();
        }

        for (int i = 0; i < ChannelCount; i++)
        {
            _enabled |= (1UL << i);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AddHandler
    //
    // Registers a handler under the specified sink.  Multiple handlers can be
    // registered under the same sink.  The handler list is replaced atomically
    // so that in-flight Write calls are not affected.
    //
    // sink:     The sink to register under
    // handler:  The handler to add
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static void AddHandler(LogSink sink, IHandleLogMessages handler)
    {
        lock (_registrationLock)
        {
            _handlers[(int)sink].Add(handler);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // RemoveHandler
    //
    // Removes a handler from the specified sink by reference equality.
    // If the handler is not found, no action is taken.
    //
    // sink:     The sink to remove from
    // handler:  The handler to remove
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static void RemoveHandler(LogSink sink, IHandleLogMessages handler)
    {
        lock (_registrationLock)
        {
            _handlers[(int)sink].Remove(handler);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Route
    //
    // Enables routing from a channel to a sink.  Messages written to the
    // channel will be dispatched to all handlers registered under the sink.
    //
    // channel:  The source channel
    // sink:     The destination sink to enable
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static void Route(LogChannel channel, LogSink sink)
    {
        _routing[(int)channel] |= (1UL << (int)sink);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Unroute
    //
    // Disables routing from a channel to a sink.
    //
    // channel:  The source channel
    // sink:     The destination sink to disable
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static void Unroute(LogChannel channel, LogSink sink)
    {
        _routing[(int)channel] &= ~(1UL << (int)sink);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Enable
    //
    // Enables a channel.  Messages written to this channel will be dispatched
    // according to its routing configuration.  Channels are enabled by default.
    //
    // channel:  The channel to enable
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static void Enable(LogChannel channel)
    {
        _enabled |= (1UL << (int)channel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Disable
    //
    // Disables a channel without destroying its routing configuration.
    // Messages written to this channel will be silently dropped until
    // the channel is re-enabled.
    //
    // channel:  The channel to disable
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static void Disable(LogChannel channel)
    {
        _enabled &= ~(1UL << (int)channel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Write
    //
    // Writes a message to the specified channel.  A timestamp is prepended.
    // The message is dispatched to all handlers on all sinks that the channel
    // is routed to, provided the channel is enabled.
    //
    // No locking is performed during dispatch.  Each handler is responsible
    // for its own thread safety.
    //
    // channel:  The channel to write to
    // message:  The message text
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static void Write(LogChannel channel, string message)
    {
        if (_shutdown)
        {
            System.Diagnostics.Debug.WriteLine("Write aborted during shutdown");
            return;
        }

        if ((_enabled & (1UL << (int)channel)) == 0)
        {
            System.Diagnostics.Debug.WriteLine("Write aborted due to channel not enabled");
            return;
        }

        ulong mask = _routing[(int)channel];

        if (mask == 0)
        {
            return;
        }

        string timestamped = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message;

        List<IHandleLogMessages>[] snapshot = _handlers;

        for (int i = 0; i < SinkCount; i++)
        {
            if ((mask & (1UL << i)) != 0)
            {
                List<IHandleLogMessages> sinkHandlers = snapshot[i];

                for (int j = 0; j < sinkHandlers.Count; j++)
                {
                    sinkHandlers[j].Write(timestamped);
                }
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // WriteMultiline
    //
    // Writes a multiline message to the specified channel.  The first line
    // receives the timestamp.  Continuation lines are padded with spaces to
    // align with the message text after the timestamp bracket.
    //
    // channel:  The channel to write to
    // message:  The message text, which may contain embedded newlines
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static void WriteMultiline(LogChannel channel, string message)
    {
        if (_shutdown)
        {
            return;
        }

        if ((_enabled & (1UL << (int)channel)) == 0)
        {
            return;
        }

        ulong mask = _routing[(int)channel];

        if (mask == 0)
        {
            return;
        }

        string timestamp = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] ";
        string padding = new string(' ', timestamp.Length);
        string[] lines = message.Split('\n');

        StringBuilder formatted = new StringBuilder();
        formatted.Append(timestamp);
        formatted.Append(lines[0].TrimEnd('\r'));

        for (int i = 1; i < lines.Length; i++)
        {
            formatted.Append(Environment.NewLine);
            formatted.Append(padding);
            formatted.Append(lines[i].TrimEnd('\r'));
        }

        string result = formatted.ToString();

        List<IHandleLogMessages>[] snapshot = _handlers;

        for (int i = 0; i < SinkCount; i++)
        {
            if ((mask & (1UL << i)) != 0)
            {
                List<IHandleLogMessages> sinkHandlers = snapshot[i];

                for (int j = 0; j < sinkHandlers.Count; j++)
                {
                    sinkHandlers[j].Write(result);
                }
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Write
    //
    // Writes a message to the General channel.  Convenience overload for
    // messages that do not belong to a specific category.
    //
    // message:  The message text
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static void Write(string message)
    {
        Write(LogChannel.General, message);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Shutdown
    //
    // Shuts down the logging system.  Sets the shutdown flag to prevent
    // further writes, then calls Shutdown() on every registered handler
    // across all sinks.  Clears all handler lists and routing.
    //
    // Safe to call multiple times.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static void Shutdown()
    {
        if (_shutdown)
        {
            return;
        }

        _shutdown = true;

        for (int i = 0; i < ChannelCount; i++)
        {
            _routing[i] = 0;
        }

        for (int i = 0; i < SinkCount; i++)
        {
            List<IHandleLogMessages> sinkHandlers = _handlers[i];

            for (int j = 0; j < sinkHandlers.Count; j++)
            {
                sinkHandlers[j].Shutdown();
            }

            lock (_registrationLock)
            {
                _handlers[i].Clear();
            }
        }
    }
}