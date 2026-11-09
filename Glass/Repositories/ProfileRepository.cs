using Glass.Core.Logging;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ProfileRepository
//
// Loads and caches all data for a single named profile.
// All public methods query the in-memory cache — no database access after construction.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class ProfileRepository
{
    private static ProfileRepository? _instance = null;
    private Profile? _profile = null;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // The single ProfileRepository.  Created the first time it is requested.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static ProfileRepository Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new ProfileRepository();
            }

            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ProfileRepository (constructor)
    //
    // Creates the repository holding an empty, unnamed profile.  No database access.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private ProfileRepository()
    {
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadProfile
    //
    // Loads the named live profile from the database, including its slot assignments, and makes it the
    // profile held by this repository.  If that profile is already the one held, nothing is read.
    // If no profile with that name exists, an empty profile with that name is held, ready for saving.
    //
    // profileName:  The name of the profile to load.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void LoadProfile(string profileName)
    {
        if ((_profile != null) && (_profile.Name == profileName))
        {
            DebugLog.Write(LogChannel.Database, "ProfileRepository.LoadProfile: '" + profileName
                + "' is already loaded, not reading it again", LogLevel.Trace);
            return;
        }
        else if (_profile != null)
        {
            DebugLog.Write(LogChannel.Database, "ProfileRepository.LoadProfile: discarding the held profile '"
                + _profile.Name + "' before loading '" + profileName + "'", LogLevel.Trace);
            _profile = null;
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, machine_id, layout_id, ServerType, Server FROM Profiles WHERE name = @name AND deleted_at IS NULL";
        cmd.Parameters.AddWithValue("@name", profileName);

        using SqliteDataReader reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            _profile = new Profile
            {
                Id = (ProfileId) (uint) reader.GetInt32(0),
                Name = reader.GetString(1),
                MachineId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                LayoutId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                ServerType = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Server = reader.IsDBNull(5) ? string.Empty : reader.GetString(5)
            };
            reader.Close();

            using SqliteCommand slotsCmd = conn.CreateCommand();
            slotsCmd.CommandText = @"
                SELECT slot_number, character_id
                FROM ProfileSlots
                WHERE profile_id = @id
                ORDER BY slot_number";
            slotsCmd.Parameters.AddWithValue("@id", _profile.Id.Value);

            using SqliteDataReader slotsReader = slotsCmd.ExecuteReader();
            while (slotsReader.Read())
            {
                _profile.Slots.Add(new SlotAssignment
                {
                    SlotNumber = slotsReader.GetInt32(0),
                    DatabaseId = (DatabaseId) (uint) slotsReader.GetInt32(1)
                });
            }

            DebugLog.Write(LogChannel.Database, $"ProfileRepository: loaded. id={_profile.Id} layoutId={_profile.LayoutId} slots={_profile.Slots.Count}.", LogLevel.Info);
        }
        else
        {
            _profile = new Profile
            {
                Name = profileName
            };
            DebugLog.Write(LogChannel.Database, $"ProfileRepository: '{profileName}' not found, created empty.", LogLevel.Error);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CreateProfile
    //
    // Makes a new, empty profile with the given name the profile held by this repository.  Any
    // profile already held is discarded.  Nothing is read from or written to the database.
    //
    // profileName:  The name of the new profile.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void CreateProfile(string profileName)
    {
        if (_profile != null)
        {
            DebugLog.Write(LogChannel.Database, "ProfileRepository.CreateProfile: discarding the held profile '"
                + _profile.Name + "' before creating '" + profileName + "'", LogLevel.Trace);
        }

        _profile = new Profile
        {
            Name = profileName
        };

        DebugLog.Write(LogChannel.Database, "ProfileRepository.CreateProfile: created empty profile '"
            + profileName + "'", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DeleteProfile
    //
    // Marks the named profile as deleted by recording the deletion time.  The profile and the rows it
    // owns stay in the database so it can be restored.  If the named profile is the one held by this
    // repository, the held profile is discarded.
    //
    // profileName:  The name of the profile to delete.
    //
    // Returns:  True if a live profile with that name was marked deleted, false if none was found.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool DeleteProfile(string profileName)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Profiles SET deleted_at = datetime('now') WHERE name = @name AND deleted_at IS NULL";
        cmd.Parameters.AddWithValue("@name", profileName);
        int rowCount = cmd.ExecuteNonQuery();

        if (rowCount == 0)
        {
            DebugLog.Write(LogChannel.Database, "ProfileRepository.DeleteProfile: no live profile named '"
                + profileName + "', nothing deleted.", LogLevel.Warn);
            return false;
        }

        DebugLog.Write(LogChannel.Database, "ProfileRepository.DeleteProfile: marked '" + profileName
            + "' deleted.", LogLevel.Trace);

        if ((_profile != null) && (_profile.Name == profileName))
        {
            DebugLog.Write(LogChannel.Database, "ProfileRepository.DeleteProfile: discarding the held profile '"
                + profileName + "'.", LogLevel.Trace);
            _profile = null;
        }

        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RenameProfile
    //
    // Renames a live profile.  The new name must not be blank and must not be held by any profile,
    // live or deleted.  If the renamed profile is the one held by this repository, the held profile
    // takes the new name.
    //
    // oldName:  The current name of the live profile.
    // newName:  The name to give it.
    //
    // Returns:  True if the profile was renamed, false if the new name was blank or taken, or no live
    //           profile had the old name.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool RenameProfile(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            DebugLog.Write(LogChannel.Database, "ProfileRepository.RenameProfile: new name for '" + oldName
                + "' is blank, not renamed.", LogLevel.Warn);
            return false;
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM Profiles WHERE name = @newName";
        checkCmd.Parameters.AddWithValue("@newName", newName);
        long takenCount = (long)checkCmd.ExecuteScalar()!;

        if (takenCount > 0)
        {
            DebugLog.Write(LogChannel.Database, "ProfileRepository.RenameProfile: '" + newName
                + "' is already held by a profile, '" + oldName + "' not renamed.", LogLevel.Warn);
            return false;
        }

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Profiles SET name = @newName WHERE name = @oldName AND deleted_at IS NULL";
        cmd.Parameters.AddWithValue("@newName", newName);
        cmd.Parameters.AddWithValue("@oldName", oldName);
        int rowCount = cmd.ExecuteNonQuery();

        if (rowCount == 0)
        {
            DebugLog.Write(LogChannel.Database, "ProfileRepository.RenameProfile: no live profile named '"
                + oldName + "', nothing renamed.", LogLevel.Warn);
            return false;
        }

        DebugLog.Write(LogChannel.Database, "ProfileRepository.RenameProfile: renamed '" + oldName
            + "' to '" + newName + "'.", LogLevel.Trace);

        if ((_profile != null) && (_profile.Name == oldName))
        {
            _profile.Name = newName;
            DebugLog.Write(LogChannel.Database, "ProfileRepository.RenameProfile: held profile now named '"
                + newName + "'.", LogLevel.Trace);
        }

        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RestoreProfile
    //
    // Clears the deletion time of the named deleted profile, making it a live profile again with
    // everything it owned before it was deleted.
    //
    // profileName:  The name of the deleted profile to restore.
    //
    // Returns:  True if a deleted profile with that name was restored, false if none was found.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool RestoreProfile(string profileName)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Profiles SET deleted_at = NULL WHERE name = @name AND deleted_at IS NOT NULL";
        cmd.Parameters.AddWithValue("@name", profileName);
        int rowCount = cmd.ExecuteNonQuery();

        if (rowCount == 0)
        {
            DebugLog.Write(LogChannel.Database, "ProfileRepository.RestoreProfile: no deleted profile named '"
                + profileName + "', nothing restored.", LogLevel.Warn);
            return false;
        }

        DebugLog.Write(LogChannel.Database, "ProfileRepository.RestoreProfile: restored '" + profileName + "'.", LogLevel.Trace);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PurgeProfile
    //
    // Permanently removes the named deleted profile: its slot assignments, page associations and member
    // rows, then the profile row, in one transaction.  A live profile is never removed.
    //
    // profileName:  The name of the deleted profile to remove.
    //
    // Returns:  True if a deleted profile with that name was removed, false if none was found.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool PurgeProfile(string profileName)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();
        using SqliteTransaction tx = conn.BeginTransaction();

        try
        {
            using SqliteCommand idCmd = conn.CreateCommand();
            idCmd.Transaction = tx;
            idCmd.CommandText = "SELECT id FROM Profiles WHERE name = @name AND deleted_at IS NOT NULL";
            idCmd.Parameters.AddWithValue("@name", profileName);
            object? idValue = idCmd.ExecuteScalar();

            if (idValue == null)
            {
                DebugLog.Write(LogChannel.Database, "ProfileRepository.PurgeProfile: no deleted profile named '"
                    + profileName + "', nothing removed.", LogLevel.Warn);
                tx.Rollback();
                return false;
            }

            uint profileId = Convert.ToUInt32(idValue);
            DebugLog.Write(LogChannel.Database, "ProfileRepository.PurgeProfile: removing '" + profileName
                + "' profileId=" + profileId + ".", LogLevel.Trace);

            string[] ownedTables = { "ProfileSlots", "ProfilePages", "ProfileMembers" };
            foreach (string table in ownedTables)
            {
                using SqliteCommand deleteCmd = conn.CreateCommand();
                deleteCmd.Transaction = tx;
                deleteCmd.CommandText = "DELETE FROM " + table + " WHERE profile_id = @id";
                deleteCmd.Parameters.AddWithValue("@id", profileId);
                int rowCount = deleteCmd.ExecuteNonQuery();
                DebugLog.Write(LogChannel.Database, "ProfileRepository.PurgeProfile: removed " + rowCount
                    + " rows from " + table + ".", LogLevel.Trace);
            }

            using SqliteCommand profileCmd = conn.CreateCommand();
            profileCmd.Transaction = tx;
            profileCmd.CommandText = "DELETE FROM Profiles WHERE id = @id";
            profileCmd.Parameters.AddWithValue("@id", profileId);
            profileCmd.ExecuteNonQuery();

            tx.Commit();
            DebugLog.Write(LogChannel.Database, "ProfileRepository.PurgeProfile: removed '" + profileName + "'.", LogLevel.Trace);
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write(LogChannel.Database, "ProfileRepository.PurgeProfile: exception removing '" + profileName
                + "': " + ex.Message + ", rolling back.", LogLevel.Error);
            tx.Rollback();
            throw;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ProfileExists
    //
    // Reports whether a live profile has the given name; deleted profiles are not counted.
    // Does not change the  Does not change the profile held by this repository.
    //
    // profileName:  The profile name to look for.
    //
    // Returns:  True if a profile with that name is in the database, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool ProfileExists(string profileName)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Profiles WHERE name = @name AND deleted_at IS NULL";
        cmd.Parameters.AddWithValue("@name", profileName);

        long rowCount = (long)cmd.ExecuteScalar()!;
        bool exists = rowCount > 0;

        DebugLog.Write(LogChannel.Database, "ProfileRepository.ProfileExists: '" + profileName + "' "
            + (exists ? "exists" : "does not exist"), LogLevel.Trace);
        return exists;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DeletedProfileExists
    //
    // Reports whether a deleted profile holds the given name.  Does not change the profile held by
    // this repository.
    //
    // profileName:  The profile name to look for.
    //
    // Returns:  True if a deleted profile has that name, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool DeletedProfileExists(string profileName)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Profiles WHERE name = @name AND deleted_at IS NOT NULL";
        cmd.Parameters.AddWithValue("@name", profileName);

        long rowCount = (long)cmd.ExecuteScalar()!;
        bool exists = rowCount > 0;

        DebugLog.Write(LogChannel.Database, "ProfileRepository.DeletedProfileExists: '" + profileName + "' "
            + (exists ? "is held by a deleted profile" : "is not held by a deleted profile"), LogLevel.Trace);
        return exists;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadedProfile
    //
    // The profile held by this repository.  Throws InvalidOperationException when no profile has
    // been loaded, because using a profile before loading one is a programming error.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private Profile LoadedProfile
    {
        get
        {
            if (_profile == null)
            {
                DebugLog.Write(LogChannel.Database, "ProfileRepository.LoadedProfile: a profile was used before one was loaded",
                    LogLevel.Error);
                Environment.FailFast("ProfileRepository: a profile was used before one was loaded.");
            }

            return _profile;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetId
    //
    // Returns the ID of the loaded profile.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public ProfileId GetId()
    {
        return LoadedProfile.Id;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetLayoutId
    //
    // Returns the layout ID assigned to this profile, or null if none is assigned.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public int? GetLayoutId()
    {
        return LoadedProfile.LayoutId;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetLayoutId
    //
    // Sets the layout ID assigned to this profile.
    // Call Save() to persist the change to the database.
    //
    // layoutId:  The ID of the layout to assign, or null to clear the assignment.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void SetLayoutId(int? layoutId)
    {
        DebugLog.Write(LogChannel.Database, $"ProfileRepository.SetLayoutId: layoutId={layoutId?.ToString() ?? "null"}.", LogLevel.Trace);
        LoadedProfile.LayoutId = layoutId;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetMachineId
    //
    // Returns the machine ID assigned to this profile, or null if not assigned.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public int? GetMachineId()
    {
        return LoadedProfile.MachineId;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetMachineId
    //
    // Sets the machine ID on the cached profile.
    //
    // machineId:  The machine ID to assign.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void SetMachineId(int? machineId)
    {
        LoadedProfile.MachineId = machineId;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetSlots
    //
    // Returns a read-only view of the cached slot assignments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<SlotAssignment> GetSlots() => LoadedProfile.Slots.AsReadOnly();

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetSlots
    //
    // Replaces the slot assignment list held by this repository.
    //
    // slots:  The new list of slot assignments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void SetSlots(List<SlotAssignment> slots)
    {
        DebugLog.Write(LogChannel.Database, $"ProfileRepository.SetSlots: {slots.Count} slots.", LogLevel.Trace);
        LoadedProfile.Slots = slots;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetSlotForCharacter
    //
    // Returns the slot assignment for the given character ID, or null if not found.
    //
    // characterId:  The character to query.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public SlotAssignment? GetSlotForCharacter(DatabaseId characterId)
    {
        return LoadedProfile.Slots.FirstOrDefault(s => s.DatabaseId == characterId);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Save
    //
    // Persists the profile name, machine_id, layout_id, and slot assignments currently held by this
    // repository to the database.
    //
    // overwrite:  If true, replaces an existing profile with the same name.
    //             If false and the profile exists, returns -1 and makes no changes.
    //
    // Returns the profileId or None if the profile already exists and overwrite is false
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public ProfileId Save(bool overwrite = false)
    {
        string profileName = LoadedProfile.Name;
        DebugLog.Write(LogChannel.Database, $"ProfileRepository.Save: name='{profileName}' slots={LoadedProfile.Slots.Count} overwrite={overwrite}.", LogLevel.Trace);

        if (string.IsNullOrWhiteSpace(profileName))
        {
            DebugLog.Write(LogChannel.Database, "ProfileRepository.Save: profile name is empty, aborting.", LogLevel.Error);
            throw new InvalidOperationException("Profile name must be set before calling Save.");
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();
        using SqliteTransaction tx = conn.BeginTransaction();

        try
        {
            using SqliteCommand checkCmd = conn.CreateCommand();
            checkCmd.Transaction = tx;
            checkCmd.CommandText = "SELECT id FROM Profiles WHERE name = @name";
            checkCmd.Parameters.AddWithValue("@name", profileName);
            object? existingId = checkCmd.ExecuteScalar();

            ProfileId profileId;

            if (existingId != null)
            {
                if (!overwrite)
                {
                    DebugLog.Write(LogChannel.Database, "ProfileRepository.Save: profile exists and overwrite=false, returning -1.", LogLevel.Warn);
                    tx.Rollback();
                    return ProfileId.None;
                }

                profileId = (ProfileId) (uint) Convert.ToInt32(existingId);
                DebugLog.Write(LogChannel.Database, $"ProfileRepository.Save: overwriting profileId={profileId}, deleting existing slots.", LogLevel.Trace);

                using SqliteCommand deleteCmd = conn.CreateCommand();
                deleteCmd.Transaction = tx;
                deleteCmd.CommandText = "DELETE FROM ProfileSlots WHERE profile_id = @id";
                deleteCmd.Parameters.AddWithValue("@id", profileId.Value);
                deleteCmd.ExecuteNonQuery();

                using SqliteCommand updateCmd = conn.CreateCommand();
                updateCmd.Transaction = tx;
                updateCmd.CommandText = "UPDATE Profiles SET machine_id = @machineId, layout_id = @layoutId, ServerType = @serverType, Server = @server WHERE id = @id";
                updateCmd.Parameters.AddWithValue("@machineId", LoadedProfile.MachineId.HasValue ? LoadedProfile.MachineId.Value : DBNull.Value);
                updateCmd.Parameters.AddWithValue("@layoutId", LoadedProfile.LayoutId.HasValue ? LoadedProfile.LayoutId.Value : DBNull.Value);
                updateCmd.Parameters.AddWithValue("@serverType", LoadedProfile.ServerType);
                updateCmd.Parameters.AddWithValue("@server", LoadedProfile.Server);
                updateCmd.Parameters.AddWithValue("@id", profileId.Value);
                updateCmd.ExecuteNonQuery();

                DebugLog.Write(LogChannel.Database, $"ProfileRepository.Save: updated profile. machineId={LoadedProfile.MachineId} layoutId={LoadedProfile.LayoutId}.", LogLevel.Trace);
            }
            else
            {
                using SqliteCommand insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = "INSERT INTO Profiles (name, machine_id, layout_id, ServerType, Server) VALUES (@name, @machineId, @layoutId, @serverType, @server); SELECT last_insert_rowid();";
                insertCmd.Parameters.AddWithValue("@name", profileName);
                insertCmd.Parameters.AddWithValue("@machineId", LoadedProfile.MachineId.HasValue ? LoadedProfile.MachineId.Value : DBNull.Value);
                insertCmd.Parameters.AddWithValue("@layoutId", LoadedProfile.LayoutId.HasValue ? LoadedProfile.LayoutId.Value : DBNull.Value);
                insertCmd.Parameters.AddWithValue("@serverType", LoadedProfile.ServerType);
                insertCmd.Parameters.AddWithValue("@server", LoadedProfile.Server);
                profileId = (ProfileId)(uint)Convert.ToInt32(insertCmd.ExecuteScalar());

                DebugLog.Write(LogChannel.Database, $"ProfileRepository.Save: inserted new profile, profileId={profileId}. machineId={LoadedProfile.MachineId} layoutId={LoadedProfile.LayoutId}.", LogLevel.Trace);
            }

            foreach (SlotAssignment slot in LoadedProfile.Slots)
            {
                DebugLog.Write(LogChannel.Database, $"ProfileRepository.Save: slot {slot.SlotNumber} = characterId={slot.DatabaseId}.", LogLevel.Trace);

                using SqliteCommand insertSlot = conn.CreateCommand();
                insertSlot.Transaction = tx;
                insertSlot.CommandText = "INSERT INTO ProfileSlots (profile_id, slot_number, character_id) VALUES (@setId, @slotNumber, @charId)";
                insertSlot.Parameters.AddWithValue("@setId", profileId.Value);
                insertSlot.Parameters.AddWithValue("@slotNumber", slot.SlotNumber);
                insertSlot.Parameters.AddWithValue("@charId", slot.DatabaseId.Value);
                insertSlot.ExecuteNonQuery();
            }

            tx.Commit();
            LoadedProfile.Id = profileId;
            DebugLog.Write(LogChannel.Database, $"ProfileRepository.Save: committed. profileId={profileId}.", LogLevel.Trace);
            return profileId;
        }
        catch (Exception ex)
        {
            DebugLog.Write(LogChannel.Database, $"ProfileRepository.Save: exception: {ex.Message}, rolling back.", LogLevel.Error);
            tx.Rollback();
            throw;
        }
    }



    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetServerType
    //
    // Returns the server type for the active profile.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string GetServerType()
    {
        return LoadedProfile.ServerType;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetServer
    //
    // Returns the server name for the active profile.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string GetServer()
    {
        return LoadedProfile.Server;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetServerType
    //
    // Sets the server type for the active profile.
    //
    // serverType:  The server type string (Test, Live, Progression).
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void SetServerType(string serverType)
    {
        LoadedProfile.ServerType = serverType;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetServer
    //
    // Sets the server name for the active profile.
    //
    // server:  The server name string.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void SetServer(string server)
    {
        LoadedProfile.Server = server;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetSummaries
    //
    // Returns a summary of each live profile, or of each deleted profile, including its server and
    // server type, ordered alphabetically by name  The character count is the number of slot
    // assignments the profile has.
    //
    // deleted:  True to summarize deleted profiles, false to summarize live profiles.
    //
    // Returns:  One summary per matching profile.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static List<ProfileSummary> GetSummaries(bool deleted)
    {
        string deletedFilter = deleted ? "p.deleted_at IS NOT NULL" : "p.deleted_at IS NULL";

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.name, p.description, p.Server, p.ServerType, COUNT(ps.id)
            FROM Profiles p
            LEFT JOIN ProfileSlots ps ON ps.profile_id = p.id
            WHERE " + deletedFilter + @"
            GROUP BY p.id
            ORDER BY p.name";

        List<ProfileSummary> summaries = new List<ProfileSummary>();
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            summaries.Add(new ProfileSummary
            {
                Name = reader.GetString(0),
                Description = reader.GetString(1),
                Server = reader.GetString(2),
                ServerType = reader.GetString(3),
                CharacterCount = (uint)reader.GetInt64(4)
            });
        }

        DebugLog.Write(LogChannel.Database, "ProfileRepository.GetSummaries: deleted=" + deleted
            + " count=" + summaries.Count + ".", LogLevel.Trace);
        return summaries;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetDescription
    //
    // Stores a new description for the named profile, live or deleted.
    //
    // profileName:  The name of the profile to describe.
    // description:  The description text; leading and trailing white space is removed.
    //
    // Returns:  True if a profile with that name was updated, false if none was found.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool SetDescription(string profileName, string description)
    {
        string trimmed = description.Trim();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Profiles SET description = @description WHERE name = @name";
        cmd.Parameters.AddWithValue("@description", trimmed);
        cmd.Parameters.AddWithValue("@name", profileName);
        int rowCount = cmd.ExecuteNonQuery();

        if (rowCount == 0)
        {
            DebugLog.Write(LogChannel.Database, "ProfileRepository.SetDescription: no profile named '"
                + profileName + "', description not stored.", LogLevel.Warn);
            return false;
        }

        DebugLog.Write(LogChannel.Database, "ProfileRepository.SetDescription: '" + profileName
            + "' description set, length=" + trimmed.Length + ".", LogLevel.Trace);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAllNames
    //
    // Returns the names of all live profiles in the database, ordered alphabetically.  Deleted
    // profiles are not included.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static List<string> GetAllNames()
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM Profiles WHERE deleted_at IS NULL ORDER BY name";

        List<string> names = new List<string>();
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        DebugLog.Write(LogChannel.Database, "ProfileRepository.GetAllNames: " + names.Count + " live profiles.", LogLevel.Trace);
        return names;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetDeletedNames
    //
    // Returns the names of all deleted profiles in the database, ordered alphabetically.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static List<string> GetDeletedNames()
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM Profiles WHERE deleted_at IS NOT NULL ORDER BY name";

        List<string> names = new List<string>();
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        DebugLog.Write(LogChannel.Database, "ProfileRepository.GetDeletedNames: " + names.Count + " deleted profiles.", LogLevel.Trace);
        return names;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetDatabaseIds
    //
    // Returns the database ids assigned to slots in this profile.
    // Generated on demand from the cached slot list; not itself cached.
    // Cold-path usage only (e.g., feeding CharacterRepository.Load(profileId) at profile launch).
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<DatabaseId> GetDatabaseIds()
    {
        List<DatabaseId> databaseIds = LoadedProfile.Slots.Select(slot => slot.DatabaseId).ToList();
        return databaseIds;
    }
}