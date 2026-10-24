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
    private readonly Dictionary<DatabaseId, Character> _charactersByDatabaseId;
    private readonly Dictionary<CharacterId, Character> _charactersByCharacterId;
    private readonly Dictionary<ZoneId, Dictionary<SpawnId, Character>> _charactersByZoneSpawn;

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
        _charactersByCharacterId = new Dictionary<CharacterId, Character>();
        _charactersByDatabaseId = new Dictionary<DatabaseId, Character>();
        _charactersByZoneSpawn = new Dictionary<ZoneId, Dictionary<SpawnId, Character>>();
        DebugLog.Write(LogChannel.Database, "CharacterRepository: singleton instance created with empty caches.", LogLevel.Trace);
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
    //   - Load(idList)         : loads only the characters with the given ids.
    //
    // databaseIds:  Optional list of Character IDs to load. If null, all characters are loaded.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Load(List<DatabaseId>? databaseIds = null)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        // Resolve the full id list when no list was supplied.
        if (databaseIds == null)
        {
            databaseIds = new List<DatabaseId>();
            using SqliteCommand idsCmd = conn.CreateCommand();
            idsCmd.CommandText = "SELECT id FROM Characters";
            using SqliteDataReader idsReader = idsCmd.ExecuteReader();
            while (idsReader.Read())
            {
                databaseIds.Add((DatabaseId) (uint) idsReader.GetInt32(0));
            }
        }

        // Filter to ids not already cached.
        List<DatabaseId> missingIds = new List<DatabaseId>();
        foreach (DatabaseId candidateId in databaseIds)
        {
            if (!_charactersByDatabaseId.ContainsKey(candidateId))
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
               platinum, gold, silver, copper, current_zone,
               character_id
        FROM Characters
        WHERE id IN (" + idPlaceholders + @")
        ORDER BY account_id, name";

        for (int parameterIndex = 0; parameterIndex < missingIds.Count; parameterIndex++)
        {
            cmd.Parameters.AddWithValue("@id" + parameterIndex, missingIds[parameterIndex].Value);
        }

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            Character character = ReadCharacterRow(reader);
            _characters.Add(character);
            _charactersByDatabaseId[(DatabaseId) (uint) character.DatabaseId] = character;
            DebugLog.Write(LogChannel.General, "Loaded character " + character.Name, LogLevel.Trace);

            if (character.CharacterId.Exists)
            {
                if (_charactersByCharacterId.TryGetValue(character.CharacterId, out Character? existing))
                {
                    DebugLog.Write(LogChannel.Database, "CharacterRepository.Load: characterId="
                        + character.CharacterId + " is already indexed for '" + existing.Name
                        + "', replacing it with '" + character.Name + "'", LogLevel.Warn);
                }

                _charactersByCharacterId[character.CharacterId] = character;
                DebugLog.Write(LogChannel.Database, "CharacterRepository.Load: indexed '" + character.Name
                    + "' by characterId=" + character.CharacterId, LogLevel.Trace);
            }
            else
            {
                DebugLog.Write(LogChannel.Database, "CharacterRepository.Load: '" + character.Name
                    + "' has no character id, not indexed by character id", LogLevel.Trace);
            }
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ReadCharacterRow
    //
    // Builds a Character from the current row of the supplied SqliteDataReader. The reader is expected
    // to have been opened with the canonical SELECT used by Load. Caller is responsible for
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
            DatabaseId = (DatabaseId) (uint) reader.GetInt32(0),
            Name = reader.GetString(1),
            Class = (EQClass)reader.GetInt32(2),
            AccountId = reader.GetInt32(3),
            Progression = reader.GetInt32(4) != 0,
            Server = reader.GetString(5),

            Level = reader.IsDBNull(6) ? null : (uint?)reader.GetInt32(6),
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

            Platinum = reader.IsDBNull(17) ? null : (ulong?)reader.GetInt64(17),
            Gold = reader.IsDBNull(18) ? null : (ulong?)reader.GetInt64(18),
            Silver = reader.IsDBNull(19) ? null : (ulong?)reader.GetInt64(19),
            Copper = reader.IsDBNull(20) ? null : (ulong?)reader.GetInt64(20),

            CurrentZone = reader.IsDBNull(21) ? ZoneId.None : (ZoneId) (uint)reader.GetInt32(21),
            CharacterId = reader.IsDBNull(22) ? CharacterId.None : (CharacterId)(uint)reader.GetInt64(22)
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

        character.DatabaseId = (DatabaseId) Convert.ToUInt32(cmd.ExecuteScalar());
        _characters.Add(character);
        _charactersByDatabaseId[(DatabaseId) (uint) character.DatabaseId] = character;

        DebugLog.Write(LogChannel.Database, "CharacterRepository.Add: added character="
            + character.Name + " id=" + character.DatabaseId
            + " server=" + character.Server
            + " class=" + character.Class
            + " accountId=" + character.AccountId, LogLevel.Trace);
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
public void Save(DatabaseId databaseId)
{
    if (!_charactersByDatabaseId.TryGetValue(databaseId, out Character? character))
    {
        DebugLog.Write(LogChannel.Database, $"CharacterRepository.Save: databaseId={databaseId} not in cache, ignoring.", LogLevel.Warn);
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
            copper = @copper,
            current_zone = @currentZone,
            character_id = @characterId
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
    cmd.Parameters.AddWithValue("@currentZone", character.CurrentZone.Exists ? character.CurrentZone.Value : DBNull.Value);
    cmd.Parameters.AddWithValue("@characterId", character.CharacterId.Exists ? character.CharacterId.Value : DBNull.Value);
    cmd.Parameters.AddWithValue("@id", character.DatabaseId.Value);

    cmd.ExecuteNonQuery();

    DebugLog.Write(LogChannel.Database, "CharacterRepository.Save: persisted character record " + character.DatabaseId
        + " name=" + character.Name
        + " class=" + character.Class
        + " level=" + (character.Level?.ToString() ?? "null")
        + " maxHp=" + (character.MaxHP?.ToString() ?? "null")
        + " maxMana=" + (character.MaxMana?.ToString() ?? "null"), LogLevel.Trace);
}

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetByDatabaseId
    //
    // Returns the cached Character whose row in the Characters table has the given database id.
    //
    // databaseId:  The database id (Characters.id) of the character.
    //
    // Returns:  The cached Character, or null if no cached character has that database id.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Character? GetByDatabaseId(DatabaseId databaseId)
    {
        if (_charactersByDatabaseId.TryGetValue(databaseId, out Character? character))
        {
            return character;
        }

        DebugLog.Write(LogChannel.Database, "CharacterRepository.GetByDatabaseId: no cached character for databaseId="
            + databaseId, LogLevel.Trace);
        return null;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetByCharacterId
    //
    // Returns the cached Character whose row in the Characters table has the given character id.
    //
    // characterId:  The server assigned persistent character id of the character.
    //
    // Returns:  The cached Character, or null if no cached character has that character id.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Character? GetByCharacterId(CharacterId characterId)
    {
        if (_charactersByCharacterId.TryGetValue(characterId, out Character? character))
        {
            return character;
        }

        DebugLog.Write(LogChannel.Database, "CharacterRepository.GetByCharacterId: no cached character for characterId="
            + characterId, LogLevel.Trace);
        return null;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetZoneSpawn
    //
    // Places a character in a zone under a spawn id.  The character's previous placement, read from its
    // current zone and spawn id, is removed from the index first; the new zone and spawn id are then
    // assigned to the character and indexed.  A zone id or spawn id that is None is rejected and nothing
    // changes.
    //
    // character:  The cached character.
    // zoneId:     The zone the character has entered.
    // spawnId:    The server assigned spawn id within that zone.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void SetZoneSpawn(Character character, ZoneId zoneId, SpawnId spawnId)
    {
        if (zoneId.Exists == false || spawnId.Exists == false)
        {
            DebugLog.Write(LogChannel.Database, "CharacterRepository.SetZoneSpawn: '" + character.Name + "' zoneId="
                + zoneId + " spawnId=" + spawnId + " is not a valid placement, nothing changed.", LogLevel.Error);
            return;
        }

        if (character.CurrentZone.Exists && character.SpawnId.Exists)
        {
            SpawnId oldSpawnId = character.SpawnId;
            ZoneId oldZoneId = character.CurrentZone;

            if (_charactersByZoneSpawn.TryGetValue(character.CurrentZone, out Dictionary<SpawnId, Character>? oldTable)
                && oldTable.TryGetValue(oldSpawnId, out Character? indexed)
                && ReferenceEquals(indexed, character))
            {
                // Remove the character from the cache, delete the Zone cache if it is now empty
                oldTable.Remove(oldSpawnId);
                if (oldTable.Count == 0)
                {
                    _charactersByZoneSpawn.Remove(oldZoneId);
                }

                DebugLog.Write(LogChannel.Database, "CharacterRepository.SetZoneSpawn: removed '" + character.Name
                    + "' from zoneId=" + oldZoneId + " spawnId=" + oldSpawnId, LogLevel.Trace);
            }
        }

        // Update character values and then put them in the cache
        character.CurrentZone = zoneId;
        character.SpawnId = spawnId;

        if (!_charactersByZoneSpawn.TryGetValue(zoneId, out Dictionary<SpawnId, Character>? zoneTable))
        {
            zoneTable = new Dictionary<SpawnId, Character>();
            _charactersByZoneSpawn[zoneId] = zoneTable;
        }

        zoneTable[spawnId] = character;
        DebugLog.Write(LogChannel.Database, "CharacterRepository.SetZoneSpawn: '" + character.Name + "' placed at zoneId="
            + zoneId + " spawnId=" + spawnId, LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetBySpawnId
    //
    // Returns the cached Character placed in the given zone under the given spawn id.
    //
    // zoneId:   The zone the spawn id is valid in.
    // spawnId:  The server assigned spawn id within that zone.
    //
    // Returns:  The cached Character, or null if none is placed there.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Character? GetBySpawnId(ZoneId zoneId, SpawnId spawnId)
    {
        if (_charactersByZoneSpawn.TryGetValue(zoneId, out Dictionary<SpawnId, Character>? zoneTable)
            && zoneTable.TryGetValue(spawnId, out Character? character))
        {
            return character;
        }

        DebugLog.Write(LogChannel.Database, "CharacterRepository.GetBySpawnId: no character at zoneId=" + zoneId
            + " spawnId=" + spawnId, LogLevel.Trace);
        return null;
    }
}