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
// One member of a group or raid, identified by the character id, which
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
// A group's members.  A group in a raid carries the raid's id; a group outside a raid has
// RaidId None.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class Group
{
    public GroupId Id;
    public RaidId RaidId = RaidId.None;
    public uint RaidGroupNumber = 0;

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
    // Adds a member to this group.  A member whose character id is already in the group is
    // rejected and nothing changes.
    //
    // member:  The member to add.
    //
    // Returns true if the member was added, false if it was rejected.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool AddMember(GroupMember member)
    {
        if (HasMember(member.Id))
        {
            DebugLog.Write(LogChannel.General, "Group.AddMember: group id 0x" + Id.Value.ToString("X")
                + " already holds '" + member.Name + "' characterId=0x" + member.Id.Value.ToString("X")
                + ", not added.", LogLevel.Warn);
            return false;
        }

        lock (GlassContext.MembershipLock)
        {
            _members.Add(member);
        }

        GlassContext.SignalBus.Publish(new SignalMembershipChanged(Id, RaidId));

        DebugLog.Write(LogChannel.General, "Group.AddMember: added '" + member.Name + "' characterId=0x"
            + member.Id.Value.ToString("X") + " to group id 0x" + Id.Value.ToString("X") + ", "
            + _members.Count + " members.", defaultLoglevel);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RemoveMember
    //
    // Removes the member with the given character id from this group.  A character not in
    // the group is rejected and nothing changes.
    //
    // characterId:  The character id of the member to remove.
    //
    // Returns true if the member was removed, false if it was not in the group.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool RemoveMember(CharacterId characterId)
    {
        for (int i = 0; i < _members.Count; i++)
        {
            if (_members[i].Id == characterId)
            {
                lock (GlassContext.MembershipLock)
                {
                    _members.RemoveAt(i);
                }

                GlassContext.SignalBus.Publish(new SignalMembershipChanged(Id, RaidId));

                DebugLog.Write(LogChannel.General, "Group.RemoveMember: removed characterId=0x"
                    + characterId.Value.ToString("X") + " from group id 0x" + Id.Value.ToString("X") + ", "
                    + _members.Count + " members left.", defaultLoglevel);
                return true;
            }
        }

        DebugLog.Write(LogChannel.General, "Group.RemoveMember: characterId=0x" + characterId.Value.ToString("X")
            + " is not in group id 0x" + Id.Value.ToString("X") + ", nothing removed.", LogLevel.Warn);
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HasMember
    //
    // Tests whether this group holds a member with the given character id.
    //
    // characterId:  The character id to look for.
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
        DebugLog.Write(LogChannel.General, "Group.TryGetMemberByName: group id 0x" + Id.Value.ToString("X")
            + " holds no member named '" + name + "'.", defaultLoglevel);
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Copy
    //
    // Makes an independent copy of this group: its ids, and designations, and
    // a new member list holding a new copy of each member.  The copy shares no list or member
    // object with this group and publishes no membership change.  The copy is made while
    // holding the membership lock.
    //
    // Returns the copy.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public Group Copy()
    {
        Group copy = new Group();

        lock (GlassContext.MembershipLock)
        {
            copy.Id = Id;
            copy.RaidId = RaidId;
            copy.RaidGroupNumber = RaidGroupNumber;
            copy.LeaderId = LeaderId;
            copy.MainTankId = MainTankId;
            copy.MainAssistId = MainAssistId;

            foreach (GroupMember member in _members)
            {
                GroupMember memberCopy = new GroupMember();
                memberCopy.Id = member.Id;
                memberCopy.Name = member.Name;
                memberCopy.Class = member.Class;
                memberCopy.Level = member.Level;
                memberCopy.Character = member.Character;
                copy._members.Add(memberCopy);
            }
        }

        return copy;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetLeader
    //
    // Makes the member with the given character id the leader of this group.  A character not
    // in the group is rejected and nothing changes.  When that member is already the leader,
    // nothing changes and no signal is published.
    //
    // characterId:  The character id of the new leader.
    //
    // Returns true if the member is the leader afterward, false if it was not in the group.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool SetLeader(CharacterId characterId)
    {
        if (HasMember(characterId) == false)
        {
            DebugLog.Write(LogChannel.General, "Group.SetLeader: characterId=0x" + characterId.Value.ToString("X")
                + " is not in group id 0x" + Id.Value.ToString("X") + ", leader not changed.", LogLevel.Warn);
            return false;
        }

        if (LeaderId == characterId)
        {
            DebugLog.Write(LogChannel.General, "Group.SetLeader: characterId=0x" + characterId.Value.ToString("X")
                + " is already leader of group id 0x" + Id.Value.ToString("X") + ", nothing changed.",
                defaultLoglevel);
            return true;
        }

        CharacterId previousLeaderId = LeaderId;

        lock (GlassContext.MembershipLock)
        {
            LeaderId = characterId;
        }

        GlassContext.SignalBus.Publish(new SignalMembershipChanged(Id, RaidId));

        DebugLog.Write(LogChannel.General, "Group.SetLeader: group id 0x" + Id.Value.ToString("X")
            + " leader changed from characterId=0x" + previousLeaderId.Value.ToString("X") + " to characterId=0x"
            + characterId.Value.ToString("X") + ".", defaultLoglevel);
        return true;
    }
}
