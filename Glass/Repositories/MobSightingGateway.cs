using Glass.Core.Logging;
using Glass.World;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Windows.Documents;

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
    // is skipped and logged at Warn.  A spawn point's spawn position is written to the spawn coordinate
    // columns; a spawn with no spawn position writes NULL there.  A failure rolls the transaction back,
    // leaving no rows from this call.
    //
    // A spawn in a dynamic zone is not written.
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
                INSERT INTO MobSightings (mob_id, zone_id, x, y, z, level, observed,
                                          is_spawn_point, spawn_x, spawn_y, spawn_z)
                VALUES (@mob_id, @zone_id, @x, @y, @z, @level, @observed,
                        @is_spawn_point, @spawn_x, @spawn_y, @spawn_z)";
            SqliteParameter mobIdParam = cmd.Parameters.Add("@mob_id", SqliteType.Integer);
            SqliteParameter zoneIdParam = cmd.Parameters.Add("@zone_id", SqliteType.Integer);
            SqliteParameter xParam = cmd.Parameters.Add("@x", SqliteType.Real);
            SqliteParameter yParam = cmd.Parameters.Add("@y", SqliteType.Real);
            SqliteParameter zParam = cmd.Parameters.Add("@z", SqliteType.Real);
            SqliteParameter levelParam = cmd.Parameters.Add("@level", SqliteType.Integer);
            SqliteParameter observedParam = cmd.Parameters.Add("@observed", SqliteType.Text);
            SqliteParameter isSpawnPointParam = cmd.Parameters.Add("@is_spawn_point", SqliteType.Integer);
            SqliteParameter spawnXParam = cmd.Parameters.Add("@spawn_x", SqliteType.Real);
            SqliteParameter spawnYParam = cmd.Parameters.Add("@spawn_y", SqliteType.Real);
            SqliteParameter spawnZParam = cmd.Parameters.Add("@spawn_z", SqliteType.Real);

            uint inserted = 0;
            uint skipped = 0;
            uint spawnPoints = 0;

            foreach (Spawn spawn in spawns)
            {
                if (spawn.MobId.Exists == false)
                {
                    DebugLog.Write(LogChannel.Database, "MobSightingGateway.InsertAll: zoneId=" + spawn.ZoneId
                        + " spawnId=" + spawn.SpawnId + " has no mob id, skipped.", LogLevel.Warn);
                    skipped++;
                    continue;
                }

                if (ZoneRepository.IsDynamicZone(spawn.ZoneId))
                {
                    continue;
                }

                mobIdParam.Value = spawn.MobId.Value;
                zoneIdParam.Value = spawn.ZoneId.Value;
                xParam.Value = spawn.X;
                yParam.Value = spawn.Y;
                zParam.Value = spawn.Z;
                levelParam.Value = spawn.Level;
                observedParam.Value = spawn.Observed.ToString("o");
                isSpawnPointParam.Value = spawn.IsSpawnPoint ? 1 : 0;

                if (spawn.SpawnPosition != null)
                {
                    spawnXParam.Value = spawn.SpawnPosition.X;
                    spawnYParam.Value = spawn.SpawnPosition.Y;
                    spawnZParam.Value = spawn.SpawnPosition.Z;
                }
                else
                {
                    spawnXParam.Value = DBNull.Value;
                    spawnYParam.Value = DBNull.Value;
                    spawnZParam.Value = DBNull.Value;

                    if (spawn.IsSpawnPoint)
                    {
                        DebugLog.Write(LogChannel.Database, "MobSightingGateway.InsertAll: zoneId=" + spawn.ZoneId
                            + " spawnId=" + spawn.SpawnId + " is a spawn point with no spawn position.",
                            LogLevel.Trace);
                    }
                }

                if (spawn.IsSpawnPoint)
                {
                    spawnPoints++;
                }

                cmd.ExecuteNonQuery();
                inserted++;
            }

            tx.Commit();
            DebugLog.Write(LogChannel.Database, "MobSightingGateway.InsertAll: committed " + inserted + " rows ("
                + spawnPoints + " spawn points), " + skipped + " skipped.", LogLevel.Trace);
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
            SELECT mob_id, zone_id, x, y, z, level, observed,
                   is_spawn_point, spawn_x, spawn_y, spawn_z
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
            SELECT mob_id, zone_id, x, y, z, level, observed,
                   is_spawn_point, spawn_x, spawn_y, spawn_z
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
    // columns are, in order: mob_id, zone_id, x, y, z, level, observed, is_spawn_point, spawn_x, spawn_y,
    // spawn_z.  The record's Name and CurrentHp are not stored and are left at their defaults.  The spawn
    // position is read when spawn_x is not NULL and left null otherwise.  A timestamp that does not parse
    // as ISO 8601 text is logged at Warn and left at DateTime.MinValue.
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
        spawn.X = (float)reader.GetDouble(2);
        spawn.Y = (float)reader.GetDouble(3);
        spawn.Z = (float)reader.GetDouble(4);
        spawn.Level = (ushort)reader.GetInt64(5);

        string observedText = reader.GetString(6);
        if (DateTime.TryParse(observedText, null, System.Globalization.DateTimeStyles.RoundtripKind,
            out spawn.Observed) == false)
        {
            DebugLog.Write(LogChannel.Database, "MobSightingGateway.ReadRecord: zoneId=" + spawn.ZoneId + " mobId="
                + spawn.MobId + " observed='" + observedText + "' does not parse, using MinValue.", LogLevel.Warn);
            spawn.Observed = DateTime.MinValue;
        }

        spawn.IsSpawnPoint = reader.GetInt64(7) != 0;

        if (reader.IsDBNull(8) == false)
        {
            spawn.SpawnPosition = new Position((float)reader.GetDouble(8), (float)reader.GetDouble(9),
                (float)reader.GetDouble(10));
        }
        else if (spawn.IsSpawnPoint)
        {
            DebugLog.Write(LogChannel.Database, "MobSightingGateway.ReadRecord: zoneId=" + spawn.ZoneId + " mobId="
                + spawn.MobId + " is a spawn point with no spawn position.", LogLevel.Trace);
        }

        return spawn;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CountByZone
    //
    // Counts the MobSightings rows of every zone that has at least one, in a single query.
    //
    // Returns a dictionary keyed by zone id whose values are the row counts; empty when the table has no rows.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Dictionary<ZoneId, uint> CountByZone()
    {
        Dictionary<ZoneId, uint> counts = new Dictionary<ZoneId, uint>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT zone_id, COUNT(*)
            FROM MobSightings
            GROUP BY zone_id";

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ZoneId zoneId = new ZoneId((uint)reader.GetInt64(0));
            uint count = (uint)reader.GetInt64(1);
            counts[zoneId] = count;
        }

        DebugLog.Write(LogChannel.Database, "MobSightingGateway.CountByZone: " + counts.Count
            + " zone(s) have sightings.", LogLevel.Trace);
        return counts;
    }

}
