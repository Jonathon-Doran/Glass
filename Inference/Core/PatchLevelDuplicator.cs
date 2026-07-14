using Glass.Core.Logging;
using Glass.Network.Protocol;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;

namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// PatchLevelDuplicator
//
// Duplicates a single source patch level into a new target patch date.  Source rows are
// not modified.  Tables duplicated:
//
//     PatchOpcode
//     FieldCollection
//     PacketField
//     Gate
//
// FieldCollection, PacketField, and Gate are keyed by (patch_date, server_type) identity rather than by a
// foreign key, so each is duplicated by reading the source rows, rewriting patch_date to
// the target, and inserting.  The whole duplication runs in a single transaction.
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
                "PatchLevelDuplicator.ctor: connection is null", LogLevel.Error);
            throw new ArgumentNullException(nameof(connection));
        }

        if (connection.State != ConnectionState.Open)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.ctor: connection is not open (state=" + connection.State + ")", LogLevel.Error);
            throw new InvalidOperationException("Connection must be open.");
        }

        _connection = connection;
        DebugLog.Write(LogChannel.InferenceDebug, "PatchLevelDuplicator.ctor: constructed", LogLevel.Trace);
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
            + ") target=" + targetPatchDate, LogLevel.Trace);

        if (string.IsNullOrWhiteSpace(sourcePatchDate))
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: sourcePatchDate is empty", LogLevel.Error);
            throw new ArgumentException("sourcePatchDate is required.", nameof(sourcePatchDate));
        }

        if (string.IsNullOrWhiteSpace(sourceServerType))
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: sourceServerType is empty", LogLevel.Error);
            throw new ArgumentException("sourceServerType is required.", nameof(sourceServerType));
        }

        if (string.IsNullOrWhiteSpace(targetPatchDate))
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: targetPatchDate is empty", LogLevel.Error);
            throw new ArgumentException("targetPatchDate is required.", nameof(targetPatchDate));
        }

        if (sourcePatchDate == targetPatchDate)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: source and target patch dates are identical ("
                + sourcePatchDate + ")", LogLevel.Error);
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
                    + ") already has " + existingTargetRows + " PatchOpcode rows", LogLevel.Error);
                throw new InvalidOperationException(
                    "Target patch level (" + targetPatchDate + ", " + sourceServerType + ") already contains rows.");
            }

            PatchLevelManager levelManager = new PatchLevelManager(_connection);
            levelManager.Create(transaction, new PatchLevel(targetPatchDate, sourceServerType));

            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: created PatchLevel row for ("
                + targetPatchDate + "," + sourceServerType + ")", LogLevel.Trace);

            Dictionary<long, long> opcodeIdMap = DuplicatePatchOpcodes(
                transaction, sourcePatchDate, sourceServerType, targetPatchDate);

            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: duplicated " + opcodeIdMap.Count + " PatchOpcode rows", LogLevel.Trace);

            if (opcodeIdMap.Count == 0)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PatchLevelDuplicator.Duplicate: no source rows for (" + sourcePatchDate + ","
                    + sourceServerType + "); nothing to do", LogLevel.Trace);
                transaction.Commit();
                return 0;
            }

            int collectionCount = DuplicateFieldCollections(
                transaction, sourcePatchDate, sourceServerType, targetPatchDate);
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: duplicated " + collectionCount + " FieldCollection rows", LogLevel.Trace);

            int fieldCount = DuplicatePacketFields(
                transaction, sourcePatchDate, sourceServerType, targetPatchDate);
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: duplicated " + fieldCount + " PacketField rows", LogLevel.Trace);

            int gateCount = DuplicateGates(
                transaction, sourcePatchDate, sourceServerType, targetPatchDate);
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: duplicated " + gateCount + " Gate rows", LogLevel.Trace);

            transaction.Commit();

            DebugLog.Write(LogChannel.InferenceDebug, "PatchLevelDuplicator.Duplicate: transaction committed", LogLevel.Trace);

            return opcodeIdMap.Count;
        }
        catch (Exception ex)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.Duplicate: failed, rolling back: " + ex.Message, LogLevel.Error);
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
            "PatchLevelDuplicator.CountTargetOpcodes: target=(" + targetPatchDate + "," + sourceServerType + ")", LogLevel.Trace);

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
                "PatchLevelDuplicator.CountTargetOpcodes: COUNT(*) returned null; treating as 0", LogLevel.Info);
            return 0;
        }

        int count = Convert.ToInt32(scalar);
        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.CountTargetOpcodes: target has " + count + " existing PatchOpcode rows", LogLevel.Trace);
        return count;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // DuplicatePatchOpcodes
    //
    // Reads every PatchOpcode row for (sourcePatchDate, sourceServerType) and inserts a
    // matching row with patch_date = targetPatchDate.  Server type, opcode_value,
    // opcode_name, version, byte_length, and gate_name are preserved verbatim.  Wire values will be
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
            + sourceServerType + ") target=" + targetPatchDate, LogLevel.Trace);

        Dictionary<long, long> idMap = new Dictionary<long, long>();

        List<SourceRow> sourceRows = new List<SourceRow>();

        using (SqliteCommand selectCommand = _connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText =
                "SELECT id, opcode_value, opcode_name, version, byte_length, gate_name " +
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
                row.GateName = reader.GetString(5);
                sourceRows.Add(row);
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePatchOpcodes: read " + sourceRows.Count + " source rows", LogLevel.Trace);

        if (sourceRows.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.DuplicatePatchOpcodes: nothing to duplicate", LogLevel.Warn);
            return idMap;
        }

        using SqliteCommand insertCommand = _connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            "INSERT INTO PatchOpcode " +
            "(patch_date, server_type, opcode_value, opcode_name, version, byte_length, gate_name) " +
            "VALUES (@patch_date, @server_type, @opcode_value, @opcode_name, @version, @byte_length, @gate_name); " +
            "SELECT last_insert_rowid();";

        SqliteParameter patchDateParameter = insertCommand.Parameters.Add("@patch_date", SqliteType.Text);
        SqliteParameter serverTypeParameter = insertCommand.Parameters.Add("@server_type", SqliteType.Text);
        SqliteParameter opcodeValueParameter = insertCommand.Parameters.Add("@opcode_value", SqliteType.Integer);
        SqliteParameter opcodeNameParameter = insertCommand.Parameters.Add("@opcode_name", SqliteType.Text);
        SqliteParameter versionParameter = insertCommand.Parameters.Add("@version", SqliteType.Integer);
        SqliteParameter byteLengthParameter = insertCommand.Parameters.Add("@byte_length", SqliteType.Integer);
        SqliteParameter gateNameParameter = insertCommand.Parameters.Add("@gate_name", SqliteType.Text);

        patchDateParameter.Value = targetPatchDate;
        serverTypeParameter.Value = sourceServerType;

        for (int rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
        {
            SourceRow row = sourceRows[rowIndex];

            opcodeValueParameter.Value = row.OpcodeValue;
            opcodeNameParameter.Value = row.OpcodeName;
            versionParameter.Value = row.Version;
            gateNameParameter.Value = row.GateName;

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
                    + row.SourceId + " name=" + row.OpcodeName, LogLevel.Error);
                throw new InvalidOperationException(
                    "Failed to retrieve new id for duplicated PatchOpcode row (source id=" + row.SourceId + ").");
            }

            long newId = Convert.ToInt64(scalar);
            idMap[row.SourceId] = newId;

            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.DuplicatePatchOpcodes: " + row.OpcodeName
                + " v" + row.Version + " source_id=" + row.SourceId + " new_id=" + newId, LogLevel.Trace);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePatchOpcodes: inserted " + idMap.Count + " rows", LogLevel.Trace);
        return idMap;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // DuplicatePacketFields
    //
    // Reads every PacketField row for (sourcePatchDate, sourceServerType) and inserts a
    // matching row with patch_date = targetPatchDate.  server_type, collection_name,
    // field_name, bit_offset, bit_length, encoding, divisor, relative_to, predicate, and
    // sequence are carried over verbatim.
    //
    // Returns the number of rows inserted.
    //
    // transaction:        Active transaction.  All reads and writes run inside it.
    // sourcePatchDate:    Patch date of the source rows.
    // sourceServerType:   Server type of the source rows.
    // targetPatchDate:    Patch date to assign to the inserted rows.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private int DuplicatePacketFields(
        SqliteTransaction transaction,
        string sourcePatchDate,
        string sourceServerType,
        string targetPatchDate)
    {
        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePacketFields: source=(" + sourcePatchDate + ","
            + sourceServerType + ") target=" + targetPatchDate, LogLevel.Trace);

        List<SourceFieldRow> sourceRows = new List<SourceFieldRow>();

        using (SqliteCommand selectCommand = _connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText =
                "SELECT collection_name, field_name, bit_offset, bit_length, encoding, divisor, relative_to, predicate, sequence " +
                "FROM PacketField " +
                "WHERE patch_date = @patch_date AND server_type = @server_type " +
                "ORDER BY collection_name, bit_offset";
            selectCommand.Parameters.AddWithValue("@patch_date", sourcePatchDate);
            selectCommand.Parameters.AddWithValue("@server_type", sourceServerType);

            using SqliteDataReader reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                SourceFieldRow row = new SourceFieldRow();
                row.CollectionName = reader.GetString(0);
                row.FieldName = reader.GetString(1);
                row.BitOffset = reader.GetInt32(2);
                row.BitLength = reader.GetInt32(3);
                row.Encoding = reader.GetString(4);
                row.Divisor = reader.GetDouble(5);
                row.RelativeTo = reader.IsDBNull(6) ? null : reader.GetString(6);
                row.Predicate = reader.IsDBNull(7) ? null : reader.GetString(7);
                row.Sequence = reader.IsDBNull(8) ? null : reader.GetInt32(8);
                sourceRows.Add(row);
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePacketFields: read " + sourceRows.Count + " source rows", LogLevel.Trace);

        if (sourceRows.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.DuplicatePacketFields: no fields to duplicate", LogLevel.Warn);
            return 0;
        }

        using SqliteCommand insertCommand = _connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            "INSERT INTO PacketField " +
            "(patch_date, server_type, collection_name, field_name, bit_offset, bit_length, encoding, divisor, relative_to, predicate, sequence) " +
            "VALUES (@patch_date, @server_type, @collection_name, @field_name, @bit_offset, @bit_length, @encoding, @divisor, @relative_to, @predicate, @sequence)";

        SqliteParameter patchDateParameter = insertCommand.Parameters.Add("@patch_date", SqliteType.Text);
        SqliteParameter serverTypeParameter = insertCommand.Parameters.Add("@server_type", SqliteType.Text);
        SqliteParameter collectionNameParameter = insertCommand.Parameters.Add("@collection_name", SqliteType.Text);
        SqliteParameter fieldNameParameter = insertCommand.Parameters.Add("@field_name", SqliteType.Text);
        SqliteParameter bitOffsetParameter = insertCommand.Parameters.Add("@bit_offset", SqliteType.Integer);
        SqliteParameter bitLengthParameter = insertCommand.Parameters.Add("@bit_length", SqliteType.Integer);
        SqliteParameter encodingParameter = insertCommand.Parameters.Add("@encoding", SqliteType.Text);
        SqliteParameter divisorParameter = insertCommand.Parameters.Add("@divisor", SqliteType.Real);
        SqliteParameter relativeToParameter = insertCommand.Parameters.Add("@relative_to", SqliteType.Text);
        SqliteParameter predicateParameter = insertCommand.Parameters.Add("@predicate", SqliteType.Text);
        SqliteParameter sequenceParameter = insertCommand.Parameters.Add("@sequence", SqliteType.Integer);

        patchDateParameter.Value = targetPatchDate;
        serverTypeParameter.Value = sourceServerType;

        int insertedCount = 0;
        for (int rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
        {
            SourceFieldRow row = sourceRows[rowIndex];

            collectionNameParameter.Value = row.CollectionName;
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

            if (row.Predicate != null)
            {
                predicateParameter.Value = row.Predicate;
            }
            else
            {
                predicateParameter.Value = DBNull.Value;
            }

            if (row.Sequence.HasValue)
            {
                sequenceParameter.Value = row.Sequence.Value;
            }
            else
            {
                sequenceParameter.Value = DBNull.Value;
            }

            int affected = insertCommand.ExecuteNonQuery();
            if (affected != 1)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PatchLevelDuplicator.DuplicatePacketFields: INSERT affected " + affected
                    + " rows for collection=" + row.CollectionName + " field=" + row.FieldName, LogLevel.Error);
                throw new InvalidOperationException(
                    "Insert into PacketField affected " + affected + " rows.");
            }

            insertedCount++;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicatePacketFields: inserted " + insertedCount + " rows", LogLevel.Trace);
        return insertedCount;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // DuplicateFieldCollections
    //
    // Reads every FieldCollection row for (sourcePatchDate, sourceServerType) and inserts a
    // matching row with patch_date = targetPatchDate.  server_type and name are carried
    // over verbatim.
    //
    // Returns the number of rows inserted.
    //
    // transaction:        Active transaction.  All reads and writes run inside it.
    // sourcePatchDate:    Patch date of the source rows.
    // sourceServerType:   Server type of the source rows.
    // targetPatchDate:    Patch date to assign to the inserted rows.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private int DuplicateFieldCollections(
        SqliteTransaction transaction,
        string sourcePatchDate,
        string sourceServerType,
        string targetPatchDate)
    {
        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicateFieldCollections: source=(" + sourcePatchDate + ","
            + sourceServerType + ") target=" + targetPatchDate, LogLevel.Trace);

        List<SourceCollectionRow> sourceRows = new List<SourceCollectionRow>();

        using (SqliteCommand selectCommand = _connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText =
                "SELECT name " +
                "FROM FieldCollection " +
                "WHERE patch_date = @patch_date AND server_type = @server_type " +
                "ORDER BY name";
            selectCommand.Parameters.AddWithValue("@patch_date", sourcePatchDate);
            selectCommand.Parameters.AddWithValue("@server_type", sourceServerType);

            using SqliteDataReader reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                SourceCollectionRow row = new SourceCollectionRow();
                row.Name = reader.GetString(0);
                sourceRows.Add(row);
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicateFieldCollections: read " + sourceRows.Count + " source rows", LogLevel.Trace);

        if (sourceRows.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.DuplicateFieldCollections: no collections to duplicate", LogLevel.Warn);
            return 0;
        }

        using SqliteCommand insertCommand = _connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            "INSERT INTO FieldCollection (name, patch_date, server_type) " +
            "VALUES (@name, @patch_date, @server_type)";

        SqliteParameter nameParameter = insertCommand.Parameters.Add("@name", SqliteType.Text);
        SqliteParameter patchDateParameter = insertCommand.Parameters.Add("@patch_date", SqliteType.Text);
        SqliteParameter serverTypeParameter = insertCommand.Parameters.Add("@server_type", SqliteType.Text);

        patchDateParameter.Value = targetPatchDate;
        serverTypeParameter.Value = sourceServerType;

        int insertedCount = 0;
        for (int rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
        {
            SourceCollectionRow row = sourceRows[rowIndex];

            nameParameter.Value = row.Name;

            int affected = insertCommand.ExecuteNonQuery();
            if (affected != 1)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PatchLevelDuplicator.DuplicateFieldCollections: INSERT affected " + affected
                    + " rows for name=" + row.Name, LogLevel.Error);
                throw new InvalidOperationException(
                    "Insert into FieldCollection affected " + affected + " rows.");
            }

            insertedCount++;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicateFieldCollections: inserted " + insertedCount + " rows", LogLevel.Trace);
        return insertedCount;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // DuplicateGates
    //
    // Reads every Gate row for (sourcePatchDate, sourceServerType) and inserts a matching
    // row with patch_date = targetPatchDate.  server_type, name, kind, child_collection,
    // field_name, and count are carried over verbatim.
    //
    // Returns the number of rows inserted.
    //
    // transaction:        Active transaction.  All reads and writes run inside it.
    // sourcePatchDate:    Patch date of the source rows.
    // sourceServerType:   Server type of the source rows.
    // targetPatchDate:    Patch date to assign to the inserted rows.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private int DuplicateGates(
        SqliteTransaction transaction,
        string sourcePatchDate,
        string sourceServerType,
        string targetPatchDate)
    {
        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicateGates: source=(" + sourcePatchDate + ","
            + sourceServerType + ") target=" + targetPatchDate, LogLevel.Trace);

        List<SourceGateRow> sourceRows = new List<SourceGateRow>();

        using (SqliteCommand selectCommand = _connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText =
                "SELECT name, kind, child_collection, field_name, count " +
                "FROM Gate " +
                "WHERE patch_date = @patch_date AND server_type = @server_type " +
                "ORDER BY name";
            selectCommand.Parameters.AddWithValue("@patch_date", sourcePatchDate);
            selectCommand.Parameters.AddWithValue("@server_type", sourceServerType);

            using SqliteDataReader reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                SourceGateRow row = new SourceGateRow();
                row.Name = reader.GetString(0);
                row.Kind = reader.GetString(1);
                row.ChildCollection = reader.GetString(2);
                row.FieldName = reader.IsDBNull(3) ? null : reader.GetString(3);
                row.Count = reader.IsDBNull(4) ? null : reader.GetInt32(4);
                sourceRows.Add(row);
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicateGates: read " + sourceRows.Count + " source rows", LogLevel.Trace);

        if (sourceRows.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PatchLevelDuplicator.DuplicateGates: no gates to duplicate", LogLevel.Warn);
            return 0;
        }

        using SqliteCommand insertCommand = _connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            "INSERT INTO Gate " +
            "(name, kind, child_collection, field_name, patch_date, server_type, count) " +
            "VALUES (@name, @kind, @child_collection, @field_name, @patch_date, @server_type, @count)";

        SqliteParameter nameParameter = insertCommand.Parameters.Add("@name", SqliteType.Text);
        SqliteParameter kindParameter = insertCommand.Parameters.Add("@kind", SqliteType.Text);
        SqliteParameter childCollectionParameter = insertCommand.Parameters.Add("@child_collection", SqliteType.Text);
        SqliteParameter fieldNameParameter = insertCommand.Parameters.Add("@field_name", SqliteType.Text);
        SqliteParameter patchDateParameter = insertCommand.Parameters.Add("@patch_date", SqliteType.Text);
        SqliteParameter serverTypeParameter = insertCommand.Parameters.Add("@server_type", SqliteType.Text);
        SqliteParameter countParameter = insertCommand.Parameters.Add("@count", SqliteType.Integer);

        patchDateParameter.Value = targetPatchDate;
        serverTypeParameter.Value = sourceServerType;

        int insertedCount = 0;
        for (int rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
        {
            SourceGateRow row = sourceRows[rowIndex];

            nameParameter.Value = row.Name;
            kindParameter.Value = row.Kind;
            childCollectionParameter.Value = row.ChildCollection;

            if (row.FieldName != null)
            {
                fieldNameParameter.Value = row.FieldName;
            }
            else
            {
                fieldNameParameter.Value = DBNull.Value;
            }

            if (row.Count.HasValue)
            {
                countParameter.Value = row.Count.Value;
            }
            else
            {
                countParameter.Value = DBNull.Value;
            }

            int affected = insertCommand.ExecuteNonQuery();
            if (affected != 1)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PatchLevelDuplicator.DuplicateGates: INSERT affected " + affected
                    + " rows for name=" + row.Name, LogLevel.Error);
                throw new InvalidOperationException(
                    "Insert into Gate affected " + affected + " rows.");
            }

            insertedCount++;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PatchLevelDuplicator.DuplicateGates: inserted " + insertedCount + " rows", LogLevel.Trace);
        return insertedCount;
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
        public string GateName = string.Empty;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SourceFieldRow
    //
    // Carries one PacketField row read from the source patch level.  Local staging type
    // so the read pass closes its reader before the write pass begins.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class SourceFieldRow
    {
        public string CollectionName = string.Empty;
        public string FieldName = string.Empty;
        public int BitOffset;
        public int BitLength;
        public string Encoding = string.Empty;
        public double Divisor;
        public string? RelativeTo;
        public string? Predicate;
        public int? Sequence;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SourceCollectionRow
    //
    // Carries one FieldCollection row read from the source patch level.  Local staging
    // type so the read pass closes its reader before the write pass begins.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class SourceCollectionRow
    {
        public string Name = string.Empty;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SourceGateRow
    //
    // Carries one Gate row read from the source patch level.  Local staging type so the
    // read pass closes its reader before the write pass begins.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private class SourceGateRow
    {
        public string Name = string.Empty;
        public string Kind = string.Empty;
        public string ChildCollection = string.Empty;
        public string? FieldName;
        public int? Count;
    }
}