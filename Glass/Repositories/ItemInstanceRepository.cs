using Glass.Core.Logging;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ItemInstanceRepository
//
// Database-backed store of the item instances held by each character.  A character's rows are replaced
// as a whole from that character's current instances; nothing is cached here, the character object is
// the in-memory copy.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class ItemInstanceRepository
{
    private static ItemInstanceRepository? _instance = null;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor.  The instance is created on first access.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static ItemInstanceRepository Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new ItemInstanceRepository();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ItemInstanceRepository
    //
    // Private constructor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private ItemInstanceRepository()
    {
        DebugLog.Write(LogChannel.Inventory, "ItemInstanceRepository: singleton instance created.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // InsertTree
    //
    // Inserts one item instance as a row of ItemInstances, sets the instance's InstanceId to the new
    // row id, then inserts each of the instance's Children the same way with this row as their parent.
    //
    // conn:         The open connection to write through.
    // tx:           The transaction the writes belong to.
    // characterId:  Row id of the character holding the instance.
    // item:         The instance to insert.
    // parentRowId:  Row id of the instance holding this one, or null for a top-level instance.
    //
    // Returns the number of rows inserted: this instance plus all descendants.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private uint InsertTree(SqliteConnection conn, SqliteTransaction tx, int characterId, ItemInstance item,
        long? parentRowId)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO ItemInstances (
                character_id, parent_id, stack_size, storage, main_position, sub_position, aug_position,
                remaining_charges, is_attuned, is_copied, item_id
            ) VALUES (
                @character_id, @parent_id, @stack_size, @storage, @main_position, @sub_position, @aug_position,
                @remaining_charges, @is_attuned, @is_copied, @item_id
            )
            RETURNING id";

        cmd.Parameters.AddWithValue("@character_id", characterId);
        cmd.Parameters.AddWithValue("@parent_id", parentRowId.HasValue ? parentRowId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@stack_size", item.StackSize);
        cmd.Parameters.AddWithValue("@storage", (uint)item.Location.Storage);
        cmd.Parameters.AddWithValue("@main_position", item.Location.MainPosition);
        cmd.Parameters.AddWithValue("@sub_position", item.Location.SubPosition);
        cmd.Parameters.AddWithValue("@aug_position", item.Location.AugPosition);
        cmd.Parameters.AddWithValue("@remaining_charges", item.RemainingCharges);
        cmd.Parameters.AddWithValue("@is_attuned", item.IsAttuned);
        cmd.Parameters.AddWithValue("@is_copied", item.IsCopied);
        cmd.Parameters.AddWithValue("@item_id", (uint)item.Id);

        long rowId = (long)cmd.ExecuteScalar()!;
        item.InstanceId = (ItemInstanceId)(uint)rowId;
        DebugLog.Write(LogChannel.Database, "ItemInstanceRepository.InsertTree: row " + rowId + " item " +
            item.Id + " at " + item.Location + ", parent row " +
            (parentRowId.HasValue ? parentRowId.Value.ToString() : "none"), LogLevel.Trace);

        uint inserted = 1;
        foreach (ItemInstance child in item.Children)
        {
            inserted += InsertTree(conn, tx, characterId, child, rowId);
        }
        return inserted;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // StoreSnapshot
    //
    // Replaces the ItemInstances rows of a character with the instances the character currently holds.
    // The character's existing rows are deleted and every held instance is inserted, parents before
    // children so each row can carry its parent's row id, all in one transaction.  Each instance's
    // InstanceId is set to its new row id.  A failure rolls the transaction back and leaves the previous
    // rows in place.
    //
    // character:  The character whose instances are written.
    //
    // Returns the number of rows inserted.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public uint StoreSnapshot(Character character)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteTransaction tx = conn.BeginTransaction();
        try
        {
            using SqliteCommand deleteCmd = conn.CreateCommand();
            deleteCmd.Transaction = tx;
            deleteCmd.CommandText = "DELETE FROM ItemInstances WHERE character_id = @character_id";
            deleteCmd.Parameters.AddWithValue("@character_id", character.CharacterId);
            int deleted = deleteCmd.ExecuteNonQuery();
            DebugLog.Write(LogChannel.Database, "ItemInstanceRepository.StoreSnapshot: deleted " + deleted +
                " rows for '" + character.Name + "'.", LogLevel.Trace);

            uint inserted = 0;
            foreach (ItemInstance item in character.Items)
            {
                if (item.Parent == null)
                {
                    inserted += InsertTree(conn, tx, character.CharacterId, item, null);
                }
            }

            tx.Commit();
            DebugLog.Write(LogChannel.Database, "ItemInstanceRepository.StoreSnapshot: inserted " + inserted +
                " rows for '" + character.Name + "'.", LogLevel.Trace);
            return inserted;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(LogChannel.Database, "ItemInstanceRepository.StoreSnapshot: failed for '" +
                character.Name + "', previous rows kept: " + ex.Message, LogLevel.Error);
            throw;
        }
    }
}