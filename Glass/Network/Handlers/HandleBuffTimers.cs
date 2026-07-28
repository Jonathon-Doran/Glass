using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;


namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleBuffTimers
//
// Handles OP_BuffTimers messages.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleBuffTimers : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _buffPosSlot;
    private readonly SlotId _spellIdSlot;
    private readonly SlotId _ticksRemainingSlot;
    private readonly SlotId _casterNameSlot;

    private readonly SlotId _playerIdSlot;
    private readonly SlotId _interTickGapSlot;
    private readonly SlotId _kindSlot;
    private readonly SlotId _buffsGateSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleBuffTimers  (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleBuffTimers(PatchLevel patchLevel)
        : base(patchLevel, "OP_Buff_Timers")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "Buff Timers Header");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        // header slots
        _playerIdSlot = _registry.IndexOfField(_collectionHandle, "PlayerID");
        _interTickGapSlot = _registry.IndexOfField(_collectionHandle, "InterTick_Gap");
        _kindSlot = _registry.IndexOfField(_collectionHandle, "Kind");
        _buffsGateSlot = _registry.IndexOfField(_collectionHandle, "Buffs");

        CollectionHandle buffEntryCollection = _registry.GetCollectionHandle(_patchLevel, "Buff Timer Entry");
        
        // Per-entry slots
        _buffPosSlot = _registry.IndexOfField(buffEntryCollection, "Buff_Slot");
        _spellIdSlot = _registry.IndexOfField(buffEntryCollection, "Spell_ID");
        _ticksRemainingSlot = _registry.IndexOfField(buffEntryCollection, "Ticks_Remaining");
        _casterNameSlot = _registry.IndexOfField(buffEntryCollection, "Caster_Name");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:       The application payload
    // metadata:   Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        switch (metadata.Channel)
        {
            case SoeConstants.StreamId.StreamClientToZone:
                HandleZoneToClient(data, metadata);
                break;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleClientToZone
    //
    // Processes client-to-zone traffic
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            if (extractor.IsPresent(_buffsGateSlot) == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleBuffTimers: no Buffs gate present", LogLevel.Warn);
                return;
            }

            GateHandle entriesGate = extractor.GetGateAt(_buffsGateSlot);
            if (entriesGate.Exists == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleBuffTimers: Buffs present but no gate", LogLevel.Warn);
                return;
            }

            uint bagCount = extractor.BagCount(entriesGate);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                extractor.EnterGate(entriesGate, bagIndex);

                uint playerID = extractor.GetUIntAt(_playerIdSlot);
                uint interTickGap = extractor.GetUIntAt(_interTickGapSlot);
                uint kind = extractor.GetUIntAt(_kindSlot);
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
    // Extracts OP_BuffTimers against the active patch and builds a display tree: a root node for
    // the collection with one leaf child per field each carrying its payload byte range.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        FieldDisplayNode root = new FieldDisplayNode();
        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            if (extractor.IsPresent(_buffsGateSlot) == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleBuffTimers.Describe: no Buffs gate present", LogLevel.Warn);
                root.Text = "Buff Timers";
                return root;
            }

            GateHandle entriesGate = extractor.GetGateAt(_buffsGateSlot);
            if (entriesGate.Exists == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleBuffTimers.Describe: Buffs present but no gate", LogLevel.Warn);
                root.Text = "Buff Timers";
                return root;
            }

            FieldNodes.AddUIntNode(extractor, _playerIdSlot, "Player ID", root);
            FieldNodes.AddUIntNode(extractor, _interTickGapSlot, "Inter-Tick Gap", root);
            FieldNodes.AddUIntNode(extractor, _kindSlot, "Kind", root, "D");



            uint bagCount = extractor.BagCount(entriesGate);
            DebugLog.Write(LogChannel.Opcodes,
                "HandleBuffTimers.Describe: walking " + bagCount + " buff entries", LogLevel.Trace);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                extractor.EnterGate(entriesGate, bagIndex);
                FieldDisplayNode entryNode = new FieldDisplayNode("Entry " + (bagIndex + 1));
                root.AddChild(entryNode);

                FieldNodes.AddUIntNode(extractor, _buffPosSlot, "Buff Position", entryNode, "D");
                FieldNodes.AddUIntNode(extractor, _spellIdSlot, "Spell ID", entryNode);
                FieldNodes.AddUIntNode(extractor, _ticksRemainingSlot, "Ticks Remaining", entryNode, "D");
                FieldNodes.AddStringNode(extractor, _casterNameSlot, "Caster", entryNode);

            }
        }
        finally
        {
            extractor.Release();
        }
        root.Text = "Buff Timers";
        return root;
    }
}