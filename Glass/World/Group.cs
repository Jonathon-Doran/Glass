using Glass.Core;
using Glass.Core.Logging;
using Glass.Core.Signals;
using Glass.Data.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Glass.World;

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
    public ServerGroupId ServerGroupId = ServerGroupId.None;

    public CharacterId LeaderId = CharacterId.None;
    public CharacterId MainTankId = CharacterId.None;
    public CharacterId MainAssistId = CharacterId.None;

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
                GlassContext.SignalBus.Publish(new SignalMembershipChanged(Id, RaidId));

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

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TryGetMemberByName
    //
    // Looks up the member of this group with the given name.  Names are compared exactly.
    //
    // name:    The character name to look for.
    // member:  Receives the member if found, null otherwise.
    //
    // Returns true if the group holds a member with that name, false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool TryGetMemberByName(string name, [NotNullWhen(true)] out GroupMember? member)
    {
        for (int i = 0; i < _members.Count; i++)
        {
            if (string.Equals(_members[i].Name, name, StringComparison.Ordinal))
            {
                member = _members[i];
                return true;
            }
        }

        member = null;
        DebugLog.Write(LogChannel.Database, "Group.TryGetMemberByName: groupId=" + Id + " holds no member named '"
            + name + "'.", defaultLoglevel);
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetLeader
    //
    // Makes the member with the given persistent character id the leader of this group.  A
    // character not in the group is rejected and nothing changes.  When that member is
    // already the leader, nothing changes and no signal is published.
    //
    // characterId:  The persistent character id of the new leader.
    //
    // Returns true if the member is the leader afterward, false if it was not in the group.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool SetLeader(CharacterId characterId)
    {
        if (HasMember(characterId) == false)
        {
            DebugLog.Write(LogChannel.Database, "Group.SetLeader: characterId=" + characterId
                + " is not in groupId=" + Id + ", leader not changed.", LogLevel.Warn);
            return false;
        }

        if (LeaderId == characterId)
        {
            DebugLog.Write(LogChannel.Database, "Group.SetLeader: characterId=" + characterId
                + " is already leader of groupId=" + Id + ", nothing changed.", defaultLoglevel);
            return true;
        }

        CharacterId previousLeaderId = LeaderId;
        LeaderId = characterId;
        GlassContext.SignalBus.Publish(new SignalMembershipChanged(Id, RaidId));

        DebugLog.Write(LogChannel.Database, "Group.SetLeader: groupId=" + Id + " leader changed from characterId="
            + previousLeaderId + " to characterId=" + characterId + ".", defaultLoglevel);
        return true;
    }
}
