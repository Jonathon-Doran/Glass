using Glass.Core;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;
using System.Collections.Generic;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// UISkinRepository
//
// Repository for loading UI skin definitions.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class UISkinRepository
{
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAll
    //
    // Returns all UI skins from the database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<UISkin> GetAll()
    {
        DebugLog.Write(DebugLog.Log_Database, "UISkinRepository.GetAll: loading all skins.");

        List<UISkin> skins = new List<UISkin>();

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM UISkins ORDER BY name";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            UISkin skin = new UISkin
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1)
            };
            skins.Add(skin);
        }

        DebugLog.Write(DebugLog.Log_Database, $"UISkinRepository.GetAll: loaded {skins.Count} skins.");
        return skins;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetById
    //
    // Returns a UI skin by ID, or null if not found.
    //
    // id: The skin ID to retrieve
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public UISkin? GetById(int id)
    {
        DebugLog.Write(DebugLog.Log_Database, $"UISkinRepository.GetById: id={id}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM UISkins WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            UISkin skin = new UISkin
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1)
            };
            DebugLog.Write(DebugLog.Log_Database, $"UISkinRepository.GetById: found '{skin.Name}'.");
            return skin;
        }

        DebugLog.Write(DebugLog.Log_Database, $"UISkinRepository.GetById: id={id} not found.");
        return null;
    }
}