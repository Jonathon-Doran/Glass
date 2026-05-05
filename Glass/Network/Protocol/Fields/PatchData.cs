using Glass.Core.Logging;
using Glass.Data;
using Microsoft.Data.Sqlite;
using System.Collections.Generic;

namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// PatchData
//
// A fully-loaded, immutable snapshot of one (patch_date, server_type) pair from the
// database.  Owns the opcode-name to wire-value map and the per-opcode field-definition
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
    private readonly string _patchDate;
    private readonly string _serverType;
    private readonly Dictionary<string, ushort> _opcodeValuesByName;
    private readonly Dictionary<OpcodeId, FieldDefinition[]> _fieldsByOpcode;
    private readonly Dictionary<string, FieldEncoding> _encodingsByString;

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
    //   3. Enumerate every OpcodeId (opcode_value, version) present in PatchOpcode for this
    //      patch.
    //   4. For each OpcodeId, load its required field definitions from PacketField, then
    //      append its optional field definitions from PacketOptionalGroup / PacketOptionalField
    //      to the same list.  Cache the combined list under the OpcodeId.
    //
    // Optional fields are folded into the same FieldDefinition[] as required fields.  Callers
    // see one flat list per OpcodeId and cannot distinguish required from optional — by
    // design.  Extraction handles missing payload bytes by leaving slots empty; handlers
    // check for empty slots on the fields they know are optional.
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
        _patchDate = patchLevel.PatchDate;
        _serverType = patchLevel.ServerType;

        DebugLog.Write(LogChannel.Opcodes, "PatchData ctor: begin patchDate=" + _patchDate
            + " serverType=" + _serverType);

        _encodingsByString = new Dictionary<string, FieldEncoding>();
        _encodingsByString.Add("uint", FieldEncoding.UInt);
        _encodingsByString.Add("int", FieldEncoding.Int);


        _encodingsByString.Add("float", FieldEncoding.Float);
        _encodingsByString.Add("uint_masked", FieldEncoding.UIntMasked);
        _encodingsByString.Add("sign_extend_fixed_div8", FieldEncoding.SignExtendFixedDiv8);
        _encodingsByString.Add("string_null_terminated", FieldEncoding.StringNullTerminated);
        _encodingsByString.Add("string_length_prefixed", FieldEncoding.StringLengthPrefixed);

        _opcodeValuesByName = new Dictionary<string, ushort>();
        _fieldsByOpcode = new Dictionary<OpcodeId, FieldDefinition[]>();

        LoadOpcodeMap();
        if (_opcodeValuesByName.Count == 0)
        {
            throw new InvalidOperationException("PatchData: no PatchOpcode rows for patchDate='"
                + _patchDate + "' serverType='" + _serverType + "'");
        }
        DebugLog.Write(LogChannel.Opcodes, "PatchData ctor: loaded "
            + _opcodeValuesByName.Count + " opcode name(s)");

        List<OpcodeId> opcodeIds = LoadOpcodeIds();
        DebugLog.Write(LogChannel.Opcodes, "PatchData ctor: enumerated "
            + opcodeIds.Count + " OpcodeId(s) for this patch");

        int opcodeIndex = 0;
        while (opcodeIndex < opcodeIds.Count)
        {
            OpcodeId opcodeId = opcodeIds[opcodeIndex];
            DebugLog.Write(LogChannel.Opcodes, "PatchData ctor: loading fields for OpcodeId opcode=0x"
                + opcodeId.Opcode.ToString("X4") + " version=" + opcodeId.Version);

            LoadFields(opcodeId);
            List<int> groupIds = LoadOptionalGroupIds(opcodeId);
            int groupIndex = 0;
            while (groupIndex < groupIds.Count)
            {
                LoadOptionalFields(opcodeId, groupIds[groupIndex]);
                groupIndex = groupIndex + 1;
            }

            opcodeIndex = opcodeIndex + 1;
        }

        DebugLog.Write(LogChannel.Opcodes, "PatchData ctor: complete, "
            + _fieldsByOpcode.Count + " OpcodeId(s) cached");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadOpcodeIds
    //
    // Returns every OpcodeId present in PatchOpcode for this patch level.  One row per
    // (opcode_value, version) pair — the same opcode_value can appear multiple times when
    // an opcode has multiple versions (e.g. OP_Tracking has three).
    //
    // Called once from the constructor.  The returned list drives the per-OpcodeId field
    // loading loop.
    //
    // Returns:
    //   The list of OpcodeIds for this patch.  Empty if the patch has no opcodes — but the
    //   constructor has already checked _opcodeValuesByName.Count and thrown in that case,
    //   so in normal flow this never returns empty.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private List<OpcodeId> LoadOpcodeIds()
    {
        List<OpcodeId> opcodeIds = new List<OpcodeId>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT opcode_value, version"
            + " FROM PatchOpcode"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType";
        cmd.Parameters.AddWithValue("@patchDate", _patchDate);
        cmd.Parameters.AddWithValue("@serverType", _serverType);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            int opcodeValueRaw = reader.GetInt32(0);
            int version = reader.GetInt32(1);
            ushort opcodeValue = (ushort)opcodeValueRaw;
            OpcodeId opcodeId = new OpcodeId(opcodeValue, version);
            opcodeIds.Add(opcodeId);
        }

        return opcodeIds;
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
        DebugLog.Write(LogChannel.Opcodes, "PatchData.LoadOpcodeMap: querying PatchOpcode for patchDate="
            + _patchDate + " serverType=" + _serverType);

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT opcode_name, opcode_value"
            + " FROM PatchOpcode"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType";
        cmd.Parameters.AddWithValue("@patchDate", _patchDate);
        cmd.Parameters.AddWithValue("@serverType", _serverType);

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
    // Loads the required field definitions for one OpcodeId from PacketField and stores
    // them in _fieldsByOpcode.  Fields are returned in bit_offset order, which is the order
    // the extractor walks them.
    //
    // An OpcodeId with no PacketField rows is not added to the cache.  GetFieldDefinitions
    // returns null for those, which the extractor treats as "opcode known but unsupported."
    //
    // Encoding strings are resolved via _encodingsByString.  An unrecognized encoding is
    // stored as FieldEncoding.Unknown; the extractor silently skips Unknown slots so a
    // single misconfigured row does not block the load or spam the hot path.
    //
    // Optional fields for the same OpcodeId are appended to this list later by
    // LoadOptionalFields.  Until that runs, the list reflects required fields only.
    //
    // Parameters:
    //   opcodeId  - The OpcodeId whose required fields to load.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadFields(OpcodeId opcodeId)
    {
        List<FieldDefinition> fields = new List<FieldDefinition>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pf.field_name, pf.bit_offset, pf.bit_length, pf.encoding"
            + " FROM PacketField pf"
            + " JOIN PatchOpcode po ON pf.patch_opcode_id = po.id"
            + " WHERE po.patch_date = @patchDate"
            + " AND po.server_type = @serverType"
            + " AND po.opcode_value = @opcodeValue"
            + " AND po.version = @version"
            + " ORDER BY pf.bit_offset";
        cmd.Parameters.AddWithValue("@patchDate", _patchDate);
        cmd.Parameters.AddWithValue("@serverType", _serverType);
        cmd.Parameters.AddWithValue("@opcodeValue", (int)opcodeId.Opcode);
        cmd.Parameters.AddWithValue("@version", opcodeId.Version);

        using SqliteDataReader reader = cmd.ExecuteReader();
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
                DebugLog.Write(LogChannel.Fields, "PatchData.LoadFields: unrecognized encoding '"
                    + encodingString + "' for opcode=0x" + opcodeId.Opcode.ToString("X4")
                    + " version=" + opcodeId.Version + " fieldName='" + fieldName
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

        if (fields.Count == 0)
        {
            return;
        }

        _fieldsByOpcode[opcodeId] = fields.ToArray();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadOptionalGroupIds
    //
    // Returns the PacketOptionalGroup.id values for one OpcodeId, ordered by bit_offset.
    // Used by LoadOptionalFields to decide whether this OpcodeId has any optional fields
    // (empty list means no) and, if so, which groups to load fields from.
    //
    // The join to PatchOpcode resolves the (opcode_value, version) pair to the matching
    // PatchOpcode.id, which PacketOptionalGroup references via patch_opcode_id.
    //
    // Parameters:
    //   opcodeId  - The OpcodeId whose optional groups to enumerate.
    //
    // Returns:
    //   The list of PacketOptionalGroup.id values for this OpcodeId.  Empty if there are
    //   none.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private List<int> LoadOptionalGroupIds(OpcodeId opcodeId)
    {
        List<int> groupIds = new List<int>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pog.id"
            + " FROM PacketOptionalGroup pog"
            + " JOIN PatchOpcode po ON pog.patch_opcode_id = po.id"
            + " WHERE po.patch_date = @patchDate"
            + " AND po.server_type = @serverType"
            + " AND po.opcode_value = @opcodeValue"
            + " AND po.version = @version"
            + " ORDER BY pog.bit_offset";
        cmd.Parameters.AddWithValue("@patchDate", _patchDate);
        cmd.Parameters.AddWithValue("@serverType", _serverType);
        cmd.Parameters.AddWithValue("@opcodeValue", (int)opcodeId.Opcode);
        cmd.Parameters.AddWithValue("@version", opcodeId.Version);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            int groupId = reader.GetInt32(0);
            groupIds.Add(groupId);
        }

        return groupIds;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadOptionalFields
    //
    // Loads the optional field definitions for one PacketOptionalGroup and appends them to
    // the FieldDefinition[] entry for the given OpcodeId in _fieldsByOpcode.  Called once
    // per group; an OpcodeId with multiple groups produces multiple calls.
    //
    // Optional FieldDefinitions have BitOffset zero.  Their position in the payload depends
    // on which earlier optional fields in the group are present in the specific packet, so
    // a static layout offset is meaningless for them.  The extractor computes the live
    // offset at extract time.  BitLength and Encoding describe the field's size and
    // decoding when it is present.
    //
    // FlagMask carries the PacketOptionalField.flag_mask column value.  The extractor
    // ANDs FlagMask against the value of the field literally named "flags" earlier in the
    // same packet to decide whether to read this field or leave its slot empty.
    //
    // Field definitions are appended in sequence_order, the order they appear in the
    // payload when present.  The extractor always adds a slot per FieldDefinition entry
    // (empty for absent optionals) so that handler-cached slot indices remain stable
    // across packets.
    //
    // Encoding strings are resolved via _encodingsByString.  An unrecognized encoding is
    // stored as FieldEncoding.Unknown.
    //
    // Parameters:
    //   opcodeId  - The OpcodeId whose cache entry receives the appended optional fields.
    //   groupId   - The PacketOptionalGroup.id whose fields to load.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadOptionalFields(OpcodeId opcodeId, int groupId)
    {
        FieldDefinition[] existing = _fieldsByOpcode[opcodeId];

        List<FieldDefinition> appended = new List<FieldDefinition>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pof.field_name, pof.bit_length, pof.encoding, pof.flag_mask,"
            + " pog.bit_offset"
            + " FROM PacketOptionalField pof"
            + " JOIN PacketOptionalGroup pog ON pof.group_id = pog.id"
            + " WHERE pof.group_id = @groupId"
            + " ORDER BY pof.sequence_order";
        cmd.Parameters.AddWithValue("@groupId", groupId);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string fieldName = reader.GetString(0);
            uint bitLength = (uint)reader.GetInt32(1);
            string encodingString = reader.GetString(2);
            uint flagMask = (uint)reader.GetInt32(3);
            uint groupBitOffset = (uint)reader.GetInt32(4);

            FieldEncoding encoding;
            bool encodingFound = _encodingsByString.TryGetValue(encodingString, out encoding);
            if (encodingFound == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.LoadOptionalFields: unrecognized encoding '"
                    + encodingString + "' for opcode=0x" + opcodeId.Opcode.ToString("X4")
                    + " version=" + opcodeId.Version + " groupId=" + groupId
                    + " fieldName='" + fieldName + "', storing as Unknown");
                encoding = FieldEncoding.Unknown;
            }

            FieldDefinition definition;
            definition.Name = fieldName;
            definition.BitOffset = groupBitOffset;
            definition.BitLength = bitLength;
            definition.Encoding = encoding;
            definition.FlagMask = flagMask;
            appended.Add(definition);
        }

        if (appended.Count == 0)
        {
            return;
        }

        FieldDefinition[] combined = new FieldDefinition[existing.Length + appended.Count];
        Array.Copy(existing, 0, combined, 0, existing.Length);
        for (int appendIndex = 0; appendIndex < appended.Count; appendIndex++)
        {
            combined[existing.Length + appendIndex] = appended[appendIndex];
        }
        _fieldsByOpcode[opcodeId] = combined;
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
                + opcodeName + "' in patchDate=" + _patchDate
                + " serverType=" + _serverType + ", returning 0");
            return 0;
        }
        return opcodeValue;
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
    public FieldDefinition[]? GetFieldDefinitions(OpcodeId opcodeId)
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

