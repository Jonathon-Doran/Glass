///////////////////////////////////////////////////////////////////////////////////////////////
// HandleZoneSpawns
//
// Handles OP_ZoneSpawns packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Handlers;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System.Buffers.Binary;
using System.Data;
using System.Text;

public class HandleTracking_Z2C : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_Tracking_Z2C";
    private readonly CollectionHandle _collectionHandle;
    private readonly PatchOpcode _opcodeHandled;
    private readonly PatchRegistry _registry;
    private readonly PatchLevel _patchLevel;
    private readonly GateDefinitionHandle _top_level_gate;
    private readonly GateDefinitionHandle _discriminator;

    private readonly SlotId _magicSlot;         // for ResolveVersion

    private readonly SlotId _countSlot;
    private readonly SlotId _trackingEntriesSlot;

    private readonly SlotId _spawnIdSlot;  
    private readonly SlotId _levelSlot;
    private readonly SlotId _nameSlot;

    private readonly uint TRACKING_MAGIC_NUMBER = 0x4f348bff;
    //private readonly bool _brief = false;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleTracking_Z2C (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_TrackingUpdate from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_TrackingUpdate, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleTracking_Z2C()
    {
        _registry =   GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        PatchOpcode baseOpcode = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _opcodeHandled = baseOpcode with { Version = 2 };
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "Tracking_V2");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _countSlot = _registry.IndexOfField(_collectionHandle, "count");
        _trackingEntriesSlot = _registry.IndexOfField(_collectionHandle, "tracking_entries");

        CollectionHandle entriesCollection = _registry.GetCollectionHandle(_patchLevel, "Tracking_Entries");

        _spawnIdSlot = _registry.IndexOfField(entriesCollection, "spawn_id");
        _nameSlot = _registry.IndexOfField(entriesCollection, "name");
        _levelSlot = _registry.IndexOfField(entriesCollection, "level");

        CollectionHandle discriminatorCollection = _registry.GetCollectionHandle(_patchLevel, "Tracking_Discriminator");
        _magicSlot = _registry.IndexOfField(discriminatorCollection, "magic");
        _discriminator = _registry.GetGateDefinitionHandle(_patchLevel, "Gate_Tracking_Discriminator");

        _ = TRACKING_MAGIC_NUMBER;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    // Log any errors in the cold-path, dispose of any local storage. 
    ///////////////////////////////////////////////////////////////////////////////////////////////

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeName
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public string OpcodeName
    {
        get { return _opcodeName; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        switch (metadata.Channel)
        {
            case SoeConstants.StreamId.StreamZoneToClient:
                HandleZoneToClient(data, metadata);
                break;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleZoneToClient
    //
    // Processes a zone-to-client V2 tracking packet.  The top-level Once gate yields the single
    // Tracking_V2 bag, whose tracking_entries slot holds the Times gate over the per-target
    // entries.  Each entry bag is entered in turn and its spawn id, level, and name are read.
    //
    // data:      The application payload.
    // metadata:  Packet metadata (timestamp, source/dest).
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            if (extractor.IsPresent(_trackingEntriesSlot) == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleTracking_Z2C.HandleZoneToClient: no tracking_entries gate present", LogLevel.Warn);
                return;
            }

            GateHandle entriesGate = extractor.GetGateAt(_trackingEntriesSlot);
            if (entriesGate.Exists == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleTracking_Z2C.HandleZoneToClient: tracking_entries present but no gate", LogLevel.Warn);
                return;
            }

            uint bagCount = extractor.BagCount(entriesGate);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                extractor.EnterGate(entriesGate, bagIndex);
                uint spawnId = extractor.GetUIntAt(_spawnIdSlot);
                uint level = extractor.GetUIntAt(_levelSlot);
                string name = extractor.GetStringAt(_nameSlot);
            }
        }
        finally
        {
            extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_Tracking V2 against the active patch and builds a display tree.  The top-level
    // Once gate yields the single Tracking_V2 bag, whose tracking_entries slot holds the Times gate
    // over the per-target entries.  A root node is built with one child node per entry, each
    // carrying that entry's name, spawn id, and level with their payload byte ranges.
    //
    // data:      The application payload.
    // metadata:  Packet metadata (timestamp, source/dest).
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        FieldDisplayNode root = new FieldDisplayNode();
        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            if (extractor.IsPresent(_trackingEntriesSlot) == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleTracking_Z2C.Describe: no tracking_entries gate present", LogLevel.Warn);
                root.Text = "Tracking";
                return root;
            }

            GateHandle entriesGate = extractor.GetGateAt(_trackingEntriesSlot);
            if (entriesGate.Exists == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleTracking_Z2C.Describe: tracking_entries present but no gate", LogLevel.Warn);
                root.Text = "Tracking";
                return root;
            }

            uint bagCount = extractor.BagCount(entriesGate);
            DebugLog.Write(LogChannel.Opcodes,
                "HandleTracking_Z2C.Describe: walking " + bagCount + " tracking entries", LogLevel.Trace);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                extractor.EnterGate(entriesGate, bagIndex);
                FieldDisplayNode entryNode = new FieldDisplayNode("Entry " + (bagIndex + 1));
                root.AddChild(entryNode);
                FieldNodes.AddStringNode(extractor, _nameSlot, "Name", entryNode);
                FieldNodes.AddUIntNode(extractor, _spawnIdSlot, "Spawn ID", entryNode);
                FieldNodes.AddUIntNode(extractor, _levelSlot, "Level", entryNode, "D");
            }
        }
        finally
        {
            extractor.Release();
        }
        root.Text = "Tracking";
        return root;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }
}

