using Glass.Core;
using Glass.Core.Logging;
using Glass.Core.Signals;
using System.Collections.Generic;

namespace Glass.World;

///////////////////////////////////////////////////////////////////////////////////////////////
// Raid
//
// A raid's groups, plus the raid members who are in no group. 
///////////////////////////////////////////////////////////////////////////////////////////////
public class Raid
{
    public const uint MaxGroups = 12;
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    public RaidId Id;
    public CharacterId LeaderId = CharacterId.None;

    private readonly List<Group> _groups = new List<Group>();
    private readonly List<GroupMember> _ungrouped = new List<GroupMember>();

    public IReadOnlyList<Group> Groups
    {
        get { return _groups; }
    }

    public IReadOnlyList<GroupMember> Ungrouped
    {
        get { return _ungrouped; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddGroup
    //
    // Adds a group to this raid and sets the group's RaidId to match.  A group already in
    // this raid is rejected and nothing changes.
    //
    // group:  The group to add.
    //
    // Returns true if the group was added, false if it was rejected.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool AddGroup(Group group)
    {
        if (_groups.Contains(group))
        {
            DebugLog.Write(LogChannel.General, "Raid.AddGroup: raid id 0x" + Id.Value.ToString("X")
                + " already holds group id 0x" + group.Id.Value.ToString("X") + ", not added.", LogLevel.Warn);
            return false;
        }

        lock (GlassContext.MembershipLock)
        {
            _groups.Add(group);
            group.RaidId = Id;
        }

        GlassContext.SignalBus.Publish(new SignalMembershipChanged(group.Id, Id));

        DebugLog.Write(LogChannel.General, "Raid.AddGroup: added group id 0x" + group.Id.Value.ToString("X")
            + " to raid id 0x" + Id.Value.ToString("X") + ", " + _groups.Count + " groups.", defaultLoglevel);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RemoveGroup
    //
    // Removes a group from this raid and sets the group's RaidId to None.  A group not in this
    // raid is rejected and nothing changes.  The group's members are left in the group.
    //
    // group:  The group to remove.
    //
    // Returns true if the group was removed, false if it was not in this raid.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool RemoveGroup(Group group)
    {
        if (_groups.Contains(group) == false)
        {
            DebugLog.Write(LogChannel.General, "Raid.RemoveGroup: group id 0x" + group.Id.Value.ToString("X")
                + " is not in raid id 0x" + Id.Value.ToString("X") + ", nothing removed.", LogLevel.Warn);
            return false;
        }

        lock (GlassContext.MembershipLock)
        {
            _groups.Remove(group);
            group.RaidId = RaidId.None;
        }

        GlassContext.SignalBus.Publish(new SignalMembershipChanged(group.Id, Id));

        DebugLog.Write(LogChannel.General, "Raid.RemoveGroup: removed group id 0x" + group.Id.Value.ToString("X")
            + " from raid id 0x" + Id.Value.ToString("X") + ", " + _groups.Count + " groups left.", defaultLoglevel);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HasUngroupedMember
    //
    // Tests whether this raid holds an ungrouped member with the given persistent character id.
    //
    // characterId:  The persistent character id to look for.
    //
    // Returns true if the raid holds that ungrouped member, false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool HasUngroupedMember(CharacterId characterId)
    {
        for (int i = 0; i < _ungrouped.Count; i++)
        {
            if (_ungrouped[i].Id == characterId)
            {
                return true;
            }
        }
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddUngroupedMember
    //
    // Adds a member to this raid's ungrouped members.  A member whose character id is already
    // ungrouped in this raid is rejected and nothing changes.
    //
    // member:  The member to add.
    //
    // Returns true if the member was added, false if it was rejected.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool AddUngroupedMember(GroupMember member)
    {
        if (HasUngroupedMember(member.Id))
        {
            DebugLog.Write(LogChannel.General, "Raid.AddUngroupedMember: raid id 0x" + Id.Value.ToString("X")
                + " already holds '" + member.Name + "' characterId=0x" + member.Id.Value.ToString("X")
                + " ungrouped, not added.", LogLevel.Warn);
            return false;
        }

        lock (GlassContext.MembershipLock)
        {
            _ungrouped.Add(member);
        }

        GlassContext.SignalBus.Publish(new SignalMembershipChanged(GroupId.None, Id));

        DebugLog.Write(LogChannel.General, "Raid.AddUngroupedMember: added '" + member.Name + "' characterId=0x"
            + member.Id.Value.ToString("X") + " to raid id 0x" + Id.Value.ToString("X") + ", " + _ungrouped.Count
            + " ungrouped.", defaultLoglevel);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RemoveUngroupedMember
    //
    // Removes the ungrouped member with the given persistent character id from this raid.  A
    // character not ungrouped in this raid is rejected and nothing changes.
    //
    // characterId:  The persistent character id of the member to remove.
    //
    // Returns true if the member was removed, false if it was not ungrouped in this raid.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool RemoveUngroupedMember(CharacterId characterId)
    {
        for (int i = 0; i < _ungrouped.Count; i++)
        {
            if (_ungrouped[i].Id == characterId)
            {
                lock (GlassContext.MembershipLock)
                {
                    _ungrouped.RemoveAt(i);
                }

                GlassContext.SignalBus.Publish(new SignalMembershipChanged(GroupId.None, Id));

                DebugLog.Write(LogChannel.General, "Raid.RemoveUngroupedMember: removed characterId=0x"
                    + characterId.Value.ToString("X") + " from raid id 0x" + Id.Value.ToString("X") + ", "
                    + _ungrouped.Count + " ungrouped left.", defaultLoglevel);
                return true;
            }
        }

        DebugLog.Write(LogChannel.General, "Raid.RemoveUngroupedMember: characterId=0x"
            + characterId.Value.ToString("X") + " is not ungrouped in raid id 0x" + Id.Value.ToString("X")
            + ", nothing removed.", LogLevel.Warn);
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Copy
    //
    // Makes an independent copy of this raid: its id and leader, a copy of each group, and a
    // new ungrouped list holding a new copy of each ungrouped member.  The copy shares no
    // group, list or member object with this raid and publishes no membership change.  The
    // group list and the ungrouped members are read while holding the membership lock.
    //
    // Returns the copy.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public Raid Copy()
    {
        Raid copy = new Raid();
        copy.Id = Id;
        copy.LeaderId = LeaderId;

        List<Group> groups;

        lock (GlassContext.MembershipLock)
        {
            groups = new List<Group>(_groups);

            foreach (GroupMember member in _ungrouped)
            {
                GroupMember memberCopy = new GroupMember();
                memberCopy.Id = member.Id;
                memberCopy.Name = member.Name;
                memberCopy.Class = member.Class;
                memberCopy.Level = member.Level;
                memberCopy.Character = member.Character;
                copy._ungrouped.Add(memberCopy);
            }
        }

        foreach (Group group in groups)
        {
            copy._groups.Add(group.Copy());
        }

        DebugLog.Write(LogChannel.General, "Raid.Copy: copied raid id 0x" + Id.Value.ToString("X") + " with "
            + copy._groups.Count + " groups and " + copy._ungrouped.Count + " ungrouped members.", defaultLoglevel);
        return copy;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetLeader
    //
    // Makes the character with the given character id the leader of this raid.  When that
    // character is already the leader, nothing changes and no signal is published.
    //
    // characterId:  The character id of the new leader.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void SetLeader(CharacterId characterId)
    {
        if (LeaderId == characterId)
        {
            DebugLog.Write(LogChannel.General, "Raid.SetLeader: characterId=0x" + characterId.Value.ToString("X")
                + " is already leader of raid id 0x" + Id.Value.ToString("X") + ", nothing changed.",
                defaultLoglevel);
            return;
        }

        CharacterId previousLeaderId = LeaderId;

        lock (GlassContext.MembershipLock)
        {
            LeaderId = characterId;
        }

        GlassContext.SignalBus.Publish(new SignalMembershipChanged(GroupId.None, Id));

        DebugLog.Write(LogChannel.General, "Raid.SetLeader: raid id 0x" + Id.Value.ToString("X")
            + " leader changed from characterId=0x" + previousLeaderId.Value.ToString("X") + " to characterId=0x"
            + characterId.Value.ToString("X") + ".", defaultLoglevel);
    }
}