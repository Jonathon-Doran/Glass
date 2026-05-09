using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol.Fields;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Metadata;

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
    private readonly PatchLevel _patchLevel;
    private readonly IHandleOpcodes?[] _handlers;
    private readonly string?[] _names;

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
        GlassContext.AppPacketBus.Subscribe(HandlePacket);

        int opcodeCount = GlassContext.PatchRegistry.GetOpcodeCount(GlassContext.CurrentPatchLevel);
        _handlers = new IHandleOpcodes?[opcodeCount];
        _names = new string?[opcodeCount];

        DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch: scanning assembly for IHandleOpcodes implementations");

        Assembly assembly = Assembly.GetExecutingAssembly();
        Type interfaceType = typeof(IHandleOpcodes);

        _patchLevel = GlassContext.CurrentPatchLevel;

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
            string opcodeName = handler.OpcodeName;

            OpcodeHandle opcode = GlassContext.PatchRegistry.GetOpcodeHandle(_patchLevel, opcodeName);

            if ((int)opcode < 0 || (int)opcode >= _handlers.Length)
            {
                continue;
            }

            ushort opcodeValue = GlassContext.PatchRegistry.GetOpcodeValue(_patchLevel, opcode);

            _handlers[opcode] = handler;
            _names[opcode] = handler.OpcodeName;

            DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch: registered " + type.Name
                + " for opcode 0x" + opcodeValue.ToString("x4"));
        }

        DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch: scan complete, "
            + _handlers.Length + " handlers registered");
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
        foreach (IHandleOpcodes handler in _handlers!)
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

        Clear();
        GlassContext.AppPacketBus.Unsubscribe(HandlePacket);
        _instance = null;
        GC.SuppressFinalize(this);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Clear
    //
    // Wipes the handler and name arrays, leaving every slot null.  Called by the
    // dispatcher constructor before populating the arrays from the reflection scan.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Clear()
    {
        for (int handleIndex = 0; handleIndex < _handlers.Length; handleIndex++)
        {
            _handlers[handleIndex] = null;
            _names[handleIndex] = null;
        }
    }

    // =============================================================================
    // IsOpcodeHandled
    //
    // Returns true if a handler is registered for the given opcode.
    //
    // Parameters:
    //   opcodeValue - the application-level opcode to check
    // =============================================================================
    public bool IsOpcodeHandled(ushort opcodeValue)
    {
        OpcodeHandle opcode = GlassContext.PatchRegistry.GetOpcodeHandle(_patchLevel, opcodeValue);
        if (opcode == -1)
        {
            return false;
        }

        return _handlers[opcode] != null;
    }

    public string? GetOpcodeName(ushort opcodeValue)
    {
        OpcodeHandle opcode = GlassContext.PatchRegistry.GetOpcodeHandle(_patchLevel, opcodeValue);
        if (opcode == -1)
        {
            return null;
        }

        return _names[opcode] ?? string.Empty;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Matches the AppPacketHandler delegate signature.  Looks up the opcode
    // in the handler dictionary and calls the handler if found.
    //
    // data:       The application payload
    // opcode:     The application-level opcode
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandlePacket(ReadOnlySpan<byte> data, ushort opcodeValue, PacketMetadata metadata)
    {
        int length = data.Length;

        DebugLog.Write(LogChannel.Opcodes, $"[SEARCH] opCode=0x{opcodeValue:X4} len={length} hex={BitConverter.ToString(data.Slice(0, length).ToArray()).Replace("-", " ").ToLowerInvariant()}");

        OpcodeHandle handle = GlassContext.PatchRegistry.GetOpcodeHandle(_patchLevel, opcodeValue);
        if ((handle == -1) || (_handlers[handle] == null))
        {
            return;
        }

        _handlers[handle]!.HandlePacket(data, metadata);
    }
}
