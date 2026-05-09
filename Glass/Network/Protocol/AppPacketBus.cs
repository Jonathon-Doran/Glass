using System;
using System.Collections.Generic;
using Glass.Core.Logging;

namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// AppPacketBus
//
// Delivers decoded application-level packets from the SOE protocol stack to any
// number of subscribers.  The protocol stack publishes; subscribers register
// independently and have no knowledge of each other or of the publisher.
//
// Delivery is synchronous on the calling thread, in subscription order.  A
// subscriber that throws does not prevent later subscribers from running.
//
// The data span is valid only for the duration of the Publish call.  Subscribers
// that need to retain the bytes must copy them.
///////////////////////////////////////////////////////////////////////////////////////////////
public class AppPacketBus
{
    ///////////////////////////////////////////////////////////////////////////////////////////
    // Handler
    //
    // Delegate signature for AppPacketBus subscribers.
    //
    // data:      The application payload, opcode bytes already stripped.  Valid
    //            only for the duration of the call.
    // opcode:    The wire opcode value.  Subscribers that care about opcode
    //            identity (name) resolve it via PatchRegistry.
    // metadata:  Source/dest IP and port, timestamp, and frame number from the
    //            underlying UDP packet that the message arrived on.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public delegate void Handler(ReadOnlySpan<byte> data,
                                  ushort opcode,
                                  PacketMetadata metadata);

    private readonly List<Handler> _subscribers;
    private readonly object _lock;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AppPacketBus (constructor)
    ///////////////////////////////////////////////////////////////////////////////////////////
    public AppPacketBus()
    {
        _subscribers = new List<Handler>();
        _lock = new object();

        DebugLog.Write(LogChannel.LowNetwork, "AppPacketBus.ctor: created");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Subscribe
    //
    // Adds a handler to the subscriber list.  Idempotent — subscribing the same
    // handler twice does not deliver twice.
    //
    // handler:  The delegate to invoke for each published packet.  Must not be null.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Subscribe(Handler handler)
    {
        if (handler == null)
        {
            DebugLog.Write(LogChannel.LowNetwork, "AppPacketBus.Subscribe: null handler, ignoring");
            return;
        }

        lock (_lock)
        {
            if (_subscribers.Contains(handler))
            {
                DebugLog.Write(LogChannel.LowNetwork,
                    "AppPacketBus.Subscribe: handler already subscribed, ignoring");
                return;
            }

            _subscribers.Add(handler);

            DebugLog.Write(LogChannel.LowNetwork,
                "AppPacketBus.Subscribe: added handler, subscriber count is now "
                + _subscribers.Count);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Unsubscribe
    //
    // Removes a handler from the subscriber list.  No-op if the handler was
    // not subscribed.
    //
    // handler:  The delegate to remove.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Unsubscribe(Handler handler)
    {
        if (handler == null)
        {
            DebugLog.Write(LogChannel.LowNetwork, "AppPacketBus.Unsubscribe: null handler, ignoring");
            return;
        }

        lock (_lock)
        {
            bool removed = _subscribers.Remove(handler);

            if (removed)
            {
                DebugLog.Write(LogChannel.LowNetwork,
                    "AppPacketBus.Unsubscribe: removed handler, subscriber count is now "
                    + _subscribers.Count);
            }
            else
            {
                DebugLog.Write(LogChannel.LowNetwork,
                    "AppPacketBus.Unsubscribe: handler was not subscribed, ignoring");
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Publish
    //
    // Delivers a decoded application-level packet to every current subscriber,
    // synchronously, in subscription order.  A subscriber that throws is logged
    // and the next subscriber still runs.
    //
    // data:      The application payload, opcode bytes already stripped.
    // opcode:    The wire opcode value.
    // metadata:  Source/dest IP and port, timestamp, and frame number.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Publish(ReadOnlySpan<byte> data, ushort opcode, PacketMetadata metadata)
    {
        // Snapshot the subscriber list under the lock so a subscriber that
        // unsubscribes during delivery does not mutate the list we are walking.
        Handler[] snapshot;

        lock (_lock)
        {
            if (_subscribers.Count == 0)
            {
                return;
            }

            snapshot = _subscribers.ToArray();
        }

        for (int i = 0; i < snapshot.Length; i++)
        {
            Handler handler = snapshot[i];

            try
            {
                handler(data, opcode, metadata);
            }
            catch (Exception ex)
            {
                DebugLog.Write(LogChannel.LowNetwork,
                    "AppPacketBus.Publish: subscriber " + i
                    + " threw " + ex.GetType().Name
                    + ": " + ex.Message);
                DebugLog.Write(LogChannel.LowNetwork,
                    "AppPacketBus.Publish: stack: " + ex.StackTrace);
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SubscriberCount
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int SubscriberCount
    {
        get
        {
            lock (_lock)
            {
                return _subscribers.Count;
            }
        }
    }
}
