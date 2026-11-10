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
// HandleGroupRoster
//
// Handles GroupRoster messages.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleGroupRoster : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _groupId_Slot;
    private readonly SlotId _memberCount_Slot;
    private readonly SlotId _leaderName_Slot;
    private readonly SlotId _rosterGate_Slot;

    private readonly SlotId _entryPosition_Slot;
    private readonly SlotId _entryName_Slot;
    private readonly SlotId _entryLevel_Slot;
    private readonly SlotId _entryTimestamp_Slot;

    private readonly SlotId _entryIsMainTank_Slot;
    private readonly SlotId _entryIsMainAssist_Slot;
    private readonly SlotId _entryIsPuller_Slot;
    private readonly SlotId _entryIsMarkNPC_Slot;

    private readonly SlotId _offline_Slot;
    private readonly SlotId _entryUnknown4_Slot;
    private readonly SlotId _entryUnknown5_Slot;
    private readonly SlotId _entryUnknown6_Slot;
    private readonly SlotId _entryUnknown7_Slot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleGroupRoster (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_GroupRoster from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_GroupRoster, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleGroupRoster(PatchLevel patchLevel)
        : base(patchLevel, "OP_Group_Roster")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        if (_opcodeHandled == PatchOpcode.None)
        {
            // happens when replaying captures before the opcode was added
            return;
        }

        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_Group_Roster");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _groupId_Slot = _registry.IndexOfField(_collectionHandle, "Group_ID");
        _memberCount_Slot = _registry.IndexOfField(_collectionHandle, "Member_Count");
        _leaderName_Slot = _registry.IndexOfField(_collectionHandle, "Leader_Name");
        _rosterGate_Slot = _registry.IndexOfField(_collectionHandle, "Roster");

        // Roster entry fields
        CollectionHandle RosterCollection = _registry.GetCollectionHandle(_patchLevel, "Group_Roster_Entry");
        _entryPosition_Slot = _registry.IndexOfField(RosterCollection, "Position");
        _entryName_Slot = _registry.IndexOfField(RosterCollection, "Name");
        _entryLevel_Slot = _registry.IndexOfField(RosterCollection, "Level");
        _entryTimestamp_Slot = _registry.IndexOfField(RosterCollection, "Timestamp");

        _entryIsMainTank_Slot = _registry.IndexOfField(RosterCollection, "Is_Main_Tank");
        _entryIsMainAssist_Slot = _registry.IndexOfField(RosterCollection, "Is_Main_Assist");
        _entryIsPuller_Slot = _registry.IndexOfField(RosterCollection, "Is_Puller");
        _entryIsMarkNPC_Slot = _registry.IndexOfField(RosterCollection, "Is_Mark_NPC");

        _offline_Slot = _registry.IndexOfField(RosterCollection, "Unknown_3");
        _entryUnknown4_Slot = _registry.IndexOfField(RosterCollection, "Unknown_4");
        _entryUnknown5_Slot = _registry.IndexOfField(RosterCollection, "Unknown_5");
        _entryUnknown6_Slot = _registry.IndexOfField(RosterCollection, "Unknown_6");
        _entryUnknown7_Slot = _registry.IndexOfField(RosterCollection, "Unknown_7");
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
    // Processes zone-to-client traffic.  Finds the group with the roster's server group id,
    // creating it when none is held, and adds each member whose Offline flag is clear.  A member
    // with the flag set is not present and is not added.  When the roster names a leader who is
    // in the group afterward, that member is made the leader.
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
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupRoster.HandleZoneToClient: No RootGate", LogLevel.Error);
                return;
            }

            ServerGroupId serverGroupId = (ServerGroupId)_extractor.GetUIntAt(_groupId_Slot);
            string leaderName = _extractor.GetStringAt(_leaderName_Slot);

            if (GroupRepository.Instance.TryGetGroupByServerId(serverGroupId, out Group? group) == false)
            {
                group = GroupRepository.Instance.CreateGroup(serverGroupId);
                if (group == null)
                {
                    DebugLog.Write(LogChannel.Opcodes, "HandleGroupRoster.HandleZoneToClient: no group for server group id 0x"
                        + serverGroupId.Value.ToString("X") + ", roster not applied.", LogLevel.Error);
                    return;
                }
            }

            GateHandle Roster_Entries_Gate = _extractor.GetGateAt(_rosterGate_Slot);
            if (Roster_Entries_Gate.Exists)
            {
                uint rosterBagCount = _extractor.BagCount(Roster_Entries_Gate);

                for (uint bagIndex = 0; bagIndex < rosterBagCount; bagIndex++)
                {
                    _extractor.EnterGate(Roster_Entries_Gate, bagIndex);

                    string name = _extractor.GetStringAt(_entryName_Slot);
                    uint level = _extractor.GetUIntAt(_entryLevel_Slot);
                    uint unknown_3 = _extractor.GetUIntAt(_offline_Slot);

                    if (unknown_3 != 0)
                    {
                        DebugLog.Write(LogChannel.Opcodes, "HandleGroupRoster.HandleZoneToClient: '" + name
                            + "' has Unknown 3 set to 0x" + unknown_3.ToString("X") + ", not present, not added to groupId="
                            + group.Id + ".", LogLevel.Trace);
                        continue;
                    }

                    AddRosterMember(group, name, level);
                }
            }
            else
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupRoster.HandleZoneToClient: roster for groupId=" + group.Id
                    + " has no member entries.", LogLevel.Warn);
            }

            if (leaderName.Length == 0)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupRoster.HandleZoneToClient: roster for groupId=" + group.Id
                    + " names no leader, leader not changed.", LogLevel.Trace);
                return;
            }

            if (CharacterRepository.Instance.TryGetCharacterIdByName(leaderName, out CharacterId leaderId) == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupRoster.HandleZoneToClient: leader '" + leaderName
                    + "' has no character id, leader of groupId=" + group.Id + " not changed.", LogLevel.Trace);
                return;
            }

            if (group.HasMember(leaderId) == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupRoster.HandleZoneToClient: leader '" + leaderName
                    + "' characterId=" + leaderId + " is not present in groupId=" + group.Id + ", leader not changed.",
                    LogLevel.Trace);
                return;
            }

            group.SetLeader(leaderId);

            DebugLog.Write(LogChannel.Opcodes, "HandleGroupRoster.HandleZoneToClient: groupId=" + group.Id + " leader is '"
                + leaderName + "' characterId=" + leaderId + ".", LogLevel.Trace);
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // AddRosterMember
    //
    // Puts one of the user's characters named in a roster entry into the given group.  The name
    // is converted to a persistent character id; a name with no character id is not one of the
    // user's characters and is skipped.  A character already in the group is left as it is.  A
    // character in a different group is removed from it first, since the roster is the server's
    // latest statement of membership.
    //
    // group:  The group the roster describes.
    // name:   The member name from the roster entry.
    // level:  The member level from the roster entry.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void AddRosterMember(Group group, string name, uint level)
    {
        if (CharacterRepository.Instance.TryGetCharacterIdByName(name, out CharacterId characterId) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleGroupRoster.AddRosterMember: '" + name
                + "' has no character id, not one of the user's characters, skipped.", LogLevel.Trace);
            return;
        }

        if (group.HasMember(characterId))
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleGroupRoster.AddRosterMember: '" + name + "' characterId="
                + characterId + " is already in groupId=" + group.Id + ", nothing changed.", LogLevel.Trace);
            return;
        }

        if (GroupRepository.Instance.TryGetGroupOf(characterId, out Group? oldGroup))
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleGroupRoster.AddRosterMember: '" + name + "' characterId="
                + characterId + " is in groupId=" + oldGroup.Id + " but the roster places it in groupId=" + group.Id
                + ", removing from groupId=" + oldGroup.Id + ".", LogLevel.Warn);

            if (GroupRepository.Instance.RemoveMember(characterId) == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupRoster.AddRosterMember: could not remove '" + name
                    + "' characterId=" + characterId + " from groupId=" + oldGroup.Id + ", not added to groupId="
                    + group.Id + ".", LogLevel.Error);
                return;
            }
        }

        GroupMember member = new GroupMember();
        member.Id = characterId;
        member.Name = name;
        member.Level = level;
        member.Character = CharacterRepository.Instance.GetByCharacterId(characterId);

        if (GroupRepository.Instance.AddMember(group, member) == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleGroupRoster.AddRosterMember: groupId=" + group.Id
                + " did not accept '" + name + "' characterId=" + characterId + ".", LogLevel.Warn);
            return;
        }

        DebugLog.Write(LogChannel.Opcodes, "HandleGroupRoster.AddRosterMember: added '" + name + "' characterId="
            + characterId + " level " + level + " to groupId=" + group.Id + ".", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_GroupRoster against the active patch and builds a display tree: a root node for
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

        uint groupId;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupRoster:  No RootGate", LogLevel.Error);
                return root;
            }

            groupId = FieldNodes.AddUIntNode(_extractor, _groupId_Slot, "Group", root);

            string leader = _extractor.GetStringAt(_leaderName_Slot);
            if (leader.Length == 0)
            {
                leader = "None";
            }
            FieldNodes.AddLabeledNode(_extractor, _leaderName_Slot, "Leader: " + leader, root);

            FieldDisplayNode rosterEntries = new FieldDisplayNode("Members");

            GateHandle Roster_Entries_Gate = _extractor.GetGateAt(_rosterGate_Slot);
            if (Roster_Entries_Gate.Exists)
            {
                uint membersSeen = 0;
                uint rosterBagCount = _extractor.BagCount(Roster_Entries_Gate);

                for (uint bagIndex = 0; bagIndex < rosterBagCount; bagIndex++)
                {
                    _extractor.EnterGate(Roster_Entries_Gate, bagIndex);
                    membersSeen++;

                    string name = _extractor.GetStringAt(_entryName_Slot);
                    FieldDisplayNode memberNode = new FieldDisplayNode(name);
                    rosterEntries.AddChild(memberNode);
                    if (name.Length == 0)
                    {
                        name = "None";
                    }
                    FieldNodes.AddLabeledNode(_extractor, _entryName_Slot, "Name: " + name, memberNode);
                    FieldNodes.AddUIntNode(_extractor, _entryPosition_Slot, "Position", memberNode, "D");
                    FieldNodes.AddUIntNode(_extractor, _entryLevel_Slot, "Level", memberNode, "D");
                    FieldNodes.AddUIntNode(_extractor, _offline_Slot, "Offline", memberNode, "D");
                    uint timestampValue = _extractor.GetUIntAt(_entryTimestamp_Slot);
                    string timestamp = DateTimeOffset.FromUnixTimeSeconds(timestampValue).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

                    if (timestampValue == 0)
                    {
                        timestamp = "None";
                    }

                    FieldNodes.AddLabeledNode(_extractor, _entryTimestamp_Slot, "Timestamp: " + timestamp, memberNode);

                    FieldNodes.AddBooleanNode(_extractor, _entryIsMainTank_Slot, "Is Main Tank", memberNode);
                    FieldNodes.AddBooleanNode(_extractor, _entryIsMainAssist_Slot, "Is Main Assist", memberNode);
                    FieldNodes.AddBooleanNode(_extractor, _entryIsPuller_Slot, "Is Puller", memberNode);
                    FieldNodes.AddBooleanNode(_extractor, _entryIsMarkNPC_Slot, "Is Mark NPC", memberNode);

                    FieldNodes.AddUIntNode(_extractor, _entryUnknown4_Slot, "Unknown 4", memberNode);
                    FieldNodes.AddUIntNode(_extractor, _entryUnknown5_Slot, "Unknown 5", memberNode);
                    FieldNodes.AddUIntNode(_extractor, _entryUnknown6_Slot, "Unknown 6", memberNode);
                    FieldNodes.AddUIntNode(_extractor, _entryUnknown7_Slot, "Unknown 7", memberNode);
                }

                if (membersSeen > 0)
                {
                    root.AddChild(rosterEntries);
                    string members = (membersSeen == 1) ? "Member" : "Members";

                    rosterEntries.Text = membersSeen.ToString() + " " + members;
                }
            }
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Group Roster (0x" + groupId.ToString("X4") + ")";
        return root;
    }
}