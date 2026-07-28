///////////////////////////////////////////////////////////////////////////////////////////////
// HandleTracking (Zone to Client)
//
// Handles OP_Tracking_Z2C messages.  
///////////////////////////////////////////////////////////////////////////////////////////////
using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Handlers;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;

public class HandleTracking_Z2C : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;
    private readonly GateDefinitionHandle _discriminator;

    private readonly SlotId _magicSlot;         // for ResolveVersion

    private readonly SlotId _countSlot;
    private readonly SlotId _trackingEntriesSlot;

    private readonly SlotId _spawnIdSlot;  
    private readonly SlotId _levelSlot;
    private readonly SlotId _nameSlot;

    private readonly uint TRACKING_MAGIC_NUMBER = 0x4f348bff;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleTracking_Z2C (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleTracking_Z2C(PatchLevel patchLevel)
        : base (patchLevel, "OP_Tracking_Z2C")
    {
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
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
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
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            if (_extractor.IsPresent(_trackingEntriesSlot) == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleTracking_Z2C.HandleZoneToClient: no tracking_entries gate present", LogLevel.Warn);
                return;
            }

            GateHandle entriesGate = _extractor.GetGateAt(_trackingEntriesSlot);
            if (entriesGate.Exists == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleTracking_Z2C.HandleZoneToClient: tracking_entries present but no gate", LogLevel.Warn);
                return;
            }

            uint bagCount = _extractor.BagCount(entriesGate);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                _extractor.EnterGate(entriesGate, bagIndex);
                uint spawnId = _extractor.GetUIntAt(_spawnIdSlot);
                uint level = _extractor.GetUIntAt(_levelSlot);
                string name = _extractor.GetStringAt(_nameSlot);
            }
        }
        finally
        {
            _extractor.Release();
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
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldDisplayNode root = new FieldDisplayNode();
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            if (_extractor.IsPresent(_trackingEntriesSlot) == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleTracking_Z2C.Describe: no tracking_entries gate present", LogLevel.Warn);
                root.Text = "Tracking";
                return root;
            }

            GateHandle entriesGate = _extractor.GetGateAt(_trackingEntriesSlot);
            if (entriesGate.Exists == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleTracking_Z2C.Describe: tracking_entries present but no gate", LogLevel.Warn);
                root.Text = "Tracking";
                return root;
            }

            uint bagCount = _extractor.BagCount(entriesGate);
            DebugLog.Write(LogChannel.Opcodes,
                "HandleTracking_Z2C.Describe: walking " + bagCount + " tracking entries", LogLevel.Trace);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                _extractor.EnterGate(entriesGate, bagIndex);
                FieldDisplayNode entryNode = new FieldDisplayNode("Entry " + (bagIndex + 1));
                root.AddChild(entryNode);
                FieldNodes.AddStringNode(_extractor, _nameSlot, "Name", entryNode);
                FieldNodes.AddUIntNode(_extractor, _spawnIdSlot, "Spawn ID", entryNode);
                FieldNodes.AddUIntNode(_extractor, _levelSlot, "Level", entryNode, "D");
            }
        }
        finally
        {
            _extractor.Release();
        }
        root.Text = "Tracking";
        return root;
    }
}

