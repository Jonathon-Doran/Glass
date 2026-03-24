using Glass.Core;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyPageRepository
//
// Provides access to key pages in the database.
// Pages are queried on demand — no preloaded cache.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class KeyPageRepository
{

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetPageNames
    //
    // Returns the names of all key pages for the given device type, ordered alphabetically.
    //
    // device:  The keyboard type to filter on
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<string> GetPageNames(KeyboardType device)
    {
        DebugLog.Write(DebugLog.Log_Database, $"KeyPageRepository.GetPageNames: device={device}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM KeyPages WHERE device = @device ORDER BY name";
        cmd.Parameters.AddWithValue("@device", device.ToString());

        var names = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        DebugLog.Write(DebugLog.Log_Database, $"KeyPageRepository.GetPageNames: device={device} found {names.Count} pages.");
        return names;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetPage
    //
    // Returns the key page with the given ID, or null if not found.
    //
    // id:  The page ID to look up
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public KeyPage? GetPage(int id)
    {
        DebugLog.Write(DebugLog.Log_Database, $"KeyPageRepository.GetPage: id={id}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, device FROM KeyPages WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            DebugLog.Write(DebugLog.Log_Database, $"KeyPageRepository.GetPage: id={id} not found.");
            return null;
        }

        return new KeyPage
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Device = reader.GetString(2).ToKeyboardType()
        };
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetPage
    //
    // Returns the key page with the given name and device type, or null if not found.
    //
    // name:    The page name to look up
    // device:  The keyboard type to filter on
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public KeyPage? GetPage(string name, KeyboardType device)
    {
        DebugLog.Write(DebugLog.Log_Database, $"KeyPageRepository.GetPage: name='{name}' device={device}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, device FROM KeyPages WHERE name = @name AND device = @device";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@device", device.ToString());

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            DebugLog.Write(DebugLog.Log_Database, $"KeyPageRepository.GetPage: name='{name}' device={device} not found.");
            return null;
        }

        DebugLog.Write(DebugLog.Log_Database, $"KeyPageRepository.GetPage: name='{name}' device={device} found. id={reader.GetInt32(0)}.");

        return new KeyPage
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Device = reader.GetString(2).ToKeyboardType()
        };
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAllPages
    //
    // Returns all key pages ordered by device then name.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<KeyPage> GetAllPages()
    {
        DebugLog.Write(DebugLog.Log_Database, "KeyPageRepository.GetAllPages: loading.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, device FROM KeyPages ORDER BY device, name";

        var pages = new List<KeyPage>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            pages.Add(new KeyPage
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Device = reader.GetString(2).ToKeyboardType()
            });
        }

        DebugLog.Write(DebugLog.Log_Database, $"KeyPageRepository.GetAllPages: found {pages.Count} pages.");
        return pages;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Save
    //
    // Inserts or updates a key page. If the page has an ID of 0, inserts a new record
    // and updates the page's ID. Otherwise updates the existing record.
    //
    // page:  The page to persist
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Save(KeyPage page)
    {
        DebugLog.Write(DebugLog.Log_Database, $"KeyPageRepository.Save: name='{page.Name}' device={page.Device}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        if (page.Id == 0)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO KeyPages (name, device) VALUES (@name, @device); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@name", page.Name);
            cmd.Parameters.AddWithValue("@device", page.Device.ToString());
            page.Id = Convert.ToInt32(cmd.ExecuteScalar());
            DebugLog.Write(DebugLog.Log_Database, $"KeyPageRepository.Save: inserted. id={page.Id}.");
        }
        else
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE KeyPages SET name = @name, device = @device WHERE id = @id";
            cmd.Parameters.AddWithValue("@name", page.Name);
            cmd.Parameters.AddWithValue("@device", page.Device.ToString());
            cmd.Parameters.AddWithValue("@id", page.Id);
            cmd.ExecuteNonQuery();
            DebugLog.Write(DebugLog.Log_Database, $"KeyPageRepository.Save: updated. id={page.Id}.");
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Delete
    //
    // Deletes the key page with the given ID.
    //
    // id:  The page ID to delete
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Delete(int id)
    {
        DebugLog.Write(DebugLog.Log_Database, $"KeyPageRepository.Delete: id={id}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM KeyPages WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();

        DebugLog.Write(DebugLog.Log_Database, $"KeyPageRepository.Delete: deleted. id={id}.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetPageId
    //
    // Returns the id of the page with the given name, or null if not found.
    //
    // name:  The page name to look up
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public int? GetPageId(string name)
    {
        DebugLog.Write(DebugLog.Log_Database, $"KeyPageRepository.GetPageId: name='{name}'.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM KeyPages WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", name);

        var result = cmd.ExecuteScalar();

        if (result == null)
        {
            DebugLog.Write(DebugLog.Log_Database, $"KeyPageRepository.GetPageId: name='{name}' not found.");
            return null;
        }

        int id = Convert.ToInt32(result);
        DebugLog.Write(DebugLog.Log_Database, $"KeyPageRepository.GetPageId: name='{name}' -> id={id}.");
        return id;
    }
}