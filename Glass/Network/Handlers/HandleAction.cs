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
// HandleAction
//
// Handles OP_Action messages.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleAction : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _targetId_Slot;
    private readonly SlotId _source_Slot;
    private readonly SlotId _spellId_Slot;
    private readonly SlotId _skill_Slot;
    private readonly SlotId _level_Slot;

    private readonly SlotId _unknown_1_Slot;
    private readonly SlotId _unknown_2_Slot;
    private readonly SlotId _unknown_3_Slot;
    private readonly SlotId _unknown_4_Slot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleAction (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_Action from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_Action, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleAction(PatchLevel patchLevel)
        : base(patchLevel, "OP_Action")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        if (_opcodeHandled == PatchOpcode.None)
        {
            // happens when replaying captures before the opcode was added
            return;
        }

        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_Action");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _targetId_Slot = _registry.IndexOfField(_collectionHandle, "Target_Id");
        _source_Slot = _registry.IndexOfField(_collectionHandle, "Source_Id");
        _spellId_Slot = _registry.IndexOfField(_collectionHandle, "Spell_Id");
        _skill_Slot = _registry.IndexOfField(_collectionHandle, "Skill");
        _level_Slot = _registry.IndexOfField(_collectionHandle, "Level");

        _unknown_1_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_1");
        _unknown_2_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_2");
        _unknown_3_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_3");
        _unknown_4_Slot = _registry.IndexOfField(_collectionHandle, "Unknown_4");
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
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleAction:  No RootGate", LogLevel.Error);
                return;
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
    // Extracts OP_Action against the active patch and builds a display tree: a root node for
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
                DebugLog.Write(LogChannel.Opcodes, "HandleAction:  No RootGate", LogLevel.Error);
                return root;
            }

            SpawnId targetId = (SpawnId)_extractor.GetUIntAt(_targetId_Slot);
            SpawnId sourceId = (SpawnId)_extractor.GetUIntAt(_source_Slot);
            SpellId spellId = (SpellId)_extractor.GetUIntAt(_spellId_Slot);
            uint skillId = _extractor.GetUIntAt(_skill_Slot);

            targetName = ZoneRepository.Instance.LookupSpawnName(zoneId, targetId);
            attackerName = ZoneRepository.Instance.LookupSpawnName(zoneId, sourceId);

            FieldNodes.AddLabeledNode(_extractor, _targetId_Slot, "Target: " + targetName + " (" +
                    targetId + ", 0x" + targetId.Value.ToString("X4") + ")", root);

            FieldNodes.AddLabeledNode(_extractor, _source_Slot, "Source: " + attackerName + " (" +
                    sourceId + ", 0x" + sourceId.Value.ToString("X4") + ")", root);
            FieldNodes.AddLabeledNode(_extractor, _skill_Slot, "Skill: " + Skills.GetSkillName(skillId), root);

            string spellName = SpellCatalog.Instance.LookupSpell(spellId);
            FieldNodes.AddLabeledNode(_extractor, _spellId_Slot, "Spell: " + spellName + " (" +
                spellId + ", 0x" + spellId.Value.ToString("X4") + ")", root);
            FieldNodes.AddUIntNode(_extractor, _level_Slot, "Level", root, "D");

            FieldNodes.AddUIntNode(_extractor, _unknown_1_Slot, "Unknown 1", root, "?");
            FieldNodes.AddFloatNode(_extractor, _unknown_2_Slot, "Unknown 2", root, "F2");
            FieldNodes.AddFloatNode(_extractor, _unknown_3_Slot, "Unknown 3", root, "F2");
            FieldNodes.AddFloatNode(_extractor, _unknown_4_Slot, "Unknown 4", root, "F2");
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Action (" + targetName + ")";
        return root;
    }
}