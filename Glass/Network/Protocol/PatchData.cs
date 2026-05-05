using Glass.Core.Logging;
using Glass.Data;
using Glass.Network.Protocol.Fields;
using Microsoft.Data.Sqlite;
using System.Collections.Generic;

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
    private readonly PatchLevel _patchLevel;
    private readonly Dictionary<string, ushort> _opcodeValuesByName;
    private readonly Dictionary<PatchOpcode, FieldDefinition[]> _fieldsByOpcode;
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
        _patchLevel = patchLevel;
        _encodingsByString = new Dictionary<string, FieldEncoding>();
        BuildEncodingMap();
        _opcodeValuesByName = new Dictionary<string, ushort>();
        _fieldsByOpcode = new Dictionary<PatchOpcode, FieldDefinition[]>();

        int opcodeCount = CountPatchOpcodes();
        if (opcodeCount == 0)
        {
            throw new InvalidOperationException("PatchData: no PatchOpcode rows for patchLevel="
                + _patchLevel);
        }

        _patchOpcodes = new PatchOpcode[opcodeCount];
        _opcodeFields = new FieldDefinition[opcodeCount][];
        _namesByHandle = new string[opcodeCount];
        _handlesByName = new Dictionary<string, OpcodeHandle>(opcodeCount);

        LoadOpcodeMap();
        LoadPatchOpcodes();

        for (int handleIndex = 0; handleIndex < opcodeCount; handleIndex++)
        {
            OpcodeHandle handle = (OpcodeHandle)handleIndex;
            PatchOpcode patchOpcode = _patchOpcodes[handle];
            DebugLog.Write(LogChannel.Opcodes, "PatchData ctor: loading fields for handle="
                + handleIndex + " opcode=0x" + patchOpcode.Opcode.ToString("X4")
                + " version=" + patchOpcode.Version);
            LoadFields(handle);
        }

        DebugLog.Write(LogChannel.Opcodes, "PatchData ctor: loaded " +
            _opcodeValuesByName.Count + " opcode name(s) and " + _patchOpcodes.Length + " opcode(s) for patch " + _patchLevel);
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
        _encodingsByString.Add("uint_masked", FieldEncoding.UIntMasked);
        _encodingsByString.Add("sign_extend_fixed_div8", FieldEncoding.SignExtendFixedDiv8);
        _encodingsByString.Add("string_null_terminated", FieldEncoding.StringNullTerminated);
        _encodingsByString.Add("string_length_prefixed", FieldEncoding.StringLengthPrefixed);
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
    // Returns:
    //   The number of PatchOpcode rows for this patch level.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private int CountPatchOpcodes()
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*)"
            + " FROM PatchOpcode"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType";
        cmd.Parameters.AddWithValue("@patchDate", _patchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", _patchLevel.ServerType);

        object? result = cmd.ExecuteScalar();
        if (result == null || result == DBNull.Value)
        {
            throw new InvalidOperationException("PatchData.CountPatchOpcodes: count query "
                + "returned null for patchLevel=" + _patchLevel);
        }

        int count = Convert.ToInt32(result);
        DebugLog.Write(LogChannel.Opcodes, "PatchData.CountPatchOpcodes: " + count
            + " PatchOpcode row(s) for patchLevel=" + _patchLevel);
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
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadPatchOpcodes()
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT opcode_value, version, opcode_name"
            + " FROM PatchOpcode"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType";
        cmd.Parameters.AddWithValue("@patchDate", _patchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", _patchLevel.ServerType);

        using SqliteDataReader reader = cmd.ExecuteReader();
        int handleIndex = 0;
        while (reader.Read())
        {
            int opcodeValueRaw = reader.GetInt32(0);
            int version = reader.GetInt32(1);
            string opcodeName = reader.GetString(2);
            ushort opcodeValue = (ushort)opcodeValueRaw;
            OpcodeHandle handle = (OpcodeHandle)handleIndex;

            PatchOpcode patchOpcode = new PatchOpcode(_patchLevel, opcodeValue, version);
            _patchOpcodes[handle] = patchOpcode;
            _namesByHandle[handle] = opcodeName;
            _handlesByName[opcodeName] = handle;

            handleIndex = handleIndex + 1;
        }

        DebugLog.Write(LogChannel.Opcodes, "PatchData.LoadPatchOpcodes: loaded "
            + handleIndex + " PatchOpcode(s) for patchLevel=" + _patchLevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadOpcodeMap
    //
    // Populates _opcodeValuesByName from the PatchOpcode table for this patch level.
    // Multiple versions of the same opcode share the same opcode_value, so SELECT DISTINCT
    // on (opcode_name, opcode_value) yields the correct one-to-one map.  Called once from
    // the constructor.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadOpcodeMap()
    {
        DebugLog.Write(LogChannel.Opcodes, "PatchData.LoadOpcodeMap: querying PatchOpcode for PatchLevel = " +
            _patchLevel);

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT opcode_name, opcode_value"
            + " FROM PatchOpcode"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType";
        cmd.Parameters.AddWithValue("@patchDate", _patchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", _patchLevel.ServerType);

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
    // Loads every field definition — required and optional — for the PatchOpcode at the
    // given handle, and stores the result under both the dictionary key and the handle-
    // indexed array slot.  A single FieldDefinition[] holds the full list; callers cannot
    // distinguish required from optional and do not need to.
    //
    // Load order within the method:
    //   1. Read required fields from PacketField, ordered by bit_offset.
    //   2. Read the optional groups for this opcode from PacketOptionalGroup, ordered by
    //      bit_offset.
    //   3. For each group, read its optional fields from PacketOptionalField, ordered by
    //      sequence_order, and append them to the same list.
    //
    // Required fields carry their own bit_offset.  Optional fields carry the bit_offset of
    // their containing group (the position in the payload where the group's run starts);
    // the extractor computes the live offset of each present optional field at decode time.
    // FlagMask carries PacketOptionalField.flag_mask for required fields it is zero.
    //
    // A PatchOpcode with no fields at all (no PacketField rows and no PacketOptionalGroup
    // rows) is not added to the dictionary and leaves the array slot null.
    // GetFieldDefinitions returns null for those, which the extractor treats as "opcode
    // known but unsupported."
    //
    // Encoding strings are resolved via _encodingsByString.  An unrecognized encoding is
    // stored as FieldEncoding.Unknown; the extractor silently skips Unknown slots so a
    // single misconfigured row does not block the load or spam the hot path.
    //
    // One database connection is opened for the lifetime of the call and reused for all
    // three query stages.
    //
    // Parameters:
    //   handle  - The OpcodeHandle of the PatchOpcode whose fields to load.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadFields(OpcodeHandle handle)
    {
        PatchOpcode patchOpcode = _patchOpcodes[handle];
        List<FieldDefinition> fields = new List<FieldDefinition>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        // Load required fields from PacketField, ordered by bit_offset.
        using (SqliteCommand requiredCmd = conn.CreateCommand())
        {
            requiredCmd.CommandText = "SELECT pf.field_name, pf.bit_offset, pf.bit_length, pf.encoding"
                + " FROM PacketField pf"
                + " JOIN PatchOpcode po ON pf.patch_opcode_id = po.id"
                + " WHERE po.patch_date = @patchDate"
                + " AND po.server_type = @serverType"
                + " AND po.opcode_value = @opcodeValue"
                + " AND po.version = @version"
                + " ORDER BY pf.bit_offset";
            requiredCmd.Parameters.AddWithValue("@patchDate", _patchLevel.PatchDate);
            requiredCmd.Parameters.AddWithValue("@serverType", _patchLevel.ServerType);
            requiredCmd.Parameters.AddWithValue("@opcodeValue", (int)patchOpcode.Opcode);
            requiredCmd.Parameters.AddWithValue("@version", patchOpcode.Version);

            using SqliteDataReader reader = requiredCmd.ExecuteReader();
            while (reader.Read())
            {
                string fieldName = reader.GetString(0);
                uint bitOffset = (uint)reader.GetInt32(1);
                uint bitLength = (uint)reader.GetInt32(2);
                string encodingString = reader.GetString(3);

                FieldEncoding encoding;
                bool encodingFound = _encodingsByString.TryGetValue(encodingString, out encoding);
                if (encodingFound == false)
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.LoadFields: unrecognized required encoding '"
                        + encodingString + "' for handle=" + (int)handle
                        + " opcode=0x" + patchOpcode.Opcode.ToString("X4")
                        + " version=" + patchOpcode.Version + " fieldName='" + fieldName
                        + "', storing as Unknown");
                    encoding = FieldEncoding.Unknown;
                }

                FieldDefinition definition;
                definition.Name = fieldName;
                definition.BitOffset = bitOffset;
                definition.BitLength = bitLength;
                definition.Encoding = encoding;
                definition.FlagMask = 0u;
                fields.Add(definition);
            }
        }

        // Load optional groups for this opcode, ordered by bit_offset.
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
            groupsCmd.Parameters.AddWithValue("@patchDate", _patchLevel.PatchDate);
            groupsCmd.Parameters.AddWithValue("@serverType", _patchLevel.ServerType);
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

        // Load optional fields for each group, ordered by sequence_order.  Appended to
        // the same list as required fields.
        for (int groupIndex = 0; groupIndex < groupIds.Count; groupIndex++)
        {
            int groupId = groupIds[groupIndex];
            uint groupBitOffset = groupBitOffsets[groupIndex];

            using SqliteCommand optionalCmd = conn.CreateCommand();
            optionalCmd.CommandText = "SELECT field_name, bit_length, encoding, flag_mask"
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
                uint flagMask = (uint)reader.GetInt32(3);

                FieldEncoding encoding;
                bool encodingFound = _encodingsByString.TryGetValue(encodingString, out encoding);
                if (encodingFound == false)
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.LoadFields: unrecognized optional encoding '"
                        + encodingString + "' for handle=" + (int)handle
                        + " opcode=0x" + patchOpcode.Opcode.ToString("X4")
                        + " version=" + patchOpcode.Version + " groupId=" + groupId
                        + " fieldName='" + fieldName + "', storing as Unknown");
                    encoding = FieldEncoding.Unknown;
                }

                FieldDefinition definition;
                definition.Name = fieldName;
                definition.BitOffset = groupBitOffset;
                definition.BitLength = bitLength;
                definition.Encoding = encoding;
                definition.FlagMask = flagMask;
                fields.Add(definition);
            }
        }

        if (fields.Count == 0)
        {
            return;
        }

        FieldDefinition[] definitionsArray = fields.ToArray();
        _fieldsByOpcode[patchOpcode] = definitionsArray;
        _opcodeFields[handle] = definitionsArray;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeValue
    //
    // Returns the wire opcode value for the given logical name in this patch level.  Called
    // by handlers at construction time so they know which wire opcode to register for.
    // Returns 0 if the name is unknown — handlers check for 0 and skip their own
    // registration if this patch does not define their opcode.  0 is not a valid wire
    // opcode in EQ, so it serves as an unambiguous "not found" sentinel.
    //
    // Cold path: handlers call this once per opcode at construction.  Not called from the
    // hot path.
    //
    // Parameters:
    //   opcodeName - The logical name (e.g. "OP_PlayerProfile").
    //
    // Returns:
    //   The wire opcode value (e.g. 0x6FA1), or 0 if the name is not in this patch.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ushort GetOpcodeValue(string opcodeName)
    {
        ushort opcodeValue;
        bool found = _opcodeValuesByName.TryGetValue(opcodeName, out opcodeValue);
        if (found == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "PatchData.GetOpcodeValue: unknown opcode name '"
                + opcodeName + "' in PatchLevel " + _patchLevel + ", returning 0");
            return 0;
        }
        return opcodeValue;
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
                + opcodeName + "' in patchLevel=" + _patchLevel + ", returning -1");
            return (OpcodeHandle)(-1);
        }
        return handle;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetFieldDefinitions
    //
    // Returns the field definitions for the given OpcodeId in this patch level.  Called by
    // handlers at construction time, once per OpcodeId the handler cares about.  The
    // returned array is the live cache entry — handlers must not modify it.  Returning the
    // cache array directly avoids per-handler allocation; FieldDefinition is a struct so
    // element reads copy out cleanly.
    //
    // Returns null if the OpcodeId is not in the cache.  This happens when the OpcodeId
    // has no PacketField rows for this patch — the load skipped caching it.  Handlers
    // receive null and treat the opcode as unsupported for this patch.
    //
    // Cold path: handlers call this once per OpcodeId at construction.  Not called from
    // the hot path.
    //
    // Parameters:
    //   opcodeId  - The OpcodeId whose field definitions to return.
    //
    // Returns:
    //   The cache array of FieldDefinitions for the OpcodeId, or null if absent.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public FieldDefinition[]? GetFieldDefinitions(PatchOpcode opcodeId)
    {
        FieldDefinition[]? definitions;
        bool found = _fieldsByOpcode.TryGetValue(opcodeId, out definitions);
        if (found == false)
        {
            return null;
        }
        return definitions;
    }
}

