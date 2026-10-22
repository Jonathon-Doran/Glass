using Glass.Core.Logging;
using Glass.World;
using Microsoft.Data.Sqlite;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// MobTypeGateway
//
// Database interface for the MobTypes table.  Nothing is cached here; every read and write goes to the
// table.  Timestamps are stored as ISO 8601 text.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class MobTypeGateway
{
    private static MobTypeGateway? _instance = null;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor.  The instance is created on first access.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static MobTypeGateway Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new MobTypeGateway();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MobTypeGateway
    //
    // Private constructor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private MobTypeGateway()
    {
        DebugLog.Write(LogChannel.Database, "MobTypeGateway: singleton instance created.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Insert
    //
    // Inserts a new MobTypes row from the given record and assigns the row id to the record's Id.  A record
    // whose Id already exists, or whose BaseName is empty, is rejected and nothing is written.  A base name
    // that already has a row raises a constraint exception, which is not caught here.
    //
    // mobType:  The record to insert.  Every column is written from the record's current field values.
    //
    // Returns the assigned MobId, or MobId.None when the record was rejected.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public MobId Insert(MobType mobType)
    {
        if (mobType.Id.Exists)
        {
            DebugLog.Write(LogChannel.Database, "MobTypeGateway.Insert: record already has id=" + mobType.Id
                + ", nothing written.", LogLevel.Error);
            return MobId.None;
        }

        if (string.IsNullOrEmpty(mobType.BaseName) == true)
        {
            DebugLog.Write(LogChannel.Database, "MobTypeGateway.Insert: record has no base name, nothing written.",
                LogLevel.Error);
            return MobId.None;
        }

        if (mobType.ZoneId.Exists == false)
        {
            DebugLog.Write(LogChannel.Database, "MobTypeGateway.Insert: record for '" + mobType.BaseName
                + "' has no zone id, nothing written.", LogLevel.Error);
            return MobId.None;
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO MobTypes (zone_id, base_name, model, class, body_type, size, walk_speed, run_speed,
                                  level_min, level_max, first_seen, last_seen, sightings,
                                  hits_observed, damage_total, damage_max, attack_interval_sum_ms,
                                  attack_interval_count, spells_cast, mana_pool, max_hp, kills, deaths)
            VALUES (@zone_id, @base_name, @model, @class, @body_type, @size, @walk_speed, @run_speed,
                    @level_min, @level_max, @first_seen, @last_seen, @sightings,
                    @hits_observed, @damage_total, @damage_max, @attack_interval_sum_ms,
                    @attack_interval_count, @spells_cast, @mana_pool, @max_hp, @kills, @deaths);
            SELECT last_insert_rowid();";
        AddParameters(cmd, mobType);

        object? result = cmd.ExecuteScalar();
        if ((result == null) || (result == DBNull.Value))
        {
            DebugLog.Write(LogChannel.Database, "MobTypeGateway.Insert: no row id returned for base_name='"
                + mobType.BaseName + "'.", LogLevel.Error);
            return MobId.None;
        }

        mobType.Id = new MobId((uint)(long)result);
        DebugLog.Write(LogChannel.Database, "MobTypeGateway.Insert: inserted id=" + mobType.Id + " base_name='"
            + mobType.BaseName + "'.", LogLevel.Trace);
        return mobType.Id;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Update
    //
    // Writes every column of the MobTypes row whose id matches the record's Id from the record's current
    // field values.  A record whose Id is None is rejected and nothing is written.  An id with no row is
    // logged at Error.
    //
    // mobType:  The record to write.
    //
    // Returns true if one row was updated, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool Update(MobType mobType)
    {
        if (mobType.Id.Exists == false)
        {
            DebugLog.Write(LogChannel.Database, "MobTypeGateway.Update: record has no id, nothing written.",
                LogLevel.Error);
            return false;
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE MobTypes SET
                zone_id = @zone_id, base_name = @base_name, model = @model, class = @class, body_type = @body_type,
                size = @size, walk_speed = @walk_speed, run_speed = @run_speed,
                level_min = @level_min, level_max = @level_max,
                first_seen = @first_seen, last_seen = @last_seen, sightings = @sightings,
                hits_observed = @hits_observed, damage_total = @damage_total, damage_max = @damage_max,
                attack_interval_sum_ms = @attack_interval_sum_ms, attack_interval_count = @attack_interval_count,
                spells_cast = @spells_cast, mana_pool = @mana_pool, max_hp = @max_hp,
                kills = @kills, deaths = @deaths
            WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", mobType.Id.Value);
        AddParameters(cmd, mobType);

        int rowsChanged = cmd.ExecuteNonQuery();
        if (rowsChanged != 1)
        {
            DebugLog.Write(LogChannel.Database, "MobTypeGateway.Update: id=" + mobType.Id + " changed " + rowsChanged
                + " rows, expected 1.", LogLevel.Error);
            return false;
        }

        DebugLog.Write(LogChannel.Database, "MobTypeGateway.Update: updated id=" + mobType.Id + " base_name='"
            + mobType.BaseName + "'.", LogLevel.Trace);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AddParameters
    //
    // Copies data out of a MobType to the given cmd.  Timestamps are written as ISO 8601 text.
    //
    // cmd:      The command receiving the parameters.
    // mobType:  The record supplying the values.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static void AddParameters(SqliteCommand cmd, MobType mobType)
    {
        cmd.Parameters.AddWithValue("@zone_id", mobType.ZoneId.Value);
        cmd.Parameters.AddWithValue("@base_name", mobType.BaseName);
        cmd.Parameters.AddWithValue("@model", mobType.Model);
        cmd.Parameters.AddWithValue("@class", mobType.Class);
        cmd.Parameters.AddWithValue("@body_type", mobType.BodyType);
        cmd.Parameters.AddWithValue("@size", mobType.Size);
        cmd.Parameters.AddWithValue("@walk_speed", mobType.WalkSpeed);
        cmd.Parameters.AddWithValue("@run_speed", mobType.RunSpeed);
        cmd.Parameters.AddWithValue("@level_min", mobType.LevelMin);
        cmd.Parameters.AddWithValue("@level_max", mobType.LevelMax);
        cmd.Parameters.AddWithValue("@first_seen", mobType.FirstSeen.ToString("o"));
        cmd.Parameters.AddWithValue("@last_seen", mobType.LastSeen.ToString("o"));
        cmd.Parameters.AddWithValue("@sightings", mobType.Sightings);
        cmd.Parameters.AddWithValue("@hits_observed", mobType.HitsObserved);
        cmd.Parameters.AddWithValue("@damage_total", mobType.DamageTotal);
        cmd.Parameters.AddWithValue("@damage_max", mobType.DamageMax);
        cmd.Parameters.AddWithValue("@attack_interval_sum_ms", mobType.AttackIntervalSumMs);
        cmd.Parameters.AddWithValue("@attack_interval_count", mobType.AttackIntervalCount);
        cmd.Parameters.AddWithValue("@spells_cast", mobType.SpellsCast);
        cmd.Parameters.AddWithValue("@mana_pool", mobType.ManaPool);
        cmd.Parameters.AddWithValue("@max_hp", mobType.MaxHp);
        cmd.Parameters.AddWithValue("@kills", mobType.Kills);
        cmd.Parameters.AddWithValue("@deaths", mobType.Deaths);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadAll
    //
    // Reads every MobTypes row into a new MobType record.  Timestamps that do not parse as ISO 8601 text
    // are logged at Warn and left at DateTime.MinValue.
    //
    // Returns the records in ascending id order; empty when the table has no rows.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<MobType> LoadAll()
    {
        List<MobType> mobTypes = new List<MobType>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, zone_id, base_name, model, class, body_type, size, walk_speed, run_speed,
                   level_min, level_max, first_seen, last_seen, sightings,
                   hits_observed, damage_total, damage_max, attack_interval_sum_ms,
                   attack_interval_count, spells_cast, mana_pool, max_hp, kills, deaths
            FROM MobTypes
            ORDER BY id";

        using SqliteDataReader reader = cmd.ExecuteReader();
        mobTypes.Add(ReadRecord(reader));

        DebugLog.Write(LogChannel.Database, "MobTypeGateway.LoadAll: read " + mobTypes.Count + " mob types.",
            LogLevel.Trace);
        return mobTypes;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TryLoadByName
    //
    // Reads the one MobTypes row with the given base name into a new MobType record.
    //
    // zoneId:    The zone this type belongs to
    // baseName:  The base name to look up, matched exactly.
    // mobType:   Receives the record if a row exists, null otherwise.
    //
    // Returns true if a row was found, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool TryLoadByName(ZoneId zoneId, string baseName, [NotNullWhen(true)] out MobType? mobType)
    {
        mobType = null;

        if (string.IsNullOrEmpty(baseName) == true)
        {
            DebugLog.Write(LogChannel.Database, "MobTypeGateway.TryLoadByName: base name is empty.", LogLevel.Warn);
            return false;
        }

        if (zoneId.Exists == false)
        {
            DebugLog.Write(LogChannel.Database, "MobTypeGateway.TryLoadByName: zoneId is None.", LogLevel.Warn);
            return false;
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, zone_id, base_name, model, class, body_type, size, walk_speed, run_speed,
                   level_min, level_max, first_seen, last_seen, sightings,
                   hits_observed, damage_total, damage_max, attack_interval_sum_ms,
                   attack_interval_count, spells_cast, mana_pool, max_hp, kills, deaths
            FROM MobTypes
            WHERE zone_id = @zone_id AND base_name = @base_name";
        cmd.Parameters.AddWithValue("@zone_id", zoneId.Value);
        cmd.Parameters.AddWithValue("@base_name", baseName);

        using SqliteDataReader reader = cmd.ExecuteReader();
        if (reader.Read() == false)
        {
            DebugLog.Write(LogChannel.Database, "MobTypeGateway.TryLoadByName: no row for zoneId=" + zoneId + " base_name='" + baseName
                + "'.", LogLevel.Trace);
            return false;
        }

        mobType = ReadRecord(reader);
        DebugLog.Write(LogChannel.Database, "MobTypeGateway.TryLoadByName: loaded id=" + mobType.Id + " base_name='"
            + baseName + "'.", LogLevel.Trace);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ReadRecord
    //
    // Builds a MobType record from the current row of a reader positioned on a MobTypes SELECT that lists
    // every column in table order.
    //
    // reader:  A reader positioned on the row to read.
    //
    // Returns the new record.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static MobType ReadRecord(SqliteDataReader reader)
    {
        MobType mobType = new MobType();
        mobType.Id = new MobId((uint)reader.GetInt64(0));
        mobType.ZoneId = new ZoneId((uint)reader.GetInt64(1));
        mobType.BaseName = reader.GetString(2);
        mobType.Model = (uint)reader.GetInt64(3);
        mobType.Class = (uint)reader.GetInt64(4);
        mobType.BodyType = (uint)reader.GetInt64(5);
        mobType.Size = (float)reader.GetDouble(6);
        mobType.WalkSpeed = (float)reader.GetDouble(7);
        mobType.RunSpeed = (float)reader.GetDouble(8);
        mobType.LevelMin = (ushort)reader.GetInt64(9);
        mobType.LevelMax = (ushort)reader.GetInt64(10);
        mobType.FirstSeen = ParseTimestamp(reader.GetString(11), mobType.Id, "first_seen");
        mobType.LastSeen = ParseTimestamp(reader.GetString(12), mobType.Id, "last_seen");
        mobType.Sightings = (uint)reader.GetInt64(13);
        mobType.HitsObserved = (uint)reader.GetInt64(14);
        mobType.DamageTotal = (ulong)reader.GetInt64(15);
        mobType.DamageMax = (uint)reader.GetInt64(16);
        mobType.AttackIntervalSumMs = (ulong)reader.GetInt64(17);
        mobType.AttackIntervalCount = (uint)reader.GetInt64(18);
        mobType.SpellsCast = (uint)reader.GetInt64(19);
        mobType.ManaPool = (uint)reader.GetInt64(20);
        mobType.MaxHp = (uint)reader.GetInt64(21);
        mobType.Kills = (uint)reader.GetInt64(22);
        mobType.Deaths = (uint)reader.GetInt64(23);
        return mobType;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ParseTimestamp
    //
    // Converts ISO 8601 text from a MobTypes timestamp column to a DateTime.  Text that does not parse is
    // logged at Warn and yields DateTime.MinValue.
    //
    // text:    The stored timestamp text.
    // id:      The row's MobId, for the log message.
    // column:  The column name, for the log message.
    //
    // Returns the parsed DateTime, or DateTime.MinValue when the text does not parse.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static DateTime ParseTimestamp(string text, MobId id, string column)
    {
        DateTime value = DateTime.MinValue;
        if (DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.RoundtripKind, out value) == false)
        {
            DebugLog.Write(LogChannel.Database, "MobTypeGateway.ParseTimestamp: id=" + id + " " + column + "='"
                + text + "' does not parse, using MinValue.", LogLevel.Warn);
            return DateTime.MinValue;
        }

        return value;
    }
}
