using Glass.Core.Logging;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// CharacterSpellGateway
//
// Database-backed store of each character's spellbook and memorized spell gems.  A character's rows are
// replaced as a whole from the character's current spells; nothing is cached here, the character object
// is the in-memory copy.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class CharacterSpellGateway
{
    private static CharacterSpellGateway? _instance = null;
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor.  The instance is created on first access.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static CharacterSpellGateway Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new CharacterSpellGateway();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CharacterSpellGateway (constructor)
    //
    // Private constructor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private CharacterSpellGateway()
    {
        DebugLog.Write(LogChannel.Database, "CharacterSpellGateway: singleton instance created.", defaultLoglevel);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // StoreSpells
    //
    // Replaces the CharacterSpells and CharacterSpellGems rows of a character with the character's current
    // spellbook and memorized gems, in one transaction.  Every gem slot is written, empty slots included,
    // keyed by its index.  A spell already written for the character is not written twice.  A failure rolls
    // the transaction back and leaves the previous rows in place.  A character with no server character id
    // is not written.
    //
    // character:  The character whose spells are written.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void StoreSpells(Character character)
    {
        if (character.CharacterId.Exists == false)
        {
            DebugLog.Write(LogChannel.Database, "CharacterSpellGateway.StoreSpells: '" + character.Name +
                "' has no valid characterId; spells not stored", LogLevel.Warn);
            return;
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteTransaction tx = conn.BeginTransaction();
        try
        {
            using SqliteCommand deleteCmd = conn.CreateCommand();
            deleteCmd.Transaction = tx;
            deleteCmd.CommandText = "DELETE FROM CharacterSpells WHERE character_id = @character_id; " +
                "DELETE FROM CharacterSpellGems WHERE character_id = @character_id";
            deleteCmd.Parameters.AddWithValue("@character_id", character.CharacterId.Value);
            int deleted = deleteCmd.ExecuteNonQuery();
            DebugLog.Write(LogChannel.Database, "CharacterSpellGateway.StoreSpells: deleted " + deleted +
                " rows for '" + character.Name + "'", defaultLoglevel);

            using SqliteCommand bookCmd = conn.CreateCommand();
            bookCmd.Transaction = tx;
            bookCmd.CommandText = "INSERT OR IGNORE INTO CharacterSpells (character_id, spell_id) " +
                "VALUES (@character_id, @spell_id)";
            bookCmd.Parameters.AddWithValue("@character_id", character.CharacterId.Value);
            SqliteParameter bookSpell = bookCmd.Parameters.Add("@spell_id", SqliteType.Integer);

            uint bookWritten = 0;
            uint bookDuplicates = 0;
            foreach (SpellId spellId in character.SpellBook)
            {
                bookSpell.Value = spellId.Value;
                if (bookCmd.ExecuteNonQuery() == 0)
                {
                    bookDuplicates++;
                    DebugLog.Write(LogChannel.Database, "CharacterSpellGateway.StoreSpells: '" + character.Name +
                        "' spellbook holds spell " + spellId + " more than once; written once", LogLevel.Warn);
                    continue;
                }

                bookWritten++;
            }

            using SqliteCommand gemCmd = conn.CreateCommand();
            gemCmd.Transaction = tx;
            gemCmd.CommandText = "INSERT INTO CharacterSpellGems (character_id, gem_index, spell_id) " +
                "VALUES (@character_id, @gem_index, @spell_id)";
            gemCmd.Parameters.AddWithValue("@character_id", character.CharacterId.Value);
            SqliteParameter gemIndex = gemCmd.Parameters.Add("@gem_index", SqliteType.Integer);
            SqliteParameter gemSpell = gemCmd.Parameters.Add("@spell_id", SqliteType.Integer);

            uint gemsWritten = 0;
            for (uint index = 0; index < character.SpellGems.Length; index++)
            {
                if (character.SpellGems[index] == SpellId.None)
                {
                    continue;
                }

                gemIndex.Value = index;
                gemSpell.Value = character.SpellGems[index].Value;
                gemCmd.ExecuteNonQuery();
                gemsWritten++;
            }

            tx.Commit();
            DebugLog.Write(LogChannel.Database, "CharacterSpellGateway.StoreSpells: '" + character.Name +
                "' stored " + bookWritten + " spellbook spells (" + bookDuplicates + " duplicates) and " +
                gemsWritten + " of " + character.SpellGems.Length + " gems", defaultLoglevel);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(LogChannel.Database, "CharacterSpellGateway.StoreSpells: failed for '" +
                character.Name + "', previous rows kept: " + ex.Message, LogLevel.Error);
            throw;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadSpells
    //
    // Replaces a character's spellbook and memorized gems with the rows stored for the character.  The
    // spellbook holds exactly the stored spells.  The gems array is sized to the highest stored gem index
    // plus one, with any index that has no row left as SpellId.None.  A character with no stored rows gets
    // an empty spellbook and no gems.  A character with no server character id is left unchanged.
    //
    // character:  The character whose spells are loaded.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void LoadSpells(Character character)
    {
        if (character.CharacterId.Exists == false)
        {
            DebugLog.Write(LogChannel.Database, "CharacterSpellGateway.LoadSpells: '" + character.Name +
                "' has no valid characterId; spells not loaded", LogLevel.Warn);
            return;
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        List<SpellId> spellBook = new List<SpellId>();

        using SqliteCommand bookCmd = conn.CreateCommand();
        bookCmd.CommandText = "SELECT spell_id FROM CharacterSpells WHERE character_id = @character_id";
        bookCmd.Parameters.AddWithValue("@character_id", character.CharacterId.Value);

        using (SqliteDataReader bookReader = bookCmd.ExecuteReader())
        {
            while (bookReader.Read())
            {
                spellBook.Add((SpellId)(uint)bookReader.GetInt64(0));
            }
        }

        character.SpellBook = spellBook.ToArray();

        List<KeyValuePair<uint, SpellId>> gems = new List<KeyValuePair<uint, SpellId>>();
        uint gemCount = 0;

        using SqliteCommand gemCmd = conn.CreateCommand();
        gemCmd.CommandText = "SELECT gem_index, spell_id FROM CharacterSpellGems WHERE character_id = @character_id";
        gemCmd.Parameters.AddWithValue("@character_id", character.CharacterId.Value);

        using (SqliteDataReader gemReader = gemCmd.ExecuteReader())
        {
            while (gemReader.Read())
            {
                uint index = (uint)gemReader.GetInt64(0);
                SpellId spellId = (SpellId)(uint)gemReader.GetInt64(1);
                gems.Add(new KeyValuePair<uint, SpellId>(index, spellId));

                if (index + 1 > gemCount)
                {
                    gemCount = index + 1;
                }
            }
        }

        SpellId[] spellGems = new SpellId[gemCount];
        for (uint index = 0; index < gemCount; index++)
        {
            spellGems[index] = SpellId.None;
        }

        foreach (KeyValuePair<uint, SpellId> gem in gems)
        {
            spellGems[gem.Key] = gem.Value;
        }

        character.SpellGems = spellGems;

        DebugLog.Write(LogChannel.Database, "CharacterSpellGateway.LoadSpells: '" + character.Name +
            "' loaded " + character.SpellBook.Length + " spellbook spells and " + gems.Count + " memorized gems in " +
            gemCount + " gem slots", defaultLoglevel);
    }
}