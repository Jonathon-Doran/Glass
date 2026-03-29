using Glass.Controls;
using Glass.Core;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// KeyBindingRepository
//
// Provides access to key bindings in the database.
// Bindings are queried and saved on demand — no preloaded cache.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class KeyBindingRepository
{
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetBindingsForPage
    //
    // Returns all key bindings for the given page ID, ordered by key.
    //
    // keyPageId:  The page to load bindings for
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<KeyBinding> GetBindingsForPage(int keyPageId)
    {
        DebugLog.Write(DebugLog.Log_Database, $"KeyBindingRepository.GetBindingsForPage: keyPageId={keyPageId}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, key, command_id, target, round_robin, label, trigger_on, key_type, repeat_interval_ms
            FROM KeyBindings
            WHERE key_page_id = @pageId
            ORDER BY key";
        cmd.Parameters.AddWithValue("@pageId", keyPageId);

        var bindings = new List<KeyBinding>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            bindings.Add(new KeyBinding
            {
                Id = reader.GetInt32(0),
                KeyPageId = keyPageId,
                Key = reader.GetString(1),
                CommandId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Target = reader.GetInt32(3),
                RoundRobin = reader.GetInt32(4) != 0,
                Label = reader.IsDBNull(5) ? null : reader.GetString(5),
                TriggerOn = (TriggerOn)reader.GetInt32(6),
                KeyType = (KeyType)reader.GetInt32(7),
                RepeatIntervalMs = reader.GetInt32(8),
            });
        }

        return bindings;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Save
    //
    // Inserts or updates a key binding. If the binding has an ID of 0, inserts a new record
    // and updates the binding's ID. Otherwise updates the existing record.
    //
    // binding:  The binding to persist
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Save(KeyBinding binding)
    {
        DebugLog.Write(DebugLog.Log_Database, $"KeyBindingRepository.Save: keyPageId={binding.KeyPageId} key='{binding.Key}' commandId={binding.CommandId} target={binding.Target} relayGroupId={binding.RelayGroupId} roundRobin={binding.RoundRobin} label='{binding.Label}'.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        if (binding.Id == 0)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO KeyBindings (key_page_id, key, command_id, target, round_robin, label, trigger_on, key_type, repeat_interval_ms)
                VALUES (@pageId, @key, @commandId, @target, @roundRobin, @label, @triggerOn, @keyType, @repeatIntervalMs);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@pageId", binding.KeyPageId);
            cmd.Parameters.AddWithValue("@key", binding.Key);
            cmd.Parameters.AddWithValue("@commandId", binding.CommandId.HasValue ? binding.CommandId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@target", binding.Target);
            cmd.Parameters.AddWithValue("@roundRobin", binding.RoundRobin ? 1 : 0);
            cmd.Parameters.AddWithValue("@label", binding.Label != null ? binding.Label : DBNull.Value);
            cmd.Parameters.AddWithValue("@triggerOn", (int)binding.TriggerOn);
            cmd.Parameters.AddWithValue("@keyType", (int)binding.KeyType);
            cmd.Parameters.AddWithValue("@repeatIntervalMs", binding.RepeatIntervalMs);

            binding.Id = Convert.ToInt32(cmd.ExecuteScalar());
            DebugLog.Write(DebugLog.Log_Database, $"KeyBindingRepository.Save: inserted. id={binding.Id}.");
        }
        else
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE KeyBindings
                SET key = @key, command_id = @commandId, target = @target, round_robin = @roundRobin, label = @label, trigger_on = @triggerOn, key_type = @keyType, repeat_interval_ms = @repeatIntervalMs
                WHERE id = @id";
            cmd.Parameters.AddWithValue("@key", binding.Key);
            cmd.Parameters.AddWithValue("@commandId", binding.CommandId.HasValue ? binding.CommandId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@target", binding.Target);
            cmd.Parameters.AddWithValue("@roundRobin", binding.RoundRobin ? 1 : 0);
            cmd.Parameters.AddWithValue("@label", binding.Label != null ? binding.Label : DBNull.Value);
            cmd.Parameters.AddWithValue("@triggerOn", (int)binding.TriggerOn);
            cmd.Parameters.AddWithValue("@keyType", (int)binding.KeyType);
            cmd.Parameters.AddWithValue("@repeatIntervalMs", binding.RepeatIntervalMs);
            cmd.Parameters.AddWithValue("@id", binding.Id); cmd.ExecuteNonQuery();


            DebugLog.Write(DebugLog.Log_Database, $"KeyBindingRepository.Save: updated. id={binding.Id}.");
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Delete
    //
    // Deletes the key binding with the given ID.
    //
    // id:  The binding ID to delete
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Delete(int id)
    {
        DebugLog.Write(DebugLog.Log_Database, $"KeyBindingRepository.Delete: id={id}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM KeyBindings WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();

        DebugLog.Write(DebugLog.Log_Database, $"KeyBindingRepository.Delete: deleted. id={id}.");
    }
}