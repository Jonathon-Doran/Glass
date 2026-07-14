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
public class HandleSkillIncrease : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_Skill_Increase";
    private readonly PatchOpcode _opcodeHandled;
    private readonly CollectionHandle _collectionHandle;
    private readonly PatchRegistry _registry;
    private readonly PatchLevel _patchLevel;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _skillIdSlot;
    private readonly SlotId _newValueSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleSkillIncrease  (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_SkillIncrease from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_SkillIncrease , GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleSkillIncrease()
    {
        _registry = GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "Skill Increase");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _skillIdSlot = _registry.IndexOfField(_collectionHandle, "Skill_ID");
        _newValueSlot = _registry.IndexOfField(_collectionHandle, "New_Value");
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
    // data:       The application payload
    // metadata:   Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
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
        FieldExtractor extractor = GlassContext.FieldExtractor;
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "Target: metadata cannot be "
                + "mapped to a character.  Dropping mob data.", LogLevel.Warn);
            return;
        }

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);
            uint skillId = extractor.GetUIntAt(_skillIdSlot);
            uint newValue = extractor.GetUIntAt(_newValueSlot);
        }
        finally
        {
            extractor.Release();
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
    public FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        FieldExtractor extractor = GlassContext.FieldExtractor;
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
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);
            uint skillId = extractor.GetUIntAt(_skillIdSlot);

            FieldNodes.AddUIntNode(extractor, _skillIdSlot, "Skill ID", root, "X4");
            FieldNodes.AddLabeledNode(extractor, _skillIdSlot, "Skill: " + GetSkillName(skillId), root);

            FieldNodes.AddUIntNode(extractor, _newValueSlot, "New Value", root, "D");

        }
        finally
        {
            extractor.Release();
        }



        root.Text = "Skill Increase (" + playerName + ")";
        return root;
    }

    private static readonly Dictionary<uint, string> SkillNames = new Dictionary<uint, string>()
    {
        { 0x0, "1H Blunt" },
        { 0x1, "1H Slashing" },
        { 0x2, "2H Blunt" },
        { 0x3, "2H Slashing" },
        { 0x4, "Abjuration" },
        { 0x5, "Alteration" },
        { 0x6, "Apply Poison" },
        { 0x7, "Archery" },
        { 0x8, "Backstab" },
        { 0x9, "Bind Wound" },
        { 0xA, "Bash" },
        { 0xB, "Block" },
        { 0xC, "Brass Instruments" },
        { 0xD, "Channeling" },
        { 0xE, "Conjuration" },
        { 0xF, "Defense" },
        { 0x10, "Disarm" },
        { 0x11, "Disarm Traps" },
        { 0x12, "Divination" },
        { 0x13, "Dodge" },
        { 0x14, "Double Attack" },
        { 0x15, "Dragon Punch" },
        { 0x16, "Dual Wield" },
        { 0x17, "Eagle Strike" },
        { 0x18, "Evocation" },
        { 0x19, "Feign Death" },
        { 0x1A, "Flying Kick" },
        { 0x1B, "Forage" },
        { 0x1C, "Hand to Hand" },
        { 0x1D, "Hide" },
        { 0x1E, "Kick" },
        { 0x1F, "Meditate" },
        { 0x20, "Mend" },
        { 0x21, "Offense" },
        { 0x22, "Parry" },
        { 0x23, "Pick Lock" },
        { 0x24, "Piercing" },
        { 0x25, "Riposte" },
        { 0x26, "Round Kick" },
        { 0x27, "Safe Fall" },
        { 0x28, "Sense Heading" },
        { 0x29, "Singing" },
        { 0x2A, "Sneak" },
        { 0x2B, "Specialize Abjuration" },
        { 0x2C, "Specialize Alteration" },
        { 0x2D, "Specialize Conjuration" },
        { 0x2E, "Specialize Divination" },
        { 0x2F, "Specialize Evocation" },
        { 0x30, "Pick Pocket" },
        { 0x31, "Stringed Instruments" },
        { 0x32, "Swimming" },
        { 0x33, "Throwing" },
        { 0x34, "Tiger Claw" },
        { 0x35, "Tracking" },
        { 0x36, "Wind Instruments" },
        { 0x37, "Fishing" },
        { 0x38, "Make Poison" },
        { 0x39, "Tinkering" },
        { 0x3A, "Research" },
        { 0x3B, "Alchemy" },
        { 0x3C, "Baking" },
        { 0x3D, "Tailoring" },
        { 0x3E, "Sense Traps" },
        { 0x3F, "Blacksmithing" },
        { 0x40, "Fletching" },
        { 0x41, "Brewing" },
        { 0x42, "Alcohol Tolerance" },
        { 0x43, "Begging" },
        { 0x44, "Jewelry" },
        { 0x45, "Pottery" },
        { 0x46, "Percussion Instruments" },
        { 0x47, "Intimidation" },
        { 0x48, "Berserking" },
        { 0x49, "Taunt" }
    };

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetSkillName
    //
    // Looks up a skill name by its byte value. Returns a descriptive string for unknown values
    // rather than throwing.
    //
    // value:    The skill value to query
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static string GetSkillName(uint skill)
    {
        if (SkillNames.TryGetValue(skill, out string? name))
        {
            return name;
        }

        DebugLog.Write(LogChannel.Opcodes, $"[GetSkillName] skill=0x{skill:X2} not in map, returning 'Unknown'");
        return $"Unknown(0x{skill:X2})";
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }
}

