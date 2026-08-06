using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.World;


namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleTargetSpells
//
// Handles OP_TargetSpells messages.  Identical structure to BuffTimers, but for the target window.
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleTargetSpells: OpcodeHandler
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
    public HandleTargetSpells(PatchLevel patchLevel)
        : base(patchLevel, "OP_TargetSpells")
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
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            if (_extractor.IsPresent(_buffsGateSlot) == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleTargetSpells: no Buffs gate present", LogLevel.Warn);
                return;
            }

            GateHandle entriesGate = _extractor.GetGateAt(_buffsGateSlot);
            if (entriesGate.Exists == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleTargetSpells: Buffs present but no gate", LogLevel.Warn);
                return;
            }

            uint bagCount = _extractor.BagCount(entriesGate);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                _extractor.EnterGate(entriesGate, bagIndex);

                uint playerID = _extractor.GetUIntAt(_playerIdSlot);
                uint interTickGap = _extractor.GetUIntAt(_interTickGapSlot);
                uint kind = _extractor.GetUIntAt(_kindSlot);
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
        ZoneId zoneId = GlassContext.SessionRegistry.ZoneFromMetadata(metadata);
        FieldDisplayNode root = new FieldDisplayNode();
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleTargetSpells:  No RootGate", LogLevel.Error);
                return root;
            }

            if (_extractor.IsPresent(_buffsGateSlot) == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleTargetSpells.Describe: no Buffs gate present", LogLevel.Warn);
                root.Text = "Buff Timers";
                return root;
            }

            GateHandle entriesGate = _extractor.GetGateAt(_buffsGateSlot);
            if (entriesGate.Exists == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleTargetSpells.Describe: Buffs present but no gate", LogLevel.Warn);
                root.Text = "Buff Timers";
                return root;
            }

            SpawnId playerId = (SpawnId)_extractor.GetUIntAt(_playerIdSlot);

            string playerName = MobRepository.Instance.LookupSpawnName(zoneId, playerId);

            FieldNodes.AddLabeledNode(_extractor, _playerIdSlot, "Spawn: " + playerName + " (" +
                    playerId + ", 0x" + playerId.Value.ToString("X4") + ")", root);
            FieldNodes.AddUIntNode(_extractor, _interTickGapSlot, "Inter-Tick Gap", root);
            FieldNodes.AddUIntNode(_extractor, _kindSlot, "Kind", root, "D");

            uint bagCount = _extractor.BagCount(entriesGate);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                _extractor.EnterGate(entriesGate, bagIndex);
                FieldDisplayNode entryNode = new FieldDisplayNode("Entry " + (bagIndex + 1));
                root.AddChild(entryNode);

                uint spellID = _extractor.GetUIntAt(_spellIdSlot);
                string spellName = SpellCatalog.Instance.LookupSpell(spellID);

                FieldNodes.AddUIntNode(_extractor, _buffPosSlot, "Buff Position", entryNode, "D");

                FieldNodes.AddLabeledNode(_extractor, _spellIdSlot, "Spell: " + spellName + " (" +
                    spellID + ", 0x" + spellID.ToString("X8") + ")", entryNode);

                FieldNodes.AddUIntNode(_extractor, _ticksRemainingSlot, "Ticks Remaining", entryNode, "D");
                FieldNodes.AddStringNode(_extractor, _casterNameSlot, "Caster", entryNode);
            }
        }
        finally
        {
            _extractor.Release();
        }
        root.Text = "Target Buff Timers";
        return root;
    }
}