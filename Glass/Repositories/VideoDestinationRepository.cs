using Glass.Core;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// VideoDestinationRepository
//
// Handles persistence of VideoDestination records.
// VideoDestinations are per-profile and define slot-relative render coordinates
// for each named VideoSource region. All slots in a profile share the same offsets.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class VideoDestinationRepository
{
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAll
    //
    // Returns all video destinations from the database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<VideoDestination> GetAll()
    {
        DebugLog.Write(DebugLog.Log_Database, "VideoDestinationRepository.GetAll: loading destinations.");

        List<VideoDestination> destinations = new List<VideoDestination>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, x, y, width, height FROM VideoDestinations";

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            VideoDestination destination = new VideoDestination
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                X = reader.GetInt32(2),
                Y = reader.GetInt32(3),
                Width = reader.GetInt32(4),
                Height = reader.GetInt32(5)
            };
            destinations.Add(destination);
        }

        DebugLog.Write(DebugLog.Log_Database, $"VideoDestinationRepository.GetAll: loaded {destinations.Count} destinations.");
        return destinations;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetByName
    //
    // Returns a video destination by name, or null if not found.
    //
    // name: The destination name to retrieve
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public VideoDestination? GetByName(string name)
    {
        DebugLog.Write(DebugLog.Log_Database, $"VideoDestinationRepository.GetByName: name='{name}'.");

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, x, y, width, height FROM VideoDestinations WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", name);

        using SqliteDataReader reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            VideoDestination destination = new VideoDestination
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                X = reader.GetInt32(2),
                Y = reader.GetInt32(3),
                Width = reader.GetInt32(4),
                Height = reader.GetInt32(5)
            };
            DebugLog.Write(DebugLog.Log_Database, $"VideoDestinationRepository.GetByName: found '{destination.Name}'.");
            return destination;
        }

        DebugLog.Write(DebugLog.Log_Database, $"VideoDestinationRepository.GetByName: '{name}' not found.");
        return null;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Save
    //
    // Inserts or updates a video destination. If Id is 0, inserts and updates Id.
    //
    // destination: The destination to save
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Save(VideoDestination destination)
    {
        DebugLog.Write(DebugLog.Log_Database, $"VideoDestinationRepository.Save: name='{destination.Name}'.");

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        if (destination.Id == 0)
        {
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO VideoDestinations (name, x, y, width, height) VALUES (@name, @x, @y, @width, @height); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@name", destination.Name);
            cmd.Parameters.AddWithValue("@x", destination.X);
            cmd.Parameters.AddWithValue("@y", destination.Y);
            cmd.Parameters.AddWithValue("@width", destination.Width);
            cmd.Parameters.AddWithValue("@height", destination.Height);
            destination.Id = Convert.ToInt32(cmd.ExecuteScalar());
            DebugLog.Write(DebugLog.Log_Database, $"VideoDestinationRepository.Save: inserted id={destination.Id}.");
        }
        else
        {
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE VideoDestinations SET name = @name, x = @x, y = @y, width = @width, height = @height WHERE id = @id";
            cmd.Parameters.AddWithValue("@name", destination.Name);
            cmd.Parameters.AddWithValue("@x", destination.X);
            cmd.Parameters.AddWithValue("@y", destination.Y);
            cmd.Parameters.AddWithValue("@width", destination.Width);
            cmd.Parameters.AddWithValue("@height", destination.Height);
            cmd.Parameters.AddWithValue("@id", destination.Id);
            cmd.ExecuteNonQuery();
            DebugLog.Write(DebugLog.Log_Database, $"VideoDestinationRepository.Save: updated id={destination.Id}.");
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Delete
    //
    // Deletes a video destination by ID.
    //
    // id: The destination ID to delete
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Delete(int id)
    {
        DebugLog.Write(DebugLog.Log_Database, $"VideoDestinationRepository.Delete: id={id}.");

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM VideoDestinations WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();

        DebugLog.Write(DebugLog.Log_Database, $"VideoDestinationRepository.Delete: deleted id={id}.");
    }
}