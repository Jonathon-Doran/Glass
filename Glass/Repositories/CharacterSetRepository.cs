using Glass.Core;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// CharacterSetRepository
//
// Loads and caches all data for a single named character set.
// All public methods query the in-memory cache — no database access after construction.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class CharacterSetRepository
{
    private CharacterSet _characterSet;
    private List<SlotAssignment> _slots;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CharacterSetRepository
    //
    // Loads an existing character set from the database if it exists, otherwise creates a new
    // empty one.  Populate with SetSlots() and call Save() to persist.
    //
    // profileName:  The name of the profile to load or create
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public CharacterSetRepository(string profileName)
    {
        DebugLog.Write(DebugLog.Log_Database, $"CharacterSetRepository: opening profile '{profileName}'");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM CharacterSets WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", profileName);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            _characterSet = new CharacterSet { Id = reader.GetInt32(0), Name = reader.GetString(1) };
            reader.Close();
            _slots = LoadSlots(_characterSet.Id);
            DebugLog.Write(DebugLog.Log_Database, $"CharacterSetRepository: loaded. id={_characterSet.Id} slots={_slots.Count}");
        }
        else
        {
            _characterSet = new CharacterSet { Name = profileName };
            _slots = new List<SlotAssignment>();
            DebugLog.Write(DebugLog.Log_Database, $"CharacterSetRepository: '{profileName}' not found, created empty.");
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetSlots
    //
    // Replaces the slot assignment list held by this repository.
    //
    // slots:  The new list of slot assignments
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void SetSlots(List<SlotAssignment> slots)
    {
        DebugLog.Write(DebugLog.Log_Database, $"CharacterSetRepository.SetSlots: {slots.Count} slots.");
        _slots = slots;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetSlots
    //
    // Returns a read-only view of the cached slot assignments.  Profile launching is a use-case.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<SlotAssignment> GetSlots() => _slots.AsReadOnly();

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetSlotForCharacter
    //
    // Returns the slot assignment for the given character name, or null if not found.
    //
    // characterName:  The character to query
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public SlotAssignment? GetSlotForCharacter(string characterName)
    {
        return _slots.FirstOrDefault(s => s.Character.Name == characterName);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Save
    //
    // Persists the character set name and slot assignments currently held by this repository
    // to the database.  Returns the profile ID on success, or -1 if the profile already
    // exists and overwrite is false.
    //
    // overwrite:  If true, replaces an existing profile with the same name.
    //             If false and the profile exists, returns -1 and makes no changes.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public int Save(bool overwrite = false)
    {
        string profileName = _characterSet.Name;

        DebugLog.Write(DebugLog.Log_Database, $"CharacterSetRepository.Save: name='{profileName}' slots={_slots.Count} overwrite={overwrite}");

        if (string.IsNullOrWhiteSpace(profileName))
        {
            DebugLog.Write(DebugLog.Log_Database, "CharacterSetRepository.Save: profile name is empty, aborting.");
            throw new InvalidOperationException("Profile name must be set before calling Save.");
        }

        using var conn = Database.Instance.Connect();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using var checkCmd = conn.CreateCommand();
            checkCmd.Transaction = tx;
            checkCmd.CommandText = "SELECT id FROM CharacterSets WHERE name = @name";
            checkCmd.Parameters.AddWithValue("@name", profileName);
            var existingId = checkCmd.ExecuteScalar();

            int profileId;
            if (existingId != null)
            {
                if (!overwrite)
                {
                    DebugLog.Write(DebugLog.Log_Database, "CharacterSetRepository.Save: profile exists and overwrite=false, returning -1.");
                    tx.Rollback();
                    return -1;
                }

                profileId = Convert.ToInt32(existingId);
                DebugLog.Write(DebugLog.Log_Database, $"CharacterSetRepository.Save: overwriting profileId={profileId}, deleting existing slots.");

                using var deleteCmd = conn.CreateCommand();
                deleteCmd.Transaction = tx;
                deleteCmd.CommandText = "DELETE FROM CharacterSetSlots WHERE character_set_id = @id";
                deleteCmd.Parameters.AddWithValue("@id", profileId);
                deleteCmd.ExecuteNonQuery();
            }
            else
            {
                using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = "INSERT INTO CharacterSets (name) VALUES (@name); SELECT last_insert_rowid();";
                insertCmd.Parameters.AddWithValue("@name", profileName);
                profileId = Convert.ToInt32(insertCmd.ExecuteScalar());
                DebugLog.Write(DebugLog.Log_Database, $"CharacterSetRepository.Save: inserted new profile, profileId={profileId}.");
            }

            foreach (var slot in _slots)
            {
                DebugLog.Write(DebugLog.Log_Database, $"CharacterSetRepository.Save: slot {slot.SlotNumber} = {slot.Character.Name} (id={slot.Character.Id}).");

                using var insertSlot = conn.CreateCommand();
                insertSlot.Transaction = tx;
                insertSlot.CommandText = "INSERT INTO CharacterSetSlots (character_set_id, slot_number, character_id) VALUES (@setId, @slotNumber, @charId)";
                insertSlot.Parameters.AddWithValue("@setId", profileId);
                insertSlot.Parameters.AddWithValue("@slotNumber", slot.SlotNumber);
                insertSlot.Parameters.AddWithValue("@charId", slot.Character.Id);
                insertSlot.ExecuteNonQuery();
            }

            tx.Commit();
            _characterSet.Id = profileId;
            DebugLog.Write(DebugLog.Log_Database, $"CharacterSetRepository.Save: committed. profileId={profileId}");
            return profileId;
        }
        catch (Exception ex)
        {
            DebugLog.Write(DebugLog.Log_Database, $"CharacterSetRepository.Save: exception: {ex.Message}, rolling back.");
            tx.Rollback();
            throw;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadCharacterSet
    //
    // Loads the character set record by name from the database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private CharacterSet LoadCharacterSet(string profileName)
    {
        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM CharacterSets WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", profileName);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException($"Character set '{profileName}' not found.");
        }

        return new CharacterSet
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1)
        };
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadSlots
    //
    // Loads all slot assignments for the given character set ID from the database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private List<SlotAssignment> LoadSlots(int characterSetId)
    {
        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT css.slot_number, c.id, c.name, c.class, c.account_id, c.progression, c.server
            FROM CharacterSetSlots css
            JOIN Characters c ON c.id = css.character_id
            WHERE css.character_set_id = @id
            ORDER BY css.slot_number";
        cmd.Parameters.AddWithValue("@id", characterSetId);

        var slots = new List<SlotAssignment>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            slots.Add(new SlotAssignment
            {
                SlotNumber = reader.GetInt32(0),
                Character = new Character
                {
                    Id = reader.GetInt32(1),
                    Name = reader.GetString(2),
                    Class = (EQClass)reader.GetInt32(3),
                    AccountId = reader.GetInt32(4),
                    Progression = reader.GetInt32(5) != 0,
                    Server = reader.GetString(6)
                }
            });
        }
        return slots;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAllNames
    //
    // Returns the names of all character sets in the database, ordered alphabetically.
    // Use this to populate profile list UI before constructing a full repository.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static List<string> GetAllNames()
    {
        DebugLog.Write(DebugLog.Log_Database, "CharacterSetRepository.GetAllNames: loading.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM CharacterSets ORDER BY name";

        var names = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        DebugLog.Write(DebugLog.Log_Database, $"CharacterSetRepository.GetAllNames: found {names.Count} profiles.");
        return names;
    }
}