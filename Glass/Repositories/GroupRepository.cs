using Glass.Core;
using Glass.Core.Logging;
using Glass.Core.Signals;
using Glass.World;
using System.Diagnostics.CodeAnalysis;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// GroupRepository
//
// In-memory repository of the groups seen this run.  Groups are held in a dictionary keyed by GroupId, and
// each member's group is indexed by the member's persistent character id, so a character is in at most one
// group.  Group ids are assigned here in sequence.  Nothing is read from or written to the database.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class GroupRepository
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    private static GroupRepository? _instance = null;

    private readonly Dictionary<GroupId, Group> _groups;
    private readonly Dictionary<CharacterId, Group> _groupsByMember;
    private readonly Dictionary<ServerGroupId, Group> _groupsByServerId;

    private const int MaxGroupMembers = 6;
    private uint _nextGroupId = 0;

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
        _groupsByServerId = new Dictionary<ServerGroupId, Group>();

        GlassContext.SessionRegistry.AllSessionsDisconnected += Clear;

        DebugLog.Write(LogChannel.Database, "GroupRepository: singleton instance created with empty tables.",
            defaultLoglevel);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CreateGroup
    //
    // Creates an empty group for the given server group id, with the next Glass group id in sequence, and
    // stores it under both ids.  A server group id of None, and a server group id already held, are each
    // rejected and nothing changes.
    //
    // serverId:  The group id the server uses for the group.
    //
    // Returns the new group, or null if the server group id was rejected.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Group? CreateGroup(ServerGroupId serverId)
    {
        if (serverId.Exists == false)
        {
            DebugLog.Write(LogChannel.Database, "GroupRepository.CreateGroup: server group id is None, "
                + "no group created.", LogLevel.Error);
            return null;
        }

        if (_groupsByServerId.TryGetValue(serverId, out Group? existing))
        {
            DebugLog.Write(LogChannel.Database, "GroupRepository.CreateGroup: server group id 0x"
                + serverId.Value.ToString("X") + " is already groupId=" + existing.Id + ", no group created.",
                LogLevel.Warn);
            return null;
        }

        Group group = new Group();
        group.Id = new GroupId(_nextGroupId);
        group.ServerGroupId = serverId;
        _nextGroupId++;

        _groups[group.Id] = group;
        _groupsByServerId[serverId] = group;
        GlassContext.SignalBus.Publish(new SignalMembershipChanged(group.Id, RaidId.None));

        DebugLog.Write(LogChannel.Database, "GroupRepository.CreateGroup: created groupId=" + group.Id
            + " for server group id 0x" + serverId.Value.ToString("X") + ".", defaultLoglevel);
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

        if (group.AddMember(member) == false)
        {
            DebugLog.Write(LogChannel.Database, "GroupRepository.AddMember: groupId=" + group.Id + " refused '"
                + member.Name + "', not indexed.", LogLevel.Warn);
            return false;
        }

        _groupsByMember[member.Id] = group;

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

        _groupsByMember.Remove(characterId);

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
        if (_groupsByMember.TryGetValue(characterId, out group))
        {
            return true;
        }

        DebugLog.Write(LogChannel.Database, "GroupRepository.TryGetGroupOf: characterId=" + characterId
            + " is in no group.", defaultLoglevel);
        return false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetGroupByServerId
    //
    // Looks up the group with the given server group id.
    //
    // serverId:  The group id the server uses for the group.
    // group:     Receives the group if found, null otherwise.
    //
    // Returns true if the group is held, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool TryGetGroupByServerId(ServerGroupId serverId, [NotNullWhen(true)] out Group? group)
    {
        if (_groupsByServerId.TryGetValue(serverId, out group))
        {
            DebugLog.Write(LogChannel.Database, "GroupRepository.TryGetGroupByServerId: server group id 0x"
                + serverId.Value.ToString("X") + " is groupId=" + group.Id + ".", defaultLoglevel);
            return true;
        }

        DebugLog.Write(LogChannel.Database, "GroupRepository.TryGetGroupByServerId: server group id 0x"
            + serverId.Value.ToString("X") + " is not held.", defaultLoglevel);
        return false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAllGroups
    //
    // Returns every group held, in ascending group id order.  The list is a new copy on every call; the
    // groups themselves are the live objects.
    //
    // Returns the groups; empty when none are held.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<Group> GetAllGroups()
    {
        List<Group> groups = new List<Group>(_groups.Values);
        groups.Sort((Group left, Group right) => left.Id.Value.CompareTo(right.Id.Value));

        DebugLog.Write(LogChannel.Database, "GroupRepository.GetAllGroups: returning " + groups.Count + " groups.",
            defaultLoglevel);
        return groups;
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

        _groups.Clear();
        _groupsByServerId.Clear();
        _groupsByMember.Clear();
        GlassContext.SignalBus.Publish(new SignalMembershipChanged(GroupId.None, RaidId.None));

        DebugLog.Write(LogChannel.Database, "GroupRepository.Clear: removed " + groupCount + " groups.",
            defaultLoglevel);
    }
}
