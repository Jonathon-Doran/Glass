using Glass.Core.Logging;
using Glass.Network.Protocol;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;

namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// PatchLevelSummary
//
// Carries one patch level and its PatchOpcode row count for display in the patch level
// management dialog.
///////////////////////////////////////////////////////////////////////////////////////////////
public class PatchLevelSummary
{
    public PatchLevel Level;
    public int OpcodeCount;
}

///////////////////////////////////////////////////////////////////////////////////////////////
// PatchLevelManager
//
// Enumerates, renames, and deletes patch levels.  A patch level is the
// (patch_date, server_type) pair present in PatchOpcode, PacketField, FieldCollection,
// and Gate.  Bound to an already-open SQLite connection owned by the caller.
///////////////////////////////////////////////////////////////////////////////////////////////
public class PatchLevelManager
{
    private readonly SqliteConnection _connection;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PatchLevelManager (constructor)
    //
    // Constructs a manager bound to an already-open SQLite connection.  The caller owns
    // the connection lifetime.
    //
    // connection:  An open SQLite connection to the Glass database.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public PatchLevelManager(SqliteConnection connection)
    {
        if (connection == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelManager.ctor: connection is null", LogLevel.Error);
            throw new ArgumentNullException(nameof(connection));
        }

        if (connection.State != ConnectionState.Open)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelManager.ctor: connection is not open (state=" + connection.State + ")", LogLevel.Error);
            throw new InvalidOperationException("Connection must be open.");
        }

        _connection = connection;
        DebugLog.Write(LogChannel.InferenceDebug, "PatchLevelManager.ctor: constructed", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetAllLevels
    //
    // Returns every patch level, each with its opcode row count, ordered by patch date
    // then server type.  Levels with no opcodes appear with a count of zero.
    //
    // Returns:  One PatchLevelSummary per patch level; empty if none exist.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<PatchLevelSummary> GetAllLevels()
    {
        DebugLog.Write(LogChannel.InferenceDebug, "PatchLevelManager.GetAllLevels: querying", LogLevel.Trace);

        List<PatchLevelSummary> summaries = new List<PatchLevelSummary>();

        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "SELECT pl.patch_date, pl.server_type, COUNT(po.id) " +
            "FROM PatchLevel pl " +
            "LEFT JOIN PatchOpcode po " +
            "  ON po.patch_date = pl.patch_date AND po.server_type = pl.server_type " +
            "GROUP BY pl.patch_date, pl.server_type " +
            "ORDER BY pl.patch_date, pl.server_type";

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            PatchLevelSummary summary = new PatchLevelSummary();
            summary.Level = new PatchLevel(reader.GetString(0), reader.GetString(1));
            summary.OpcodeCount = reader.GetInt32(2);
            summaries.Add(summary);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelManager.GetAllLevels: found " + summaries.Count + " patch levels", LogLevel.Trace);
        return summaries;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Exists
    //
    // Returns true if the given patch level is present in the PatchLevel table.
    //
    // level:  The patch level to test.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool Exists(PatchLevel level)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM PatchLevel " +
            "WHERE patch_date = @patch_date AND server_type = @server_type";
        command.Parameters.AddWithValue("@patch_date", level.PatchDate);
        command.Parameters.AddWithValue("@server_type", level.ServerType);

        int count = Convert.ToInt32(command.ExecuteScalar());

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelManager.Exists: " + level + " count=" + count, LogLevel.Trace);
        return count > 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Rename
    //
    // Reassigns every row belonging to the given patch level to a new patch level.  Runs
    // in a single transaction; throws and rolls back if the target level already exists.
    //
    // level:     The patch level to rename.
    // newLevel:  The patch level to assign.
    //
    // Returns:  The total number of rows updated.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int Rename(PatchLevel level, PatchLevel newLevel)
    {
        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelManager.Rename: level=" + level + " newLevel=" + newLevel, LogLevel.Trace);

        if (newLevel.IsNone)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelManager.Rename: newLevel is None", LogLevel.Error);
            throw new ArgumentException("newLevel must identify a patch.", nameof(newLevel));
        }

        if (level == newLevel)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelManager.Rename: new level matches current level (" + level + ")", LogLevel.Error);
            throw new ArgumentException("New patch level must differ from the current level.");
        }

        using SqliteTransaction transaction = _connection.BeginTransaction();

        try
        {
            if (LevelHasOpcodes(transaction, newLevel.PatchDate, newLevel.ServerType))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PatchLevelManager.Rename: target " + newLevel + " already has rows", LogLevel.Error);
                throw new InvalidOperationException(
                    "Target patch level " + newLevel + " already contains rows.");
            }

            int totalUpdated = 0;
            totalUpdated += UpdateLevel(transaction, "PatchLevel", level, newLevel);
            totalUpdated += UpdateLevel(transaction, "PatchOpcode", level, newLevel);
            totalUpdated += UpdateLevel(transaction, "PacketField", level, newLevel);
            totalUpdated += UpdateLevel(transaction, "FieldCollection", level, newLevel);
            totalUpdated += UpdateLevel(transaction, "Gate", level, newLevel);

            transaction.Commit();

            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelManager.Rename: committed, " + totalUpdated + " rows updated", LogLevel.Trace);
            return totalUpdated;
        }
        catch (Exception ex)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelManager.Rename: failed, rolling back: " + ex.Message, LogLevel.Error);
            transaction.Rollback();
            throw;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LevelHasOpcodes
    //
    // Returns true if any PatchOpcode row exists for the given (patch_date, server_type)
    // pair.  
    //
    // transaction:  Active transaction.  The query runs inside it.
    // patchDate:    Patch date to test.
    // serverType:   Server type to test.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private bool LevelHasOpcodes(SqliteTransaction transaction, string patchDate, string serverType)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM PatchOpcode " +
            "WHERE patch_date = @patch_date AND server_type = @server_type";
        command.Parameters.AddWithValue("@patch_date", patchDate);
        command.Parameters.AddWithValue("@server_type", serverType);

