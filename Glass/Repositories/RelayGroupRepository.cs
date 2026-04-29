using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// RelayGroupRepository
//
// Provides access to relay groups and their character membership in the database.
// Raises MembershipChanged when a character is added to or removed from a group.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class RelayGroupRepository
{
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAllGroups
    //
    // Returns all relay groups with their full membership populated, ordered alphabetically by name.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<RelayGroup> GetAllGroups()
    {
        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM RelayGroups ORDER BY name";

        var ids = new List<int>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetInt32(0));
        }
        reader.Close();

        var groups = new List<RelayGroup>();
        foreach (int id in ids)
        {
            RelayGroup? group = GetGroup(id);
            if (group != null)
            {
                groups.Add(group);
            }
        }

        return groups;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetGroup
    //
    // Returns the relay group with the given ID, including its members, or null if not found.
    //
    // groupId:  The group to load
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public RelayGroup? GetGroup(int groupId)
    {
        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM RelayGroups WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", groupId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            DebugLog.Write(LogChannel.Database, $"RelayGroupRepository.GetGroup: groupId={groupId} not found.");
            return null;
        }

        var group = new RelayGroup
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1)
        };
        reader.Close();

        group.Characters = GetMembers(groupId);
        return group;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAllGroupsForProfile
    //
    // Returns all relay groups with membership filtered to characters in the given profile.
    // Groups with no members in the profile are included but will have empty Characters lists.
    //
    // profileId:  The profile to filter membership by
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<RelayGroup> GetAllGroupsForProfile(int profileId)
    {
        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM RelayGroups ORDER BY name";

        List<RelayGroup> groups = new List<RelayGroup>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            groups.Add(new RelayGroup
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1)
            });
        }
        reader.Close();

        foreach (RelayGroup group in groups)
        {
            group.Characters = GetMembersForProfile(group.Id, profileId);
        }

        return groups;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetMembers
    //
    // Returns the characters that are members of the given relay group.
    //
    // groupId:  The group to query
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<Character> GetMembers(int groupId)
    {
        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT c.id, c.name, c.class, c.account_id, c.progression
            FROM Characters c
            INNER JOIN CharacterRelayGroups crg ON crg.character_id = c.id
            WHERE crg.relay_group_id = @groupId
            ORDER BY c.name";
        cmd.Parameters.AddWithValue("@groupId", groupId);

        var members = new List<Character>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            members.Add(new Character
            {
                CharacterId = reader.GetInt32(0),
                Name = reader.GetString(1),
                Class = (EQClass)reader.GetInt32(2),
                AccountId = reader.GetInt32(3),
                Progression = reader.GetInt32(4) != 0
            });
        }

        return members;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetMembersForProfile
    //
    // Returns characters that are members of the given relay group AND in the given profile.
    //
    // groupId:    The relay group to query
    // profileId:  The profile to filter by
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<Character> GetMembersForProfile(int groupId, int profileId)
    {
        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        SELECT c.id, c.name, c.class, c.account_id, c.progression
        FROM Characters c
        INNER JOIN CharacterRelayGroups crg ON crg.character_id = c.id
        INNER JOIN ProfileSlots ps ON ps.character_id = c.id
        WHERE crg.relay_group_id = @groupId
          AND ps.profile_id = @profileId
        ORDER BY c.name";
        cmd.Parameters.AddWithValue("@groupId", groupId);
        cmd.Parameters.AddWithValue("@profileId", profileId);

        List<Character> members = new List<Character>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            members.Add(new Character
            {
                CharacterId = reader.GetInt32(0),
                Name = reader.GetString(1),
                Class = (EQClass)reader.GetInt32(2),
                AccountId = reader.GetInt32(3),
                Progression = reader.GetInt32(4) != 0
            });
        }

        return members;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AddMember
    //
    // Adds a character to a relay group. Raises MembershipChanged on success.
    //
    // groupId:      The group to add to
    // characterId:  The character to add
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void AddMember(int groupId, int characterId)
    {
        DebugLog.Write(LogChannel.Database, $"RelayGroupRepository.AddMember: groupId={groupId} characterId={characterId}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR IGNORE INTO CharacterRelayGroups (relay_group_id, character_id)
            VALUES (@groupId, @characterId)";
        cmd.Parameters.AddWithValue("@groupId", groupId);
        cmd.Parameters.AddWithValue("@characterId", characterId);
        cmd.ExecuteNonQuery();

        DebugLog.Write(LogChannel.Database, $"RelayGroupRepository.AddMember: added. groupId={groupId} characterId={characterId}.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RemoveMember
    //
    // Removes a character from a relay group. Raises MembershipChanged on success.
    //
    // groupId:      The group to remove from
    // characterId:  The character to remove
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void RemoveMember(int groupId, int characterId)
    {
        DebugLog.Write(LogChannel.Database, $"RelayGroupRepository.RemoveMember: groupId={groupId} characterId={characterId}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM CharacterRelayGroups
            WHERE relay_group_id = @groupId AND character_id = @characterId";
        cmd.Parameters.AddWithValue("@groupId", groupId);
        cmd.Parameters.AddWithValue("@characterId", characterId);
        cmd.ExecuteNonQuery();

        DebugLog.Write(LogChannel.Database, $"RelayGroupRepository.RemoveMember: removed. groupId={groupId} characterId={characterId}.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CreateGroup
    //
    // Creates a new relay group with the given name.
    // Returns the new group ID.
    //
    // name:  The name of the new group
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public int CreateGroup(string name)
    {
        DebugLog.Write(LogChannel.Database, $"RelayGroupRepository.CreateGroup: name='{name}'.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO RelayGroups (name) VALUES (@name); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@name", name);
        int id = Convert.ToInt32(cmd.ExecuteScalar());

        DebugLog.Write(LogChannel.Database, $"RelayGroupRepository.CreateGroup: created. id={id}.");
        return id;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DeleteGroup
    //
    // Deletes the relay group with the given ID.
    // Throws InvalidOperationException if the group has members.
    //
    // groupId:  The group to delete
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void DeleteGroup(int groupId)
    {
        DebugLog.Write(LogChannel.Database, $"RelayGroupRepository.DeleteGroup: groupId={groupId}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM CharacterRelayGroups WHERE relay_group_id = @groupId";
        checkCmd.Parameters.AddWithValue("@groupId", groupId);
        int memberCount = Convert.ToInt32(checkCmd.ExecuteScalar());

        if (memberCount > 0)
        {
            DebugLog.Write(LogChannel.Database, $"RelayGroupRepository.DeleteGroup: groupId={groupId} has {memberCount} members, cannot delete.");
            throw new InvalidOperationException($"Cannot delete relay group {groupId} — it has {memberCount} members.");
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM RelayGroups WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", groupId);
        cmd.ExecuteNonQuery();

        DebugLog.Write(LogChannel.Database, $"RelayGroupRepository.DeleteGroup: deleted. groupId={groupId}.");
    }
}