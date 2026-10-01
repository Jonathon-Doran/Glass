using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyAliasGateway
//
// Provides access to key aliases in the database.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class KeyAliasGateway
{
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAllAliases
    //
    // Returns all key aliases ordered alphabetically by name.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<KeyAlias> GetAllAliases()
    {
        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, value FROM KeyAliases ORDER BY name";

        var aliases = new List<KeyAlias>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            aliases.Add(new KeyAlias
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Value = reader.GetString(2)
            });
        }

        DebugLog.Write(LogChannel.Database, "GetAllAliases return " + aliases.Count + " members", LogLevel.Trace);
        return aliases;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAlias
    //
    // Returns the key alias with the given name, or null if not found.
    //
    // name:  The alias name to look up
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public KeyAlias? GetAlias(string name)
    {
        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, value FROM KeyAliases WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", name);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            DebugLog.Write(LogChannel.Database, $"KeyAliasRepository.GetAlias: name='{name}' not found.", LogLevel.Warn);
            return null;
        }

        return new KeyAlias
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Value = reader.GetString(2)
        };
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Resolve
    //
    // Returns the value (actual keystroke) for the given alias name, or null if not found.
    // Use this at config load time to resolve symbolic names to actual keystrokes.
    //
    // name:  The alias name to resolve
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public string? Resolve(string name)
    {
        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM KeyAliases WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", name);

        var result = cmd.ExecuteScalar();

        if (result == null)
        {
            DebugLog.Write(LogChannel.Database, $"KeyAliasRepository.Resolve: name='{name}' not found.", LogLevel.Warn);
            return null;
        }

        string value = result.ToString()!;
        return value;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Save
    //
    // Inserts or updates a key alias. If the alias has an id of 0, inserts a new record
    // and updates the alias id. Otherwise updates the existing record.
    //
    // alias:  The alias to persist
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Save(KeyAlias alias)
    {
        DebugLog.Write(LogChannel.Database, $"KeyAliasRepository.Save: name='{alias.Name}' value='{alias.Value}'.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        if (alias.Id == 0)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO KeyAliases (name, value) VALUES (@name, @value); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@name", alias.Name);
            cmd.Parameters.AddWithValue("@value", alias.Value);
            alias.Id = Convert.ToInt32(cmd.ExecuteScalar());
            DebugLog.Write(LogChannel.Database, $"KeyAliasRepository.Save: inserted. id={alias.Id}.", LogLevel.Trace);
        }
        else
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE KeyAliases SET name = @name, value = @value WHERE id = @id";
            cmd.Parameters.AddWithValue("@name", alias.Name);
            cmd.Parameters.AddWithValue("@value", alias.Value);
            cmd.Parameters.AddWithValue("@id", alias.Id);
            cmd.ExecuteNonQuery();
            DebugLog.Write(LogChannel.Database, $"KeyAliasRepository.Save: updated. id={alias.Id}.", LogLevel.Trace);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Delete
    //
    // Deletes the key alias with the given id.
    //
    // id:  The alias id to delete
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Delete(int id)
    {
        DebugLog.Write(LogChannel.Database, $"KeyAliasRepository.Delete: id={id}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM KeyAliases WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();

        DebugLog.Write(LogChannel.Database, $"KeyAliasRepository.Delete: deleted.", LogLevel.Trace);
    }
}
