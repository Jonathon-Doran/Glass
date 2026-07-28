using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Handlers;
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
    private readonly FrozenDictionary<PatchOpcode, OpcodeHandler> _handlers;
    private static readonly object _instanceLock = new object();
    private static BusState _busState = BusState.On;

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
    // Private.  Scans the executing assembly for all non-abstract classes deriving from
    // OpcodeHandler, instantiates each one via its PatchLevel constructor, and registers
    // it by opcode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private OpcodeDispatch()
    {
        int opcodeCount = GlassContext.PatchRegistry.GetOpcodeCount(GlassContext.CurrentPatchLevel);
        Dictionary<PatchOpcode, OpcodeHandler> builder = new Dictionary<PatchOpcode, OpcodeHandler>();

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeDispatch: scanning assembly for OpcodeHandler implementations", LogLevel.Trace);

        Assembly assembly = Assembly.GetExecutingAssembly();
        Type baseType = typeof(OpcodeHandler);

        _patchLevel = GlassContext.CurrentPatchLevel;

        foreach (Type type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface)
            {
                continue;
            }

            if (!baseType.IsAssignableFrom(type))
            {
                continue;
            }

            ConstructorInfo? constructor = type.GetConstructor(new Type[] { typeof(PatchLevel) });

            if (constructor == null)
            {
                DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch: skipping " + type.Name
                    + " — no PatchLevel constructor", LogLevel.Warn);
                continue;
            }

            OpcodeHandler handler = (OpcodeHandler)constructor.Invoke(new object[] { _patchLevel });
            PatchOpcode patchOpcode = handler.OpcodeHandled;

            if (patchOpcode.Exists == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch: skipping " + type.Name
                    + " — handler reports no opcode for patch level " + _patchLevel, LogLevel.Trace);
                continue;
            }

            builder[patchOpcode] = handler;

            DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch: registered " + type.Name
                + " for opcode " + patchOpcode, LogLevel.Trace);
        }

        _handlers = builder.ToFrozenDictionary();

        GlassContext.PacketBus.Subscribe(HandlePacket);
        DebugLog.Write(LogChannel.Opcodes, "OpcodeDispatch: scan complete, "
            + _handlers.Count + " handlers registered", LogLevel.Trace);
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
            DebugLog.Write(LogChannel.Opcodes, "packet arrived at handler during shutdown", LogLevel.Warn);
            return;
        }

        // V0 opcodes are synthetic and not handled.  Silently discard.
        if (metadata.Opcode.Version == 0)
        {
            return;
        }

        if (_handlers.TryGetValue(metadata.Opcode, out OpcodeHandler? handler) == true)
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

        if (_handlers.TryGetValue(baseOpcode, out OpcodeHandler? versionResolver) == false)
        {
            return new PatchOpcode(_patchLevel, opcodeValue, 0);
        }

        uint version = versionResolver.ResolveVersion(data, metadata);
        PatchOpcode resolvedOpcode = new PatchOpcode(_patchLevel, opcodeValue, version);
        return resolvedOpcode;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Asks the handler for the packet's opcode to return a display tree for
    // the payload.  Returns null when no handler is registered for the opcode.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns:   The root FieldDisplayNode, or null when no handler is registered.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public FieldDisplayNode? Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        if (_handlers.TryGetValue(metadata.Opcode, out OpcodeHandler? handler) == true)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeDispatch.Describe: describing " + metadata.Opcode, LogLevel.Trace);
            return handler.Describe(data, metadata);
        }

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeDispatch.Describe: no handler for " + metadata.Opcode, LogLevel.Trace);
        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SetBusState
    //
    // Sets dispatch's attachment to the PacketBus and returns the prior state so the caller
    // can restore it.  Off unsubscribes the live instance's HandlePacket when one exists.
    // On resubscribes the live instance when one exists.  Setting the current state again
    // is a logged no-op that still returns the prior state.
    //
    // state:  BusState.On or BusState.Off.
    //
    // Returns:  The state in effect before this call.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public BusState SetBusState(BusState state)
    {
        lock (_instanceLock)
        {
            BusState previous = _busState;

            if (state == previous)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "OpcodeDispatch.SetBusState: already " + state + ", no change", LogLevel.Warn);
                return previous;
            }

            _busState = state;

            if (state == BusState.Off)
            {
                GlassContext.PacketBus.Unsubscribe(_instance!.HandlePacket);
                DebugLog.Write(LogChannel.Opcodes,
                    "OpcodeDispatch.SetBusState: unsubscribed live instance, dispatch off",
                    LogLevel.Info);
            }
            else
            {
                GlassContext.PacketBus.Subscribe(_instance!.HandlePacket);
                DebugLog.Write(LogChannel.Opcodes,
                    "OpcodeDispatch.SetBusState: resubscribed live instance, dispatch on",
                    LogLevel.Info);
            }

            return previous;
        }
    }
}
