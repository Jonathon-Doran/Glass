using Glass.Core.Logging;
using Glass.World;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// MobSightingGateway
//
// Database interface for the MobSightings table, one row per observed spawn instance.  Rows are only ever
// inserted.  Timestamps are stored as ISO 8601 text.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class MobSightingGateway
{
    private static MobSightingGateway? _instance = null;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor.  The instance is created on first access.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static MobSightingGateway Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new MobSightingGateway();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MobSightingGateway
    //
    // Private constructor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private MobSightingGateway()
    {
        DebugLog.Write(LogChannel.Database, "MobSightingGateway: singleton instance created.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // InsertAll
    //
    // Inserts one MobSightings row per given Spawn in a single transaction.  A spawn whose MobId is None
    // is skipped and logged at Warn.  A failure rolls the transaction back, leaving no rows from this call.
    //
    // spawns:  The spawn records to write.
    //
    // Returns the number of rows inserted.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public uint InsertAll(IReadOnlyList<Spawn> spawns)
    {
        if (spawns.Count == 0)
        {
            DebugLog.Write(LogChannel.Database, "MobSightingGateway.InsertAll: no spawns, nothing written.",
                LogLevel.Trace);
            return 0;
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteTransaction tx = conn.BeginTransaction();
        try
        {
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO MobSightings (mob_id, zone_id, spawn_id, x, y, z, level, observed)
                VALUES (@mob_id, @zone_id, @spawn_id, @x, @y, @z, @level, @observed)";
            SqliteParameter mobIdParam = cmd.Parameters.Add("@mob_id", SqliteType.Integer);
            SqliteParameter zoneIdParam = cmd.Parameters.Add("@zone_id", SqliteType.Integer);
            SqliteParameter spawnIdParam = cmd.Parameters.Add("@spawn_id", SqliteType.Integer);
            SqliteParameter xParam = cmd.Parameters.Add("@x", SqliteType.Real);
            SqliteParameter yParam = cmd.Parameters.Add("@y", SqliteType.Real);
            SqliteParameter zParam = cmd.Parameters.Add("@z", SqliteType.Real);
            SqliteParameter levelParam = cmd.Parameters.Add("@level", SqliteType.Integer);
            SqliteParameter observedParam = cmd.Parameters.Add("@observed", SqliteType.Text);

            uint inserted = 0;
            uint skipped = 0;

            foreach (Spawn spawn in spawns)
            {
                if (spawn.MobId.Exists == false)
                {
                    DebugLog.Write(LogChannel.Database, "MobSightingGateway.InsertAll: zoneId=" + spawn.ZoneId
                        + " spawnId=" + spawn.SpawnId + " has no mob id, skipped.", LogLevel.Warn);
                    skipped++;
                    continue;
                }

                mobIdParam.Value = spawn.MobId.Value;
                zoneIdParam.Value = spawn.ZoneId.Value;
                spawnIdParam.Value = spawn.SpawnId.Value;
                xParam.Value = spawn.X;
                yParam.Value = spawn.Y;
                zParam.Value = spawn.Z;
                levelParam.Value = spawn.Level;
                observedParam.Value = spawn.Observed.ToString("o");
                cmd.ExecuteNonQuery();
                inserted++;
            }

            tx.Commit();
            DebugLog.Write(LogChannel.Database, "MobSightingGateway.InsertAll: committed " + inserted + " rows, "
                + skipped + " skipped.", LogLevel.Trace);
            return inserted;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(LogChannel.Database, "MobSightingGateway.InsertAll: failed, transaction rolled back: "
                + ex.Message, LogLevel.Error);
            throw;
        }
    }
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadByZone
    //
    // Reads every MobSightings row for the given zone into new Spawn records.  A zone id that is None
    // yields an empty list.
    //
    // zoneId:  The zone whose sightings are read.
    //
    // Returns the records in ascending order of observation; empty when there are none.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<Spawn> LoadByZone(ZoneId zoneId)
    {
        List<Spawn> spawns = new List<Spawn>();

        if (zoneId.Exists == false)
        {
            DebugLog.Write(LogChannel.Database, "MobSightingGateway.LoadByZone: zoneId is None.", LogLevel.Warn);
            return spawns;
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT mob_id, zone_id, spawn_id, x, y, z, level, observed
            FROM MobSightings
            WHERE zone_id = @zone_id
            ORDER BY observed";
        cmd.Parameters.AddWithValue("@zone_id", zoneId.Value);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            spawns.Add(ReadRecord(reader));
        }

        DebugLog.Write(LogChannel.Database, "MobSightingGateway.LoadByZone: zoneId=" + zoneId + " read "
            + spawns.Count + " sightings.", LogLevel.Trace);
        return spawns;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadByMobId
    //
    // Reads every MobSightings row for the given mob type into new Spawn records, across all zones.  A
    // mob id that is None yields an empty list.
    //
    // mobId:  The mob type whose sightings are read.
    //
    // Returns the records in ascending observed order; empty when there are none.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<Spawn> LoadByMobId(MobId mobId)
    {
        List<Spawn> spawns = new List<Spawn>();

        if (mobId.Exists == false)
        {
            DebugLog.Write(LogChannel.Database, "MobSightingGateway.LoadByMobId: mobId is None.", LogLevel.Warn);
            return spawns;
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT mob_id, zone_id, spawn_id, x, y, z, level, observed
            FROM MobSightings
            WHERE mob_id = @mob_id
            ORDER BY observed";
        cmd.Parameters.AddWithValue("@mob_id", mobId.Value);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            spawns.Add(ReadRecord(reader));
        }

        DebugLog.Write(LogChannel.Database, "MobSightingGateway.LoadByMobId: mobId=" + mobId + " read "
            + spawns.Count + " sightings.", LogLevel.Trace);
        return spawns;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ReadRecord
    //
    // Builds a Spawn record from the current row of a reader positioned on a MobSightings SELECT whose
    // columns are, in order: mob_id, zone_id, spawn_id, x, y, z, level, observed.  The
    // record's Name and CurrentHp are not stored and are left at their defaults.  A timestamp that does
    // not parse as ISO 8601 text is logged at Warn and left at DateTime.MinValue.
    //
    // reader:  A reader positioned on the row to read.
    //
    // Returns the new record.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static Spawn ReadRecord(SqliteDataReader reader)
    {
        Spawn spawn = new Spawn();
        spawn.MobId = new MobId((uint)reader.GetInt64(0));
        spawn.ZoneId = new ZoneId((uint)reader.GetInt64(1));
        spawn.SpawnId = new SpawnId((uint)reader.GetInt64(2));
        spawn.X = (float)reader.GetDouble(3);
        spawn.Y = (float)reader.GetDouble(4);
        spawn.Z = (float)reader.GetDouble(5);
        spawn.Level = (ushort)reader.GetInt64(6);

        string observedText = reader.GetString(7);
        if (DateTime.TryParse(observedText, null, System.Globalization.DateTimeStyles.RoundtripKind,
            out spawn.Observed) == false)
        {
            DebugLog.Write(LogChannel.Database, "MobSightingGateway.ReadRecord: zoneId=" + spawn.ZoneId + " spawnId="
                + spawn.SpawnId + " first_seen='" + observedText + "' does not parse, using MinValue.", LogLevel.Warn);
            spawn.Observed = DateTime.MinValue;
        }

        return spawn;
    }

}
