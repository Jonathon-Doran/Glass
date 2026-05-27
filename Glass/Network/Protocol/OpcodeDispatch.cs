using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol.Fields;
using System;
using System.Collections.Frozen;
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
    private readonly FrozenDictionary<ushort, IHandleOpcodes> _handlers;
    private static readonly object _instanceLock = new object();

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
                lock (_instanceLock)
                {
                    _instance = new OpcodeDispatch();
                }
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
        int opcodeCount = GlassContext.PatchRegistry.GetOpcodeCount(GlassContext.CurrentPatchLevel);
        Dictionary<ushort, IHandleOpcodes> builder = new Dictionary<ushort, IHandleOpcodes>();

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
            ushort opcodeValue = GlassContext.PatchRegistry.GetOpcodeValue(_patchLevel, opcode);

            builder[opcodeValue] = handler;

            DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch: registered " + type.Name
                + " for opcode 0x" + opcodeValue.ToString("x4"));
        }

        _handlers = builder.ToFrozenDictionary();

        GlassContext.PacketBus.Subscribe(HandlePacket);
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
        lock (_instanceLock)
        {
            GlassContext.PacketBus.Unsubscribe(HandlePacket);
            _instance = null;
            GC.SuppressFinalize(this);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // RebuildForCurrentPatchLevel
    //
    // Disposes any existing OpcodeDispatch instance and forces construction
    // of a fresh one against GlassContext.CurrentPatchLevel.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private static readonly object _rebuildLock = new object();

    public static void RebuildForCurrentPatchLevel()
    {
        lock (_rebuildLock)
        {
            if (_instance != null)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "OpcodeDispatch.RebuildForCurrentPatchLevel: disposing prior instance");
                _instance.Dispose();
            }

            OpcodeDispatch fresh = Instance;
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeDispatch.RebuildForCurrentPatchLevel: fresh instance constructed");
        }
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
        if (Volatile.Read(ref _instance) == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "packet arrived at handler during shutdown");
            return;
        }

        if (_handlers.TryGetValue(opcodeValue, out IHandleOpcodes? handler) == true)
        {
            handler.HandlePacket(data, metadata);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ResolveOpcodeHandle
    //
    // Resolves an OpcodeHandle for the application packet described by opcodeValue, data,
    // and metadata.  Looks up the handler for the wire opcode, asks the handler for its
    // version, and asks PatchRegistry for the handle that matches the resulting PatchOpcode.
    //
    // Returns (OpcodeHandle)(-1) when no handler is registered for the wire opcode or when
    // the registry has no entry matching the PatchOpcode.
    //
    // opcodeValue:  Wire opcode value from the application packet header.
    // data:         Application payload (opcode bytes already stripped).
    // metadata:     Packet metadata for the packet being resolved.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeHandle ResolveOpcodeHandle(ushort opcodeValue, ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        if (_handlers.TryGetValue(opcodeValue, out IHandleOpcodes? handler) == false)
        {
            return (OpcodeHandle)(-1);
        }

        uint version = handler.ResolveVersion(data, metadata);

        PatchOpcode patchOpcode = new PatchOpcode(_patchLevel, opcodeValue, (int)version);
        OpcodeHandle handle = GlassContext.PatchRegistry.GetOpcodeHandle(patchOpcode);

        if ((int)handle < 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeDispatch.ResolveOpcodeHandle: registry has no entry for wire opcode 0x"
                + opcodeValue.ToString("x4") + " version " + version);
            return (OpcodeHandle)(-1);
        }

        return handle;
    }
}
