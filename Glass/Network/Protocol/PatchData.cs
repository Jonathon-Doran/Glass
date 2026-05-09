using Glass.Core.Logging;
using Glass.Data;
using Glass.Network.Protocol.Fields;
using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// PatchData
//
// Opcode data for a single patch level.
// Owns the opcode-name to wire-value map and the per-opcode field-definition
// cache for that patch.  After construction the object is read-only and safe to use from
// any thread without locking.
//
// Loaded eagerly: the constructor reads every PatchOpcode row and every PacketField row
// for the patch, builds the dictionaries, and is done.  Lookups in the hot path are
// dictionary reads with no database access.
//
// Construction is by the patch-explicit constructor used here.  A second constructor
// resolving "latest patch_date for this server_type" will be added next.
//
// Lifetime: one PatchData per (patch_date, server_type) the application currently cares
// about.  Glass typically holds one (the active session's patch).  Inference may hold
// several when comparing patches.  PatchData objects are not pooled or shared between
// FieldExtractor instances — they are owned by whichever component constructed them.
///////////////////////////////////////////////////////////////////////////////////////////////
public class PatchData
{
    public readonly PatchLevel PatchLevel;
    private readonly Dictionary<string, ushort> _opcodeValuesByName;
    private readonly Dictionary<string, FieldEncoding> _encodingsByString;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _patchOpcodes
    //
    // Flat array of every PatchOpcode loaded for this patch level, indexed by OpcodeHandle.
    // Allocated once in the constructor sized to the number of rows returned by
    // LoadPatchOpcodes; never resized.  An OpcodeHandle is a position in this array, valid
    // only for this PatchData instance.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private readonly PatchOpcode[] _patchOpcodes;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _opcodeFields
    //
    // Parallel array to _patchOpcodes: _opcodeFields[handle] is the FieldDefinition[] for
    // _patchOpcodes[handle], or null if that opcode has no fields defined for this patch
    //
    // Allocated once in the constructor sized to the number of rows returned by
    // LoadPatchOpcodes; never resized.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private readonly FieldDefinition[]?[] _opcodeFields;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _opcodeOptionalGroups
    //
    // Parallel array to _patchOpcodes: _opcodeOptionalGroups[handle] is the OptionalGroup
    // for the opcode at that handle, or null if the opcode has no optional block defined
    // for this patch.  Most opcodes have no optional block; those slots stay null.
    //
    // Allocated once in the constructor sized to the number of rows returned by
    // LoadPatchOpcodes; never resized.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private readonly OptionalGroup?[] _opcodeOptionalGroups;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // _handlesByValue
    //
    // Maps the wire opcode value to the OpcodeHandle assigned at load time.  Used on the
    // hot path by the dispatcher to resolve an incoming wire opcode to the handle that
    // indexes its handler array.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private readonly Dictionary<ushort, OpcodeHandle> _handlesByValue;

