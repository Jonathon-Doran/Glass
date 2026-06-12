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
public class OpcodeDispatch
{
    private static OpcodeDispatch? _instance = null;
    private readonly PatchLevel _patchLevel;
    private readonly FrozenDictionary<PatchOpcode, IHandleOpcodes> _handlers;
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
        Dictionary<PatchOpcode, IHandleOpcodes> builder = new Dictionary<PatchOpcode, IHandleOpcodes>();

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
            PatchOpcode patchOpcode = handler.OpcodeHandled;

            if (patchOpcode.Exists == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch: skipping " + type.Name
                    + " — handler reports no opcode for patch level " + _patchLevel);
                continue;
            }

            builder[patchOpcode] = handler;

            DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch: registered " + type.Name
                + " for opcode " + patchOpcode );
        }

        _handlers = builder.ToFrozenDictionary();

        GlassContext.PacketBus.Subscribe(HandlePacket);
        DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch: scan complete, "
            + _handlers.Count + " handlers registered");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // DisposeInstance
    //
    // Shuts down the singleton if one exists, without constructing one: unsubscribes its
    // packet handler from the bus and clears _instance.  Safe to call when no instance
    // exists and safe to call repeatedly.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static void DisposeInstance()
    {
        lock (_instanceLock)
        {
            if (_instance == null)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "OpcodeDispatch.DisposeInstance: no instance, nothing to dispose");
                return;
            }

            GlassContext.PacketBus.Unsubscribe(_instance.HandlePacket);
            GC.SuppressFinalize(_instance);
            _instance = null;

            DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch.DisposeInstance: disposed");
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
                OpcodeDispatch.DisposeInstance();
            }

            OpcodeDispatch fresh = Instance;
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeDispatch.RebuildForCurrentPatchLevel: fresh instance constructed");
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Matches the AppPacketHandler delegate signature.  Resolves the wire opcode to its
    // version-correct PatchOpcode and calls the registered handler if one exists.
    //
    // data:        The application payload
    // metadata:    Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        if (Volatile.Read(ref _instance) == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "packet arrived at handler during shutdown");
            return;
        }

        // V0 opcodes are synthetic and not handled.  Silently discard.
        if (metadata.Opcode.Version == 0)
        {
            return;
        }

        if (_handlers.TryGetValue(metadata.Opcode, out IHandleOpcodes? handler) == true)
        {
            handler.HandlePacket(data, metadata);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ResolvePatchOpcode
    //
    // Resolves a wire opcode value to its versioned PatchOpcode for this patch level.  Finds the
    // version-1 handler for the wire value, asks it for the version this packet decodes to, and
    // returns the PatchOpcode carrying that version.  Returns a synthetic version-0 PatchOpcode
    // carrying the wire value when no handler is registered, so every observed wire value has a
    // populated identity that cold-path consumers can key and name uniformly.
    //
    // opcodeValue: The wire opcode value from the application packet header
    // data:        The application payload
    // metadata:    Packet metadata for the packet being resolved
    //
    // Returns the versioned PatchOpcode, or a synthetic version-0 PatchOpcode when the wire value
    // has no handler.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode ResolvePatchOpcode(OpcodeValue opcodeValue, ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        PatchOpcode baseOpcode = new PatchOpcode(_patchLevel, opcodeValue, 1);

        if (_handlers.TryGetValue(baseOpcode, out IHandleOpcodes? versionResolver) == false)
        {
            return new PatchOpcode(_patchLevel, opcodeValue, 0);
        }

        uint version = versionResolver.ResolveVersion(data, metadata);
        PatchOpcode resolvedOpcode = new PatchOpcode(_patchLevel, opcodeValue, version);
        return resolvedOpcode;
    }
}
