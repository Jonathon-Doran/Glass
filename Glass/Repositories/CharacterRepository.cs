using Glass.Core;
using Glass.Core.Logging;
using Glass.Data;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// CharacterRepository
//
// Loads and caches all characters from the database on construction.
// All public methods operate against the in-memory cache.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class CharacterRepository
{
    private static CharacterRepository? _instance = null;

    private readonly List<Character> _characters;
    private readonly Dictionary<int, Character> _charactersById;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor. The instance is created on first access with empty caches;
    // no database work is performed by the constructor. Call Load() or Load(profileId) to populate.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static CharacterRepository Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new CharacterRepository();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CharacterRepository
    //
    // Private constructor. Initializes empty caches. Does no database work.
    // Use CharacterRepository.Instance to obtain the singleton, then call Load() to populate from the
    // full Characters table or Load(idList) to populate a specific subset.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private CharacterRepository()
    {
        _characters = new List<Character>();
        _charactersById = new Dictionary<int, Character>();
        DebugLog.Write(LogChannel.Database, "CharacterRepository: singleton instance created with empty caches.");
    }

    // Returns all cached characters.
    public IReadOnlyList<Character> GetAll() => _characters.AsReadOnly();

    // Returns the character with the given name, or null if not found.
    public Character? GetByName(string name) => _characters.FirstOrDefault(c => c.Name == name);

    // Returns all characters belonging to the given account.
    public IReadOnlyList<Character> GetByAccount(int accountId) =>
        _characters.Where(c => c.AccountId == accountId).ToList().AsReadOnly();

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Load
    //
    // Loads characters from the database into the cache. Idempotent: characters already cached
    // are skipped, not re-read or duplicated.
    //
    // Cold path. Two usage modes:
    //   - Load(null) or Load() : loads all characters in the Characters table.
    //   - Load(idList)         : loads only the characters with the given ids. Caller typically
    //                            obtains the id list from ProfileRepository.GetCharacterIds().
    //
    // characterIds:  Optional list of character ids to load. If null, all characters are loaded.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Load(List<int>? characterIds = null)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        // Resolve the full id list when no list was supplied.
        if (characterIds == null)
        {
            characterIds = new List<int>();
            using SqliteCommand idsCmd = conn.CreateCommand();
            idsCmd.CommandText = "SELECT id FROM Characters";
            using SqliteDataReader idsReader = idsCmd.ExecuteReader();
            while (idsReader.Read())
            {
                characterIds.Add(idsReader.GetInt32(0));
            }
        }

        // Filter to ids not already cached.
        List<int> missingIds = new List<int>();
        foreach (int candidateId in characterIds)
        {
            if (!_charactersById.ContainsKey(candidateId))
            {
                missingIds.Add(candidateId);
            }
        }

        if (missingIds.Count == 0)
        {
            return;
        }

        // Build the WHERE id IN (...) clause with parameter placeholders.
        string idPlaceholders = string.Join(",", missingIds.Select((_, index) => "@id" + index));

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
        SELECT id, name, class, account_id, progression, server,
               level, practice_points, max_hp, max_mana,
               strength, stamina, charisma, dexterity,
               intelligence, agility, wisdom,
               platinum, gold, silver, copper
        FROM Characters
        WHERE id IN (" + idPlaceholders + @")
        ORDER BY account_id, name";

        for (int parameterIndex = 0; parameterIndex < missingIds.Count; parameterIndex++)
        {
            cmd.Parameters.AddWithValue("@id" + parameterIndex, missingIds[parameterIndex]);
        }

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            Character character = ReadCharacterRow(reader);
            _characters.Add(character);
            _charactersById[character.CharacterId] = character;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ReadCharacterRow
    //
    // Builds a Character from the current row of the supplied SqliteDataReader. The reader is expected
    // to have been opened with the canonical 21-column SELECT used by Load. Caller is responsible for
    // advancing the reader (this method does not call Read()).
    //
    // Persisted fields are populated from the reader. Ephemeral fields (SpawnId) remain null.
    // Nullable persisted columns produce null property values when the database column is NULL.
    //
    // reader:  The reader positioned on a row to convert.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private Character ReadCharacterRow(SqliteDataReader reader)
    {
        Character character = new Character
        {
            CharacterId = reader.GetInt32(0),
            Name = reader.GetString(1),
            Class = (EQClass)reader.GetInt32(2),
            AccountId = reader.GetInt32(3),
            Progression = reader.GetInt32(4) != 0,
            Server = reader.GetString(5),

            Level = reader.IsDBNull(6) ? null : (uint?) reader.GetInt32(6),
            PracticePoints = reader.IsDBNull(7) ? null : (uint?)reader.GetInt32(7),
            MaxHP = reader.IsDBNull(8) ? null : (uint?)reader.GetInt32(8),
            MaxMana = reader.IsDBNull(9) ? null : (uint?)reader.GetInt32(9),

            Strength = reader.IsDBNull(10) ? null : (uint?)reader.GetInt32(10),
            Stamina = reader.IsDBNull(11) ? null : (uint?)reader.GetInt32(11),
            Charisma = reader.IsDBNull(12) ? null : (uint?)reader.GetInt32(12),
            Dexterity = reader.IsDBNull(13) ? null : (uint?)reader.GetInt32(13),
            Intelligence = reader.IsDBNull(14) ? null : (uint?)reader.GetInt32(14),
            Agility = reader.IsDBNull(15) ? null : (uint?)reader.GetInt32(15),
            Wisdom = reader.IsDBNull(16) ? null : (uint?)reader.GetInt32(16),

            Platinum = reader.IsDBNull(17) ? null : (uint?)reader.GetInt32(17),
            Gold = reader.IsDBNull(18) ? null : (uint?)reader.GetInt32(18),
            Silver = reader.IsDBNull(19) ? null : (uint?)reader.GetInt32(19),
            Copper = reader.IsDBNull(20) ? null : (uint?)reader.GetInt32(20)
        };

        return character;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Add
    //
    // Inserts a new character into the database and updates the in-memory cache.  Note that most fields
    // are initially null and will be set via the network.
    //
    // character:  The character to add.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Add(Character character)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Characters (name, class, account_id, server, progression) VALUES (@name, @class, @accountId, @server, @progression); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@name", character.Name);
        cmd.Parameters.AddWithValue("@class", (int)character.Class);
        cmd.Parameters.AddWithValue("@accountId", character.AccountId);
        cmd.Parameters.AddWithValue("@server", character.Server);
        cmd.Parameters.AddWithValue("@progression", character.Progression ? 1 : 0);

        character.CharacterId = Convert.ToInt32(cmd.ExecuteScalar());
        _characters.Add(character);

        DebugLog.Write(LogChannel.Database, "CharacterRepository.Add: added character="
            + character.Name + " id=" + character.CharacterId
            + " server=" + character.Server
            + " class=" + character.Class
            + " accountId=" + character.AccountId);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Save
//
// Persists the cached character's full row to the database. The cache is the source of truth;
// callers mutate the cached Character reference directly and call Save to persist.
//
// Called by:
//   - Profile Editor flows after the user edits a cached character.
//   - Session disconnect handler to capture packet-sourced state at session end.
//
// characterId:  The persistent Glass character id of the character to save.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public void Save(int characterId)
{
    if (!_charactersById.TryGetValue(characterId, out Character? character))
    {
        DebugLog.Write(LogChannel.Database, $"CharacterRepository.Save: characterId={characterId} not in cache, ignoring.");
        return;
    }

    using SqliteConnection conn = Database.Instance.Connect();
    conn.Open();

    using SqliteCommand cmd = conn.CreateCommand();
    cmd.CommandText = @"
        UPDATE Characters SET
            name = @name,
            class = @class,
            account_id = @accountId,
            server = @server,
            progression = @progression,
            level = @level,
            practice_points = @practicePoints,
            max_hp = @maxHp,
            max_mana = @maxMana,
            strength = @strength,
            stamina = @stamina,
            charisma = @charisma,
            dexterity = @dexterity,
            intelligence = @intelligence,
            agility = @agility,
            wisdom = @wisdom,
            platinum = @platinum,
            gold = @gold,
            silver = @silver,
            copper = @copper
        WHERE id = @id";

    cmd.Parameters.AddWithValue("@name", character.Name);
    cmd.Parameters.AddWithValue("@class", (int)character.Class);
    cmd.Parameters.AddWithValue("@accountId", character.AccountId);
    cmd.Parameters.AddWithValue("@server", character.Server);
    cmd.Parameters.AddWithValue("@progression", character.Progression ? 1 : 0);

    cmd.Parameters.AddWithValue("@level",          character.Level.HasValue          ? character.Level.Value          : DBNull.Value);
    cmd.Parameters.AddWithValue("@practicePoints", character.PracticePoints.HasValue ? character.PracticePoints.Value : DBNull.Value);
    cmd.Parameters.AddWithValue("@maxHp",          character.MaxHP.HasValue          ? character.MaxHP.Value          : DBNull.Value);
    cmd.Parameters.AddWithValue("@maxMana",        character.MaxMana.HasValue        ? character.MaxMana.Value        : DBNull.Value);

    cmd.Parameters.AddWithValue("@strength",     character.Strength.HasValue     ? character.Strength.Value     : DBNull.Value);
    cmd.Parameters.AddWithValue("@stamina",      character.Stamina.HasValue      ? character.Stamina.Value      : DBNull.Value);
    cmd.Parameters.AddWithValue("@charisma",     character.Charisma.HasValue     ? character.Charisma.Value     : DBNull.Value);
    cmd.Parameters.AddWithValue("@dexterity",    character.Dexterity.HasValue    ? character.Dexterity.Value    : DBNull.Value);
    cmd.Parameters.AddWithValue("@intelligence", character.Intelligence.HasValue ? character.Intelligence.Value : DBNull.Value);
    cmd.Parameters.AddWithValue("@agility",      character.Agility.HasValue      ? character.Agility.Value      : DBNull.Value);
    cmd.Parameters.AddWithValue("@wisdom",       character.Wisdom.HasValue       ? character.Wisdom.Value       : DBNull.Value);

    cmd.Parameters.AddWithValue("@platinum", character.Platinum.HasValue ? character.Platinum.Value : DBNull.Value);
    cmd.Parameters.AddWithValue("@gold",     character.Gold.HasValue     ? character.Gold.Value     : DBNull.Value);
    cmd.Parameters.AddWithValue("@silver",   character.Silver.HasValue   ? character.Silver.Value   : DBNull.Value);
    cmd.Parameters.AddWithValue("@copper",   character.Copper.HasValue   ? character.Copper.Value   : DBNull.Value);

    cmd.Parameters.AddWithValue("@id", character.CharacterId);

    cmd.ExecuteNonQuery();

    DebugLog.Write(LogChannel.Database, "CharacterRepository.Save: persisted characterId=" + character.CharacterId
        + " name=" + character.Name
        + " class=" + character.Class
        + " level=" + (character.Level?.ToString() ?? "null")
        + " maxHp=" + (character.MaxHP?.ToString() ?? "null")
        + " maxMana=" + (character.MaxMana?.ToString() ?? "null"));
}

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetById
    //
    // Hot-path lookup. Returns the cached Character with the given persistent Glass character id,
    // or null if not found.
    //
    // characterId:  The persistent Glass character id.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Character? GetById(int characterId)
    {
        if (_charactersById.TryGetValue(characterId, out Character? character))
        {
            return character;
        }
        return null;
    }
}