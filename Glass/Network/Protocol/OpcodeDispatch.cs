using System;
using System.Collections.Generic;
using System.Reflection;
using Glass.Core;

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
public class OpcodeDispatch
{
    private static OpcodeDispatch? _instance = null;
    private readonly Dictionary<ushort, IHandleOpcodes> _handlers;

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

        DebugLog.Write("OpcodeDispatch: scanning assembly for IHandleOpcodes implementations");

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
                DebugLog.Write("OpcodeDispatch: skipping " + type.Name
                    + " — no default constructor");
                continue;
            }

            IHandleOpcodes handler = (IHandleOpcodes)constructor.Invoke(null);
            ushort opcode = handler.Opcode;

            if (_handlers.ContainsKey(opcode))
            {
                DebugLog.Write("OpcodeDispatch: WARNING — duplicate handler for opcode 0x"
                    + opcode.ToString("x4") + ": " + type.Name
                    + " conflicts with " + _handlers[opcode].GetType().Name
                    + ", keeping first");
                continue;
            }

            _handlers[opcode] = handler;

            DebugLog.Write("OpcodeDispatch: registered " + type.Name
                + " for opcode 0x" + opcode.ToString("x4"));
        }

        DebugLog.Write("OpcodeDispatch: scan complete, "
            + _handlers.Count + " handlers registered");
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
        if (_handlers.TryGetValue(opcode, out IHandleOpcodes? handler))
        {
            handler.HandlePacket(data, length, direction, opcode, metadata);
        }
    }
}
