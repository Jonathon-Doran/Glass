using Glass.Core;
using Glass.Core.Logging;
using Glass.Core.Signals;
using Glass.World;
using System.Diagnostics.CodeAnalysis;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// GroupRepository
//
// In-memory repository of the groups seen this run.  Groups are held in a dictionary keyed by GroupId, the
// server's group id, and each member's group is indexed by the member's persistent character id, so a
// character is in at most one group.  Nothing is read from or written to the database.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public class GroupRepository
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    private static GroupRepository? _instance = null;

    private readonly Dictionary<GroupId, Group> _groups;
    private readonly Dictionary<CharacterId, Group> _groupsByMember;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor.  The instance is created on first access with empty tables.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static GroupRepository Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new GroupRepository();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GroupRepository
    //
    // Private constructor.  Initializes empty tables.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private GroupRepository()
    {
        _groups = new Dictionary<GroupId, Group>();
        _groupsByMember = new Dictionary<CharacterId, Group>();

        // register the signal handlers
        GlassContext.SessionRegistry.AllSessionsDisconnected += Clear;
        GlassContext.SignalBus.Subscribe<SignalSessionRemoved>(OnSessionRemoved);

        DebugLog.Write(LogChannel.Database, "GroupRepository: singleton instance created with empty tables.",
            defaultLoglevel);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CreateGroup
    //
    // Creates an empty group with the given group id and stores it.  A group id of None, and a group id
    // already held, are each rejected and nothing changes.
    //
    // groupId:  The group id the server uses for the group.
    //
    // Returns the new group, or null if the group id was rejected.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Group? CreateGroup(GroupId groupId)
    {
        if (groupId.Exists == false)
        {
            DebugLog.Write(LogChannel.Database, "GroupRepository.CreateGroup: group id is None, no group created.",
                LogLevel.Error);
            return null;
        }

        if (_groups.ContainsKey(groupId))
        {
            DebugLog.Write(LogChannel.Database, "GroupRepository.CreateGroup: group id 0x"
                + groupId.Value.ToString("X") + " is already held, no group created.", LogLevel.Warn);
            return null;
        }

        Group group = new Group();
        group.Id = groupId;

        lock (GlassContext.MembershipLock)
        {
            _groups[group.Id] = group;
        }

        DebugLog.Write(LogChannel.Database, "GroupRepository.CreateGroup: created group id 0x"
            + groupId.Value.ToString("X") + ".", defaultLoglevel);
        return group;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AddMember
    //
    // Adds a member to a group and indexes the member's group by its persistent character id.  A group not
    // held by this repository, a member whose character id is None, and a member already in a group are
    // each rejected, and nothing changes.  A member the group itself refuses is not indexed.
    //
    // group:   The group receiving the member.
    // member:  The member to add.
    //
    // Returns true if the member was added, false if it was rejected.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool AddMember(Group group, GroupMember member)
    {
        if (_groups.ContainsKey(group.Id) == false)
        {
            DebugLog.Write(LogChannel.Database, "GroupRepository.AddMember: groupId=" + group.Id
                + " is not held here, '" + member.Name + "' not added.", LogLevel.Error);
            return false;
        }

        if (member.Id.Exists == false)
        {
            DebugLog.Write(LogChannel.Database, "GroupRepository.AddMember: '" + member.Name
                + "' has no character id, not added to groupId=" + group.Id + ".", LogLevel.Error);
            return false;
        }

        if (_groupsByMember.TryGetValue(member.Id, out Group? existing))
        {
            DebugLog.Write(LogChannel.Database, "GroupRepository.AddMember: '" + member.Name + "' characterId="
                + member.Id + " is already in groupId=" + existing.Id + ", not added to groupId=" + group.Id + ".",
                LogLevel.Warn);
            return false;
        }

        lock (GlassContext.MembershipLock)
        {
            _groupsByMember[member.Id] = group;
        }

        if (group.AddMember(member) == false)
        {
            lock (GlassContext.MembershipLock)
            {
                _groupsByMember.Remove(member.Id);
            }

            DebugLog.Write(LogChannel.General, "GroupRepository.AddMember: group id 0x" + group.Id.Value.ToString("X")
                + " refused '" + member.Name + "', index entry removed.", LogLevel.Warn);
            return false;
        }

        DebugLog.Write(LogChannel.Database, "GroupRepository.AddMember: indexed '" + member.Name + "' characterId="
            + member.Id + " to groupId=" + group.Id + ".", defaultLoglevel);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RemoveMember
    //
    // Removes the member with the given persistent character id from its group and from the index.  A
    // character in no group is rejected and nothing changes.  A group left with no members is kept.
    //
    // characterId:  The persistent character id of the member to remove.
    //
    // Returns true if the member was removed, false if the character was in no group.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool RemoveMember(CharacterId characterId)
    {
        if (_groupsByMember.TryGetValue(characterId, out Group? group) == false)
        {
            DebugLog.Write(LogChannel.Database, "GroupRepository.RemoveMember: characterId=" + characterId
                + " is in no group, nothing removed.", LogLevel.Warn);
            return false;
        }

        lock (GlassContext.MembershipLock)
        {
            _groupsByMember.Remove(characterId);
        }

        if (group.RemoveMember(characterId) == false)
        {
            DebugLog.Write(LogChannel.Database, "GroupRepository.RemoveMember: characterId=" + characterId
                + " was indexed to groupId=" + group.Id + " but the group does not hold it.", LogLevel.Error);
        }

        DebugLog.Write(LogChannel.Database, "GroupRepository.RemoveMember: removed characterId=" + characterId
            + " from the index for groupId=" + group.Id + ".", defaultLoglevel);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetGroupOf
    //
    // Looks up the group holding the member with the given persistent character id.
    //
    // characterId:  The persistent character id of the member.
    // group:        Receives the group if found, null otherwise.
    //
    // Returns true if the character is in a group, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool TryGetGroupOf(CharacterId characterId, [NotNullWhen(true)] out Group? group)
    {
        lock (GlassContext.MembershipLock)
        {
            if (_groupsByMember.TryGetValue(characterId, out group))
            {
                return true;
            }
        }

        DebugLog.Write(LogChannel.Database, "GroupRepository.TryGetGroupOf: characterId=" + characterId
            + " is in no group.", defaultLoglevel);
        return false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetGroup
    //
    // Looks up the group with the given group id.
    //
    // groupId:  The group id the server uses for the group.
    // group:    Receives the group if found, null otherwise.
    //
    // Returns true if the group is held, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool TryGetGroup(GroupId groupId, [NotNullWhen(true)] out Group? group)
    {
        Group? held;

        lock (GlassContext.MembershipLock)
        {
            _groups.TryGetValue(groupId, out held);
        }

        if (held != null)
        {
            group = held;
            DebugLog.Write(LogChannel.Database, "GroupRepository.TryGetGroup: group id 0x"
                + groupId.Value.ToString("X") + " is held.", defaultLoglevel);
            return true;
        }

        group = null;
        DebugLog.Write(LogChannel.Database, "GroupRepository.TryGetGroup: group id 0x"
            + groupId.Value.ToString("X") + " is not held.", defaultLoglevel);
        return false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAllGroups
    //
    // Returns every group held, in ascending group id order.  The list and the groups in it are copies,
    // independent of the groups held here.
    //
    // Returns the groups; empty when none are held.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<Group> GetAllGroups()
    {
        List<Group> liveGroups;
        lock (GlassContext.MembershipLock)
        {
            liveGroups = new List<Group>(_groups.Values);
        }

        List<Group> groups = new List<Group>();
        foreach (Group group in liveGroups)
        {
            groups.Add(group.Copy());
        }
        groups.Sort((Group left, Group right) => left.Id.Value.CompareTo(right.Id.Value));

        DebugLog.Write(LogChannel.Database, "GroupRepository.GetAllGroups: returning " + groups.Count + " groups.",
            defaultLoglevel);
        return groups;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnSessionRemoved
    //
    // Removes the character named by the signal from its group, since a character whose session has closed is no
    // longer present.  A character in no group is left as it is.
    //
    // signal:  The signal carrying the persistent character id of the character whose session closed.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void OnSessionRemoved(SignalSessionRemoved signal)
    {
        if (_groupsByMember.ContainsKey(signal.CharacterId) == false)
        {
            DebugLog.Write(LogChannel.Database, "GroupRepository.OnSessionRemoved: characterId=" + signal.CharacterId
                + " is in no group, nothing removed.", defaultLoglevel);
            return;
        }

        if (RemoveMember(signal.CharacterId) == false)
        {
            DebugLog.Write(LogChannel.Database, "GroupRepository.OnSessionRemoved: characterId=" + signal.CharacterId
                + " could not be removed from its group.", LogLevel.Error);
            return;
        }

        DebugLog.Write(LogChannel.Database, "GroupRepository.OnSessionRemoved: removed characterId=" + signal.CharacterId
            + " from its group, session " + signal.SessionId + " closed.", defaultLoglevel);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Clear
    //
    // Removes every group and every member index entry.  Group ids already issued are not reused.  A
    // membership change is published so that views of the groups rebuild.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Clear()
    {
        int groupCount = _groups.Count;

        lock (GlassContext.MembershipLock)
        {
            _groups.Clear();
            _groupsByMember.Clear();
        }

        GlassContext.SignalBus.Publish(new SignalMembershipChanged(GroupId.None, RaidId.None));

        DebugLog.Write(LogChannel.Database, "GroupRepository.Clear: removed " + groupCount + " groups.",
            defaultLoglevel);
    }
}
