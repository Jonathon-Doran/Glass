using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;
using System.Collections.Generic;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// UISkinGateway
//
// Repository for loading UI skin definitions.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class UISkinGateway
{
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAll
    //
    // Returns all UI skins from the database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<UISkin> GetAll()
    {
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

        DebugLog.Write(LogChannel.Database, $"UISkinGateway.GetAll: loaded {skins.Count} skins.", LogLevel.Trace);
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
            DebugLog.Write(LogChannel.Database, $"UISkinGateway.GetById: found '{skin.Name}'.", LogLevel.Trace);
            return skin;
        }

        DebugLog.Write(LogChannel.Database, $"UISkinGateway.GetById: id={id} not found.", LogLevel.Warn);
        return null;
    }
}