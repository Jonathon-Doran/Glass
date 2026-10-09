using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.World;
using System;
using System.Buffers.Binary;
using System.Security.Policy;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleNewSpellEffect
//
// Handles OP_NewSpellEffect messages.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleNewSpellEffect : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _targetID_Slot;
    private readonly SlotId _spellID_Slot;
    private readonly SlotId _ticksRemaining_Slot;
    private readonly SlotId _totalTicks_Slot;
    private readonly SlotId _unknown_1_Slot;
    private readonly SlotId _casterID_Slot;
    private readonly SlotId _casterLevel_Slot;
    private readonly SlotId _position_Slot;
    private readonly SlotId _unknown_2_Slot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleNewSpellEffect (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_NewSpellEffect from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_NewSpellEffect, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleNewSpellEffect(PatchLevel patchLevel)
        : base(patchLevel, "OP_New_Spell_Effect")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "New_Spell_Effect");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _targetID_Slot = _registry.IndexOfField(_collectionHandle, "Target_ID");
        _spellID_Slot = _registry.IndexOfField(_collectionHandle, "Spell_ID");
        _ticksRemaining_Slot = _registry.IndexOfField(_collectionHandle, "Ticks_Remaining");
        _totalTicks_Slot = _registry.IndexOfField(_collectionHandle, "Total_Ticks");
        _unknown_1_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_1");
        _casterID_Slot = _registry.IndexOfField(_collectionHandle, "Caster_ID");
        _casterLevel_Slot = _registry.IndexOfField(_collectionHandle, "Caster_Level");
        _position_Slot = _registry.IndexOfField(_collectionHandle, "Position");
        _unknown_2_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_2");
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
    // Decodes a new spell effect from the zone stream and stores it as an active spell on the
    // character bound to the stream's connection, replacing whatever was at that buff position.
    //
    // data:      The application payload
    // metadata:  Packet metadata; selects the connection whose character receives the spell
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleNewSpellEffect.HandleZoneToClient: no root gate; " +
                    "spell not stored", LogLevel.Error);
                return;
            }

            Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;
            if (character == null)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleNewSpellEffect.HandleZoneToClient: connection has no " +
                    "character; spell not stored", LogLevel.Warn);
                return;
            }

            uint position = _extractor.GetUIntAt(_position_Slot);
            SpellId spellId = (SpellId)_extractor.GetUIntAt(_spellID_Slot);
            uint casterId = _extractor.GetUIntAt(_casterID_Slot);
            uint casterLevel = _extractor.GetUIntAt(_casterLevel_Slot);
            uint totalTicks = _extractor.GetUIntAt(_totalTicks_Slot);
            uint remainingTicks = _extractor.GetUIntAt(_ticksRemaining_Slot);
            uint targetId = _extractor.GetUIntAt(_targetID_Slot);

            character.SetActiveSpell(new ActiveSpell(position, spellId, casterId, casterLevel, totalTicks,
                remainingTicks));

            DebugLog.Write(LogChannel.Fields, "HandleNewSpellEffect.HandleZoneToClient: stored spell " + spellId +
                " at position " + position + " for '" + character.Name + "', target 0x" + targetId.ToString("X4") +
                ", caster 0x" + casterId.ToString("X") + ", caster level " + casterLevel + ", ticks " +
                remainingTicks + "/" + totalTicks, LogLevel.Trace);
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_NewSpellEffect against the active patch and builds a display tree: a root node for
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
        string spellName;
        string targetName;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleNewSpellEffect:  No RootGate", LogLevel.Error);
                return root;
            }

            SpawnId targetId = (SpawnId)_extractor.GetUIntAt(_targetID_Slot);
            SpellId spellId = (SpellId)_extractor.GetUIntAt(_spellID_Slot);

            spellName = SpellCatalog.Instance.LookupSpell(spellId);
            FieldNodes.AddLabeledNode(_extractor, _spellID_Slot, "Spell: " + spellName + " (" +
                spellId + ", 0x" + spellId.Value.ToString("X4") + ")", root);

            targetName = MobRepository.Instance.LookupSpawnName(zoneId, targetId);

            FieldNodes.AddLabeledNode(_extractor, _targetID_Slot, "Target: " + targetName + " (" +
                    targetId + ", 0x" + targetId.Value.ToString("X4") + ")", root);
            FieldNodes.AddUIntNode(_extractor, _ticksRemaining_Slot, "Ticks Remaining", root, "D");
            FieldNodes.AddUIntNode(_extractor, _totalTicks_Slot, "Total Ticks", root, "D");
            FieldNodes.AddUIntNode(_extractor, _casterID_Slot, "Caster ID", root, "X4");
            FieldNodes.AddUIntNode(_extractor, _casterLevel_Slot, "Caster Level", root, "D");
            FieldNodes.AddFloatNode(_extractor, _unknown_1_Slot, "Unknown 1", root, "F2");
            FieldNodes.AddUIntNode(_extractor, _position_Slot, "Position", root, "D");
            FieldNodes.AddUIntNode(_extractor, _unknown_2_Slot, "Unknown 2", root, "?");
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "New Spell Effect (" + spellName + ")";
        return root;
    }
}