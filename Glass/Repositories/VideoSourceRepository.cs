using Glass.Core;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// VideoSourceRepository
//
// Handles persistence of VideoSource records.
// VideoSources are profile-independent — they form a global catalog of
// named regions within a captured EQ client window.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class VideoSourceRepository
{
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAll
    //
    // Returns all video sources from the database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<VideoSource> GetAll()
    {
        DebugLog.Write(DebugLog.Log_Database, "VideoSourceRepository.GetAll: loading sources.");

        List<VideoSource> sources = new List<VideoSource>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, ui_skin_id, x, y, width, height FROM VideoSources";

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            VideoSource source = new VideoSource
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                UISkinId = reader.GetInt32(2),
                X = reader.GetInt32(3),
                Y = reader.GetInt32(4),
                Width = reader.GetInt32(5),
                Height = reader.GetInt32(6)
            };
            sources.Add(source);
        }

        DebugLog.Write(DebugLog.Log_Database, $"VideoSourceRepository.GetAll: loaded {sources.Count} sources.");
        return sources;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetById
    //
    // Returns a video source by ID, or null if not found.
    //
    // id: The source ID to retrieve
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public VideoSource? GetById(int id)
    {
        DebugLog.Write(DebugLog.Log_Database, $"VideoSourceRepository.GetById: id={id}.");

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, ui_skin_id, x, y, width, height FROM VideoSources WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using SqliteDataReader reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            VideoSource source = new VideoSource
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                UISkinId = reader.GetInt32(2),
                X = reader.GetInt32(3),
                Y = reader.GetInt32(4),
                Width = reader.GetInt32(5),
                Height = reader.GetInt32(6)
            };
            DebugLog.Write(DebugLog.Log_Database, $"VideoSourceRepository.GetById: found '{source.Name}'.");
            return source;
        }

        DebugLog.Write(DebugLog.Log_Database, $"VideoSourceRepository.GetById: id={id} not found.");
        return null;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Save
    //
    // Inserts or updates a video source. If Id is 0, inserts and updates Id.
    //
    // source: The source to save
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Save(VideoSource source)
    {
        DebugLog.Write(DebugLog.Log_Database, $"VideoSourceRepository.Save: name='{source.Name}' uiSkinId={source.UISkinId}.");

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        if (source.Id == 0)
        {
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO VideoSources (name, ui_skin_id, x, y, width, height) VALUES (@name, @uiSkinId, @x, @y, @width, @height); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@name", source.Name);
            cmd.Parameters.AddWithValue("@uiSkinId", source.UISkinId);
            cmd.Parameters.AddWithValue("@x", source.X);
            cmd.Parameters.AddWithValue("@y", source.Y);
            cmd.Parameters.AddWithValue("@width", source.Width);
            cmd.Parameters.AddWithValue("@height", source.Height);
            source.Id = Convert.ToInt32(cmd.ExecuteScalar());
            DebugLog.Write(DebugLog.Log_Database, $"VideoSourceRepository.Save: inserted id={source.Id}.");
        }
        else
        {
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE VideoSources SET name = @name, ui_skin_id = @uiSkinId, x = @x, y = @y, width = @width, height = @height WHERE id = @id";
            cmd.Parameters.AddWithValue("@name", source.Name);
            cmd.Parameters.AddWithValue("@uiSkinId", source.UISkinId);
            cmd.Parameters.AddWithValue("@x", source.X);
            cmd.Parameters.AddWithValue("@y", source.Y);
            cmd.Parameters.AddWithValue("@width", source.Width);
            cmd.Parameters.AddWithValue("@height", source.Height);
            cmd.Parameters.AddWithValue("@id", source.Id);
            cmd.ExecuteNonQuery();
            DebugLog.Write(DebugLog.Log_Database, $"VideoSourceRepository.Save: updated id={source.Id}.");
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetByUISkin
    //
    // Returns all video sources for a specific UI skin.
    //
    // uiSkinId: The UI skin ID to filter by
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<VideoSource> GetByUISkin(int uiSkinId)
    {
        DebugLog.Write(DebugLog.Log_Database, $"VideoSourceRepository.GetByUISkin: uiSkinId={uiSkinId}.");

        List<VideoSource> sources = new List<VideoSource>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, ui_skin_id, x, y, width, height FROM VideoSources WHERE ui_skin_id = @uiSkinId";
        cmd.Parameters.AddWithValue("@uiSkinId", uiSkinId);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            VideoSource source = new VideoSource
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                UISkinId = reader.GetInt32(2),
                X = reader.GetInt32(3),
                Y = reader.GetInt32(4),
                Width = reader.GetInt32(5),
                Height = reader.GetInt32(6)
            };
            sources.Add(source);
        }

        DebugLog.Write(DebugLog.Log_Database, $"VideoSourceRepository.GetByUISkin: loaded {sources.Count} sources.");
        return sources;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Delete
    //
    // Deletes the VideoSource with the given ID. All VideoDestinations referencing
    // this source will be cascade-deleted by the database.
    //
    // id:  The ID of the source to delete.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Delete(int id)
    {
        DebugLog.Write(DebugLog.Log_Database, $"VideoSourceRepository.Delete: id={id}.");

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM VideoSources WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        int rows = cmd.ExecuteNonQuery();

        DebugLog.Write(DebugLog.Log_Database, $"VideoSourceRepository.Delete: {rows} row(s) deleted for id={id}.");
    }
}