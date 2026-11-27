using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Reference;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;
using System.Security.Policy;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleDeath
//
// Handles OP_Death messages.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleDeath : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _spawnId_Slot;
    private readonly SlotId _killerId_Slot;
    private readonly SlotId _spellId_Slot;
    private readonly SlotId _damage_Slot;
    private readonly SlotId _skill_Slot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleDeath (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_Death from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_Death, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleDeath(PatchLevel patchLevel)
        : base(patchLevel, "OP_Death")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        if (_opcodeHandled == PatchOpcode.None)
        {
            // happens when replaying captures before the opcode was added
            return;
        }

        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_Death");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _spawnId_Slot = _registry.IndexOfField(_collectionHandle, "spawn_id");
        _killerId_Slot = _registry.IndexOfField(_collectionHandle, "killer_id");
        _spellId_Slot = _registry.IndexOfField(_collectionHandle, "spell_id");
        _damage_Slot = _registry.IndexOfField(_collectionHandle, "damage");
        _skill_Slot = _registry.IndexOfField(_collectionHandle, "skill");
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
        uint spawnId;
        uint killerId;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleDeath:  No RootGate", LogLevel.Error);
                return;
            }

            spawnId = _extractor.GetUIntAt(_spawnId_Slot);
            killerId = _extractor.GetUIntAt(_killerId_Slot);

        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_Death against the active patch and builds a display tree: a root node for
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
        string killerName;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleDeath:  No RootGate", LogLevel.Error);
                return root;
            }

            SpawnId spawnId = (SpawnId)_extractor.GetUIntAt(_spawnId_Slot);
            SpawnId killerId = (SpawnId)_extractor.GetUIntAt(_killerId_Slot);
            SpellId spellId = (SpellId)_extractor.GetUIntAt(_spellId_Slot);
            uint skillId = _extractor.GetUIntAt(_skill_Slot);

            targetName = ZoneRepository.Instance.LookupSpawnName(zoneId, spawnId);
            killerName = ZoneRepository.Instance.LookupSpawnName(zoneId, killerId);

            FieldNodes.AddLabeledNode(_extractor, _spawnId_Slot, "Target: " + targetName + " (" +
                    spawnId + ", 0x" + spawnId.Value.ToString("X4") + ")", root);

            FieldNodes.AddLabeledNode(_extractor, _spawnId_Slot, "Killer: " + killerName + " (" +
                    killerId + ", 0x" + killerId.Value.ToString("X4") + ")", root);
            FieldNodes.AddLabeledNode(_extractor, _skill_Slot, "Skill: " + Skills.GetSkillName(skillId), root);

            string spellName = SpellCatalog.Instance.LookupSpell(spellId);
            FieldNodes.AddLabeledNode(_extractor, _spellId_Slot, "Spell: " + spellName + " (" +
                spellId + ", 0x" + spellId.Value.ToString("X4") + ")", root);
            FieldNodes.AddUIntNode(_extractor, _damage_Slot, "Damage", root, "D");
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Death (" + targetName + ")";
        return root;
    }
}