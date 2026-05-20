using Glass.Core.Logging;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;

namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// PatchLevelDuplicator
//
// Duplicates a single source patch level into a new target patch date.  Source rows are
// not modified.  Tables duplicated, in dependency order:
//
//     PatchOpcode
//       -> PatchOpcodeChannel
//       -> PacketOptionalGroup
//            -> PacketOptionalField
//       -> PacketField
//
// Old-to-new id mapping is built by matching on (opcode_name, version) within the same
// server type for opcodes, and on (patch_opcode_id, bit_offset) for optional groups.
// The whole duplication runs in a single transaction.
///////////////////////////////////////////////////////////////////////////////////////////////
public class PatchLevelDuplicator
{
    private readonly SqliteConnection _connection;


    ///////////////////////////////////////////////////////////////////////////////////////////
    // PatchLevelDuplicator (constructor)
    //
    // Constructs a duplicator bound to an already-open SQLite connection.  The caller owns
    // the connection lifetime.
    //
    // connection:  An open SQLite connection to the Glass database.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public PatchLevelDuplicator(SqliteConnection connection)
    {
        if (connection == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.ctor: connection is null");
            throw new ArgumentNullException(nameof(connection));
        }

        if (connection.State != ConnectionState.Open)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.ctor: connection is not open (state=" + connection.State + ")");
            throw new InvalidOperationException("Connection must be open.");
        }

