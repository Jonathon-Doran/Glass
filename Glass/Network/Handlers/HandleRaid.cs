using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.World;
using System;
using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleRaid
//
// Handles OP_Raid packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleRaid : OpcodeHandler
{
    private readonly CollectionHandle _top_level_collection;
    private readonly GateDefinitionHandle _top_level_gate;
    private GateHandle _root_gate;

    private readonly SlotId _subType_Slot;
    private readonly SlotId _name_Slot;
    private readonly SlotId _raidId_Slot;

    private const uint UngroupedNumber = 0xFFFFFFFF;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleRaid (constructor)
    //
    // Resolves the opcode, caches the header field slots this handler reads, and builds the
    // subtype table: one entry per subtype, holding the slot of the subtype's gate and the
    // methods that handle and describe the subtype.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleRaid(PatchLevel patchLevel)
        : base(patchLevel, "OP_Raid_General")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        if (_opcodeHandled == PatchOpcode.None)
        {
            // happens when replaying captures before the opcode was added
            return;
        }

        _top_level_collection = _registry.GetCollectionHandle(_patchLevel, "OP_Raid_General");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _subType_Slot = _registry.IndexOfField(_top_level_collection, "Subtype");
        _name_Slot = _registry.IndexOfField(_top_level_collection, "Name");
        _raidId_Slot = _registry.IndexOfField(_top_level_collection, "RaidId");

        _subtypes.Add(new SubtypeEntry(_registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_00"),
            Handle_Z2C_00, Describe_Z2C_00));
        _subtypes.Add(new SubtypeEntry(_registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_01"),
            Handle_Z2C_01, Describe_Z2C_01));
        _subtypes.Add(new SubtypeEntry(_registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_03"),
            Handle_Z2C_03, Describe_Z2C_03));
        _subtypes.Add(new SubtypeEntry(_registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_05"),
            Handle_Z2C_05, Describe_Z2C_05));
        _subtypes.Add(new SubtypeEntry(_registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_06"),
            Handle_Z2C_06, Describe_Z2C_06));
        _subtypes.Add(new SubtypeEntry(_registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_08"),
            Handle_Z2C_08, Describe_Z2C_08));
        _subtypes.Add(new SubtypeEntry(_registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_0A"),
            Handle_Z2C_0A, Describe_Z2C_0A));
        _subtypes.Add(new SubtypeEntry(_registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_0D"),
            Handle_Z2C_0D, Describe_Z2C_0D));
        _subtypes.Add(new SubtypeEntry(_registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_0F"),
            Handle_Z2C_0F, Describe_Z2C_0F));
        _subtypes.Add(new SubtypeEntry(_registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_10"),
            Handle_Z2C_10, Describe_Z2C_10));
        _subtypes.Add(new SubtypeEntry(_registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_11"),
            Handle_Z2C_11, Describe_Z2C_11));
        _subtypes.Add(new SubtypeEntry(_registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_12"),
            Handle_Z2C_12, Describe_Z2C_12));
        _subtypes.Add(new SubtypeEntry(_registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_14"),
            Handle_Z2C_14, Describe_Z2C_14));
        _subtypes.Add(new SubtypeEntry(_registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_16"),
            Handle_Z2C_16, Describe_Z2C_16));
        _subtypes.Add(new SubtypeEntry(_registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_18"),
            Handle_Z2C_18, Describe_Z2C_18));
        _subtypes.Add(new SubtypeEntry(_registry.IndexOfField(_top_level_collection, "Gate_Raid_Z2C_1E"),
            Handle_Z2C_1E, Describe_Z2C_1E));

        DebugLog.Write(LogChannel.Opcodes, "HandleRaid: built subtype table with " + _subtypes.Count + " entries.",
            LogLevel.Trace);
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
            _root_gate = _extractor.Extract(_top_level_gate, data);

            if (!_root_gate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleRaid:  No RootGate", LogLevel.Error);
                return;
            }

            uint subtype = _extractor.GetUIntAt(_subType_Slot);
            string characterName = _extractor.GetStringAt(_name_Slot);
            RaidId raidId = (RaidId) _extractor.GetUIntAt(_raidId_Slot);

            HandleAllSubtypes(raidId, metadata);
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleAllSubtypes - Act on the one subtype collection present under the OP_Raid_General header
    //
    // Walks the subtype table and, for the first subtype whose gate holds data, enters the
    // subtype's first bag, calls the subtype's handler, and returns to the root gate.  Exactly
    // one subtype should be present in the data.
    //
    // raidId:    The raid id from the message header
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns:   Nothing
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleAllSubtypes(RaidId raidId, PacketMetadata metadata)
    {
        foreach (SubtypeEntry entry in _subtypes)
        {
            if (_extractor.IsPresent(entry.GateSlot) == false)
            {
                continue;
            }

            GateHandle gate = _extractor.GetGateAt(entry.GateSlot);
            if (gate.Exists == false)
            {
                continue;
            }

            if (_extractor.BagCount(gate) == 0)
            {
                continue;
            }

            _extractor.EnterGate(gate, 0);
            entry.Handle(raidId, metadata);
            _extractor.EnterGate(_root_gate, 0);
            return;
        }

        DebugLog.Write(LogChannel.Opcodes, "HandleRaid.HandleAllSubtypes: no known subtype data present for raid id 0x"
            + raidId.Value.ToString("X") + ".", LogLevel.Warn);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // IsReceiver
    //
    // Tests whether a message arrived on the session of the character with the given
    // character id.
    //
    // metadata:     Packet metadata of the message
    // characterId:  The character id to test against the receiving session's character
    //
    // Returns true if the receiving session belongs to that character, false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private static bool IsReceiver(PacketMetadata metadata, CharacterId characterId)
    {
        if (metadata.Character == null)
        {
            return false;
        }

        return metadata.Character.CharacterId == characterId;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_00 -- handle the Z2C 00 subtype fields for OP_Raid_General (Add member to raid)
    //
    // Adds the member to the raid with the header's raid id, acting only on the copy received
    // by the raid leader's session, since the added member does not receive this message.  A
    // member in a group brings its group into the raid; a member in no group is held as one of
    // the raid's ungrouped members.  A raid that is not held is an error.
    //
    // raidId:    The raid id from the message header
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Handle_Z2C_00(RaidId raidId, PacketMetadata metadata)
    {
        CollectionHandle Z2C_00_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_00");
        SlotId name_Slot = _registry.IndexOfField(Z2C_00_Collection, "Name");
        SlotId characterId_Slot = _registry.IndexOfField(Z2C_00_Collection, "CharacterID");
        SlotId position_Slot = _registry.IndexOfField(Z2C_00_Collection, "Position");
        SlotId class_Slot = _registry.IndexOfField(Z2C_00_Collection, "Class");
        SlotId level_Slot = _registry.IndexOfField(Z2C_00_Collection, "Level");

        string name = _extractor.GetStringAt(name_Slot);
        CharacterId characterId = (CharacterId)_extractor.GetUIntAt(characterId_Slot);
        EQClass classValue = (EQClass)_extractor.GetUIntAt(class_Slot);
        uint level = _extractor.GetUIntAt(level_Slot);

        if (RaidRepository.Instance.TryGetRaid(raidId, out Raid? raid) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_00: raid id 0x" + raidId.Value.ToString("X")
                + " is not held, '" + name + "' not added.", LogLevel.Error);
            return;
        }

        if (IsReceiver(metadata, raid.LeaderId) == false)
        {
            return;
        }

        if (GroupRepository.Instance.TryGetGroupOf(characterId, out Group? group))
        {
            if (group.RaidId == raid.Id)
            {
                return;
            }

            if (raid.AddGroup(group) == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_00: raid id 0x" + raid.Id.Value.ToString("X")
                    + " did not accept group id 0x" + group.Id.Value.ToString("X") + " of '" + name + "'.",
                    LogLevel.Warn);
                return;
            }

            DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_00: '" + name + "' characterId=0x"
                + characterId.Value.ToString("X") + " brought group id 0x" + group.Id.Value.ToString("X")
                + " into raid id 0x" + raid.Id.Value.ToString("X") + ".", LogLevel.Trace);
            return;
        }

        if (raid.HasUngroupedMember(characterId))
        {
            return;
        }

        GroupMember member = new GroupMember();
        member.Id = characterId;
        member.Name = name;
        member.Class = classValue;
        member.Level = level;
        member.Character = CharacterRepository.Instance.GetByCharacterId(characterId);

        raid.AddUngroupedMember(member);

        DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_00: added '" + name + "' characterId=0x"
            + characterId.Value.ToString("X") + " level " + level + " to raid id 0x" + raid.Id.Value.ToString("X")
            + " as ungrouped.", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_01 -- handle the Z2C 01 subtype fields for OP_Raid_General (Remove member from raid)
    //
    // Removes the named member from the raid with the header's raid id, acting only on the copy
    // received by the member's own session.  An ungrouped member is removed from the raid's
    // ungrouped members; a member in one of the raid's groups is removed from that group.  A
    // name with no character id is ignored.  A raid that is not held is an error.
    //
    // raidId:    The raid id from the message header
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Handle_Z2C_01(RaidId raidId, PacketMetadata metadata)
    {
        CollectionHandle Z2C_01_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_01");
        SlotId name_Slot = _registry.IndexOfField(Z2C_01_Collection, "Name");

        string name = _extractor.GetStringAt(name_Slot);

        if (CharacterRepository.Instance.TryGetCharacterIdByName(name, out CharacterId characterId) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_01: '" + name
                + "' has no character id, ignored.", LogLevel.Trace);
            return;
        }

        if (IsReceiver(metadata, characterId) == false)
        {
            return;
        }

        if (RaidRepository.Instance.TryGetRaid(raidId, out Raid? raid) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_01: raid id 0x" + raidId.Value.ToString("X")
                + " is not held, '" + name + "' not removed.", LogLevel.Error);
            return;
        }

        if (raid.HasUngroupedMember(characterId))
        {
            raid.RemoveUngroupedMember(characterId);
            return;
        }

        if (GroupRepository.Instance.TryGetGroupOf(characterId, out Group? group) && (group.RaidId == raid.Id))
        {
            GroupRepository.Instance.RemoveMember(characterId);

            DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_01: removed '" + name + "' characterId=0x"
                + characterId.Value.ToString("X") + " from group id 0x" + group.Id.Value.ToString("X")
                + " of raid id 0x" + raid.Id.Value.ToString("X") + ".", LogLevel.Trace);
            return;
        }

        DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_01: '" + name + "' characterId=0x"
            + characterId.Value.ToString("X") + " is not in raid id 0x" + raid.Id.Value.ToString("X")
            + ", nothing removed.", LogLevel.Warn);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_03 -- handle the Z2C 03 subtype fields for OP_Raid_General (Member change group)
    //
    // Updates the ungrouped members of the raid with the header's raid id, acting only on the
    // copy received by the moved member's own session.  A member moved to no group is added to
    // them; a member moved into a group is removed from them.  A name with no character id is
    // ignored.  A raid that is not held is an error.
    //
    // raidId:    The raid id from the message header
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Handle_Z2C_03(RaidId raidId, PacketMetadata metadata)
    {
        CollectionHandle Z2C_03_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_03");
        SlotId name_Slot = _registry.IndexOfField(Z2C_03_Collection, "Name");
        SlotId groupId_Slot = _registry.IndexOfField(Z2C_03_Collection, "GroupId");

        string name = _extractor.GetStringAt(name_Slot);
        uint groupNumber = _extractor.GetUIntAt(groupId_Slot);

        if (CharacterRepository.Instance.TryGetCharacterIdByName(name, out CharacterId characterId) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_03: '" + name
                + "' has no character id, ignored.", LogLevel.Trace);
            return;
        }

        if (IsReceiver(metadata, characterId) == false)
        {
            return;
        }

        if (RaidRepository.Instance.TryGetRaid(raidId, out Raid? raid) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_03: raid id 0x" + raidId.Value.ToString("X")
                + " is not held, '" + name + "' not changed.", LogLevel.Error);
            return;
        }

        if (groupNumber != UngroupedNumber)
        {
            raid.RemoveUngroupedMember(characterId);
            return;
        }

        if (raid.HasUngroupedMember(characterId))
        {
            return;
        }

        GroupMember member = new GroupMember();
        member.Id = characterId;
        member.Name = name;
        member.Character = metadata.Character;
        if ((metadata.Character != null) && metadata.Character.Level.HasValue)
        {
            member.Level = metadata.Character.Level.Value;
        }

        raid.AddUngroupedMember(member);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_05 -- handle the Z2C 05 subtype fields for OP_Raid_General (Disband raid)
    //
    // Removes the raid with the header's raid id.  A raid that is not held has already been
    // removed and is left as it is.
    //
    // raidId:    The raid id from the message header
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Handle_Z2C_05(RaidId raidId, PacketMetadata metadata)
    {
        CollectionHandle Z2C_01_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_01");
 
        // duplicates are sent to all members, we remove on the first message
        if (RaidRepository.Instance.TryGetRaid(raidId, out Raid? raid) == false)
        {
            return;
        }

        RaidRepository.Instance.RemoveRaid(raid);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_06 -- handle the Z2C 06 subtype fields for OP_Raid_General (Raid roster)
    //
    // Not yet acted upon; logs that the subtype was seen.
    //
    // raidId:    The raid id from the message header
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Handle_Z2C_06(RaidId raidId, PacketMetadata metadata)
    {

    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_08 -- handle the Z2C 08 subtype fields for OP_Raid_General (Form raid)
    //
    // Creates the raid with the header's raid id and makes the forming leader its leader.  A
    // raid already held is left as it is.
    //
    // raidId:    The raid id from the message header
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Handle_Z2C_08(RaidId raidId, PacketMetadata metadata)
    {
        CollectionHandle Z2C_08_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_08");
        SlotId name_Slot = _registry.IndexOfField(Z2C_08_Collection, "Name");
        SlotId characterID_Slot = _registry.IndexOfField(Z2C_08_Collection, "CharacterId");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_08_Collection, "Unknown1");

        string name = _extractor.GetStringAt(name_Slot);
        CharacterId leaderId = (CharacterId)_extractor.GetUIntAt(characterID_Slot);

        if (RaidRepository.Instance.TryGetRaid(raidId, out Raid? raid))
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_08: raid id 0x" + raidId.Value.ToString("X")
                + " formed by '" + name + "' is already held.", LogLevel.Trace);
            return;
        }

        raid = RaidRepository.Instance.CreateRaid(raidId);
        if (raid == null)
        {
            return;
        }

        raid.SetLeader(leaderId);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_0A -- handle the Z2C 0A subtype fields for OP_Raid_General (Initialize raid)
    //
    // Not yet acted upon; logs that the subtype was seen.
    //
    // raidId:    The raid id from the message header
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Handle_Z2C_0A(RaidId raidId, PacketMetadata metadata)
    {
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_0D -- handle the Z2C 0D subtype fields for OP_Raid_General (New group leader)
    //
    // Makes the named member the leader of the group that holds it, acting only on the copy
    // received by the member's own session.  A name with no character id is ignored.  A member
    // in no group is an error.
    //
    // raidId:    The raid id from the message header
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Handle_Z2C_0D(RaidId raidId, PacketMetadata metadata)
    {
        CollectionHandle Z2C_0D_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_0D");
        SlotId name_Slot = _registry.IndexOfField(Z2C_0D_Collection, "Name");

        string name = _extractor.GetStringAt(name_Slot);

        if (CharacterRepository.Instance.TryGetCharacterIdByName(name, out CharacterId characterId) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_0D: '" + name
                + "' has no character id, ignored.", LogLevel.Trace);
            return;
        }

        if (IsReceiver(metadata, characterId) == false)
        {
            return;
        }

        if (GroupRepository.Instance.TryGetGroupOf(characterId, out Group? group) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_0D: '" + name + "' characterId=0x"
                + characterId.Value.ToString("X") + " is named group leader in raid id 0x"
                + raidId.Value.ToString("X") + " but is in no group.", LogLevel.Error);
            return;
        }

        group.SetLeader(characterId);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_0F -- handle the Z2C 0F subtype fields for OP_Raid_General (Member leaves group)
    //
    // Adds the member who left a group to the ungrouped members of the raid with the header's
    // raid id, acting only on the copy received by the member's own session.  A raid that is
    // not held is an error.
    //
    // raidId:    The raid id from the message header
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Handle_Z2C_0F(RaidId raidId, PacketMetadata metadata)
    {
        CollectionHandle Z2C_0F_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_0F");
        SlotId leavingCharName_Slot = _registry.IndexOfField(Z2C_0F_Collection, "Leaving_Character");
        SlotId characterId_Slot = _registry.IndexOfField(Z2C_0F_Collection, "Character_ID");

        CharacterId characterId = (CharacterId)_extractor.GetUIntAt(characterId_Slot);
        if (IsReceiver(metadata, characterId) == false)
        {
            return;
        }

        string name = _extractor.GetStringAt(leavingCharName_Slot);

        if (RaidRepository.Instance.TryGetRaid(raidId, out Raid? raid) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_0F: raid id 0x" + raidId.Value.ToString("X")
                + " is not held, '" + name + "' not changed.", LogLevel.Error);
            return;
        }

        if (raid.HasUngroupedMember(characterId))
        {
            return;
        }

        GroupMember member = new GroupMember();
        member.Id = characterId;
        member.Name = name;
        member.Character = metadata.Character;
        if ((metadata.Character != null) && metadata.Character.Level.HasValue)
        {
            member.Level = metadata.Character.Level.Value;
        }

        raid.AddUngroupedMember(member);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_10 -- handle the Z2C 10 subtype fields for OP_Raid_General (Member joins group)
    //
    // Removes the named member from the ungrouped members of the raid with the header's raid
    // id, and adds the member's group to the raid when the group is held and not yet in it,
    // acting only on the copy received by the member's own session.  A name with no character
    // id is ignored.  A raid that is not held is an error.
    //
    // raidId:    The raid id from the message header
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Handle_Z2C_10(RaidId raidId, PacketMetadata metadata)
    {
        CollectionHandle Z2C_10_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_10");
        SlotId name_Slot = _registry.IndexOfField(Z2C_10_Collection, "Name");

        string name = _extractor.GetStringAt(name_Slot);

        if (CharacterRepository.Instance.TryGetCharacterIdByName(name, out CharacterId characterId) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_10: '" + name
                + "' has no character id, ignored.", LogLevel.Trace);
            return;
        }

        if (IsReceiver(metadata, characterId) == false)
        {
            return;
        }

        if (RaidRepository.Instance.TryGetRaid(raidId, out Raid? raid) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_10: raid id 0x" + raidId.Value.ToString("X")
                + " is not held, '" + name + "' not changed.", LogLevel.Error);
            return;
        }

        if (raid.HasUngroupedMember(characterId))
        {
            raid.RemoveUngroupedMember(characterId);
        }

        if (GroupRepository.Instance.TryGetGroupOf(characterId, out Group? group) && (group.RaidId != raid.Id))
        {
            raid.AddGroup(group);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_11 -- handle the Z2C 11 subtype fields for OP_Raid_General (Request raid lock)
    //
    // Not yet acted upon; logs that the subtype was seen.
    //
    // raidId:    The raid id from the message header
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Handle_Z2C_11(RaidId raidId, PacketMetadata metadata)
    {
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_12 -- handle the Z2C 12 subtype fields for OP_Raid_General (Request raid unlock)
    //
    // Not yet acted upon; logs that the subtype was seen.
    //
    // raidId:    The raid id from the message header
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Handle_Z2C_12(RaidId raidId, PacketMetadata metadata)
    {
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_14 -- handle the Z2C 14 subtype fields for OP_Raid_General (Invite character to raid)
    //
    // Not yet acted upon; logs that the subtype was seen.  For this subtype the header value is
    // a ticket, not a raid id.
    // raidId:    The raid id from the message header
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Handle_Z2C_14(RaidId raidId, PacketMetadata metadata)
    {
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_16 -- handle the Z2C 16 subtype fields for OP_Raid_General (Invite group to raid)
    //
    // Not yet acted upon; logs that the subtype was seen.  For this subtype the header value is
    // a ticket, not a raid id.
    //
    // raidId:    The raid id from the message header
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Handle_Z2C_16(RaidId raidId, PacketMetadata metadata)
    {
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_18 -- handle the Z2C 18 subtype fields for OP_Raid_General (Return to raid)
    //
    // Not yet acted upon; logs that the subtype was seen.
    //
    // raidId:    The raid id from the message header
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Handle_Z2C_18(RaidId raidId, PacketMetadata metadata)
    {
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Handle_Z2C_1E -- handle the Z2C 1E subtype fields for OP_Raid_General (Raid leadership change)
    //
    // Makes the named character the leader of the raid with the header's raid id, acting only
    // on the copy received by that character's own session.  A name with no character id is
    // ignored.  A raid that is not held is an error.
    //
    // raidId:    The raid id from the message header
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Handle_Z2C_1E(RaidId raidId, PacketMetadata metadata)
    {
        CollectionHandle Z2C_1E_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_1E");
        SlotId name_Slot = _registry.IndexOfField(Z2C_1E_Collection, "New_Leader");

        string name = _extractor.GetStringAt(name_Slot);

        if (CharacterRepository.Instance.TryGetCharacterIdByName(name, out CharacterId characterId) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_1E: '" + name
                + "' has no character id, ignored.", LogLevel.Trace);
            return;
        }

        if (IsReceiver(metadata, characterId) == false)
        {
            return;
        }

        if (RaidRepository.Instance.TryGetRaid(raidId, out Raid? raid) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleRaid.Handle_Z2C_1E: raid id 0x" + raidId.Value.ToString("X")
                + " is not held, leader '" + name + "' not set.", LogLevel.Error);
            return;
        }

        raid.SetLeader(characterId);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_Raid against the active patch and builds a display tree: a root node for
    // the collection with one leaf child per field (short_name, long_name, zone_id), each
    // carrying its payload byte range.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldDisplayNode root = new FieldDisplayNode();

        try
        {
            _root_gate = _extractor.Extract(_top_level_gate, data);

            if (!_root_gate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleRaid:  No RootGate", LogLevel.Error);
                return root;
            }

            uint subtype = FieldNodes.AddUIntNode(_extractor, _subType_Slot, "Subtype", root);
            FieldNodes.AddStringNode(_extractor, _name_Slot, "Name", root);
            FieldNodes.AddUIntNode(_extractor, _raidId_Slot, "Raid ID or Ticket", root);

            string operation;

            switch (subtype)
            {
                case 0x0:  operation = "Add member to raid"; break;
                case 0x1:  operation = "Remove member from raid"; break;
                case 0x3:  operation = "Member change group"; break;
                case 0x5:  operation = "Disband raid"; break;
                case 0x6:  operation = "Raid roster"; break;
                case 0x8:  operation = "Form raid"; break;
                case 0xa:  operation = "Initialize raid"; break;
                case 0xd:  operation = "New Group Leader"; break;
                case 0xf:  operation = "Member leaves group"; break;
                case 0x10:  operation = "Member joins group"; break;
                case 0x11:  operation = "Request raid lock"; break;
                case 0x12:  operation = "Request raid unlock"; break;
                case 0x14:  operation = "Invite character to raid"; break;
                case 0x16:  operation = "Invite group to raid"; break;
                case 0x18:  operation = "Return to raid"; break;
                case 0x1E:  operation = "Raid leadership change"; break;
                default:    operation = "Unknown 0x" + subtype.ToString("X"); break;
            }
            root.Text = OpcodeName + " (" + operation + ")";

            DescribeAllSubtypes(root);
        }
        finally
        {
            _extractor.Release();
        }

        return root;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // DescribeAllSubtypes - Describe the one subtype collection present under the OP_Raid_General header
    //
    // Walks the subtype table and, for the first subtype whose gate holds data, enters the
    // subtype's first bag, calls the subtype's describe method, and returns to the root gate.
    // Exactly one subtype should be present in the data.
    //
    // parent:  The parent FDN where new data should be stored
    //
    // Returns:   Nothing
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void DescribeAllSubtypes(FieldDisplayNode parent)
    {
        foreach (SubtypeEntry entry in _subtypes)
        {
            if (_extractor.IsPresent(entry.GateSlot) == false)
            {
                continue;
            }

            GateHandle gate = _extractor.GetGateAt(entry.GateSlot);
            if (gate.Exists == false)
            {
                continue;
            }

            if (_extractor.BagCount(gate) == 0)
            {
                continue;
            }

            _extractor.EnterGate(gate, 0);
            entry.Describe(parent);
            _extractor.EnterGate(_root_gate, 0);
            return;
        }

        DebugLog.Write(LogChannel.Opcodes, "HandleRaid.DescribeAllSubtypes: no known subtype data present.",
            LogLevel.Warn);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_00 -- describe the Z2C 00 subtype fields for OP_Raid_General (Add Member)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // parent:    The parent FDN where data should be displayed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Describe_Z2C_00(FieldDisplayNode parent)
    {
        CollectionHandle Z2C_00_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_00");
        SlotId name_Slot = _registry.IndexOfField(Z2C_00_Collection, "Name");
        SlotId characterId_Slot = _registry.IndexOfField(Z2C_00_Collection, "CharacterID");
        SlotId position_Slot = _registry.IndexOfField(Z2C_00_Collection, "Position");
        SlotId class_Slot = _registry.IndexOfField(Z2C_00_Collection, "Class");
        SlotId level_Slot = _registry.IndexOfField(Z2C_00_Collection, "Level");
        SlotId isGroupLeader_Slot = _registry.IndexOfField(Z2C_00_Collection, "Is_Group_Leader");

        string name = FieldNodes.AddStringNode(_extractor, name_Slot, "New Member", parent);
        FieldNodes.AddUIntNode(_extractor, characterId_Slot, "Character ID", parent);
        FieldNodes.AddUIntNode(_extractor, position_Slot, "Position", parent);

        EQClass classValue = (EQClass)_extractor.GetUIntAt(class_Slot);

        FieldNodes.AddLabeledNode(_extractor, class_Slot, "Class: " + classValue.ToDisplayString() + " (" + classValue.ToString("X") +
            ")", parent);
        FieldNodes.AddUIntNode(_extractor, level_Slot, "Level", parent);
        FieldNodes.AddUIntNode(_extractor, isGroupLeader_Slot, "Is Group Leader", parent);

        parent.Text = OpcodeName + " (Add " + name + " to raid)";
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_01 -- describe the Z2C 01 subtype fields for OP_Raid_General (Remove from raid)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // parent:    The parent FDN where data should be displayed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Describe_Z2C_01(FieldDisplayNode parent)
    {
        CollectionHandle Z2C_01_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_01");
        SlotId name_Slot = _registry.IndexOfField(Z2C_01_Collection, "Name");

        string name = FieldNodes.AddStringNode(_extractor, name_Slot, "Name", parent);

        parent.Text = OpcodeName + " (Remove " + name + " from raid)";
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_03 -- describe the Z2C 03 subtype fields for OP_Raid_General
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // parent:    The parent FDN where data should be displayed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Describe_Z2C_03(FieldDisplayNode parent)
    {
        CollectionHandle Z2C_03_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_03");
        SlotId name_Slot = _registry.IndexOfField(Z2C_03_Collection, "Name");
        SlotId groupId_Slot = _registry.IndexOfField(Z2C_03_Collection, "GroupId");

        string name = FieldNodes.AddStringNode(_extractor, name_Slot, "Name", parent);
        int group = (int)FieldNodes.AddUIntNode(_extractor, groupId_Slot, "Group", parent, "X");

        parent.Text = OpcodeName + " (" + name + " changes to group " + group + ")";
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_05 -- describe the Z2C 05 subtype fields for OP_Raid_General (Disband Raid)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // parent:    The parent FDN where data should be displayed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Describe_Z2C_05(FieldDisplayNode parent)
    {
        CollectionHandle Z2C_01_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_01");
        SlotId name_Slot = _registry.IndexOfField(Z2C_01_Collection, "Name");

        string name = FieldNodes.AddStringNode(_extractor, name_Slot, "Name", parent);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_06 -- describe the Z2C 06 subtype fields for OP_Raid_General
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // parent:    The parent FDN where data should be displayed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Describe_Z2C_06(FieldDisplayNode parent)
    {
        CollectionHandle Z2C_06_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_Roster");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_06_Collection, "Unknown_1");
        SlotId unknown2_Slot = _registry.IndexOfField(Z2C_06_Collection, "Unknown_2");
        SlotId leaderName_Slot = _registry.IndexOfField(Z2C_06_Collection, "Leader_Name");
        SlotId memberCount_Slot = _registry.IndexOfField(Z2C_06_Collection, "Member_Count");
        SlotId roster_Slot = _registry.IndexOfField(Z2C_06_Collection, "Roster");


        FieldNodes.AddStringNode(_extractor, leaderName_Slot, "Leader", parent);
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown2_Slot, "Unknown 2", parent, "X");
        FieldNodes.AddUIntNode(_extractor, memberCount_Slot, "Member Count", parent, "D");

        CollectionHandle RosterEntries_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_Roster_Entry");
        SlotId entryMemberName_Slot = _registry.IndexOfField(RosterEntries_Collection, "Member_Name");
        SlotId entryUnknown1_Slot = _registry.IndexOfField(RosterEntries_Collection, "Unknown_1");
        SlotId entryClass_Slot = _registry.IndexOfField(RosterEntries_Collection, "Class");
        SlotId entryGroup_Slot = _registry.IndexOfField(RosterEntries_Collection, "Group_Number");
        SlotId entryLevel_Slot = _registry.IndexOfField(RosterEntries_Collection, "Level");
        SlotId entryIsRLeader_Slot = _registry.IndexOfField(RosterEntries_Collection, "Is_Raid_Leader");
        SlotId entryIsGLeader_Slot = _registry.IndexOfField(RosterEntries_Collection, "Is_Group_Leader");

        GateHandle Roster_Entries_Gate = _extractor.GetGateAt(roster_Slot);
        if (Roster_Entries_Gate.Exists)
        {
            uint membersSeen = 0;

            uint rosterBagCount = _extractor.BagCount(Roster_Entries_Gate);
            FieldDisplayNode rosterEntries = new FieldDisplayNode("Members");
            for (uint bagIndex = 0; bagIndex < rosterBagCount; bagIndex++)
            {
                _extractor.EnterGate(Roster_Entries_Gate, bagIndex);
                membersSeen++;

                string memberName = _extractor.GetStringAt(entryMemberName_Slot);
                EQClass classValue = (EQClass)_extractor.GetUIntAt(entryClass_Slot);

                FieldDisplayNode memberNode = new FieldDisplayNode(memberName);
                rosterEntries.AddChild(memberNode);

                FieldNodes.AddStringNode(_extractor, entryMemberName_Slot, "Member", memberNode);
                FieldNodes.AddLabeledNode(_extractor, entryClass_Slot, "Class: " + classValue.ToDisplayString() + " (0x" +
                       classValue.ToString("X") + ")", memberNode);
                FieldNodes.AddUIntNode(_extractor, entryLevel_Slot, "Level", memberNode, "D");
                FieldNodes.AddUIntNode(_extractor, entryGroup_Slot, "Group", memberNode, "D");
                FieldNodes.AddUIntNode(_extractor, entryIsRLeader_Slot, "Is Raid Leader", memberNode, "D");
                FieldNodes.AddUIntNode(_extractor, entryIsGLeader_Slot, "Is Group Leader", memberNode, "D");
                FieldNodes.AddUIntNode(_extractor, entryUnknown1_Slot, "Unknown 1", memberNode);
            }
            if (membersSeen > 0)
            {
                parent.AddChild(rosterEntries);
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_08 -- describe the Z2C 08 subtype fields for OP_Raid_General
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // parent:    The parent FDN where data should be displayed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Describe_Z2C_08(FieldDisplayNode parent)
    {
        CollectionHandle Z2C_08_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_08");
        SlotId name_Slot = _registry.IndexOfField(Z2C_08_Collection, "Name");
        SlotId characterID_Slot = _registry.IndexOfField(Z2C_08_Collection, "CharacterId");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_08_Collection, "Unknown1");

        FieldNodes.AddStringNode(_extractor, name_Slot, "Leader ", parent);
        FieldNodes.AddUIntNode(_extractor, characterID_Slot, "Character ID", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_0A -- describe the Z2C 0A subtype fields for OP_Raid_General (Initialize Raid)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // parent:    The parent FDN where data should be displayed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Describe_Z2C_0A(FieldDisplayNode parent)
    {
        CollectionHandle Z2C_0A_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_0A");
        SlotId name_Slot = _registry.IndexOfField(Z2C_0A_Collection, "Name");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_0A_Collection, "Unknown1");
        SlotId unknown2_Slot = _registry.IndexOfField(Z2C_0A_Collection, "Unknown2");

        FieldNodes.AddStringNode(_extractor, name_Slot, "Name", parent);
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown2_Slot, "Unknown 2", parent, "X");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_0D -- describe the Z2C 0D subtype fields for OP_Raid_General  (New group leader)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // parent:    The parent FDN where data should be displayed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Describe_Z2C_0D(FieldDisplayNode parent)
    {
        CollectionHandle Z2C_0D_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_0D");
        SlotId name_Slot = _registry.IndexOfField(Z2C_0D_Collection, "Name");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_0D_Collection, "Unknown1");
        SlotId group_Slot = _registry.IndexOfField(Z2C_0D_Collection, "Group_Number");

        string name = FieldNodes.AddStringNode(_extractor, name_Slot, "New Group Leader", parent);
        int group = (int)FieldNodes.AddUIntNode(_extractor, group_Slot, "Group", parent, "D");
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");

        parent.Text = OpcodeName + " (" + name + " is leader of group " + group + ")";
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_0F -- describe the Z2C 0F subtype fields for OP_Raid_General (Member Leaves Group)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // parent:    The parent FDN where data should be displayed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Describe_Z2C_0F(FieldDisplayNode parent)
    {
        CollectionHandle Z2C_0F_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_0F");
        SlotId name_Slot = _registry.IndexOfField(Z2C_0F_Collection, "Name");
        SlotId leaderId_Slot = _registry.IndexOfField(Z2C_0F_Collection, "Leader_ID");
        SlotId leavingCharName_Slot = _registry.IndexOfField(Z2C_0F_Collection, "Leaving_Character");
        SlotId characterId_Slot = _registry.IndexOfField(Z2C_0F_Collection, "Character_ID");

        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_0F_Collection, "Unknown1");
        SlotId unknown2_Slot = _registry.IndexOfField(Z2C_0F_Collection, "Unknown2");
        SlotId unknown3_Slot = _registry.IndexOfField(Z2C_0F_Collection, "Unknown3");

        FieldNodes.AddStringNode(_extractor, name_Slot, "Leader", parent);
        FieldNodes.AddUIntNode(_extractor, leaderId_Slot, "Leader ID", parent, "X");
        string name = FieldNodes.AddStringNode(_extractor, leavingCharName_Slot, "Leaving Character", parent);
        FieldNodes.AddUIntNode(_extractor, characterId_Slot, "Chararacter ID", parent, "X");

        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown2_Slot, "Unknown 2", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown3_Slot, "Unknown 3", parent, "X");

        parent.Text = OpcodeName + " (" + name + " leaves group)";
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_10 -- describe the Z2C 10 subtype fields for OP_Raid_General (Member Joins Group)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // parent:    The parent FDN where data should be displayed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Describe_Z2C_10(FieldDisplayNode parent)
    {
        CollectionHandle Z2C_10_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_10");
        SlotId name_Slot = _registry.IndexOfField(Z2C_10_Collection, "Name");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_10_Collection, "Unknown1");
        SlotId unknown2_Slot = _registry.IndexOfField(Z2C_10_Collection, "Unknown2");

        string name = FieldNodes.AddStringNode(_extractor, name_Slot, "Name", parent);
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown2_Slot, "Unknown 2", parent, "X");

        parent.Text = OpcodeName + " (" + name + " joins group)";
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_11 -- describe the Z2C 11 subtype fields for OP_Raid_General (Request lock)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // parent:    The parent FDN where data should be displayed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Describe_Z2C_11(FieldDisplayNode parent)
    {
        CollectionHandle Z2C_11_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_11");
        SlotId name_Slot = _registry.IndexOfField(Z2C_11_Collection, "Name");
        SlotId characterId_Slot = _registry.IndexOfField(Z2C_11_Collection, "CharacterId");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_11_Collection, "Unknown1");

        FieldNodes.AddStringNode(_extractor, name_Slot, "Locker", parent);
        FieldNodes.AddUIntNode(_extractor, characterId_Slot, "ID", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_12 -- describe the Z2C 12 subtype fields for OP_Raid_General (Request Unlock)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // parent:    The parent FDN where data should be displayed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Describe_Z2C_12(FieldDisplayNode parent)
    {
        CollectionHandle Z2C_12_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_12");
        SlotId name_Slot = _registry.IndexOfField(Z2C_12_Collection, "Name");
        SlotId characterId_Slot = _registry.IndexOfField(Z2C_12_Collection, "CharacterId");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_12_Collection, "Unknown1");

        FieldNodes.AddStringNode(_extractor, name_Slot, "Unlocker", parent);
        FieldNodes.AddUIntNode(_extractor, characterId_Slot, "ID", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_14 -- describe the Z2C 14 subtype fields for OP_Raid_General (Invite Character to Raid)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // parent:    The parent FDN where data should be displayed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Describe_Z2C_14(FieldDisplayNode parent)
    {
        CollectionHandle Z2C_14_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_14");
        SlotId name_Slot = _registry.IndexOfField(Z2C_14_Collection, "Character_Name");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_14_Collection, "Unknown1");
        SlotId unknown2_Slot = _registry.IndexOfField(Z2C_14_Collection, "Unknown2");

        FieldNodes.AddStringNode(_extractor, name_Slot, "Raid Leader", parent);
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown2_Slot, "Unknown 2", parent, "X");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_16 -- describe the Z2C 16 subtype fields for OP_Raid_General (Invite Group to Raid)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // parent:    The parent FDN where data should be displayed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Describe_Z2C_16(FieldDisplayNode parent)
    {
        CollectionHandle Z2C_16_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_16");
        SlotId name_Slot = _registry.IndexOfField(Z2C_16_Collection, "Name");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_16_Collection, "Unknown1");
        SlotId unknown2_Slot = _registry.IndexOfField(Z2C_16_Collection, "Unknown2");

        FieldNodes.AddStringNode(_extractor, name_Slot, "Raid Leader", parent);
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");
        FieldNodes.AddUIntNode(_extractor, unknown2_Slot, "Unknown 2", parent, "X");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_18 -- describe the Z2C 18 subtype fields for OP_Raid_General (Return to Raid)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // parent:    The parent FDN where data should be displayed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Describe_Z2C_18(FieldDisplayNode parent)
    {
        CollectionHandle Z2C_18_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_18");
        SlotId name_Slot = _registry.IndexOfField(Z2C_18_Collection, "Name");
        SlotId class_Slot = _registry.IndexOfField(Z2C_18_Collection, "Class");
        SlotId level_Slot = _registry.IndexOfField(Z2C_18_Collection, "Level");

        EQClass classValue = (EQClass)_extractor.GetUIntAt(class_Slot);

        FieldNodes.AddStringNode(_extractor, name_Slot, "Character", parent);
        FieldNodes.AddLabeledNode(_extractor, class_Slot, "Class: " + classValue.ToDisplayString() + " (0x" +
            classValue.ToString("X") + ")", parent);
        FieldNodes.AddUIntNode(_extractor, level_Slot, "Level", parent, "D");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe_Z2C_1E -- describe the Z2C 1E subtype fields for OP_Raid_General (Raid Leadership Change)
    //
    // Creates FieldDisplayNodes for the subtype data
    //
    // parent:    The parent FDN where data should be displayed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void Describe_Z2C_1E(FieldDisplayNode parent)
    {
        CollectionHandle Z2C_1E_Collection = _registry.GetCollectionHandle(_patchLevel, "Raid_Z2C_1E");
        SlotId name_Slot = _registry.IndexOfField(Z2C_1E_Collection, "New_Leader");
        SlotId unknown1_Slot = _registry.IndexOfField(Z2C_1E_Collection, "Unknown1");

        string name = FieldNodes.AddStringNode(_extractor, name_Slot, "New Leader", parent);
        FieldNodes.AddUIntNode(_extractor, unknown1_Slot, "Unknown 1", parent, "X");

        parent.Text = OpcodeName + " (" + name + " is new raid leader)";
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SubtypeEntry
    //
    // One OP_Raid_General subtype: the slot of the gate that selects the subtype's collection,
    // the method that acts on the subtype's data, and the method that describes it.  Both
    // methods are called with the extractor already inside the subtype's first bag.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private class SubtypeEntry
    {
        public readonly SlotId GateSlot;
        public readonly Action<RaidId, PacketMetadata> Handle;
        public readonly Action<FieldDisplayNode> Describe;

        ///////////////////////////////////////////////////////////////////////////////////////////
        // SubtypeEntry (constructor)
        //
        // Holds the gate slot and the two methods for one subtype.
        //
        // gateSlot:  The slot of the gate that selects the subtype's collection.
        // handle:    The method that acts on the subtype's data, given the header's raid id and the
        //            packet metadata.
        // describe:  The method that adds the subtype's fields to the given display node.
        ///////////////////////////////////////////////////////////////////////////////////////////
        public SubtypeEntry(SlotId gateSlot, Action<RaidId, PacketMetadata> handle, Action<FieldDisplayNode> describe)
        {
            GateSlot = gateSlot;
            Handle = handle;
            Describe = describe;
        }
    }

    private readonly List<SubtypeEntry> _subtypes = new List<SubtypeEntry>();
}
