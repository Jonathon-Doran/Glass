using Glass.Core;
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
    private Profile _profile;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ProfileRepository
    //
    // Loads the named profile from the database, including its slot assignments.
    // If the profile does not exist, an empty profile is created ready for saving.
    //
    // profileName:  The name of the profile to load.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public ProfileRepository(string profileName)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, machine_id, layout_id FROM Profiles WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", profileName);

        using SqliteDataReader reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            _profile = new Profile
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                MachineId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                LayoutId = reader.IsDBNull(3) ? null : reader.GetInt32(3)
            };
            reader.Close();

            using SqliteCommand slotsCmd = conn.CreateCommand();
            slotsCmd.CommandText = @"
                SELECT slot_number, character_id
                FROM ProfileSlots
                WHERE profile_id = @id
                ORDER BY slot_number";
            slotsCmd.Parameters.AddWithValue("@id", _profile.Id);

            using SqliteDataReader slotsReader = slotsCmd.ExecuteReader();
            while (slotsReader.Read())
            {
                _profile.Slots.Add(new SlotAssignment
                {
                    SlotNumber = slotsReader.GetInt32(0),
                    CharacterId = slotsReader.GetInt32(1)
                });
            }

            DebugLog.Write(DebugLog.Log_Database, $"ProfileRepository: loaded. id={_profile.Id} layoutId={_profile.LayoutId} slots={_profile.Slots.Count}.");
        }
        else
        {
            _profile = new Profile
            {
                Name = profileName
            };
            DebugLog.Write(DebugLog.Log_Database, $"ProfileRepository: '{profileName}' not found, created empty.");
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetId
    //
    // Returns the ID of the loaded profile, or 0 if not found.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public int GetId()
    {
        return _profile?.Id ?? 0;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetLayoutId
    //
    // Returns the layout ID assigned to this profile, or null if none is assigned.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public int? GetLayoutId()
    {
        return _profile.LayoutId;
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
        DebugLog.Write(DebugLog.Log_Database, $"ProfileRepository.SetLayoutId: layoutId={layoutId?.ToString() ?? "null"}.");
        _profile.LayoutId = layoutId;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetMachineId
    //
    // Returns the machine ID assigned to this profile, or null if not assigned.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public int? GetMachineId()
    {
        return _profile.MachineId;
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
        _profile.MachineId = machineId;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetSlots
    //
    // Returns a read-only view of the cached slot assignments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<SlotAssignment> GetSlots() => _profile.Slots.AsReadOnly();

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetSlots
    //
    // Replaces the slot assignment list held by this repository.
    //
    // slots:  The new list of slot assignments.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void SetSlots(List<SlotAssignment> slots)
    {
        DebugLog.Write(DebugLog.Log_Database, $"ProfileRepository.SetSlots: {slots.Count} slots.");
        _profile.Slots = slots;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetSlotForCharacter
    //
    // Returns the slot assignment for the given character ID, or null if not found.
    //
    // characterId:  The character to query.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public SlotAssignment? GetSlotForCharacter(int characterId)
    {
        return _profile.Slots.FirstOrDefault(s => s.CharacterId == characterId);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Save
    //
    // Persists the profile name, slot assignments, and machine ID to the database.
    // Returns the profile ID on success, or -1 if the profile already exists and overwrite is false.
    //
    // overwrite:  If true, replaces an existing profile with the same name.
    //             If false and the profile exists, returns -1 and makes no changes.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public int Save(bool overwrite = false)
    {
        string profileName = _profile.Name;
        DebugLog.Write(DebugLog.Log_Database, $"ProfileRepository.Save: name='{profileName}' slots={_profile.Slots.Count} overwrite={overwrite}.");

        if (string.IsNullOrWhiteSpace(profileName))
        {
            DebugLog.Write(DebugLog.Log_Database, "ProfileRepository.Save: profile name is empty, aborting.");
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

            int profileId;
            if (existingId != null)
            {
                if (!overwrite)
                {
                    DebugLog.Write(DebugLog.Log_Database, "ProfileRepository.Save: profile exists and overwrite=false, returning -1.");
                    tx.Rollback();
                    return -1;
                }

                profileId = Convert.ToInt32(existingId);
                DebugLog.Write(DebugLog.Log_Database, $"ProfileRepository.Save: overwriting profileId={profileId}, deleting existing slots.");

                using SqliteCommand deleteCmd = conn.CreateCommand();
                deleteCmd.Transaction = tx;
                deleteCmd.CommandText = "DELETE FROM ProfileSlots WHERE profile_id = @id";
                deleteCmd.Parameters.AddWithValue("@id", profileId);
                deleteCmd.ExecuteNonQuery();

                using SqliteCommand updateCmd = conn.CreateCommand();
                updateCmd.Transaction = tx;
                updateCmd.CommandText = "UPDATE Profiles SET machine_id = @machineId WHERE id = @id";
                updateCmd.Parameters.AddWithValue("@machineId", _profile.MachineId.HasValue ? _profile.MachineId.Value : DBNull.Value);
                updateCmd.Parameters.AddWithValue("@id", profileId);
                updateCmd.ExecuteNonQuery();
            }
            else
            {
                using SqliteCommand insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = "INSERT INTO Profiles (name, machine_id) VALUES (@name, @machineId); SELECT last_insert_rowid();";
                insertCmd.Parameters.AddWithValue("@name", profileName);
                insertCmd.Parameters.AddWithValue("@machineId", _profile.MachineId.HasValue ? _profile.MachineId.Value : DBNull.Value);
                profileId = Convert.ToInt32(insertCmd.ExecuteScalar());
                DebugLog.Write(DebugLog.Log_Database, $"ProfileRepository.Save: inserted new profile, profileId={profileId}.");
            }

            foreach (SlotAssignment slot in _profile.Slots)
            {
                DebugLog.Write(DebugLog.Log_Database, $"ProfileRepository.Save: slot {slot.SlotNumber} = characterId={slot.CharacterId}.");

                using SqliteCommand insertSlot = conn.CreateCommand();
                insertSlot.Transaction = tx;
                insertSlot.CommandText = "INSERT INTO ProfileSlots (profile_id, slot_number, character_id) VALUES (@setId, @slotNumber, @charId)";
                insertSlot.Parameters.AddWithValue("@setId", profileId);
                insertSlot.Parameters.AddWithValue("@slotNumber", slot.SlotNumber);
                insertSlot.Parameters.AddWithValue("@charId", slot.CharacterId);
                insertSlot.ExecuteNonQuery();
            }

            tx.Commit();
            _profile.Id = profileId;
            DebugLog.Write(DebugLog.Log_Database, $"ProfileRepository.Save: committed. profileId={profileId}.");
            return profileId;
        }
        catch (Exception ex)
        {
            DebugLog.Write(DebugLog.Log_Database, $"ProfileRepository.Save: exception: {ex.Message}, rolling back.");
            tx.Rollback();
            throw;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAllNames
    //
    // Returns the names of all profiles in the database, ordered alphabetically.
    // Use this to populate profile list UI before constructing a full repository.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static List<string> GetAllNames()
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM Profiles ORDER BY name";

        List<string> names = new List<string>();
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}