        _connection = connection;
        DebugLog.Write(LogChannel.InferenceDebug, "PatchLevelDuplicator.ctor: constructed");
    }


    ///////////////////////////////////////////////////////////////////////////////////////////
    // Duplicate
    //
    // Duplicates the source patch level into targetPatchDate, preserving server type.
    // Throws on any error and rolls back the transaction.  Returns the number of
    // PatchOpcode rows duplicated.
    //
    // sourcePatchDate:    Patch date of the source rows.
    // sourceServerType:   Server type of the source rows.  Carried over to the target.
    // targetPatchDate:    Patch date to assign to the duplicated rows.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int Duplicate(string sourcePatchDate, string sourceServerType, string targetPatchDate)
    {
        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.Duplicate: source=(" + sourcePatchDate + "," + sourceServerType
            + ") target=" + targetPatchDate);

        if (string.IsNullOrWhiteSpace(sourcePatchDate))
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: sourcePatchDate is empty");
            throw new ArgumentException("sourcePatchDate is required.", nameof(sourcePatchDate));
        }

        if (string.IsNullOrWhiteSpace(sourceServerType))
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: sourceServerType is empty");
            throw new ArgumentException("sourceServerType is required.", nameof(sourceServerType));
        }

        if (string.IsNullOrWhiteSpace(targetPatchDate))
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: targetPatchDate is empty");
            throw new ArgumentException("targetPatchDate is required.", nameof(targetPatchDate));
        }

        if (sourcePatchDate == targetPatchDate)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: source and target patch dates are identical ("
                + sourcePatchDate + ")");
            throw new ArgumentException("Source and target patch dates must differ.");
        }

        using SqliteTransaction transaction = _connection.BeginTransaction();

        try
        {
            int existingTargetRows = CountTargetOpcodes(transaction, targetPatchDate, sourceServerType);
            if (existingTargetRows > 0)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PatchLevelDuplicator.Duplicate: target (" + targetPatchDate + "," + sourceServerType
                    + ") already has " + existingTargetRows + " PatchOpcode rows");
                throw new InvalidOperationException(
                    "Target patch level (" + targetPatchDate + ", " + sourceServerType + ") already contains rows.");
            }

            Dictionary<long, long> opcodeIdMap = DuplicatePatchOpcodes(
                transaction, sourcePatchDate, sourceServerType, targetPatchDate);

            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: duplicated " + opcodeIdMap.Count + " PatchOpcode rows");

            if (opcodeIdMap.Count == 0)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PatchLevelDuplicator.Duplicate: no source rows for (" + sourcePatchDate + ","
                    + sourceServerType + "); nothing to do");
                transaction.Commit();
                return 0;
            }

            int channelCount = DuplicatePatchOpcodeChannels(transaction, opcodeIdMap);
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: duplicated " + channelCount + " PatchOpcodeChannel rows");

            Dictionary<long, long> groupIdMap = DuplicatePacketOptionalGroups(transaction, opcodeIdMap);
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: duplicated " + groupIdMap.Count + " PacketOptionalGroup rows");

            int fieldCount = DuplicatePacketFields(transaction, opcodeIdMap);
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: duplicated " + fieldCount + " PacketField rows");

            int optionalFieldCount = DuplicatePacketOptionalFields(transaction, groupIdMap);
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: duplicated " + optionalFieldCount + " PacketOptionalField rows");

            transaction.Commit();
            DebugLog.Write(LogChannel.InferenceDebug, "PatchLevelDuplicator.Duplicate: transaction committed");

            return opcodeIdMap.Count;
        }
        catch (Exception ex)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: failed, rolling back: " + ex.Message);
            transaction.Rollback();
            throw;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CountTargetOpcodes
    //
    // Returns the number of PatchOpcode rows already present for the target (patch_date,
    // server_type) pair.  Used as a precondition guard so the duplicator does not mix
    // freshly-duplicated rows into a target level that already contains data.
    //
    // transaction:        Active transaction.  The query runs inside it.
    // targetPatchDate:    Patch date to count under.
    // sourceServerType:   Server type to count under (carried over from the source).
    ///////////////////////////////////////////////////////////////////////////////////////////
    private int CountTargetOpcodes(SqliteTransaction transaction, string targetPatchDate, string sourceServerType)
    {
        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.CountTargetOpcodes: target=(" + targetPatchDate + "," + sourceServerType + ")");

        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM PatchOpcode " +
            "WHERE patch_date = @patch_date AND server_type = @server_type";
        command.Parameters.AddWithValue("@patch_date", targetPatchDate);
        command.Parameters.AddWithValue("@server_type", sourceServerType);

        object? scalar = command.ExecuteScalar();
        if (scalar == null || scalar == DBNull.Value)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.CountTargetOpcodes: COUNT(*) returned null; treating as 0");
            return 0;
        }

        int count = Convert.ToInt32(scalar);
        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.CountTargetOpcodes: target has " + count + " existing PatchOpcode rows");
        return count;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // DuplicatePatchOpcodes
    //
    // Reads every PatchOpcode row for (sourcePatchDate, sourceServerType) and inserts a
    // matching row with patch_date = targetPatchDate.  Server type, opcode_value,
    // opcode_name, version, and byte_length are preserved verbatim.  Wire values will be
    // wrong for a new patch and are expected to be edited afterward.
    //
    // Returns a dictionary mapping each source PatchOpcode.id to its newly-inserted
    // target id.  This map is consumed by the downstream child-table duplicators.
    //
    // transaction:        Active transaction.  All reads and writes run inside it.
    // sourcePatchDate:    Patch date of the source rows.
    // sourceServerType:   Server type of the source rows.
    // targetPatchDate:    Patch date to assign to the inserted rows.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private Dictionary<long, long> DuplicatePatchOpcodes(
        SqliteTransaction transaction,
        string sourcePatchDate,
        string sourceServerType,
        string targetPatchDate)
    {
        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePatchOpcodes: source=(" + sourcePatchDate + ","
            + sourceServerType + ") target=" + targetPatchDate);

        Dictionary<long, long> idMap = new Dictionary<long, long>();

        List<SourceRow> sourceRows = new List<SourceRow>();

        using (SqliteCommand selectCommand = _connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText =
                "SELECT id, opcode_value, opcode_name, version, byte_length " +
                "FROM PatchOpcode " +
                "WHERE patch_date = @patch_date AND server_type = @server_type " +
                "ORDER BY id";
            selectCommand.Parameters.AddWithValue("@patch_date", sourcePatchDate);
            selectCommand.Parameters.AddWithValue("@server_type", sourceServerType);

            using SqliteDataReader reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                SourceRow row = new SourceRow();
                row.SourceId = reader.GetInt64(0);
                row.OpcodeValue = reader.GetInt64(1);
                row.OpcodeName = reader.GetString(2);
                row.Version = reader.GetInt32(3);
                row.ByteLength = reader.IsDBNull(4) ? null : reader.GetInt32(4);
                sourceRows.Add(row);
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePatchOpcodes: read " + sourceRows.Count + " source rows");

        if (sourceRows.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.DuplicatePatchOpcodes: nothing to duplicate");
            return idMap;
        }

        using SqliteCommand insertCommand = _connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            "INSERT INTO PatchOpcode " +
            "(patch_date, server_type, opcode_value, opcode_name, version, byte_length) " +
            "VALUES (@patch_date, @server_type, @opcode_value, @opcode_name, @version, @byte_length); " +
            "SELECT last_insert_rowid();";

        SqliteParameter patchDateParameter = insertCommand.Parameters.Add("@patch_date", SqliteType.Text);
        SqliteParameter serverTypeParameter = insertCommand.Parameters.Add("@server_type", SqliteType.Text);
        SqliteParameter opcodeValueParameter = insertCommand.Parameters.Add("@opcode_value", SqliteType.Integer);
        SqliteParameter opcodeNameParameter = insertCommand.Parameters.Add("@opcode_name", SqliteType.Text);
        SqliteParameter versionParameter = insertCommand.Parameters.Add("@version", SqliteType.Integer);
        SqliteParameter byteLengthParameter = insertCommand.Parameters.Add("@byte_length", SqliteType.Integer);

        patchDateParameter.Value = targetPatchDate;
        serverTypeParameter.Value = sourceServerType;

        for (int rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
        {
            SourceRow row = sourceRows[rowIndex];

            opcodeValueParameter.Value = row.OpcodeValue;
            opcodeNameParameter.Value = row.OpcodeName;
            versionParameter.Value = row.Version;
            if (row.ByteLength.HasValue)
            {
                byteLengthParameter.Value = row.ByteLength.Value;
            }
            else
            {
                byteLengthParameter.Value = DBNull.Value;
            }

            object? scalar = insertCommand.ExecuteScalar();
            if (scalar == null || scalar == DBNull.Value)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PatchLevelDuplicator.DuplicatePatchOpcodes: last_insert_rowid returned null for source id="
                    + row.SourceId + " name=" + row.OpcodeName);
                throw new InvalidOperationException(
                    "Failed to retrieve new id for duplicated PatchOpcode row (source id=" + row.SourceId + ").");
            }

            long newId = Convert.ToInt64(scalar);
            idMap[row.SourceId] = newId;

            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.DuplicatePatchOpcodes: " + row.OpcodeName
                + " v" + row.Version + " source_id=" + row.SourceId + " new_id=" + newId);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePatchOpcodes: inserted " + idMap.Count + " rows");
        return idMap;
    }


    ///////////////////////////////////////////////////////////////////////////////////////////
    // SourceRow
    //
    // Carries one PatchOpcode row read from the source patch level.  Used as a local
    // staging type so the read pass closes its reader before the write pass begins,
    // which avoids reader-while-writing on the same connection.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class SourceRow
    {
        public long SourceId;
        public long OpcodeValue;
        public string OpcodeName = string.Empty;
        public int Version;
        public int? ByteLength;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // DuplicatePatchOpcodeChannels
    //
    // For every PatchOpcodeChannel row whose patch_opcode_id is a key in opcodeIdMap,
    // inserts a copy that points at the corresponding new patch_opcode_id.  The channel
    // string is carried over verbatim.
    //
    // Returns the number of rows inserted.
    //
    // transaction:  Active transaction.  All reads and writes run inside it.
    // opcodeIdMap:  Source PatchOpcode.id -> new PatchOpcode.id, produced by
    //               DuplicatePatchOpcodes.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private int DuplicatePatchOpcodeChannels(SqliteTransaction transaction, Dictionary<long, long> opcodeIdMap)
    {
        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePatchOpcodeChannels: " + opcodeIdMap.Count + " opcode ids in scope");

        if (opcodeIdMap.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.DuplicatePatchOpcodeChannels: opcodeIdMap is empty; nothing to do");
            return 0;
        }

        List<SourceChannelRow> sourceRows = new List<SourceChannelRow>();

        using (SqliteCommand selectCommand = _connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText =
                "SELECT patch_opcode_id, channel " +
                "FROM PatchOpcodeChannel " +
                "WHERE patch_opcode_id = @patch_opcode_id";
            SqliteParameter patchOpcodeIdParameter = selectCommand.Parameters.Add("@patch_opcode_id", SqliteType.Integer);

            foreach (KeyValuePair<long, long> entry in opcodeIdMap)
            {
                patchOpcodeIdParameter.Value = entry.Key;

                using SqliteDataReader reader = selectCommand.ExecuteReader();
                while (reader.Read())
                {
                    SourceChannelRow row = new SourceChannelRow();
                    row.SourcePatchOpcodeId = reader.GetInt64(0);
                    row.Channel = reader.GetString(1);
                    sourceRows.Add(row);
                }
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePatchOpcodeChannels: read " + sourceRows.Count + " source rows");

        if (sourceRows.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.DuplicatePatchOpcodeChannels: no channels to duplicate");
            return 0;
        }

        using SqliteCommand insertCommand = _connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            "INSERT INTO PatchOpcodeChannel (patch_opcode_id, channel) " +
            "VALUES (@patch_opcode_id, @channel)";
        SqliteParameter newOpcodeIdParameter = insertCommand.Parameters.Add("@patch_opcode_id", SqliteType.Integer);
        SqliteParameter channelParameter = insertCommand.Parameters.Add("@channel", SqliteType.Text);

        int insertedCount = 0;
        for (int rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
        {
            SourceChannelRow row = sourceRows[rowIndex];

            long newOpcodeId;
            if (!opcodeIdMap.TryGetValue(row.SourcePatchOpcodeId, out newOpcodeId))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PatchLevelDuplicator.DuplicatePatchOpcodeChannels: no mapping for source patch_opcode_id="
                    + row.SourcePatchOpcodeId + " channel=" + row.Channel);
                throw new InvalidOperationException(
                    "No mapping for source PatchOpcodeChannel.patch_opcode_id=" + row.SourcePatchOpcodeId + ".");
            }

            newOpcodeIdParameter.Value = newOpcodeId;
            channelParameter.Value = row.Channel;

            int affected = insertCommand.ExecuteNonQuery();
            if (affected != 1)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PatchLevelDuplicator.DuplicatePatchOpcodeChannels: INSERT affected " + affected
                    + " rows for source patch_opcode_id=" + row.SourcePatchOpcodeId + " channel=" + row.Channel);
                throw new InvalidOperationException(
                    "Insert into PatchOpcodeChannel affected " + affected + " rows.");
            }

            insertedCount++;
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.DuplicatePatchOpcodeChannels: channel=" + row.Channel
                + " source_opcode_id=" + row.SourcePatchOpcodeId + " new_opcode_id=" + newOpcodeId);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePatchOpcodeChannels: inserted " + insertedCount + " rows");
        return insertedCount;
    }


    ///////////////////////////////////////////////////////////////////////////////////////////
    // SourceChannelRow
    //
    // Carries one PatchOpcodeChannel row read from the source patch level.  Local staging
    // type so the read pass closes its reader before the write pass begins.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class SourceChannelRow
    {
        public long SourcePatchOpcodeId;
        public string Channel = string.Empty;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // DuplicatePacketOptionalGroups
    //
    // For every PacketOptionalGroup row whose patch_opcode_id is a key in opcodeIdMap,
    // inserts a copy that points at the corresponding new patch_opcode_id.  bit_offset,
    // flags_bit_length, and flag_field_name are carried over verbatim.
    //
    // Returns a dictionary mapping each source PacketOptionalGroup.id to its
    // newly-inserted target id.  This map is consumed by DuplicatePacketOptionalFields.
    //
    // transaction:  Active transaction.  All reads and writes run inside it.
    // opcodeIdMap:  Source PatchOpcode.id -> new PatchOpcode.id, produced by
    //               DuplicatePatchOpcodes.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private Dictionary<long, long> DuplicatePacketOptionalGroups(
        SqliteTransaction transaction,
        Dictionary<long, long> opcodeIdMap)
    {
        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePacketOptionalGroups: " + opcodeIdMap.Count + " opcode ids in scope");

        Dictionary<long, long> groupIdMap = new Dictionary<long, long>();

        if (opcodeIdMap.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.DuplicatePacketOptionalGroups: opcodeIdMap is empty; nothing to do");
            return groupIdMap;
        }

        List<SourceGroupRow> sourceRows = new List<SourceGroupRow>();

        using (SqliteCommand selectCommand = _connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText =
                "SELECT id, patch_opcode_id, bit_offset, flags_bit_length, flag_field_name " +
                "FROM PacketOptionalGroup " +
                "WHERE patch_opcode_id = @patch_opcode_id " +
                "ORDER BY id";
            SqliteParameter patchOpcodeIdParameter = selectCommand.Parameters.Add("@patch_opcode_id", SqliteType.Integer);

            foreach (KeyValuePair<long, long> entry in opcodeIdMap)
            {
                patchOpcodeIdParameter.Value = entry.Key;

                using SqliteDataReader reader = selectCommand.ExecuteReader();
                while (reader.Read())
                {
                    SourceGroupRow row = new SourceGroupRow();
                    row.SourceId = reader.GetInt64(0);
                    row.SourcePatchOpcodeId = reader.GetInt64(1);
                    row.BitOffset = reader.GetInt32(2);
                    row.FlagsBitLength = reader.GetInt32(3);
                    row.FlagFieldName = reader.GetString(4);
                    sourceRows.Add(row);
                }
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePacketOptionalGroups: read " + sourceRows.Count + " source rows");

        if (sourceRows.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.DuplicatePacketOptionalGroups: no groups to duplicate");
            return groupIdMap;
        }

        using SqliteCommand insertCommand = _connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            "INSERT INTO PacketOptionalGroup " +
            "(patch_opcode_id, bit_offset, flags_bit_length, flag_field_name) " +
            "VALUES (@patch_opcode_id, @bit_offset, @flags_bit_length, @flag_field_name); " +
            "SELECT last_insert_rowid();";

        SqliteParameter newOpcodeIdParameter = insertCommand.Parameters.Add("@patch_opcode_id", SqliteType.Integer);
        SqliteParameter bitOffsetParameter = insertCommand.Parameters.Add("@bit_offset", SqliteType.Integer);
        SqliteParameter flagsBitLengthParameter = insertCommand.Parameters.Add("@flags_bit_length", SqliteType.Integer);
        SqliteParameter flagFieldNameParameter = insertCommand.Parameters.Add("@flag_field_name", SqliteType.Text);

        for (int rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
        {
            SourceGroupRow row = sourceRows[rowIndex];

            long newOpcodeId;
            if (!opcodeIdMap.TryGetValue(row.SourcePatchOpcodeId, out newOpcodeId))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PatchLevelDuplicator.DuplicatePacketOptionalGroups: no mapping for source patch_opcode_id="
                    + row.SourcePatchOpcodeId + " source group_id=" + row.SourceId);
                throw new InvalidOperationException(
                    "No mapping for source PacketOptionalGroup.patch_opcode_id=" + row.SourcePatchOpcodeId + ".");
            }

            newOpcodeIdParameter.Value = newOpcodeId;
            bitOffsetParameter.Value = row.BitOffset;
            flagsBitLengthParameter.Value = row.FlagsBitLength;
            flagFieldNameParameter.Value = row.FlagFieldName;

            object? scalar = insertCommand.ExecuteScalar();
            if (scalar == null || scalar == DBNull.Value)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PatchLevelDuplicator.DuplicatePacketOptionalGroups: last_insert_rowid returned null for source group_id="
                    + row.SourceId);
                throw new InvalidOperationException(
                    "Failed to retrieve new id for duplicated PacketOptionalGroup row (source id=" + row.SourceId + ").");
            }

            long newGroupId = Convert.ToInt64(scalar);
            groupIdMap[row.SourceId] = newGroupId;

            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.DuplicatePacketOptionalGroups: source_group_id=" + row.SourceId
                + " new_group_id=" + newGroupId
                + " source_opcode_id=" + row.SourcePatchOpcodeId + " new_opcode_id=" + newOpcodeId
                + " bit_offset=" + row.BitOffset
                + " flag_field=" + row.FlagFieldName);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePacketOptionalGroups: inserted " + groupIdMap.Count + " rows");
        return groupIdMap;
    }


    ///////////////////////////////////////////////////////////////////////////////////////////
    // SourceGroupRow
    //
    // Carries one PacketOptionalGroup row read from the source patch level.  Local
    // staging type so the read pass closes its reader before the write pass begins.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class SourceGroupRow
    {
        public long SourceId;
        public long SourcePatchOpcodeId;
        public int BitOffset;
        public int FlagsBitLength;
        public string FlagFieldName = string.Empty;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // DuplicatePacketFields
    //
    // For every PacketField row whose patch_opcode_id is a key in opcodeIdMap, inserts a
    // copy that points at the corresponding new patch_opcode_id.  field_name, bit_offset,
    // bit_length, encoding, divisor, and relative_to are carried over verbatim.
    //
    // Returns the number of rows inserted.
    //
    // transaction:  Active transaction.  All reads and writes run inside it.
    // opcodeIdMap:  Source PatchOpcode.id -> new PatchOpcode.id, produced by
    //               DuplicatePatchOpcodes.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private int DuplicatePacketFields(SqliteTransaction transaction, Dictionary<long, long> opcodeIdMap)
    {
        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePacketFields: " + opcodeIdMap.Count + " opcode ids in scope");

        if (opcodeIdMap.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.DuplicatePacketFields: opcodeIdMap is empty; nothing to do");
            return 0;
        }

        List<SourceFieldRow> sourceRows = new List<SourceFieldRow>();

        using (SqliteCommand selectCommand = _connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText =
                "SELECT patch_opcode_id, field_name, bit_offset, bit_length, encoding, divisor, relative_to " +
                "FROM PacketField " +
                "WHERE patch_opcode_id = @patch_opcode_id " +
                "ORDER BY id";
            SqliteParameter patchOpcodeIdParameter = selectCommand.Parameters.Add("@patch_opcode_id", SqliteType.Integer);

            foreach (KeyValuePair<long, long> entry in opcodeIdMap)
            {
                patchOpcodeIdParameter.Value = entry.Key;

                using SqliteDataReader reader = selectCommand.ExecuteReader();
                while (reader.Read())
                {
                    SourceFieldRow row = new SourceFieldRow();
                    row.SourcePatchOpcodeId = reader.GetInt64(0);
                    row.FieldName = reader.GetString(1);
                    row.BitOffset = reader.GetInt32(2);
                    row.BitLength = reader.GetInt32(3);
                    row.Encoding = reader.GetString(4);
                    row.Divisor = reader.GetDouble(5);
                    row.RelativeTo = reader.IsDBNull(6) ? null : reader.GetString(6);
                    sourceRows.Add(row);
                }
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePacketFields: read " + sourceRows.Count + " source rows");

        if (sourceRows.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.DuplicatePacketFields: no fields to duplicate");
            return 0;
        }

        using SqliteCommand insertCommand = _connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            "INSERT INTO PacketField " +
            "(patch_opcode_id, field_name, bit_offset, bit_length, encoding, divisor, relative_to) " +
            "VALUES (@patch_opcode_id, @field_name, @bit_offset, @bit_length, @encoding, @divisor, @relative_to)";

        SqliteParameter newOpcodeIdParameter = insertCommand.Parameters.Add("@patch_opcode_id", SqliteType.Integer);
        SqliteParameter fieldNameParameter = insertCommand.Parameters.Add("@field_name", SqliteType.Text);
        SqliteParameter bitOffsetParameter = insertCommand.Parameters.Add("@bit_offset", SqliteType.Integer);
        SqliteParameter bitLengthParameter = insertCommand.Parameters.Add("@bit_length", SqliteType.Integer);
        SqliteParameter encodingParameter = insertCommand.Parameters.Add("@encoding", SqliteType.Text);
        SqliteParameter divisorParameter = insertCommand.Parameters.Add("@divisor", SqliteType.Real);
        SqliteParameter relativeToParameter = insertCommand.Parameters.Add("@relative_to", SqliteType.Text);

        int insertedCount = 0;
        for (int rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
        {
            SourceFieldRow row = sourceRows[rowIndex];

            long newOpcodeId;
            if (!opcodeIdMap.TryGetValue(row.SourcePatchOpcodeId, out newOpcodeId))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PatchLevelDuplicator.DuplicatePacketFields: no mapping for source patch_opcode_id="
                    + row.SourcePatchOpcodeId + " field=" + row.FieldName);
                throw new InvalidOperationException(
                    "No mapping for source PacketField.patch_opcode_id=" + row.SourcePatchOpcodeId + ".");
            }

            newOpcodeIdParameter.Value = newOpcodeId;
            fieldNameParameter.Value = row.FieldName;
            bitOffsetParameter.Value = row.BitOffset;
            bitLengthParameter.Value = row.BitLength;
            encodingParameter.Value = row.Encoding;
            divisorParameter.Value = row.Divisor;
            if (row.RelativeTo != null)
            {
                relativeToParameter.Value = row.RelativeTo;
            }
            else
            {
                relativeToParameter.Value = DBNull.Value;
            }

            int affected = insertCommand.ExecuteNonQuery();
            if (affected != 1)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PatchLevelDuplicator.DuplicatePacketFields: INSERT affected " + affected
                    + " rows for source patch_opcode_id=" + row.SourcePatchOpcodeId
                    + " field=" + row.FieldName);
                throw new InvalidOperationException(
                    "Insert into PacketField affected " + affected + " rows.");
            }

            insertedCount++;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePacketFields: inserted " + insertedCount + " rows");
        return insertedCount;
    }


    ///////////////////////////////////////////////////////////////////////////////////////////
    // SourceFieldRow
    //
    // Carries one PacketField row read from the source patch level.  Local staging type
    // so the read pass closes its reader before the write pass begins.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class SourceFieldRow
    {
        public long SourcePatchOpcodeId;
        public string FieldName = string.Empty;
        public int BitOffset;
        public int BitLength;
        public string Encoding = string.Empty;
        public double Divisor;
        public string? RelativeTo;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // DuplicatePacketOptionalFields
    //
    // For every PacketOptionalField row whose group_id is a key in groupIdMap, inserts a
    // copy that points at the corresponding new group_id.  flag_mask, sequence_order,
    // field_name, bit_length, encoding, and divisor are carried over verbatim.
    //
    // Returns the number of rows inserted.
    //
    // transaction:  Active transaction.  All reads and writes run inside it.
    // groupIdMap:   Source PacketOptionalGroup.id -> new PacketOptionalGroup.id,
    //               produced by DuplicatePacketOptionalGroups.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private int DuplicatePacketOptionalFields(SqliteTransaction transaction, Dictionary<long, long> groupIdMap)
    {
        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePacketOptionalFields: " + groupIdMap.Count + " group ids in scope");

        if (groupIdMap.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.DuplicatePacketOptionalFields: groupIdMap is empty; nothing to do");
            return 0;
        }

        List<SourceOptionalFieldRow> sourceRows = new List<SourceOptionalFieldRow>();

        using (SqliteCommand selectCommand = _connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText =
                "SELECT group_id, flag_mask, sequence_order, field_name, bit_length, encoding, divisor " +
                "FROM PacketOptionalField " +
                "WHERE group_id = @group_id " +
                "ORDER BY sequence_order";
            SqliteParameter groupIdParameter = selectCommand.Parameters.Add("@group_id", SqliteType.Integer);

            foreach (KeyValuePair<long, long> entry in groupIdMap)
            {
                groupIdParameter.Value = entry.Key;

                using SqliteDataReader reader = selectCommand.ExecuteReader();
                while (reader.Read())
                {
                    SourceOptionalFieldRow row = new SourceOptionalFieldRow();
                    row.SourceGroupId = reader.GetInt64(0);
                    row.FlagMask = reader.GetInt64(1);
                    row.SequenceOrder = reader.GetInt32(2);
                    row.FieldName = reader.GetString(3);
                    row.BitLength = reader.GetInt32(4);
                    row.Encoding = reader.GetString(5);
                    row.Divisor = reader.GetDouble(6);
                    sourceRows.Add(row);
                }
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePacketOptionalFields: read " + sourceRows.Count + " source rows");

        if (sourceRows.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.DuplicatePacketOptionalFields: no optional fields to duplicate");
            return 0;
        }

        using SqliteCommand insertCommand = _connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            "INSERT INTO PacketOptionalField " +
            "(group_id, flag_mask, sequence_order, field_name, bit_length, encoding, divisor) " +
            "VALUES (@group_id, @flag_mask, @sequence_order, @field_name, @bit_length, @encoding, @divisor)";

        SqliteParameter newGroupIdParameter = insertCommand.Parameters.Add("@group_id", SqliteType.Integer);
        SqliteParameter flagMaskParameter = insertCommand.Parameters.Add("@flag_mask", SqliteType.Integer);
        SqliteParameter sequenceOrderParameter = insertCommand.Parameters.Add("@sequence_order", SqliteType.Integer);
        SqliteParameter fieldNameParameter = insertCommand.Parameters.Add("@field_name", SqliteType.Text);
        SqliteParameter bitLengthParameter = insertCommand.Parameters.Add("@bit_length", SqliteType.Integer);
        SqliteParameter encodingParameter = insertCommand.Parameters.Add("@encoding", SqliteType.Text);
        SqliteParameter divisorParameter = insertCommand.Parameters.Add("@divisor", SqliteType.Real);

        int insertedCount = 0;
        for (int rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
        {
            SourceOptionalFieldRow row = sourceRows[rowIndex];

            long newGroupId;
            if (!groupIdMap.TryGetValue(row.SourceGroupId, out newGroupId))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PatchLevelDuplicator.DuplicatePacketOptionalFields: no mapping for source group_id="
                    + row.SourceGroupId + " field=" + row.FieldName);
                throw new InvalidOperationException(
                    "No mapping for source PacketOptionalField.group_id=" + row.SourceGroupId + ".");
            }

            newGroupIdParameter.Value = newGroupId;
            flagMaskParameter.Value = row.FlagMask;
            sequenceOrderParameter.Value = row.SequenceOrder;
            fieldNameParameter.Value = row.FieldName;
            bitLengthParameter.Value = row.BitLength;
            encodingParameter.Value = row.Encoding;
            divisorParameter.Value = row.Divisor;

            int affected = insertCommand.ExecuteNonQuery();
            if (affected != 1)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PatchLevelDuplicator.DuplicatePacketOptionalFields: INSERT affected " + affected
                    + " rows for source group_id=" + row.SourceGroupId
                    + " field=" + row.FieldName);
                throw new InvalidOperationException(
                    "Insert into PacketOptionalField affected " + affected + " rows.");
            }

            insertedCount++;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePacketOptionalFields: inserted " + insertedCount + " rows");
        return insertedCount;
    }


    ///////////////////////////////////////////////////////////////////////////////////////////
    // SourceOptionalFieldRow
    //
    // Carries one PacketOptionalField row read from the source patch level.  Local
    // staging type so the read pass closes its reader before the write pass begins.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class SourceOptionalFieldRow
    {
        public long SourceGroupId;
        public long FlagMask;
        public int SequenceOrder;
        public string FieldName = string.Empty;
        public int BitLength;
        public string Encoding = string.Empty;
        public double Divisor;
    }
}