        int count = Convert.ToInt32(command.ExecuteScalar());

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelManager.LevelHasOpcodes: (" + patchDate + "," + serverType + ") has "
            + count + " PatchOpcode rows", LogLevel.Trace);
        return count > 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // UpdateLevel
    //
    // Sets patch_date and server_type on every row of the named table matching the given
    // patch level.
    //
    // SQL injection is not possible through tableName.  SQLite cannot parameterize an
    // identifier, so the table name is concatenated into the command text.  This is safe
    // because tableName only ever receives compile-time string literals; no user input
    // reaches this parameter.  All value comparisons are parameterized.
    //
    // transaction:  Active transaction.  The update runs inside it.
    // tableName:    The table to update.  Must be a compile-time string literal.
    // level:        The patch level whose rows to update.
    // newLevel:     The patch level to assign.
    //
    // Returns:  The number of rows updated.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private int UpdateLevel(SqliteTransaction transaction, string tableName, PatchLevel level, PatchLevel newLevel)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE " + tableName + " SET patch_date = @new_patch_date, server_type = @new_server_type " +
            "WHERE patch_date = @patch_date AND server_type = @server_type";
        command.Parameters.AddWithValue("@new_patch_date", newLevel.PatchDate);
        command.Parameters.AddWithValue("@new_server_type", newLevel.ServerType);
        command.Parameters.AddWithValue("@patch_date", level.PatchDate);
        command.Parameters.AddWithValue("@server_type", level.ServerType);

        int updated = command.ExecuteNonQuery();

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelManager.UpdateLevel: " + tableName + " updated " + updated + " rows", LogLevel.Trace);
        return updated;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Delete
    //
    // Deletes every row belonging to the given patch level.  Runs in a single
    // transaction; throws and rolls back on any error.
    //
    // level:  The patch level to delete.
    //
    // Returns:  The total number of rows deleted.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int Delete(PatchLevel level)
    {
        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelManager.Delete: level=" + level, LogLevel.Trace);

        if (level.IsNone)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelManager.Delete: level is None", LogLevel.Error);
            throw new ArgumentException("level must identify a patch.", nameof(level));
        }

        using SqliteTransaction transaction = _connection.BeginTransaction();

        try
        {
            int totalDeleted = 0;
            totalDeleted += DeleteLevelRows(transaction, "PatchLevel", level);
            totalDeleted += DeleteLevelRows(transaction, "PatchOpcode", level);
            totalDeleted += DeleteLevelRows(transaction, "PacketField", level);
            totalDeleted += DeleteLevelRows(transaction, "FieldCollection", level);
            totalDeleted += DeleteLevelRows(transaction, "Gate", level);

            transaction.Commit();

            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelManager.Delete: committed, " + totalDeleted + " rows deleted", LogLevel.Trace);
            return totalDeleted;
        }
        catch (Exception ex)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelManager.Delete: failed, rolling back: " + ex.Message, LogLevel.Error);
            transaction.Rollback();
            throw;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // DeleteLevelRows
    //
    // Deletes every row of the named table matching the given patch level.
    //
    // SQL injection is not possible through tableName.  SQLite cannot parameterize an
    // identifier, so the table name is concatenated into the command text.  This is safe
    // because tableName only ever receives compile-time string literals; no user input
    // reaches this parameter.  All value comparisons are parameterized.
    //
    // transaction:  Active transaction.  The delete runs inside it.
    // tableName:    The table to delete from.  Must be a compile-time string literal.
    // level:        The patch level whose rows to delete.
    //
    // Returns:  The number of rows deleted.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private int DeleteLevelRows(SqliteTransaction transaction, string tableName, PatchLevel level)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM " + tableName + " " +
            "WHERE patch_date = @patch_date AND server_type = @server_type";
        command.Parameters.AddWithValue("@patch_date", level.PatchDate);
        command.Parameters.AddWithValue("@server_type", level.ServerType);

        int deleted = command.ExecuteNonQuery();

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelManager.DeleteLevelRows: " + tableName + " deleted " + deleted + " rows", LogLevel.Trace);
        return deleted;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Create
    //
    // Inserts a row into PatchLevel for the given patch level, inside the caller's
    // transaction.  Throws if the level already exists (UNIQUE constraint).
    //
    // transaction:  Active transaction.  The insert runs inside it.
    // level:        The patch level to create.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Create(SqliteTransaction transaction, PatchLevel level)
    {
        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelManager.Create: level=" + level, LogLevel.Trace);

        if (level.IsNone)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelManager.Create: level is None", LogLevel.Error);
            throw new ArgumentException("level must identify a patch.", nameof(level));
        }

        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO PatchLevel (patch_date, server_type) " +
            "VALUES (@patch_date, @server_type)";
        command.Parameters.AddWithValue("@patch_date", level.PatchDate);
        command.Parameters.AddWithValue("@server_type", level.ServerType);

        command.ExecuteNonQuery();

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelManager.Create: inserted " + level, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Create
    //
    // Inserts a row into PatchLevel for the given patch level, in its own transaction.
    // Throws if the level already exists (UNIQUE constraint).
    //
    // level:  The patch level to create.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Create(PatchLevel level)
    {
        using SqliteTransaction transaction = _connection.BeginTransaction();

        try
        {
            Create(transaction, level);
            transaction.Commit();
        }
        catch (Exception ex)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelManager.Create: failed, rolling back: " + ex.Message, LogLevel.Error);
            transaction.Rollback();
            throw;
        }
    }
}
