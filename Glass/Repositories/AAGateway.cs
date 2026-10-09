using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.World;
using Microsoft.Data.Sqlite;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// AAGateway
//
// Database interface for alternate advancement ability records and their effects.  Nothing is cached
// here; every read and write goes to the AARecords and AAEffects tables.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class AAGateway
{
    private static AAGateway? _instance = null;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor.  The instance is created on first access.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static AAGateway Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new AAGateway();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AAGateway
    //
    // Private constructor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private AAGateway()
    {
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LookupRecord
    //
    // Reads one ability and its effects from the database.
    //
    // id:  The ability to read.
    //
    // Returns the ability, or null when the id is AAId.None or no row has that id.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public AARecord? LookupRecord(AAId id)
    {
        if (!id.Exists)
        {
            DebugLog.Write(LogChannel.Database, "AAGateway.LookupRecord: called with no id.", LogLevel.Warn);
            return null;
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        AARecord? record = ReadRecord(conn, null, id);
        if (record == null)
        {
            DebugLog.Write(LogChannel.Database, "AAGateway.LookupRecord: AA " + id + " is not stored.",
                LogLevel.Trace);
            return null;
        }

        DebugLog.Write(LogChannel.Database, "AAGateway.LookupRecord: found AA " + id + " '" + record.Name + "'.",
            LogLevel.Trace);
        return record;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ReadRecord
    //
    // Reads one ability and its effects through an open connection.  The effects are returned in slot
    // order.
    //
    // conn:  The open connection to read through.
    // tx:    The transaction the reads belong to, or null when there is none.
    // id:    The ability to read.
    //
    // Returns the ability, or null when no row has that id.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private AARecord? ReadRecord(SqliteConnection conn, SqliteTransaction? tx, AAId id)
    {
        AARecord record = new AARecord();

        using (SqliteCommand recordCmd = conn.CreateCommand())
        {
            recordCmd.Transaction = tx;
            recordCmd.CommandText = @"
                SELECT name, description, required_level, cost, seq
                FROM AARecords
                WHERE id = @id";
            recordCmd.Parameters.AddWithValue("@id", id.Value);

            using SqliteDataReader recordReader = recordCmd.ExecuteReader();
            if (!recordReader.Read())
            {
                DebugLog.Write(LogChannel.Database, "AAGateway.ReadRecord: no row for AA " + id + ".",
                    LogLevel.Trace);
                return null;
            }

            record.Id = id;
            record.Name = recordReader.GetString(0);
            record.Description = recordReader.GetString(1);
            record.RequiredLevel = (uint)recordReader.GetInt64(2);
            record.Cost = (uint)recordReader.GetInt64(3);
            record.Seq = (uint)recordReader.GetInt64(4);
        }

        using (SqliteCommand effectCmd = conn.CreateCommand())
        {
            effectCmd.Transaction = tx;
            effectCmd.CommandText = @"
                SELECT slot, spa, base, unknown_1, unknown_2, unknown_3
                FROM AAEffects
                WHERE aa_id = @aa_id
                ORDER BY slot";
            effectCmd.Parameters.AddWithValue("@aa_id", id.Value);

            using SqliteDataReader effectReader = effectCmd.ExecuteReader();
            while (effectReader.Read())
            {
                AAEffect effect = new AAEffect();
                effect.Slot = (uint)effectReader.GetInt64(0);
                effect.Spa = (SPAId)effectReader.GetInt32(1);
                effect.Base = effectReader.GetInt32(2);
                effect.Unknown_1 = (uint)effectReader.GetInt64(3);
                effect.Unknown_2 = (uint)effectReader.GetInt64(4);
                effect.Unknown_3 = (uint)effectReader.GetInt64(5);
                record.Effects.Add(effect);
            }
        }

        DebugLog.Write(LogChannel.Database, "AAGateway.ReadRecord: read AA " + id + " '" + record.Name +
            "' with " + record.Effects.Count + " effects.", LogLevel.Trace);
        return record;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // WriteRecord
    //
    // Writes one ability and its effects through an open connection.  The AARecords row is inserted, or
    // updated in place when a row with the same id exists.  The ability's existing AAEffects rows are
    // deleted and one row is inserted for each of its current effects.
    //
    // conn:    The open connection to write through.
    // tx:      The transaction the writes belong to.
    // record:  The ability to write.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void WriteRecord(SqliteConnection conn, SqliteTransaction tx, AARecord record)
    {
        using (SqliteCommand recordCmd = conn.CreateCommand())
        {
            recordCmd.Transaction = tx;
            recordCmd.CommandText = @"
                INSERT INTO AARecords (id, name, description, required_level, cost, seq)
                VALUES (@id, @name, @description, @required_level, @cost, @seq)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    description = excluded.description,
                    required_level = excluded.required_level,
                    cost = excluded.cost,
                    seq = excluded.seq";
            recordCmd.Parameters.AddWithValue("@id", record.Id.Value);
            recordCmd.Parameters.AddWithValue("@name", record.Name);
            recordCmd.Parameters.AddWithValue("@description", record.Description);
            recordCmd.Parameters.AddWithValue("@required_level", record.RequiredLevel);
            recordCmd.Parameters.AddWithValue("@cost", record.Cost);
            recordCmd.Parameters.AddWithValue("@seq", record.Seq);
            recordCmd.ExecuteNonQuery();
        }
        DebugLog.Write(LogChannel.Database, "AAGateway.WriteRecord: wrote AARecords row for AA " + record.Id +
            " '" + record.Name + "'.", LogLevel.Trace);

        using (SqliteCommand deleteCmd = conn.CreateCommand())
        {
            deleteCmd.Transaction = tx;
            deleteCmd.CommandText = "DELETE FROM AAEffects WHERE aa_id = @aa_id";
            deleteCmd.Parameters.AddWithValue("@aa_id", record.Id.Value);
            int deleted = deleteCmd.ExecuteNonQuery();
            DebugLog.Write(LogChannel.Database, "AAGateway.WriteRecord: deleted " + deleted +
                " AAEffects rows for AA " + record.Id + ".", LogLevel.Trace);
        }

        using (SqliteCommand effectCmd = conn.CreateCommand())
        {
            effectCmd.Transaction = tx;
            effectCmd.CommandText = @"
                INSERT INTO AAEffects (aa_id, slot, spa, base, unknown_1, unknown_2, unknown_3)
                VALUES (@aa_id, @slot, @spa, @base, @unknown_1, @unknown_2, @unknown_3)";
            effectCmd.Parameters.AddWithValue("@aa_id", record.Id.Value);
            SqliteParameter slotParam = effectCmd.Parameters.Add("@slot", SqliteType.Integer);
            SqliteParameter spaParam = effectCmd.Parameters.Add("@spa", SqliteType.Integer);
            SqliteParameter baseParam = effectCmd.Parameters.Add("@base", SqliteType.Integer);
            SqliteParameter unknown1Param = effectCmd.Parameters.Add("@unknown_1", SqliteType.Integer);
            SqliteParameter unknown2Param = effectCmd.Parameters.Add("@unknown_2", SqliteType.Integer);
            SqliteParameter unknown3Param = effectCmd.Parameters.Add("@unknown_3", SqliteType.Integer);

            foreach (AAEffect effect in record.Effects)
            {
                slotParam.Value = effect.Slot;
                spaParam.Value = (int)effect.Spa;
                baseParam.Value = effect.Base;
                unknown1Param.Value = effect.Unknown_1;
                unknown2Param.Value = effect.Unknown_2;
                unknown3Param.Value = effect.Unknown_3;
                effectCmd.ExecuteNonQuery();
            }
        }
        DebugLog.Write(LogChannel.Database, "AAGateway.WriteRecord: inserted " + record.Effects.Count +
            " AAEffects rows for AA " + record.Id + ".", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // StoreRecords
    //
    // Stores a set of abilities in one transaction.  An ability with no stored row is inserted.  An
    // ability whose stored values differ is rewritten.  An ability whose stored values match is left
    // alone.  An ability without an identifier is skipped.  A failure rolls the transaction back and
    // leaves the previous rows in place.
    //
    // records:  The abilities to store.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void StoreRecords(IReadOnlyList<AARecord> records)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteTransaction tx = conn.BeginTransaction();
        try
        {
            uint inserted = 0;
            uint updated = 0;
            uint unchanged = 0;
            uint skipped = 0;

            foreach (AARecord record in records)
            {
                if (!record.Id.Exists)
                {
                    DebugLog.Write(LogChannel.Database, "AAGateway.StoreRecords: skipping an ability with no id, name '" +
                        record.Name + "'.", LogLevel.Warn);
                    skipped++;
                    continue;
                }

                AARecord? stored = ReadRecord(conn, tx, record.Id);
                if (stored == null)
                {
                    WriteRecord(conn, tx, record);
                    inserted++;
                    DebugLog.Write(LogChannel.Database, "AAGateway.StoreRecords: inserted AA " + record.Id + ".",
                        LogLevel.Trace);
                }
                else if (SameValues(stored, record))
                {
                    unchanged++;
                    DebugLog.Write(LogChannel.Database, "AAGateway.StoreRecords: AA " + record.Id + " unchanged.",
                        LogLevel.Trace);
                }
                else
                {
                    WriteRecord(conn, tx, record);
                    updated++;
                    DebugLog.Write(LogChannel.Database, "AAGateway.StoreRecords: updated AA " + record.Id + ".",
                        LogLevel.Trace);
                }
            }

            tx.Commit();
            DebugLog.Write(LogChannel.Database, "AAGateway.StoreRecords: committed. " + inserted + " inserted, " +
                updated + " updated, " + unchanged + " unchanged, " + skipped + " skipped.", LogLevel.Trace);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(LogChannel.Database, "AAGateway.StoreRecords: failed, transaction rolled back: " + ex.Message,
                LogLevel.Error);
            throw;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SameValues
    //
    // Compares two abilities value by value, including their effects in order.  Needed to update stale AA records.
    //
    // first:   One ability.
    // second:  The other ability.
    //
    // Returns true when every value of the two abilities matches.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static bool SameValues(AARecord first, AARecord second)
    {
        if ((first.Id != second.Id) ||
            (first.Name != second.Name) ||
            (first.Description != second.Description) ||
            (first.RequiredLevel != second.RequiredLevel) ||
            (first.Cost != second.Cost) ||
            (first.Seq != second.Seq))
        {
            DebugLog.Write(LogChannel.Database, "AAGateway.SameValues: AA " + first.Id + " and AA " + second.Id +
                " differ in the record values.", LogLevel.Trace);
            return false;
        }

        if (first.Effects.Count != second.Effects.Count)
        {
            DebugLog.Write(LogChannel.Database, "AAGateway.SameValues: AA " + first.Id + " effect count differs, " +
                first.Effects.Count + " and " + second.Effects.Count + ".", LogLevel.Trace);
            return false;
        }

        for (int index = 0; index < first.Effects.Count; index++)
        {
            AAEffect firstEffect = first.Effects[index];
            AAEffect secondEffect = second.Effects[index];

            if ((firstEffect.Slot != secondEffect.Slot) ||
                (firstEffect.Spa != secondEffect.Spa) ||
                (firstEffect.Base != secondEffect.Base) ||
                (firstEffect.Unknown_1 != secondEffect.Unknown_1) ||
                (firstEffect.Unknown_2 != secondEffect.Unknown_2) ||
                (firstEffect.Unknown_3 != secondEffect.Unknown_3))
            {
                DebugLog.Write(LogChannel.Database, "AAGateway.SameValues: AA " + first.Id +
                    " differs at effect index " + index + ".", LogLevel.Trace);
                return false;
            }
        }

        DebugLog.Write(LogChannel.Database, "AAGateway.SameValues: AA " + first.Id + " matches.", LogLevel.Trace);
        return true;
    }
}