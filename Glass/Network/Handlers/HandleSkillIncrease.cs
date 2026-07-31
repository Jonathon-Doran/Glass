using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;
using System.Security.Policy;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleSkillIncrease
//
// Handles OP_SkillIncrease packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleSkillIncrease : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _skillIdSlot;
    private readonly SlotId _newValueSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleSkillIncrease  (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleSkillIncrease(PatchLevel patchLevel)
        : base(patchLevel, "OP_Skill_Increase")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "Skill Increase");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _skillIdSlot = _registry.IndexOfField(_collectionHandle, "Skill_ID");
        _newValueSlot = _registry.IndexOfField(_collectionHandle, "New_Value");
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
                HandleClientToZone(data, metadata);
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
    private void HandleClientToZone(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "Target: metadata cannot be "
                + "mapped to a character.  Dropping mob data.", LogLevel.Warn);
            return;
        }

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            uint skillId = _extractor.GetUIntAt(_skillIdSlot);
            uint newValue = _extractor.GetUIntAt(_newValueSlot);
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_SkillIncrease against the active patch and builds a display tree: a root node for
    // the collection with one leaf child per field each carrying its payload byte range.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        FieldDisplayNode root = new FieldDisplayNode();
        string playerName;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "SkillIncrease: metadata cannot be "
                + "mapped to a character..", LogLevel.Warn);
            root.Text = "Skill Increase (Unknown)";
            return root;
        }

        playerName = character.Name;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            uint skillId = _extractor.GetUIntAt(_skillIdSlot);

            FieldNodes.AddUIntNode(_extractor, _skillIdSlot, "Skill ID", root, "X4");
            FieldNodes.AddLabeledNode(_extractor, _skillIdSlot, "Skill: " + Skills.GetSkillName(skillId), root);

            FieldNodes.AddUIntNode(_extractor, _newValueSlot, "New Value", root, "D");

        }
        finally
        {
            _extractor.Release();
        }



        root.Text = "Skill Increase (" + playerName + ")";
        return root;
    }
}