    private readonly string[] _namesByHandle;
    private readonly Dictionary<string, OpcodeHandle> _handlesByName;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PatchData (constructor, explicit patch)
    //
    // Loads the opcode-name-to-wire-value map and every opcode's field definitions for the
    // given patch level.  Both are fully populated before the constructor returns.  After
    // construction the instance is read-only.
    //
    // Load order:
    //   1. Build the encoding string table (used while reading PacketField rows).
    //   2. Load the opcode-name-to-wire-value map from PatchOpcode.
    //   3. Enumerate every PatchOpcode for this patch.  Allocate the parallel handle-
    //      indexed arrays from the resulting count.
    //   4. For each PatchOpcode, store it under its handle, then load its required field
    //      definitions from PacketField, then append its optional field definitions from
    //      PacketOptionalGroup / PacketOptionalField.
    //
    // Optional fields are folded into the same FieldDefinition[] as required fields.  Callers
    // see one flat list per opcode and cannot distinguish required from optional — by design.
    // Extraction handles missing payload bytes by leaving slots empty; handlers check for
    // empty slots on the fields they know are optional.
    //
    // Throws InvalidOperationException if no PatchOpcode rows exist for the requested patch.
    // This covers the case where the database has no patch data at all for the server type
    // (e.g. a freshly-seeded Test server before Inference has discovered any opcodes).
    //
    // Parameters:
    //   patchLevel  - The patch level represented.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchData(PatchLevel patchLevel)
    {
        PatchLevel = patchLevel;
        _encodingsByString = new Dictionary<string, FieldEncoding>();
        BuildEncodingMap();
        _opcodeValuesByName = new Dictionary<string, ushort>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        int opcodeCount = CountPatchOpcodes(conn);
        if (opcodeCount == 0)
        {
            throw new InvalidOperationException("PatchData: no PatchOpcode rows for patchLevel="
                + PatchLevel);
        }

        _patchOpcodes = new PatchOpcode[opcodeCount];
        _opcodeFields = new FieldDefinition[opcodeCount][];
        _opcodeOptionalGroups = new OptionalGroup?[opcodeCount];
        _namesByHandle = new string[opcodeCount];
        _handlesByName = new Dictionary<string, OpcodeHandle>(opcodeCount);
        _handlesByValue = new Dictionary<ushort, OpcodeHandle>(opcodeCount);



        LoadOpcodeMap(conn);
        LoadPatchOpcodes(conn);

        for (int handleIndex = 0; handleIndex < opcodeCount; handleIndex++)
        {
            OpcodeHandle handle = (OpcodeHandle)handleIndex;
            PatchOpcode patchOpcode = _patchOpcodes[handle];
            DebugLog.Write(LogChannel.Opcodes, "PatchData ctor: loading fields for handle="
                + handleIndex + " opcode=0x" + patchOpcode.Opcode.ToString("X4")
                + " version=" + patchOpcode.Version);
            LoadFields(handle, conn);
        }

        DebugLog.Write(LogChannel.Opcodes, "PatchData ctor: loaded " +
            _opcodeValuesByName.Count + " opcode name(s) and " + _patchOpcodes.Length + " opcode(s) for patch " + PatchLevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // BuildEncodingMap
    //
    // Build a map of encoding strings to enumerations to assist with field parsing from the database.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void BuildEncodingMap()
    {
        _encodingsByString.Add("uint", FieldEncoding.UInt);
        _encodingsByString.Add("int", FieldEncoding.Int);
        _encodingsByString.Add("float", FieldEncoding.Float);
        _encodingsByString.Add("uint_msb", FieldEncoding.UIntMsb);
        _encodingsByString.Add("uint_masked", FieldEncoding.UIntMasked);
        _encodingsByString.Add("signmag_msb", FieldEncoding.SignMagnitudeMsb);
        _encodingsByString.Add("signmag_lsb", FieldEncoding.SignMagnitudeLsb);
        _encodingsByString.Add("opt_signmag_msb", FieldEncoding.OptSignMagnitudeMsb);
        _encodingsByString.Add("string_null_terminated", FieldEncoding.StringNullTerminated);
        _encodingsByString.Add("string_length_prefixed", FieldEncoding.StringLengthPrefixed);
        _encodingsByString.Add("optional_group", FieldEncoding.OptionalGroup);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CountPatchOpcodes
    //
    // Returns the number of PatchOpcode rows for this patch level.  Used by the constructor
    // to size the handle-indexed arrays before any per-opcode loading runs.
    //
    // Throws InvalidOperationException if the count query returns null, which would
    // indicate a database problem rather than an empty result (an empty PatchOpcode table
    // for this patch produces a count of 0, not null).
    //
    // Parameters:
    //   conn    - An open database connection, owned by the caller. 
    //
    // Returns:
    //   The number of PatchOpcode rows for this patch level.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private int CountPatchOpcodes(SqliteConnection conn)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*)"
            + " FROM PatchOpcode"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType";
        cmd.Parameters.AddWithValue("@patchDate", PatchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", PatchLevel.ServerType);

        object? result = cmd.ExecuteScalar();
        if (result == null || result == DBNull.Value)
        {
            throw new InvalidOperationException("PatchData.CountPatchOpcodes: count query "
                + "returned null for patchLevel=" + PatchLevel);
        }

        int count = Convert.ToInt32(result);
        DebugLog.Write(LogChannel.Opcodes, "PatchData.CountPatchOpcodes: " + count
            + " PatchOpcode row(s) for patchLevel=" + PatchLevel);
        return count;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadPatchOpcodes
    //
    // Reads every PatchOpcode row for this patch level and populates the handle-indexed
    // structures: _patchOpcodes, _namesByHandle, and _handlesByName.  Row order from the
    // database determines handle assignment — the first row read becomes handle 0, the
    // second handle 1, and so on.
    //
    // The arrays and dictionary must be allocated by the constructor before this method
    // runs.  The size is determined by CountPatchOpcodes, which uses the same WHERE clause
    // and so produces a count consistent with what this method will read.
    //
    // Parameters:
    //   conn    - An open database connection, owned by the caller. 
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadPatchOpcodes(SqliteConnection conn)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT opcode_value, version, opcode_name"
            + " FROM PatchOpcode"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType";
        cmd.Parameters.AddWithValue("@patchDate", PatchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", PatchLevel.ServerType);

        using SqliteDataReader reader = cmd.ExecuteReader();
        int handleIndex = 0;
        while (reader.Read())
        {
            int opcodeValueRaw = reader.GetInt32(0);
            int version = reader.GetInt32(1);
            string opcodeName = reader.GetString(2);
            ushort opcodeValue = (ushort)opcodeValueRaw;
            OpcodeHandle handle = (OpcodeHandle)handleIndex;

            PatchOpcode patchOpcode = new PatchOpcode(PatchLevel, opcodeValue, version);
            _patchOpcodes[handle] = patchOpcode;
            _namesByHandle[handle] = opcodeName;
            _handlesByName[opcodeName] = handle;
            _handlesByValue[opcodeValue] = handle;

            handleIndex = handleIndex + 1;
        }

        DebugLog.Write(LogChannel.Opcodes, "PatchData.LoadPatchOpcodes: loaded "
            + handleIndex + " PatchOpcode(s) for patchLevel=" + PatchLevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadOpcodeMap
    //
    // Populates _opcodeValuesByName from the PatchOpcode table for this patch level.
    // Multiple versions of the same opcode share the same opcode_value, so SELECT DISTINCT
    // on (opcode_name, opcode_value) yields the correct one-to-one map.  Called once from
    // the constructor.
    //
    // Parameters:
    //   conn    - An open database connection, owned by the caller. 
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadOpcodeMap(SqliteConnection conn)
    {
        DebugLog.Write(LogChannel.Opcodes, "PatchData.LoadOpcodeMap: querying PatchOpcode for PatchLevel = " +
            PatchLevel);

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT opcode_name, opcode_value"
            + " FROM PatchOpcode"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType";
        cmd.Parameters.AddWithValue("@patchDate", PatchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", PatchLevel.ServerType);

        using SqliteDataReader reader = cmd.ExecuteReader();
        int rowCount = 0;
        while (reader.Read())
        {
            string opcodeName = reader.GetString(0);
            int opcodeValueRaw = reader.GetInt32(1);
            ushort opcodeValue = (ushort)opcodeValueRaw;
            _opcodeValuesByName[opcodeName] = opcodeValue;
            rowCount++;
        }

        DebugLog.Write(LogChannel.Opcodes, "PatchData.LoadOpcodeMap: read " + rowCount
            + " row(s), map now has " + _opcodeValuesByName.Count + " entries");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadFields
    //
    // Builds the field metadata for one opcode and stores it in the handle-indexed arrays.
    // Required field definitions, optional field definitions (slot allocators), and the
    // OptionalGroup metadata are loaded by helpers that return their results; this method
    // merges the FieldDefinitions, assigns _opcodeFields[handle], then builds the
    // OptionalGroup once IndexOfField can resolve names against the assigned array.
    //
    // Order matters: _opcodeFields[handle] must be set before LoadOptionalGroup runs,
    // because LoadOptionalGroup calls IndexOfField, which reads the array.
    //
    // An opcode with no fields at all (no PacketField rows and no PacketOptionalGroup
    // rows) leaves _opcodeFields[handle] and _opcodeOptionalGroups[handle] both null.
    // The extractor treats null _opcodeFields as "opcode known but unsupported" and
    // skips dispatch.
    //
    // Parameters:
    //   handle  - The OpcodeHandle whose fields to load.
    //   conn    - An open database connection, owned by the caller. 
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadFields(OpcodeHandle handle, SqliteConnection conn)
    {
        List<FieldDefinition> requiredFields = LoadRequiredFields(handle, conn);
        List<FieldDefinition> optionalFields = LoadOptionalFields(handle, conn);

        int totalCount = requiredFields.Count + optionalFields.Count;
        if (totalCount == 0)
        {
            return;
        }

        List<FieldDefinition> allFields = new List<FieldDefinition>(totalCount);
        allFields.AddRange(requiredFields);
        allFields.AddRange(optionalFields);
        _opcodeFields[handle] = allFields.ToArray();

        OptionalGroup? group = LoadOptionalGroup(handle, conn);
        if (group != null)
        {
            _opcodeOptionalGroups[handle] = group;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadRequiredFields
    //
    // Reads PacketField rows for the opcode at the given handle, ordered by bit_offset,
    // and returns them as a list of FieldDefinition entries.
    //
    // Unrecognized encoding strings are stored as FieldEncoding.Unknown so the row still
    // produces a slot; the extractor silently skips Unknown slots.
    //
    // Parameters:
    //   handle  - The OpcodeHandle whose required fields to load.
    //   conn    - An open database connection.
    //
    // Returns:
    //   The required field definitions, possibly empty if the opcode has no PacketField
    //   rows for this patch.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private List<FieldDefinition> LoadRequiredFields(OpcodeHandle handle, SqliteConnection conn)
    {
        PatchOpcode patchOpcode = _patchOpcodes[handle];
        List<FieldDefinition> fields = new List<FieldDefinition>();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pf.field_name, pf.bit_offset, pf.bit_length, pf.encoding, pf.divisor"
            + " FROM PacketField pf"
            + " JOIN PatchOpcode po ON pf.patch_opcode_id = po.id"
            + " WHERE po.patch_date = @patchDate"
            + " AND po.server_type = @serverType"
            + " AND po.opcode_value = @opcodeValue"
            + " AND po.version = @version"
            + " ORDER BY pf.bit_offset";
        cmd.Parameters.AddWithValue("@patchDate", PatchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", PatchLevel.ServerType);
        cmd.Parameters.AddWithValue("@opcodeValue", (int)patchOpcode.Opcode);
        cmd.Parameters.AddWithValue("@version", patchOpcode.Version);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string fieldName = reader.GetString(0);
            uint bitOffset = (uint)reader.GetInt32(1);
            uint bitLength = (uint)reader.GetInt32(2);
            string encodingString = reader.GetString(3);
            float divisor = (float)reader.GetFloat(4);

            FieldEncoding encoding;
            bool encodingFound = _encodingsByString.TryGetValue(encodingString, out encoding);
            if (encodingFound == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.LoadRequiredFields: unrecognized encoding '"
                    + encodingString + "' for opcode=" + _namesByHandle[(int)handle]
                    + " 0x" + patchOpcode.Opcode.ToString("X4")
                    + " version=" + patchOpcode.Version + " fieldName='" + fieldName
                    + "', storing as Unknown");
                encoding = FieldEncoding.Unknown;
            }

            FieldDefinition definition;
            definition.Name = fieldName;
            definition.BitOffset = bitOffset;
            definition.BitLength = bitLength;
            definition.Encoding = encoding;
            definition.Divisor = divisor;
            fields.Add(definition);
        }

        return fields;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadOptionalFields
    //
    // Reads PacketOptionalGroup and PacketOptionalField rows for the opcode at the given
    // handle and returns the FieldDefinition entries that allocate slots for each optional
    // sub-field.  Each entry carries the group's bit_offset (which is unused at extract
    // time for optional fields) and the row's encoding (an opt_* sentinel that Extract's
    // main switch skips).
    //
    // The OptionalGroup metadata itself is built by LoadOptionalGroup, after
    // _opcodeFields[handle] is assigned.  This method only produces the slot-allocator
    // FieldDefinitions.
    //
    // Parameters:
    //   handle  - The OpcodeHandle whose optional fields to load.
    //   conn    - An open database connection.
    //
    // Returns:
    //   The optional sub-field definitions, possibly empty if the opcode has no
    //   PacketOptionalGroup rows for this patch.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private List<FieldDefinition> LoadOptionalFields(OpcodeHandle handle, SqliteConnection conn)
    {
        PatchOpcode patchOpcode = _patchOpcodes[handle];
        List<FieldDefinition> fields = new List<FieldDefinition>();

        List<int> groupIds = new List<int>();
        List<uint> groupBitOffsets = new List<uint>();

        using (SqliteCommand groupsCmd = conn.CreateCommand())
        {
            groupsCmd.CommandText = "SELECT pog.id, pog.bit_offset"
                + " FROM PacketOptionalGroup pog"
                + " JOIN PatchOpcode po ON pog.patch_opcode_id = po.id"
                + " WHERE po.patch_date = @patchDate"
                + " AND po.server_type = @serverType"
                + " AND po.opcode_value = @opcodeValue"
                + " AND po.version = @version"
                + " ORDER BY pog.bit_offset";
            groupsCmd.Parameters.AddWithValue("@patchDate", PatchLevel.PatchDate);
            groupsCmd.Parameters.AddWithValue("@serverType", PatchLevel.ServerType);
            groupsCmd.Parameters.AddWithValue("@opcodeValue", (int)patchOpcode.Opcode);
            groupsCmd.Parameters.AddWithValue("@version", patchOpcode.Version);

            using SqliteDataReader reader = groupsCmd.ExecuteReader();
            while (reader.Read())
            {
                int groupId = reader.GetInt32(0);
                uint groupBitOffset = (uint)reader.GetInt32(1);
                groupIds.Add(groupId);
                groupBitOffsets.Add(groupBitOffset);
            }
        }

        for (int groupIndex = 0; groupIndex < groupIds.Count; groupIndex++)
        {
            int groupId = groupIds[groupIndex];
            uint groupBitOffset = groupBitOffsets[groupIndex];

            using SqliteCommand optionalCmd = conn.CreateCommand();
            optionalCmd.CommandText = "SELECT field_name, bit_length, encoding, divisor"
                + " FROM PacketOptionalField"
                + " WHERE group_id = @groupId"
                + " ORDER BY sequence_order";
            optionalCmd.Parameters.AddWithValue("@groupId", groupId);

            using SqliteDataReader reader = optionalCmd.ExecuteReader();
            while (reader.Read())
            {
                string fieldName = reader.GetString(0);
                uint bitLength = (uint)reader.GetInt32(1);
                string encodingString = reader.GetString(2);
                float divisor = reader.GetFloat(3);

                FieldEncoding encoding;
                bool encodingFound = _encodingsByString.TryGetValue(encodingString, out encoding);
                if (encodingFound == false)
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.LoadOptionalFields: unrecognized encoding '"
                        + encodingString + "' for opcode=" + _namesByHandle[(int)handle]
                        + " 0x" + patchOpcode.Opcode.ToString("X4")
                        + " version=" + patchOpcode.Version + " groupId=" + groupId
                        + " fieldName='" + fieldName + "', storing as Unknown");
                    encoding = FieldEncoding.Unknown;
                }

                FieldDefinition definition;
                definition.Name = fieldName;
                definition.BitOffset = groupBitOffset;
                definition.BitLength = bitLength;
                definition.Encoding = encoding;
                definition.Divisor = divisor;
                fields.Add(definition);
            }
        }

        return fields;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadOptionalGroup
    //
    // Reads PacketOptionalGroup and PacketOptionalField rows for the opcode at the given
    // handle, resolves field names to slot indices via IndexOfField, and returns the
    // built OptionalGroup.  Must be called after _opcodeFields[handle] is assigned.
    //
    // Today's schema supports multiple groups per opcode but the in-memory structure
    // holds one — if a future opcode declares more than one group, only the last one
    // read is returned.  Refactor when that case shows up.
    //
    // Parameters:
    //   handle  - The OpcodeHandle whose optional group to load.
    //   conn    - An open database connection.
    //
    // Returns:
    //   The OptionalGroup, or null if the opcode has no PacketOptionalGroup rows.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private OptionalGroup? LoadOptionalGroup(OpcodeHandle handle, SqliteConnection conn)
    {
        PatchOpcode patchOpcode = _patchOpcodes[handle];
        OptionalGroup? result = null;

        List<int> groupIds = new List<int>();
        List<string> groupFlagFieldNames = new List<string>();


        using (SqliteCommand groupsCmd = conn.CreateCommand())
        {
            groupsCmd.CommandText = "SELECT pog.id, pog.flag_field_name"
                + " FROM PacketOptionalGroup pog"
                + " JOIN PatchOpcode po ON pog.patch_opcode_id = po.id"
                + " WHERE po.patch_date = @patchDate"
                + " AND po.server_type = @serverType"
                + " AND po.opcode_value = @opcodeValue"
                + " AND po.version = @version"
                + " ORDER BY pog.bit_offset";
            groupsCmd.Parameters.AddWithValue("@patchDate", PatchLevel.PatchDate);
            groupsCmd.Parameters.AddWithValue("@serverType", PatchLevel.ServerType);
            groupsCmd.Parameters.AddWithValue("@opcodeValue", (int)patchOpcode.Opcode);
            groupsCmd.Parameters.AddWithValue("@version", patchOpcode.Version);

            using SqliteDataReader reader = groupsCmd.ExecuteReader();
            while (reader.Read())
            {
                int groupId = reader.GetInt32(0);
                string flagFieldName = reader.GetString(1);
                groupIds.Add(groupId);
                groupFlagFieldNames.Add(flagFieldName);
            }
        }

        for (int groupIndex = 0; groupIndex < groupIds.Count; groupIndex++)
        {
            int groupId = groupIds[groupIndex];
            string flagFieldName = groupFlagFieldNames[groupIndex];

            uint flagsLength = GetFieldBitLength(handle, flagFieldName);

            FieldIndex flagSlotIndex = IndexOfField(handle, flagFieldName);


            List<OptionalSubField> subFields = new List<OptionalSubField>();

            using SqliteCommand optionalCmd = conn.CreateCommand();
            optionalCmd.CommandText = "SELECT field_name, bit_length, encoding, divisor"
                + " FROM PacketOptionalField"
                + " WHERE group_id = @groupId"
                + " ORDER BY sequence_order";
            optionalCmd.Parameters.AddWithValue("@groupId", groupId);

            using SqliteDataReader reader = optionalCmd.ExecuteReader();
            while (reader.Read())
            {
                string fieldName = reader.GetString(0);
                uint bitLength = (uint)reader.GetInt32(1);
                string encodingString = reader.GetString(2);
                float divisor = reader.GetFloat(3);

                FieldEncoding encoding;
                bool encodingFound = _encodingsByString.TryGetValue(encodingString, out encoding);
                if (encodingFound == false)
                {
                    encoding = FieldEncoding.Unknown;
                }

                FieldIndex slotIndex = IndexOfField(handle, fieldName);

                OptionalSubField subField;
                subField.SlotIndex = (int)slotIndex;
                subField.BitLength = bitLength;
                subField.Encoding = encoding;
                subField.Name = fieldName;
                subField.Divisor = divisor;
                subFields.Add(subField);
            }

            result = new OptionalGroup((int)flagSlotIndex, flagsLength, subFields.ToArray());
        }

        return result;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeHandle
    //
    // Returns the OpcodeHandle for the given opcode name in this patch.  Called by handlers
    // at construction time, once per opcode they care about.  Cold path.
    //
    // Returns (OpcodeHandle)(-1) if the name is unknown to this patch.  Handlers check for
    // this sentinel and skip their own dispatch registration if their opcode is missing —
    // the expected case when a patch genuinely lacks an opcode the handler knows about.
    //
    // Parameters:
    //   opcodeName  - The logical name (e.g. "OP_PlayerProfile").
    //
    // Returns:
    //   The OpcodeHandle for the named opcode, or (OpcodeHandle)(-1) if not in this patch.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeHandle GetOpcodeHandle(string opcodeName)
    {
        OpcodeHandle handle;
        bool found = _handlesByName.TryGetValue(opcodeName, out handle);
        if (found == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "PatchData.GetOpcodeHandle: unknown opcode name '"
                + opcodeName + "' in patchLevel=" + PatchLevel + ", returning -1");
            return (OpcodeHandle)(-1);
        }
        return handle;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeHandle
    //
    // Returns the OpcodeHandle for the given wire opcode value in this patch.  Called on
    // the hot path by the dispatcher to resolve an incoming wire opcode to the handle
    // that indexes its handler array.
    //
    // Returns (OpcodeHandle)(-1) if the wire value is not in this patch.
    //
    // Parameters:
    //   opcodeValue  - The wire opcode value (e.g. 0x6FA1).
    //
    // Returns:
    //   The OpcodeHandle for the wire value, or (OpcodeHandle)(-1) if not in this patch.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeHandle GetOpcodeHandle(ushort opcodeValue)
    {
        OpcodeHandle handle;
        bool found = _handlesByValue.TryGetValue(opcodeValue, out handle);
        if (found == false)
        {
            return (OpcodeHandle)(-1);
        }
        return handle;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeName
    //
    // Returns the opcode name for the given OpcodeHandle in this patch.
    //
    // Parameters:
    //   handle  - The OpcodeHandle whose name to return.
    //
    // Returns:
    //   The opcode name (e.g. "OP_PlayerProfile").
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string GetOpcodeName(OpcodeHandle handle)
    {
        return _namesByHandle[handle];
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeValue
    //
    // Returns the wire opcode value for the given OpcodeHandle in this patch.
    //
    // Parameters:
    //   handle  - The OpcodeHandle whose wire opcode value to return.
    //
    // Returns:
    //   The wire opcode value (e.g. 0x6FA1), or 0 if the handle is invalid.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public ushort GetOpcodeValue(OpcodeHandle handle)
    {
        if ((int)handle < 0 || (int)handle >= _patchOpcodes.Length)
        {
            return 0;
        }

        return _patchOpcodes[handle].Opcode;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeCount
    //
    // Returns the number of opcodes loaded for this patch.  Equal to the length of the
    // handle-indexed arrays and the count of PatchOpcode rows for this patch.
    //
    // Returns:
    //   The number of opcodes in this patch.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int GetOpcodeCount()
    {
        return _patchOpcodes.Length;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetFieldDefinitions
    //
    // Returns the FieldDefinition array for the given OpcodeHandle, or null if the opcode
    // has no fields loaded for this patch.
    //
    // Parameters:
    //   handle  - The OpcodeHandle whose field definitions to return.
    //
    // Returns:
    //   The FieldDefinition array, or null if absent.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public FieldDefinition[]? GetFieldDefinitions(OpcodeHandle opcode)
    {
        return _opcodeFields[opcode];
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOptionalGroup
    //
    // Returns the OptionalGroup for the given OpcodeHandle, or null if the opcode has no
    // optional block defined for this patch.  Most opcodes have no optional block; null
    // is the common case.
    //
    // Called from FieldExtractor.Extract when it dispatches on the OptionalGroup encoding,
    // to look up the metadata the optional-block helper needs to walk the sub-fields.
    //
    // Parameters:
    //   handle  - The OpcodeHandle whose optional group to return.
    //
    // Returns:
    //   The OptionalGroup, or null if the opcode has no optional block.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OptionalGroup? GetOptionalGroup(OpcodeHandle handle)
    {
        return _opcodeOptionalGroups[handle];
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetFieldBitLength
    //
    // Returns the BitLength of the named field within the FieldDefinition list for the
    // given OpcodeHandle. 
    //
    // Returns 0 if the opcode has no fields loaded for this patch, or if the named field
    // is not present in the loaded definitions.
    //
    // Parameters:
    //   handle    - The OpcodeHandle whose field definitions to search.
    //   fieldName - The field_name column value to look up.
    //
    // Returns:
    //   The BitLength of the named field, or 0 if not found.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public uint GetFieldBitLength(OpcodeHandle opcode, string fieldName)
    {
        FieldDefinition[]? definitions = _opcodeFields[opcode];
        if (definitions == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.GetFieldBitLength: no field definitions for opcode '" +
                _namesByHandle[opcode] + "' in patchLevel=" + PatchLevel + ", returning 0");
            return 0u;
        }

        for (int fieldIndex = 0; fieldIndex < definitions.Length; fieldIndex++)
        {
            if (definitions[fieldIndex].Name == fieldName)
            {
                return definitions[fieldIndex].BitLength;
            }
        }

        DebugLog.Write(LogChannel.Fields, "PatchData.GetFieldBitLength: field '" + fieldName
            + "' not in definitions for opcode '" + _namesByHandle[opcode] +
            "' in patchLevel=" + PatchLevel + ", returning 0");
        return 0u;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // IndexOfField
    //
    // Returns the FieldIndex of the named field within the FieldDefinition list for the
    // given OpcodeHandle.  Called by handlers at construction time to cache field indices
    // for hot-path reads from FieldBags.  Cold path.
    //
    // Returns (FieldIndex)(-1) if the opcode has no fields loaded for this patch, or if
    // the named field is not present in the loaded definitions.
    //
    // Parameters:
    //   handle    - The OpcodeHandle whose field definitions to search.
    //   fieldName - The field_name column value to look up.
    //
    // Returns:
    //   The FieldIndex of the named field, or (FieldIndex)(-1) if not found.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public FieldIndex IndexOfField(OpcodeHandle opcode, string fieldName)
    {
        FieldDefinition[]? definitions = _opcodeFields[opcode];
        if (definitions == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.IndexOfField: no field definitions for opcode '" +
                _namesByHandle[opcode] + "' in patchLevel=" + PatchLevel + "), returning -1");
            return (FieldIndex)(-1);
        }

        for (int fieldIndex = 0; fieldIndex < definitions.Length; fieldIndex++)
        {
            if (definitions[fieldIndex].Name == fieldName)
            {
                return (FieldIndex)fieldIndex;
            }
        }

        DebugLog.Write(LogChannel.Fields, "PatchData.IndexOfField: field '" + fieldName
            + "' not in definitions for opcode '" + _namesByHandle[opcode] + 
            "' in patchLevel=" + PatchLevel + "), returning -1");
        return (FieldIndex)(-1);
    }
}

