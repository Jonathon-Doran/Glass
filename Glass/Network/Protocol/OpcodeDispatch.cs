using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol.Fields;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeDispatch
//
// Singleton that dispatches application-level packets to registered handlers
// by opcode.  At construction, scans the executing assembly for all classes
// implementing IHandleOpcodes, instantiates each one, and registers it.
//
// Exposes HandlePacket matching the AppPacketHandler delegate so it can be
// wired directly to SoeStream.OnAppPacket.
///////////////////////////////////////////////////////////////////////////////////////////////
public class OpcodeDispatch : IDisposable
{
    private static OpcodeDispatch? _instance = null;
    private readonly Dictionary<ushort, IHandleOpcodes> _handlers;
    private readonly Dictionary<ushort, string> _names;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Returns the singleton instance, creating it on first access.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static OpcodeDispatch Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new OpcodeDispatch();
            }

            return _instance;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeDispatch (constructor)
    //
    // Private.  Scans the executing assembly for all non-abstract classes
    // implementing IHandleOpcodes, instantiates each one via its default
    // constructor, and registers it by opcode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private OpcodeDispatch()
    {
        _handlers = new Dictionary<ushort, IHandleOpcodes>();
        _names = new Dictionary<ushort, string>();

        DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch: scanning assembly for IHandleOpcodes implementations");

        Assembly assembly = Assembly.GetExecutingAssembly();
        Type interfaceType = typeof(IHandleOpcodes);

        foreach (Type type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface)
            {
                continue;
            }

            if (!interfaceType.IsAssignableFrom(type))
            {
                continue;
            }

            ConstructorInfo? constructor = type.GetConstructor(Type.EmptyTypes);

            if (constructor == null)
            {
                DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch: skipping " + type.Name
                    + " — no default constructor");
                continue;
            }

            IHandleOpcodes handler = (IHandleOpcodes)constructor.Invoke(null);
            ushort opcode = handler.Opcode;

            if (_handlers.ContainsKey(opcode))
            {
                DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch: WARNING — duplicate handler for opcode 0x"
                    + opcode.ToString("x4") + ": " + type.Name
                    + " conflicts with " + _handlers[opcode].GetType().Name
                    + ", keeping first");
                continue;
            }

            _handlers[opcode] = handler;
            _names[opcode] = handler.OpcodeName;

            DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch: registered " + type.Name
                + " for opcode 0x" + opcode.ToString("x4"));
        }

        DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch: scan complete, "
            + _handlers.Count + " handlers registered");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    // Disposes every registered handler and tears down the singleton.  Called from the
    // session-disconnected path, after all sessions are confirmed disconnected and no
    // more packets will arrive.  Cold-path; handlers may log freely from their own
    // Dispose methods.
    //
    // Clears _instance so that any subsequent access to OpcodeDispatch builds a fresh
    // singleton with a fresh assembly scan.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Dispose()
    {
        foreach (IHandleOpcodes handler in _handlers.Values)
        {
            try
            {
                handler.Dispose();
            }
            catch (Exception ex)
            {
                DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch.Dispose: handler "
                    + handler.GetType().Name + " threw during Dispose: " + ex.Message);
            }
        }

        _handlers.Clear();
        _names.Clear();

        _instance = null;
        GC.SuppressFinalize(this);
    }

    // =============================================================================
    // IsOpcodeHandled
    //
    // Returns true if a handler is registered for the given opcode.
    //
    // Parameters:
    //   opcode - the application-level opcode to check
    // =============================================================================
    public bool IsOpcodeHandled(ushort opcode)
    {
        return _handlers.ContainsKey(opcode);
    }

    public string? GetOpcodeName(ushort opcode)
    {
        if (_names.ContainsKey(opcode))
        {
            return _names[opcode];
        }

        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Matches the AppPacketHandler delegate signature.  Looks up the opcode
    // in the handler dictionary and calls the handler if found.
    //
    // data:       The application payload
    // length:     Length of the application payload
    // direction:  Direction byte (DirectionClient or DirectionServer)
    // opcode:     The application-level opcode
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandlePacket(ReadOnlySpan<byte> data, int length,
                              byte direction, ushort opcode, PacketMetadata metadata)
    {
        DebugLog.Write(LogChannel.Opcodes, $"[SEARCH] dir=0x{direction:X2} opCode=0x{opcode:X4} len={length} hex={BitConverter.ToString(data.Slice(0, length).ToArray()).Replace("-", " ").ToLowerInvariant()}");


        if (_handlers.TryGetValue(opcode, out IHandleOpcodes? handler))
        {
            handler.HandlePacket(data, metadata);
        }
    }
}
