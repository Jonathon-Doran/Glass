using Glass.Core;
using Glass.Data;
using Glass.Data.Models;
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
        DebugLog.Write(DebugLog.Log_Database, "CharacterRepository: loading.");

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

        DebugLog.Write(DebugLog.Log_Database, $"CharacterRepository: loaded {_characters.Count} characters.");
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
}