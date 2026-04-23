using Glass.Core;
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
    private readonly List<Character> _characters;

    public CharacterRepository()
    {
        _characters = new List<Character>();

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, class, account_id, progression, server FROM Characters ORDER BY account_id, name";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            _characters.Add(new Character
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Class = (EQClass)reader.GetInt32(2),
                AccountId = reader.GetInt32(3),
                Progression = reader.GetInt32(4) != 0,
                Server = reader.GetString(5)
            });
        }
    }

    // Returns all cached characters.
    public IReadOnlyList<Character> GetAll() => _characters.AsReadOnly();

    // Returns the character with the given id, or null if not found.
    public Character? GetById(int id) => _characters.FirstOrDefault(c => c.Id == id);

    // Returns the character with the given name, or null if not found.
    public Character? GetByName(string name) => _characters.FirstOrDefault(c => c.Name == name);

    // Returns all characters belonging to the given account.
    public IReadOnlyList<Character> GetByAccount(int accountId) =>
        _characters.Where(c => c.AccountId == accountId).ToList().AsReadOnly();

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Add
    //
    // Inserts a new character into the database and updates the in-memory cache.
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

        character.Id = Convert.ToInt32(cmd.ExecuteScalar());
        _characters.Add(character);

        DebugLog.Write(DebugLog.Log_Database, "CharacterRepository.Add: added character="
            + character.Name + " id=" + character.Id
            + " server=" + character.Server
            + " class=" + character.Class
            + " accountId=" + character.AccountId);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Update
    //
    // Updates an existing character in the database and refreshes the in-memory cache.
    //
    // character:  The character to update. Must have a valid Id.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Update(Character character)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Characters SET name = @name, class = @class, account_id = @accountId, server = @server, progression = @progression WHERE id = @id";
        cmd.Parameters.AddWithValue("@name", character.Name);
        cmd.Parameters.AddWithValue("@class", (int)character.Class);
        cmd.Parameters.AddWithValue("@accountId", character.AccountId);
        cmd.Parameters.AddWithValue("@server", character.Server);
        cmd.Parameters.AddWithValue("@progression", character.Progression ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", character.Id);
        cmd.ExecuteNonQuery();

        int index = _characters.FindIndex(c => c.Id == character.Id);
        if (index >= 0)
        {
            _characters[index] = character;
        }

        DebugLog.Write(DebugLog.Log_Database, "CharacterRepository.Update: updated character="
            + character.Name + " id=" + character.Id
            + " server=" + character.Server
            + " class=" + character.Class
            + " accountId=" + character.AccountId);
    }
}