using Glass.Core;
using Glass.Core.Logging;
using Glass.Core.Signals;
using System.Collections.Generic;

namespace Glass.World;

///////////////////////////////////////////////////////////////////////////////////////////////
// Raid
//
// A raid of up to MaxGroups groups, plus the raid members who are in no group.  Id is
// assigned by Glass and is valid for the run only.  Groups are held as an array indexed by
// raid group number minus one, because a group's number is part of how the raid is
// organized: a numbered group can be empty, and moving a member between groups changes the
// number they are under.  An empty position holds null.  The groups and the ungrouped
// members are changed only through this class.
///////////////////////////////////////////////////////////////////////////////////////////////
public class Raid
{
    public const uint MaxGroups = 12;
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    public RaidId Id;
    public CharacterId LeaderId = CharacterId.None;

    private readonly Group?[] _groups = new Group?[MaxGroups];
    private readonly List<GroupMember> _ungrouped = new List<GroupMember>();

    public IReadOnlyList<GroupMember> Ungrouped
    {
        get { return _ungrouped; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PlaceGroup
    //
    // Places a group at a numbered position in this raid and sets the group's RaidId and
    // RaidGroupNumber to match.  A number outside 1 to MaxGroups and an occupied position are
    // each rejected, and nothing changes.
    //
    // group:            The group to place.
    // raidGroupNumber:  The position in the raid, from 1.
    //
    // Returns true if the group was placed, false if it was rejected.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool PlaceGroup(Group group, uint raidGroupNumber)
    {
        if ((raidGroupNumber < 1) || (raidGroupNumber > MaxGroups))
        {
            DebugLog.Write(LogChannel.Database, "Raid.PlaceGroup: raid group number " + raidGroupNumber
                + " is outside 1 to " + MaxGroups + ", groupId=" + group.Id + " not placed in raidId="
                + Id + ".", LogLevel.Error);
            return false;
        }

        Group? occupant = _groups[raidGroupNumber - 1];
        if (occupant != null)
        {
            DebugLog.Write(LogChannel.Database, "Raid.PlaceGroup: raidId=" + Id + " group " + raidGroupNumber
                + " already holds groupId=" + occupant.Id + ", groupId=" + group.Id + " not placed.",
                LogLevel.Warn);
            return false;
        }

        lock (GlassContext.MembershipLock)
        {
            _groups[raidGroupNumber - 1] = group;
            group.RaidId = Id;
            group.RaidGroupNumber = raidGroupNumber;
        }

        GlassContext.SignalBus.Publish(new SignalMembershipChanged(group.Id, Id));

        DebugLog.Write(LogChannel.Database, "Raid.PlaceGroup: placed groupId=" + group.Id + " in raidId=" + Id
            + " as group " + raidGroupNumber + ".", defaultLoglevel);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RemoveGroup
    //
    // Removes a group from its numbered position in this raid and sets the group's RaidId to
    // None and its RaidGroupNumber to 0.  A group not at its recorded position in this raid is
    // rejected and nothing changes.  The group's members are left in the group.
    //
    // group:  The group to remove.
    //
    // Returns true if the group was removed, false if it was not in this raid.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool RemoveGroup(Group group)
    {
        uint raidGroupNumber = group.RaidGroupNumber;

        if ((group.RaidId != Id) || (raidGroupNumber < 1) || (raidGroupNumber > MaxGroups)
            || (ReferenceEquals(_groups[raidGroupNumber - 1], group) == false))
        {
            DebugLog.Write(LogChannel.Database, "Raid.RemoveGroup: groupId=" + group.Id + " with raidId="
                + group.RaidId + " group " + raidGroupNumber + " is not in raidId=" + Id + ", nothing removed.",
                LogLevel.Warn);
            return false;
        }

        lock (GlassContext.MembershipLock)
        {
            _groups[raidGroupNumber - 1] = null;
            group.RaidId = RaidId.None;
            group.RaidGroupNumber = 0;
        }

        GlassContext.SignalBus.Publish(new SignalMembershipChanged(group.Id, Id));

        DebugLog.Write(LogChannel.Database, "Raid.RemoveGroup: removed groupId=" + group.Id + " from raidId=" + Id
            + " group " + raidGroupNumber + ".", defaultLoglevel);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GroupAt
    //
    // Looks up the group at a numbered position in this raid.  A number outside 1 to MaxGroups
    // is logged at Error.
    //
    // raidGroupNumber:  The position in the raid, from 1.
    //
    // Returns the group at that position, or null when the position is empty or the number is
    // out of range.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public Group? GroupAt(uint raidGroupNumber)
    {
        if ((raidGroupNumber < 1) || (raidGroupNumber > MaxGroups))
        {
            DebugLog.Write(LogChannel.Database, "Raid.GroupAt: raid group number " + raidGroupNumber
                + " is outside 1 to " + MaxGroups + " in raidId=" + Id + ".", LogLevel.Error);
            return null;
        }

        return _groups[raidGroupNumber - 1];
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
            DebugLog.Write(LogChannel.Database, "Raid.AddUngroupedMember: raidId=" + Id + " already holds '"
                + member.Name + "' characterId=" + member.Id + " ungrouped, not added.", LogLevel.Warn);
            return false;
        }

        lock (GlassContext.MembershipLock)
        {
            _ungrouped.Add(member);
        }

        GlassContext.SignalBus.Publish(new SignalMembershipChanged(GroupId.None, Id));

        DebugLog.Write(LogChannel.Database, "Raid.AddUngroupedMember: added '" + member.Name + "' characterId="
            + member.Id + " to raidId=" + Id + ", " + _ungrouped.Count + " ungrouped.", defaultLoglevel);
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

                DebugLog.Write(LogChannel.Database, "Raid.RemoveUngroupedMember: removed characterId="
                    + characterId + " from raidId=" + Id + ", " + _ungrouped.Count + " ungrouped left.",
                    defaultLoglevel);
                return true;
            }
        }
        GlassContext.SignalBus.Publish(new SignalMembershipChanged(GroupId.None, Id));

        DebugLog.Write(LogChannel.Database, "Raid.RemoveUngroupedMember: characterId=" + characterId
            + " is not ungrouped in raidId=" + Id + ", nothing removed.", LogLevel.Warn);
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Copy
    //
    // Makes an independent copy of this raid: its id and leader, a copy of each placed group
    // at the same position, and a new ungrouped list holding a new copy of each ungrouped
    // member.  The copy shares no group, list or member object with this raid and publishes
    // no membership change.  The copy is made while holding the membership lock.
    //
    // Returns the copy.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public Raid Copy()
    {
        Raid copy = new Raid();
        copy.Id = Id;
        copy.LeaderId = LeaderId;

        for (uint position = 0; position < MaxGroups; position++)
        {
            Group? group = _groups[position];
            if (group != null)
            {
                copy._groups[position] = group.Copy();
            }
        }

        lock (GlassContext.MembershipLock)
        {
            foreach (GroupMember member in _ungrouped)
            {
                GroupMember memberCopy = new GroupMember();
                lock (GlassContext.MembershipLock)
                {
                    memberCopy.Id = member.Id;
                    memberCopy.Name = member.Name;
                    memberCopy.Class = member.Class;
                    memberCopy.Level = member.Level;
                    memberCopy.Character = member.Character;
                }
                copy._ungrouped.Add(memberCopy);
            }
        }

        DebugLog.Write(LogChannel.Database, "Raid.Copy: copied raidId=" + Id + " with "
            + copy._ungrouped.Count + " ungrouped members.", defaultLoglevel);
        return copy;
    }
}