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
// HandleDamage
//
// Handles OP_Death messages.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleDamage : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _targetId_Slot;
    private readonly SlotId _attackerId_Slot;
    private readonly SlotId _damage_Slot;
    private readonly SlotId _flags_Slot;
    private readonly SlotId _spellId_Slot;
    private readonly SlotId _attackerLevel_Slot;
    private readonly SlotId _unknown1_Slot;
    private readonly SlotId _unknown2_Slot;
    private readonly SlotId _skill_Slot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleDamage (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_Damage from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_Damage, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleDamage(PatchLevel patchLevel)
        : base(patchLevel, "OP_Damage")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        if (_opcodeHandled == PatchOpcode.None)
        {
            // happens when replaying captures before the opcode was added
            return;
        }

        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_Damage");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _targetId_Slot = _registry.IndexOfField(_collectionHandle, "TargetSpawn");
        _attackerId_Slot = _registry.IndexOfField(_collectionHandle, "AttackerSpawn");
        _damage_Slot = _registry.IndexOfField(_collectionHandle, "Damage");
        _flags_Slot = _registry.IndexOfField(_collectionHandle, "Flags");
        _spellId_Slot = _registry.IndexOfField(_collectionHandle, "SpellId");
        _attackerLevel_Slot = _registry.IndexOfField(_collectionHandle, "AttackerLevel");
        _unknown1_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_1");
        _unknown2_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_2");
        _skill_Slot = _registry.IndexOfField(_collectionHandle, "Skill");
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
    // Processes zone-to-client traffic
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        uint targetId;
        uint attackerId;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleDeath:  No RootGate", LogLevel.Error);
                return;
            }

            targetId = _extractor.GetUIntAt(_targetId_Slot);
            attackerId = _extractor.GetUIntAt(_attackerId_Slot);

        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_Damage against the active patch and builds a display tree: a root node for
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

        string targetName;
        string attackerName;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleDeath:  No RootGate", LogLevel.Error);
                return root;
            }

            SpawnId targetId = (SpawnId)_extractor.GetUIntAt(_targetId_Slot);
            SpawnId attackerId = (SpawnId)_extractor.GetUIntAt(_attackerId_Slot);
            uint damage = _extractor.GetUIntAt(_damage_Slot);
            SpellId spellId = (SpellId) _extractor.GetUIntAt(_spellId_Slot);
            float unknown_1 = _extractor.GetFloatAt(_unknown1_Slot);
            uint skillId = _extractor.GetUIntAt(_skill_Slot);

            targetName = ZoneRepository.Instance.LookupSpawnName(zoneId, targetId);
            attackerName = ZoneRepository.Instance.LookupSpawnName(zoneId, attackerId);

            FieldNodes.AddLabeledNode(_extractor, _targetId_Slot, "Target: " + targetName + " (" +
                    targetId + ", 0x" + targetId.Value.ToString("X4") + ")", root);

            FieldNodes.AddLabeledNode(_extractor, _attackerId_Slot, "Attacker: " + attackerName + " (" +
                    attackerId + ", 0x" + attackerId.Value.ToString("X4") + ")", root);
            FieldNodes.AddUIntNode(_extractor, _damage_Slot, "Damage", root, "D");
            FieldNodes.AddUIntNode(_extractor, _flags_Slot, "Flags", root, "X2");
            string spellName = SpellCatalog.Instance.LookupSpell(spellId);
            FieldNodes.AddLabeledNode(_extractor, _spellId_Slot, "Spell: " + spellName + " (" + spellId.Value.ToString() + ")", root);
            FieldNodes.AddUIntNode(_extractor, _attackerLevel_Slot, "Attacker Level", root, "D");
            FieldNodes.AddFloatNode(_extractor, _unknown1_Slot, "Unknown 1", root, "F2");
            FieldNodes.AddFloatNode(_extractor, _unknown2_Slot, "Unknown 2", root, "F2");
            string skillName = Skills.GetSkillName(skillId);
            FieldNodes.AddLabeledNode(_extractor, _skill_Slot, "Skill: " + skillName + " (0x" + skillId.ToString("X4") + ")", root);
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Damage (" + targetName + ")";
        return root;
    }
}
