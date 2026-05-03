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
    private readonly Dictionary<(string, int), FieldDefinition[]> _fieldsByOpcodeAndVersion;
    private readonly Dictionary<string, FieldEncoding> _encodingsByString;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PatchData (constructor, explicit patch)
    //
    // Loads the opcode map and every opcode's field definitions for the given patch_date and
    // server_type.  Both are fully populated before the constructor returns.  After
    // construction the instance is read-only.
    //
    // Throws InvalidOperationException if no PatchOpcode rows exist for the requested patch.
    // This is rare in normal operation but covers the case where the database has no patch
    // data at all for the server type (e.g. a freshly-seeded Test server before Inference
    // has discovered any opcodes).
    //
    // Parameters:
    //   patchLevel  - The patch level represented
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchData(PatchLevel patchLevel)
    {
        _patchDate = patchLevel.PatchDate;
        _serverType = patchLevel.ServerType;

        _encodingsByString = new Dictionary<string, FieldEncoding>();
        _encodingsByString.Add("uint8", FieldEncoding.UInt8);
        _encodingsByString.Add("uint16_le", FieldEncoding.UInt16LE);
        _encodingsByString.Add("uint16_be", FieldEncoding.UInt16BE);
        _encodingsByString.Add("int16_le", FieldEncoding.Int16LE);
        _encodingsByString.Add("int32_le", FieldEncoding.Int32LE);
        _encodingsByString.Add("int32_be", FieldEncoding.Int32BE);
        _encodingsByString.Add("uint32_le", FieldEncoding.UInt32LE);
        _encodingsByString.Add("float_le", FieldEncoding.FloatLE);
        _encodingsByString.Add("float_be", FieldEncoding.FloatBE);
        _encodingsByString.Add("uint_le_masked", FieldEncoding.UIntLEMasked);
        _encodingsByString.Add("sign_extend_fixed_div8", FieldEncoding.SignExtendFixedDiv8);
        _encodingsByString.Add("string_null_terminated", FieldEncoding.StringNullTerminated);
        _encodingsByString.Add("string_length_prefixed", FieldEncoding.StringLengthPrefixed);

        DebugLog.Write(LogChannel.Opcodes, "PatchData ctor: encoding string table built with "
            + _encodingsByString.Count + " entries, patchDate=" + _patchDate
            + " serverType=" + _serverType);

        _opcodeValuesByName = new Dictionary<string, ushort>();
        _fieldsByOpcodeAndVersion = new Dictionary<(string, int), FieldDefinition[]>();

        LoadOpcodeMap();
        if (_opcodeValuesByName.Count == 0)
        {
            DebugLog.Write(LogChannel.Opcodes, "PatchData ctor: no PatchOpcode rows for patchDate="
                + _patchDate + " serverType=" + _serverType + ", throwing");
            throw new InvalidOperationException("PatchData: no PatchOpcode rows for patchDate='"
                + _patchDate + "' serverType='" + _serverType + "'");
        }
        DebugLog.Write(LogChannel.Opcodes, "PatchData ctor: loaded "
            + _opcodeValuesByName.Count + " opcode names");

        LoadFieldDefinitions();
        DebugLog.Write(LogChannel.Opcodes, "PatchData ctor: loaded field definitions for "
            + _fieldsByOpcodeAndVersion.Count + " (opcode, version) pair(s)");
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
    // LoadFieldDefinitions
    //
    // Populates _fieldsByOpcodeAndVersion with every (opcode, version) combination's field
    // definitions in one pass.  Issues a single SELECT joining PacketField to PatchOpcode
    // for this patch, ordered by opcode_name, version, then bit_offset.  Reads rows
    // sequentially and flushes accumulated fields into the dictionary on each
    // (opcode_name, version) boundary.
    //
    // Called once from the constructor, after LoadOpcodeMap.  An (opcode, version) pair
    // present in the opcode map but with no PacketField rows is not added to the cache;
    // GetFieldDefinitions returns null for those, mirroring the previous extractor behavior
    // for unknown-but-loaded opcodes.
    //
    // The version column distinguishes channel variants of the same opcode within one
    // patch — for example, OP_Tracking has three different payload formats sharing one wire
    // value.  Handlers that care about more than one channel call GetFieldDefinitions once
    // per version and store the resulting field indices separately.
    //
    // Encoding strings are resolved to FieldEncoding via _encodingsByString.  Unrecognized
    // encodings are stored as FieldEncoding.Unknown; Extract silently skips slots with
    // Unknown encoding, so a misconfigured row does not block the load or spam the hot
    // path log on every packet.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadFieldDefinitions()
    {
        DebugLog.Write(LogChannel.Network, "PatchData.LoadFieldDefinitions: querying PacketField for patchDate="
            + _patchDate + " serverType=" + _serverType);

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT po.opcode_name, po.version, pf.field_name, pf.bit_offset,"
            + " pf.bit_length, pf.encoding"
            + " FROM PacketField pf"
            + " JOIN PatchOpcode po ON pf.patch_opcode_id = po.id"
            + " WHERE po.patch_date = @patchDate"
            + " AND po.server_type = @serverType"
            + " ORDER BY po.opcode_name, po.version, pf.bit_offset";
        cmd.Parameters.AddWithValue("@patchDate", _patchDate);
        cmd.Parameters.AddWithValue("@serverType", _serverType);

        using SqliteDataReader reader = cmd.ExecuteReader();

        string currentOpcodeName = string.Empty;
        int currentVersion = 0;
        bool haveCurrentGroup = false;
        List<FieldDefinition> currentFields = new List<FieldDefinition>();
        int totalRowCount = 0;
        int unknownEncodingCount = 0;

        while (reader.Read())
        {
            string opcodeName = reader.GetString(0);
            int version = reader.GetInt32(1);
            string fieldName = reader.GetString(2);
            uint bitOffset = (uint)reader.GetInt32(3);
            uint bitLength = (uint)reader.GetInt32(4);
            string encodingString = reader.GetString(5);

            bool boundary = haveCurrentGroup == false
                || opcodeName != currentOpcodeName
                || version != currentVersion;

            if (boundary == true)
            {
                if (haveCurrentGroup == true)
                {
                    _fieldsByOpcodeAndVersion[(currentOpcodeName, currentVersion)]
                        = currentFields.ToArray();
                    DebugLog.Write(LogChannel.Fields, "PatchData.LoadFieldDefinitions: cached "
                        + currentFields.Count + " field(s) for opcodeName='"
                        + currentOpcodeName + "' version=" + currentVersion);
                }
                currentOpcodeName = opcodeName;
                currentVersion = version;
                haveCurrentGroup = true;
                currentFields = new List<FieldDefinition>();
            }

            FieldEncoding encoding;
            bool encodingFound = _encodingsByString.TryGetValue(encodingString, out encoding);
            if (encodingFound == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.LoadFieldDefinitions: unrecognized encoding '"
                    + encodingString + "' for opcodeName='" + opcodeName
                    + "' version=" + version + " fieldName='" + fieldName
                    + "', storing as Unknown");
                encoding = FieldEncoding.Unknown;
                unknownEncodingCount++;
            }

            FieldDefinition definition;
            definition.Name = fieldName;
            definition.BitOffset = bitOffset;
            definition.BitLength = bitLength;
            definition.Encoding = encoding;
            currentFields.Add(definition);
            totalRowCount++;
        }

        if (haveCurrentGroup == true)
        {
            _fieldsByOpcodeAndVersion[(currentOpcodeName, currentVersion)]
                = currentFields.ToArray();
            DebugLog.Write(LogChannel.Opcodes, "PatchData.LoadFieldDefinitions: cached "
                + currentFields.Count + " field(s) for opcodeName='"
                + currentOpcodeName + "' version=" + currentVersion);
        }

        DebugLog.Write(LogChannel.Opcodes, "PatchData.LoadFieldDefinitions: read " + totalRowCount
            + " row(s) across " + _fieldsByOpcodeAndVersion.Count
            + " (opcode, version) pair(s), " + unknownEncodingCount + " unknown encoding(s)");
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
    // Returns the field definitions for the given opcode name and version in this patch
    // level.  Called by handlers at construction time, once per (opcode name, version) pair
    // the handler cares about.  The returned array is the live cache entry — handlers must
    // not modify it.  Returning the cache array directly avoids per-handler allocation;
    // FieldDefinition is a struct so element reads copy out cleanly.
    //
    // Returns null if the (opcode name, version) pair is not in the cache.  Two distinct
    // failure modes collapse into the same null result:
    //
    //   - The opcode name is unknown to this patch.  In practice handlers validate the
    //     opcode name first via GetOpcodeValue (checking for the 0 sentinel) and skip the
    //     field-definitions call entirely, so this path is defensive.
    //
    //   - The opcode name is known but no PacketField rows exist for the requested version.
    //     A real possibility for opcodes with multiple channel variants where one variant
    //     has not yet been characterized.  Handlers that ask for a missing version receive
    //     null and should treat that variant as unsupported.
    //
    // Cold path: handlers call this once per opcode/version at construction.  Not called
    // from the hot path.
    //
    // Parameters:
    //   opcodeName  - The logical name (e.g. "OP_PlayerProfile").
    //   version     - The version number to load.  Defaults to 1, which is the right answer
    //                 for opcodes whose payload does not vary by channel.
    //
    // Returns:
    //   The cache array of FieldDefinitions for the (opcode, version) pair, or null if the
    //   pair is not in the cache.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public FieldDefinition[]? GetFieldDefinitions(string opcodeName, int version = 1)
    {
        FieldDefinition[]? definitions;
        bool found = _fieldsByOpcodeAndVersion.TryGetValue((opcodeName, version), out definitions);
        if (found == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "PatchData.GetFieldDefinitions: no cached entry for opcodeName='"
                + opcodeName + "' version=" + version + " in patchDate=" + _patchDate
                + " serverType=" + _serverType + ", returning null");
            return null;
        }

        DebugLog.Write(LogChannel.Opcodes, "PatchData.GetFieldDefinitions: returning "
            + definitions!.Length + " field(s) for opcodeName='" + opcodeName
            + "' version=" + version);
        return definitions;
    }
}
