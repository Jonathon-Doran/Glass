using Glass.Core;
using Glass.Core.Logging;
using Glass.Core.Signals;
using Glass.Data.Models;
using System;
using System.Collections.Generic;

namespace Glass.World;

///////////////////////////////////////////////////////////////////////////////////////////////
// GroupDesignation
//
// Assignments the game makes within one group or raid, such as leader or main tank.  These
// belong to the membership, not to the character: the same character can hold a designation
// in one group and none in the next.  A member may hold several at once.
///////////////////////////////////////////////////////////////////////////////////////////////
[Flags]
public enum GroupDesignation : uint
{
    None = 0,
    Leader = 1,
    MainTank = 2,
    MainAssist = 4
}

///////////////////////////////////////////////////////////////////////////////////////////////
// GroupMember
//
// One member of a group or raid, identified by the server's persistent character id, which
// is valid across zones.  A spawn id is not held here; incoming zone data tagged with a
// spawn id is translated to the member through the zone's placements.  Class and level are
// held for members that are not one of the user's characters.  Character refers to the
// user's own character when the member is one, and is null otherwise.
//
// Designations are assignments within this membership.  They are distinct from a
// character's primary role (tank, healer, melee dps, caster dps, crowd control), which is
// one per character, and from its capabilities (slow, mez, root, buff, and so on), which
// are a mask with several per character.  Neither role nor capabilities are held here.
//
// Membership is held for the run only and is never stored in the database.
///////////////////////////////////////////////////////////////////////////////////////////////
public class GroupMember
{
    public CharacterId Id;
    public string Name = string.Empty;
    public EQClass Class;
    public uint Level;
    public GroupDesignation Designations = GroupDesignation.None;
    public Character? Character = null;
}

///////////////////////////////////////////////////////////////////////////////////////////////
// Group
//
// A group of up to MaxMembers members.  Id is assigned by Glass and is valid for the run
// only.  A group in a raid carries the raid's id and its numbered position in the raid, 1
// to 12; a group outside a raid has RaidId None, and RaidGroupNumber is then 0, meaning no
// position.  Members are held as a list because a member's position within the group
// carries no meaning, only its presence.  The list is changed only through this class.
///////////////////////////////////////////////////////////////////////////////////////////////
public class Group
{
    public const int MaxMembers = 6;

    public GroupId Id;
    public RaidId RaidId = RaidId.None;
    public uint RaidGroupNumber = 0;

    private readonly List<GroupMember> _members = new List<GroupMember>();
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    public IReadOnlyList<GroupMember> Members
    {
        get { return _members; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddMember
    //
    // Adds a member to this group.  A member whose character id is already in the group, and
    // a group that already holds MaxMembers members, are each rejected and nothing changes.
    //
    // member:  The member to add.
    //
    // Returns true if the member was added, false if it was rejected.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool AddMember(GroupMember member)
    {
        if (HasMember(member.Id))
        {
            DebugLog.Write(LogChannel.Database, "Group.AddMember: groupId=" + Id + " already holds '"
                + member.Name + "' characterId=" + member.Id + ", not added.", LogLevel.Warn);
            return false;
        }

        if (_members.Count >= MaxMembers)
        {
            DebugLog.Write(LogChannel.Database, "Group.AddMember: groupId=" + Id + " is full, '"
                + member.Name + "' not added.", LogLevel.Warn);
            return false;
        }

        _members.Add(member);
        GlassContext.SignalBus.Publish(new SignalMembershipChanged(Id, RaidId));

        DebugLog.Write(LogChannel.Database, "Group.AddMember: added '" + member.Name + "' characterId="
            + member.Id + " to groupId=" + Id + ", " + _members.Count + " members.", defaultLoglevel);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RemoveMember
    //
    // Removes the member with the given persistent character id from this group.  A character
    // not in the group is rejected and nothing changes.
    //
    // characterId:  The persistent character id of the member to remove.
    //
    // Returns true if the member was removed, false if it was not in the group.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool RemoveMember(CharacterId characterId)
    {
        for (int i = 0; i < _members.Count; i++)
        {
            if (_members[i].Id == characterId)
            {
                _members.RemoveAt(i);
                DebugLog.Write(LogChannel.Database, "Group.RemoveMember: removed characterId=" + characterId
                    + " from groupId=" + Id + ", " + _members.Count + " members left.", defaultLoglevel);
                return true;
            }
        }

        DebugLog.Write(LogChannel.Database, "Group.RemoveMember: characterId=" + characterId
            + " is not in groupId=" + Id + ", nothing removed.", LogLevel.Warn);
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HasMember
    //
    // Tests whether this group holds a member with the given persistent character id.
    //
    // characterId:  The persistent character id to look for.
    //
    // Returns true if the group holds that member, false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool HasMember(CharacterId characterId)
    {
        for (int i = 0; i < _members.Count; i++)
        {
            if (_members[i].Id == characterId)
            {
                return true;
            }
        }
        return false;
    }
}
