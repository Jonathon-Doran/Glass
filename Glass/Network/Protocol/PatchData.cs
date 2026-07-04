using Glass.Core;
using Glass.Core.Logging;
using Glass.Data;
using Glass.Network.Protocol.Fields;
using Microsoft.Data.Sqlite;
using System.Reflection.Emit;
using System.Xml.Linq;

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
    private readonly Dictionary<OpcodeValue, string> _opcodeNamesByValue;
    private readonly Dictionary<string, FieldEncoding> _encodingsByString;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _patchOpcodeByOpcodeHandle
    //
    // Flat array of every PatchOpcode loaded for this patch level, indexed by Opcode.
    // Allocated once in the constructor sized to the number of rows; never resized.  An Opcode is a position in this array, valid
    // only for this PatchData instance.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private readonly PatchOpcode[] _patchOpcodeByOpcodeHandle;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // _collections
    //
    // The patch's collections, indexed by CollectionIndex.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private Collection[] _collections;


    private readonly Dictionary<string, CollectionIndex> _collectionIndexByCollectionName;
    private CollectionHandle[] _collectionHandleByIndex;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // _opcodeHandlesByPatchOpcode
    //
    // Maps the PatchOpcode to the OpcodeHandle assigned at load time.  Used on the
    // hot path by the dispatcher to resolve an incoming PatchOpcode to the opcodeHandle that
    // indexes its handler array.
    //
    // 16-June:  This is currently widely used
    ///////////////////////////////////////////////////////////////////////////////////////////
    private readonly Dictionary<PatchOpcode, OpcodeHandle> _opcodeHandlesByPatchOpcode;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // _opcodeNamesByOpcodeHandle
    //
    // Parallel array to _patchOpcodeByOpcodeHandle: _opcodeNamesByOpcodeHandle[opcodeHandle] is the opcode_name string
    // for _patchOpcodeByOpcodeHandle[opcodeHandle].  Allocated once in the constructor sized to the number
    // of rows; never resized.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private readonly string[] _opcodeNamesByOpcodeHandle;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // _opcodeHandlesByName
    //
    // Reverse map of _opcodeNamesByOpcodeHandle: opcode_name string to Opcode. 
    ///////////////////////////////////////////////////////////////////////////////////////////
    private readonly Dictionary<string, OpcodeHandle> _opcodeHandlesByName;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // _pendingRelativeNames
    //
    // Scratch list used during the constructor's per-opcode field-loading loop.
    // LoadRequiredFields appends one entry per FieldDefinition it returns, holding the raw
    // relative_to string from the PacketField row (null when the column is NULL).
    // ResolveRelativeAnchors consumes the list to populate FieldDefinition.RelativeToSlot
    // on the assembled array, then clears it for reuse by the next opcode.  The constructor
    // sets the field to null after the loop completes so the empty list is released for
    // garbage collection — PatchData has no need to retain it after construction.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private List<string?>? _pendingRelativeNames;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // _gate
    //
    // Flat array of childCollection GateDefinition structures for this patch level, indexed by GateDefinitionHandle.
    // Sized by CountGates and filled incrementally as each gate is childCollection at its
    // referrer's load time, in resolution order.  A GateDefinitionHandle is a position in this array.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private readonly GateDefinition[] _gate;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // _gateHandlesByName
    //
    // Maps a gate's name to the GateDefinitionHandle assigned at load time.
    // The reverse lookup is the definition's Name member via the _gate array.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private readonly Dictionary<string, GateDefinitionHandle> _gateHandlesByName;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // _gateFieldNamesByHandle
    //
    // Load-time map from a GateDefinitionHandle to the raw field_name string its Gate row carries,
    // for gates whose multiplicity reads a count field.  Populated by LoadGateDefinitions
    // and consumed by the field-loading pass, which pairs the name with the referencing
    // collection to build the pending entries ResolveGates resolves.  Set to null by the
    // constructor after loading completes.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private Dictionary<GateDefinitionHandle, string>? _gateFieldNamesByHandle;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PendingFieldPredicate
    //
    // Load-time record pairing a field that carries a predicate with the raw predicate string read
    // from its PacketField row.  Created during a collection's field read loop for every row whose
    // predicate column is non-null, and consumed after ResolveRelativeAnchors has reordered the
    // collection's field array.  The owner is held by name because the reorder invalidates the
    // read-loop position; the resolve step looks the name up against the finalized array to find
    // the element to write.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private struct PendingFieldPredicate
    {
        public string OwnerFieldName;
        public string RawPredicate;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeRecord
    //
    // Everything loaded for one PatchOpcode row, stored at its OpcodeHandle in _opcodes.
    // PatchOpcode is the opcode's identity (patch level, wire value, version); Name is its
    // opcode_name string; Gate is the handle of its top-level gate, the entry point for
    // extracting its payload, or GateDefinitionHandle.None when the row named no loaded gate.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private struct OpcodeRecord
    {
        public PatchOpcode PatchOpcode;
        public string Name;
        public GateDefinitionHandle Gate;
        public CollectionHandle CollectionHandle;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // _opcodes
    //
    // Flat array of every OpcodeRecord for this patch level, indexed by OpcodeHandle.
    // Allocated once in the constructor sized by CountPatchOpcodes and fully populated by
    // LoadPatchOpcodes in database row order; never resized.  An OpcodeHandle is a position
    // in this array, valid only for this PatchData instance.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private readonly OpcodeRecord[] _opcodes;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _pendingFieldPredicates
    //
    // Scratch list used during a collection's field read loop in LoadFields.  Holds one entry per
    // field whose predicate column is non-null.  The predicate-resolution step consumes the list
    // after ResolveRelativeAnchors reorders the field array, then clears it for reuse by the next
    // collection.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private List<PendingFieldPredicate>? _pendingFieldPredicates;

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
    //   3. Enumerate every PatchOpcode for this patch.  Allocate the parallel opcodeHandle-
    //      indexed arrays from the resulting count.
    //   4. For each PatchOpcode, store it under its opcodeHandle, then load its required field
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
        _opcodeNamesByValue = new Dictionary<OpcodeValue, string>();

        _pendingFieldPredicates = new List<PendingFieldPredicate>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        int opcodeCount = CountPatchOpcodes(conn);
        int collectionCount = CountPatchCollections(conn);
        uint gateCount = CountGates(conn);

        if (opcodeCount == 0)
        {
            throw new InvalidOperationException("PatchData: no PatchOpcode rows for patchLevel="
                + PatchLevel);
        }

        _patchOpcodeByOpcodeHandle = new PatchOpcode[opcodeCount];
        _collections = new Collection[collectionCount];
        _opcodeNamesByOpcodeHandle = new string[opcodeCount];
        _opcodeHandlesByName = new Dictionary<string, OpcodeHandle>(opcodeCount);
        _opcodeHandlesByPatchOpcode = new Dictionary<PatchOpcode, OpcodeHandle>(opcodeCount);
        _pendingRelativeNames = new List<string?>();

        _opcodes = new OpcodeRecord[opcodeCount];
        _collectionHandleByIndex = new CollectionHandle[collectionCount];

        _collectionIndexByCollectionName = new Dictionary<string, CollectionIndex>(collectionCount);
        _gate = new GateDefinition[gateCount];
        _gateHandlesByName = new Dictionary<string, GateDefinitionHandle>((int)gateCount);
        _gateFieldNamesByHandle = new Dictionary<GateDefinitionHandle, string>();

        LoadOpcodeMap(conn);
        LoadPatchCollections(conn);
        LoadGateDefinitions(conn);
        LoadPatchOpcodes(conn);

        for (uint collectionIndex = 0; collectionIndex < collectionCount; collectionIndex++)
        {
            CollectionIndex collection = (CollectionIndex) collectionIndex;

            LoadFields(collection, conn);
        }
        ResolveGates();
        _pendingRelativeNames = null;
        _pendingFieldPredicates = null;
        _gateFieldNamesByHandle = null;
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
        _encodingsByString.Add("csv_token", FieldEncoding.CsvToken);
        _encodingsByString.Add("uint_msb", FieldEncoding.UIntMsb);
        _encodingsByString.Add("uint_masked", FieldEncoding.UIntMasked);
        _encodingsByString.Add("signmag_msb", FieldEncoding.SignMagnitudeMsb);
        _encodingsByString.Add("signmag_lsb", FieldEncoding.SignMagnitudeLsb);
        _encodingsByString.Add("opt_signmag_msb", FieldEncoding.OptSignMagnitudeMsb);
        _encodingsByString.Add("string_null_terminated", FieldEncoding.StringNullTerminated);
        _encodingsByString.Add("string_length_prefixed", FieldEncoding.StringLengthPrefixed);
        _encodingsByString.Add("blob", FieldEncoding.Blob);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CountPatchOpcodes
    //
    // Returns the number of PatchOpcode rows for this patch level.  Used by the constructor
    // to size the opcodeHandle-indexed arrays before any per-opcode loading runs.
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

        return count;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CountPatchCollections
    //
    // Returns the number of FieldCollection rows for this patch level.
    //
    // Throws InvalidOperationException if the count query returns null, which indicates a
    // database problem rather than an empty result (an empty FieldCollection table for this
    // patch produces a count of 0, not null).
    //
    // Parameters:
    //   conn    - An open database connection, owned by the caller.
    //
    // Returns:
    //   The number of FieldCollection rows for this patch level.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private int CountPatchCollections(SqliteConnection conn)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*)"
            + " FROM FieldCollection"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType";
        cmd.Parameters.AddWithValue("@patchDate", PatchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", PatchLevel.ServerType);

        object? result = cmd.ExecuteScalar();
        if (result == null || result == DBNull.Value)
        {
            throw new InvalidOperationException("PatchData.CountCollections: count query "
                + "returned null for patchLevel=" + PatchLevel);
        }

        int count = Convert.ToInt32(result);

        return count;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CountGates
    //
    // Returns the number of Gate rows for this patch level.
    //
    // Throws InvalidOperationException if the count query returns null, which indicates a
    // database problem rather than an empty result (an empty Gate table for this patch
    // produces a count of 0, not null).
    //
    // Parameters:
    //   conn    - An open database connection, owned by the caller.
    //
    // Returns:
    //   The number of Gate rows for this patch level.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private uint CountGates(SqliteConnection conn)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*)"
            + " FROM Gate"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType";
        cmd.Parameters.AddWithValue("@patchDate", PatchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", PatchLevel.ServerType);

        object? result = cmd.ExecuteScalar();
        if (result == null || result == DBNull.Value)
        {
            throw new InvalidOperationException("PatchData.CountGates: count query "
                + "returned null for patchLevel=" + PatchLevel);
        }

        uint count = Convert.ToUInt32(result);

        return count;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadPatchOpcodes
    //
    // Reads every PatchOpcode row for this patch level and populates _opcodes and the
    // handle-lookup dictionaries _opcodeHandlesByName and _opcodeHandlesByPatchOpcode.  Row
    // order from the database determines opcodeHandle assignment — the first row read becomes
    // opcodeHandle 0, the second opcodeHandle 1, and so on.
    //
    // Each row's gate_name is resolved to a GateDefinitionHandle through _gateHandlesByName, so the
    // gate definitions must be loaded before this method runs.  A null gate_name or a name
    // that resolves to no loaded gate stores GateDefinitionHandle.None and is logged.
    //
    // The array and dictionaries must be allocated by the constructor before this method
    // runs.  The size is determined by CountPatchOpcodes, which uses the same WHERE clause
    // and so produces a count consistent with what this method will read.
    //
    // Parameters:
    //   conn    - An open database connection, owned by the caller.
    //
    // Returns:
    //   The number of opcodes read.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private uint LoadPatchOpcodes(SqliteConnection conn)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT opcode_value, version, opcode_name, gate_name"
            + " FROM PatchOpcode"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType";
        cmd.Parameters.AddWithValue("@patchDate", PatchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", PatchLevel.ServerType);

        using SqliteDataReader reader = cmd.ExecuteReader();
        uint handleIndex = 0;
        while (reader.Read() == true)
        {
            int opcodeValueRaw = reader.GetInt32(0);
            uint version = (uint)reader.GetInt32(1);
            string opcodeName = reader.GetString(2);
            OpcodeValue opcodeValue = (OpcodeValue)opcodeValueRaw;
            OpcodeHandle opcodeHandle = (OpcodeHandle)handleIndex;
            PatchOpcode patchOpcode = new PatchOpcode(PatchLevel, opcodeValue, version);

            GateDefinitionHandle gateHandle = GateDefinitionHandle.None;
            if (reader.IsDBNull(3) == false)
            {
                string gateName = reader.GetString(3);
                bool gateFound = _gateHandlesByName.TryGetValue(gateName, out gateHandle);
                if (gateFound == false)
                {
                    gateHandle = GateDefinitionHandle.None;
                    DebugLog.Write(LogChannel.Opcodes, "PatchData.LoadPatchOpcodes: opcode '"
                        + opcodeName + "' version " + version + " names gate '" + gateName
                        + "' that is not loaded, storing GateDefinitionHandle.None", LogLevel.Warn);
                }
            }
            else
            {
                DebugLog.Write(LogChannel.Opcodes, "PatchData.LoadPatchOpcodes: opcode '"
                    + opcodeName + "' version " + version + " has no gate_name", LogLevel.Warn);
            }

            OpcodeRecord record;
            record.PatchOpcode = patchOpcode;
            record.Name = opcodeName;
            record.Gate = gateHandle;
            if (gateHandle.Exists)
            {
                record.CollectionHandle = _gate[gateHandle].ChildCollection;
            }
            else
            {
                record.CollectionHandle = CollectionHandle.None;
            }


            _opcodes[opcodeHandle] = record;

            _opcodeHandlesByName[opcodeName] = opcodeHandle;
            _opcodeHandlesByPatchOpcode[patchOpcode] = opcodeHandle;

            _patchOpcodeByOpcodeHandle[opcodeHandle] = patchOpcode;
            _opcodeNamesByOpcodeHandle[opcodeHandle] = opcodeName;

            handleIndex = handleIndex + 1;
        }

        return handleIndex;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadPatchCollections
    //
    // Reads every FieldCollection row for this patch level and populates the opcodeHandle-indexed
    // structures: _collectionNamesByHandle and _handlesByCollectionName.  Row order from the
    // database determines opcodeHandle assignment — the first row read becomes opcodeHandle 0, the
    // second opcodeHandle 1, and so on.
    //
    // The array and dictionary must be allocated by the constructor before this method runs.
    // The size is determined by CountPatchCollections, which uses the same WHERE clause and
    // so produces a count consistent with what this method will read.
    //
    // Parameters:
    //   conn    - An open database connection, owned by the caller.
    //
    // Returns:
    //   The number of collections read.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private uint LoadPatchCollections(SqliteConnection conn)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name"
            + " FROM FieldCollection"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType";
        cmd.Parameters.AddWithValue("@patchDate", PatchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", PatchLevel.ServerType);
        PatchRegistry registry = GlassContext.PatchRegistry;

        using SqliteDataReader reader = cmd.ExecuteReader();
        uint handleIndex = 0;

        while (reader.Read())
        {
            CollectionIndex index = (CollectionIndex) handleIndex;
            string collectionName = reader.GetString(0);
            CollectionHandle handle = registry.RegisterCollection(this, index);

            DebugLog.Write(LogChannel.Fields, "Collection '" + collectionName + "' has collection handle=" + handle,
                LogLevel.Info);

            _collectionIndexByCollectionName[collectionName] = index;
            _collectionHandleByIndex[handleIndex] = handle;
            _collections[handleIndex] = new Collection(collectionName, PatchLevel);

            handleIndex = handleIndex + 1;
        }

        return handleIndex;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadGateDefinitions
    //
    // Reads every Gate row for this patch level and populates _gate and _gateHandlesByName.
    // Row order from the database determines GateDefinitionHandle assignment — the first row read
    // becomes handle 0, the second handle 1, and so on.  Each row's kind is parsed to a
    // MultiplicityKind and its child_collection is resolved to a CollectionIndex through the
    // collection name map, which must be populated before this method runs.  A row carrying a
    // field_name has that name recorded in _gateFieldNamesByHandle for the field-loading pass;
    // FieldSlot is stored as None until resolution.
    //
    // An unparsable kind is stored as Once and logged.  A child_collection naming no loaded
    // collection is stored as CollectionIndex.None and logged.
    //
    // Parameters:
    //   conn    - An open database connection, owned by the caller.
    //
    // Returns:
    //   The number of gate definitions read.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private uint LoadGateDefinitions(SqliteConnection conn)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name, kind, child_collection, field_name, count"
            + " FROM Gate"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType";
        cmd.Parameters.AddWithValue("@patchDate", PatchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", PatchLevel.ServerType);

        using SqliteDataReader reader = cmd.ExecuteReader();
        uint handleIndex = 0;
        while (reader.Read() == true)
        {
            string gateName = reader.GetString(0);
            string kindString = reader.GetString(1);
            string childCollectionName = reader.GetString(2);

            MultiplicityKind kind;
            bool kindParsed = Enum.TryParse<MultiplicityKind>(kindString, out kind);
            if (kindParsed == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.LoadGateDefinitions: gate '" + gateName
                    + "' has unparsable kind '" + kindString + "', storing as Once", LogLevel.Warn);
                kind = MultiplicityKind.Once;
            }

            CollectionIndex childCollection = CollectionIndex.None;
            if (childCollectionName.Length > 0)
            {
                bool collectionFound = _collectionIndexByCollectionName.TryGetValue(
                    childCollectionName, out childCollection);
                if (collectionFound == false)
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.LoadGateDefinitions: gate '" + gateName
                        + "' names child collection '" + childCollectionName
                        + "' that is not loaded for patchLevel=" + PatchLevel
                        + ", storing CollectionIndex.None", LogLevel.Warn);
                    childCollection = CollectionIndex.None;
                }
            }

            GateDefinitionHandle handle = (GateDefinitionHandle) handleIndex;
            CollectionHandle collectionHandle = CollectionHandle.None;
            if (childCollection.Exists)
            {
                collectionHandle = _collectionHandleByIndex[childCollection];
            }

            GateDefinition definition;
            definition.Name = gateName;
            definition.PatchLevel = PatchLevel;
            definition.Kind = kind;
            definition.ChildCollection = collectionHandle;
            definition.FieldSlot = SlotId.None;
            definition.FieldSlotLocal = true;
            definition.Count = 0;
            definition.CountFieldName = string.Empty;

            if (reader.IsDBNull(4) == false)
            {
                definition.Count = (uint)reader.GetInt32(4);
                DebugLog.Write(LogChannel.Fields, "PatchData.LoadGateDefinitions: gate '" + gateName
                    + "' carries constant count " + definition.Count, LogLevel.Trace);
            }

            _gate[handle] = definition;
            _gateHandlesByName[gateName] = handle;

            if (reader.IsDBNull(3) == false)
            {
                string fieldName = reader.GetString(3);
                _gateFieldNamesByHandle![handle] = fieldName;
                definition.CountFieldName = fieldName;
                DebugLog.Write(LogChannel.Fields, "PatchData.LoadGateDefinitions: gate '" + gateName
                    + "' carries count field '" + fieldName + "', pending resolution at reference time", LogLevel.Trace);
            }

            handleIndex = handleIndex + 1;
        }

        return handleIndex;
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
            PatchLevel, LogLevel.Trace);

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
            OpcodeValue opcodeValue = (OpcodeValue) reader.GetInt32(1);
            _opcodeNamesByValue[opcodeValue] = opcodeName;
            rowCount++;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadFields
    //
    // Reads the PacketField rows for the collection at the given opcodeHandle, ordered by bit_offset,
    // builds their FieldDefinition array,  and resolves relative
    // anchors.  A collection with no PacketField rows leaves the entry null.
    //
    // Unrecognized encoding strings are stored as FieldEncoding.Unknown.  The relative_to value
    // of each row is appended to _pendingRelativeNames for ResolveRelativeAnchors, which is
    // cleared before this method returns.  A row whose sequence column is null takes its database
    // row position as its sequence so every field carries one.
    //
    // Parameters:
    //   index  - The CollectionIndex whose fields to load.
    //   conn    - An open database connection, owned by the caller.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadFields(CollectionIndex index, SqliteConnection conn)
    {
        string collectionName = GetCollectionNameFromIndex(index);
        List<FieldDefinition> fields = new List<FieldDefinition>();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT field_name, bit_offset, bit_length, encoding, divisor, relative_to, " 
                    + " predicate, sequence"
                    + " FROM PacketField"
                    + " WHERE patch_date = @patchDate"
                    + " AND server_type = @serverType"
                    + " AND collection_name = @collectionName"
                    + " ORDER BY bit_offset";
        cmd.Parameters.AddWithValue("@patchDate", PatchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", PatchLevel.ServerType);
        cmd.Parameters.AddWithValue("@collectionName", collectionName);

        uint rowPosition = 0;

        using (SqliteDataReader reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                string fieldName = reader.GetString(0);
                uint bitOffset = (uint)reader.GetInt32(1);
                uint bitLength = (uint)reader.GetInt32(2);
                string encodingString = reader.GetString(3);
                float divisor = reader.GetFloat(4);
                string? relativeToName;
                if (reader.IsDBNull(5))
                {
                    relativeToName = null;
                }
                else
                {
                    relativeToName = reader.GetString(5);
                }

                FieldEncoding encoding;
                GateDefinitionHandle gate = GateDefinitionHandle.None;

                if (encodingString.StartsWith("Gate", StringComparison.Ordinal) == true)
                {
                    bool gateFound = _gateHandlesByName.TryGetValue(encodingString, out gate);
                    if (gateFound == false)
                    {
                        gate = GateDefinitionHandle.None;
                        DebugLog.Write(LogChannel.Fields, "PatchData.LoadFields: collection='"
                            + collectionName + "' field='" + fieldName + "' references gate '"
                            + encodingString + "' that is not loaded, storing GateDefinitionHandle.None", LogLevel.Warn);
                    }
                    encoding = FieldEncoding.Gate;
                }
                else
                {
                    bool encodingFound = _encodingsByString.TryGetValue(encodingString, out encoding);
                    if (encodingFound == false)
                    {
                        DebugLog.Write(LogChannel.Fields, "PatchData.LoadFields: unrecognized encoding '"
                            + encodingString + "' for collection='" + collectionName
                            + "' fieldName='" + fieldName + "', storing as Unknown", LogLevel.Warn);
                        encoding = FieldEncoding.Unknown;
                    }
                }

                string? predicateString;
                if (reader.IsDBNull(6))
                {
                    predicateString = null;
                }
                else
                {
                    predicateString = reader.GetString(6);
                }

                uint sequence;
                if (reader.IsDBNull(7))
                {
                    sequence = rowPosition;
                }
                else
                {
                    sequence = (uint)reader.GetInt32(7);
                }

                FieldDefinition definition;
                definition.Name = fieldName;
                definition.BitOffset = bitOffset;
                definition.BitLength = bitLength;
                definition.Encoding = encoding;
                definition.Divisor = divisor;
                definition.RelativeToSlot = null;
                definition.Gate = gate;
                definition.Predicate = default;
                definition.Sequence = sequence;
                definition.BlobByteCount = (encoding == FieldEncoding.Blob) ? bitLength / 8u : 0u;
                fields.Add(definition);
                _pendingRelativeNames!.Add(relativeToName);


                if (predicateString != null)
                {
                    PendingFieldPredicate pendingPredicate;
                    pendingPredicate.OwnerFieldName = fieldName;
                    pendingPredicate.RawPredicate = predicateString;
                    _pendingFieldPredicates!.Add(pendingPredicate);
                }

                rowPosition = rowPosition + 1;
            }
        }

        if (fields.Count == 0)
        {
            _pendingRelativeNames!.Clear();
            return;
        }
        _collections[index].Fields = fields.ToArray();
        ResolveRelativeAnchors(index);
        ResolvePredicates(index);

        _pendingRelativeNames!.Clear();
        _pendingFieldPredicates!.Clear();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ResolveRelativeAnchors
    //
    // Reorders collections so that every relative field appears after the field it
    // anchors on, then resolves each anchor name to a slot index and writes it into
    // FieldDefinition.RelativeToSlot.  The ordering is produced by Kahn's algorithm over the
    // name-anchor graph and handles chains of any depth.
    //
    // _pendingRelativeNames supplies the anchor name for each field
    //
    // An anchor name not present in the field list logs and leaves RelativeToSlot null on
    // that field.  A cycle in the graph logs each unresolved field and appends the affected
    // fields in their original order.
    //
    // Parameters:
    //   collectionIndex  - The index of the Collection whose field array to reorder and resolve.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ResolveRelativeAnchors(CollectionIndex collectionIndex)
    {
        FieldDefinition[]? definitions = GetFieldDefinitions(collectionIndex);
        if (definitions == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ResolveRelativeAnchors: no field "
                + "definitions for collection='" + GetCollectionNameFromIndex(collectionIndex)
                + "', nothing to resolve", LogLevel.Warn);
            return;
        }

        List<string?> pending = _pendingRelativeNames!;
        int definitionCount = definitions.Length;

        Dictionary<string, string> anchorNameByFieldName = new Dictionary<string, string>();
        for (int pendingIndex = 0; pendingIndex < pending.Count; pendingIndex++)
        {
            string? anchorName = pending[pendingIndex];
            if (anchorName == null)
            {
                continue;
            }
            string ownName = definitions[pendingIndex].Name;
            anchorNameByFieldName[ownName] = anchorName;
        }

        Dictionary<string, int> indexByName = new Dictionary<string, int>(definitionCount);
        for (int positionIndex = 0; positionIndex < definitionCount; positionIndex++)
        {
            indexByName[definitions[positionIndex].Name] = positionIndex;
        }

        int[] inDegree = new int[definitionCount];
        List<int>[] dependents = new List<int>[definitionCount];
        for (int initIndex = 0; initIndex < definitionCount; initIndex++)
        {
            dependents[initIndex] = new List<int>();
        }

        for (int edgeIndex = 0; edgeIndex < definitionCount; edgeIndex++)
        {
            string ownName = definitions[edgeIndex].Name;
            string anchorName;
            bool hasAnchor = anchorNameByFieldName.TryGetValue(ownName, out anchorName!);
            if (hasAnchor == false)
            {
                continue;
            }

            int anchorIndex;
            bool anchorPresent = indexByName.TryGetValue(anchorName, out anchorIndex);
            if (anchorPresent == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.ResolveRelativeAnchors: collection='"
                    + GetCollectionNameFromIndex(collectionIndex) + "' field='" + ownName
                    + "' references unknown anchor '" + anchorName
                    + "', will be left absolute", LogLevel.Warn);
                continue;
            }

            dependents[anchorIndex].Add(edgeIndex);
            inDegree[edgeIndex] = inDegree[edgeIndex] + 1;
        }

        Queue<int> ready = new Queue<int>();
        for (int readyIndex = 0; readyIndex < definitionCount; readyIndex++)
        {
            if (inDegree[readyIndex] == 0)
            {
                ready.Enqueue(readyIndex);
            }
        }

        List<FieldDefinition> ordered = new List<FieldDefinition>(definitionCount);
        while (ready.Count > 0)
        {
            int currentIndex = ready.Dequeue();
            ordered.Add(definitions[currentIndex]);

            List<int> currentDependents = dependents[currentIndex];
            for (int dependentSlot = 0; dependentSlot < currentDependents.Count; dependentSlot++)
            {
                int dependentIndex = currentDependents[dependentSlot];
                inDegree[dependentIndex] = inDegree[dependentIndex] - 1;
                if (inDegree[dependentIndex] == 0)
                {
                    ready.Enqueue(dependentIndex);
                }
            }
        }

        if (ordered.Count != definitionCount)
        {
            for (int leftoverIndex = 0; leftoverIndex < definitionCount; leftoverIndex++)
            {
                if (inDegree[leftoverIndex] > 0)
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.ResolveRelativeAnchors: collection='"
                        + GetCollectionNameFromIndex(collectionIndex) + "' field='"
                        + definitions[leftoverIndex].Name
                        + "' has unresolved anchor dependency, appending in original order", LogLevel.Warn);
                    ordered.Add(definitions[leftoverIndex]);
                }
            }
        }

        Dictionary<string, uint> finalIndexByName = new Dictionary<string, uint>(definitionCount);
        for (uint finalIndex = 0; finalIndex < ordered.Count; finalIndex++)
        {
            finalIndexByName[ordered[(int)finalIndex].Name] = finalIndex;
        }

        for (int resolveIndex = 0; resolveIndex < ordered.Count; resolveIndex++)
        {
            FieldDefinition entry = ordered[resolveIndex];
            string anchorName;
            bool hasAnchor = anchorNameByFieldName.TryGetValue(entry.Name, out anchorName!);
            if (hasAnchor == false)
            {
                continue;
            }

            uint resolvedIndex;
            bool resolved = finalIndexByName.TryGetValue(anchorName, out resolvedIndex);
            if (resolved == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.ResolveRelativeAnchors: collection='"
                    + GetCollectionNameFromIndex(collectionIndex) + "' field='" + entry.Name
                    + "' references unknown anchor '" + anchorName
                    + "', leaving RelativeToSlot null", LogLevel.Warn);
                continue;
            }

            entry.RelativeToSlot = resolvedIndex;
            ordered[resolveIndex] = entry;
        }

        _collections[collectionIndex].Fields = ordered.ToArray();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ResolveGates
    //
    // Resolves each gate's count field to a SlotId where possible and writes it onto the gate's
    // FieldSlot.  Run once after every collection's fields are loaded.
    //
    // A qualified field name ("Collection.field") resolves the named collection at load time and
    // stores the SlotId on the definition.  An unqualified (bare) field name cannot be resolved
    // at load time because a shared gate does not uniquely identify the collection that contains
    // it; the bare name is stored in CountFieldName for dynamic resolution at extraction time.
    //
    // A qualified name naming an unknown collection, or a field not present in the named
    // collection, is logged and skipped.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ResolveGates()
    {
        uint resolvedCount = 0;

        foreach (KeyValuePair<GateDefinitionHandle, string> entry in _gateFieldNamesByHandle!)
        {
            GateDefinitionHandle gateHandle = entry.Key;
            string expression = entry.Value;

            int dotIndex = expression.IndexOf('.');

            if (dotIndex >= 0)
            {
                // Qualified: resolve collection and slot at load time.
                string collectionName = expression.Substring(0, dotIndex).Trim();
                string countFieldName = expression.Substring(dotIndex + 1).Trim();

                CollectionIndex sourceCollection;
                bool collectionFound = _collectionIndexByCollectionName.TryGetValue(
                    collectionName, out sourceCollection);
                if (collectionFound == false)
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.ResolveGates: gate '"
                        + _gate[gateHandle].Name + "' count expression '" + expression
                        + "' names collection '" + collectionName
                        + "' that is not loaded for patchLevel=" + PatchLevel
                        + ", skipping gate resolution", LogLevel.Warn);
                    continue;
                }

                SlotId slot = IndexOfField(sourceCollection, countFieldName);
                if (slot.Exists == false)
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.ResolveGates: gate '"
                        + _gate[gateHandle].Name + "' count field '" + countFieldName
                        + "' not present in collection '"
                        + GetCollectionNameFromIndex(sourceCollection) + "' for patchLevel="
                        + PatchLevel + ", skipping gate resolution", LogLevel.Warn);
                    continue;
                }

                _gate[gateHandle].FieldSlot = slot;
                _gate[gateHandle].FieldSlotLocal = false;
                _gate[gateHandle].CountFieldName = string.Empty;

                DebugLog.Write(LogChannel.Fields, "PatchData.ResolveGates: gate '"
                    + _gate[gateHandle].Name + "' qualified count field '" + countFieldName
                    + "' resolved to slot " + slot + " in collection '" + collectionName
                    + "' for patchLevel=" + PatchLevel, LogLevel.Trace);

                resolvedCount = resolvedCount + 1;
            }
            else
            {
                // Bare name: store for dynamic resolution at extraction time.
                string countFieldName = expression.Trim();

                _gate[gateHandle].FieldSlot = SlotId.None;
                _gate[gateHandle].FieldSlotLocal = true;
                _gate[gateHandle].CountFieldName = countFieldName;

                DebugLog.Write(LogChannel.Fields, "PatchData.ResolveGates: gate '"
                    + _gate[gateHandle].Name + "' bare count field '" + countFieldName
                    + "' stored for dynamic resolution at extraction time", LogLevel.Trace);
            }
        }

        DebugLog.Write(LogChannel.Fields, "PatchData.ResolveGates: " + resolvedCount
            + " gate(s) resolved at load time for patchLevel=" + PatchLevel, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ResolvePredicates
    //
    // Resolves each pending predicate for the given collection against its finalized field
    // array.  Run after ResolveRelativeAnchors has reordered the array, so every source and
    // owner name resolves against the order the extractor will see.  For each pending entry
    // the raw string is parsed into op and operand, the source field name is childCollection to a
    // slot index, and the result is written onto the owner field's Predicate.
    //
    // The owner is located by name because the reorder invalidated the read-loop position.
    // The write indexes the backing array directly so it lands on the stored struct rather
    // than a copy.
    //
    // A predicate string that fails to parse, a source name not present in the collection,
    // or an owner name not present in the collection is a broken patch definition: the
    // failure is logged with a stack trace and the process is terminated, since a field
    // whose presence depends on an unresolvable predicate must never decode.
    //
    // Parameters:
    //   index  - The index of the Collection whose pending predicates to resolve.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ResolvePredicates(CollectionIndex index)
    {
        List<PendingFieldPredicate> pending = _pendingFieldPredicates!;
        FieldDefinition[] definitions = GetFieldDefinitions(index);

        if (definitions.Length == 0)
        {
            if (pending.Count > 0)
            {
                string message = "PatchData.ResolvePredicates: collection '"
                    + GetCollectionNameFromIndex(index) + "' has " + pending.Count
                    + " pending predicate(s) but no fields for patchLevel=" + PatchLevel
                    + " -- broken patch definition, aborting";

                DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                    + Environment.StackTrace);

                Environment.FailFast(message);
            }

            DebugLog.Write(LogChannel.Fields, "PatchData.ResolvePredicates: collection '"
                + GetCollectionNameFromIndex(index) + "' has no fields and no pending "
                + "predicates, nothing to resolve");
            return;
        }

        for (int pendingIndex = 0; pendingIndex < pending.Count; pendingIndex++)
        {
            PendingFieldPredicate entry = pending[pendingIndex];

            string sourceName;
            FieldPredicate predicate;
            bool parsed = ParsePredicate(entry.RawPredicate, out sourceName, out predicate);
            if (parsed == false)
            {
                string message = "PatchData.ResolvePredicates: collection '"
                    + GetCollectionNameFromIndex(index) + "' field '" + entry.OwnerFieldName
                    + "' predicate '" + entry.RawPredicate + "' failed to parse for patchLevel="
                    + PatchLevel + " -- broken patch definition, aborting";

                DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                    + Environment.StackTrace);

                Environment.FailFast(message);
            }

            SlotId sourceSlot = IndexOfField(index, sourceName);
            if (sourceSlot.Exists == false)
            {
                string message = "PatchData.ResolvePredicates: collection '"
                    + GetCollectionNameFromIndex(index) + "' field '" + entry.OwnerFieldName
                    + "' predicate '" + entry.RawPredicate + "' names source field '"
                    + sourceName + "' not present in the collection for patchLevel=" + PatchLevel
                    + " -- broken patch definition, aborting";

                DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                    + Environment.StackTrace);

                Environment.FailFast(message);
            }


            SlotId ownerSlot = IndexOfField(index, entry.OwnerFieldName);
            if (ownerSlot.Exists == false)
            {
                string message = "PatchData.ResolvePredicates: collection '"
                    + GetCollectionNameFromIndex(index) + "' predicate owner field '"
                    + entry.OwnerFieldName + "' not present in the collection for patchLevel="
                    + PatchLevel + " -- broken patch definition, aborting";

                DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                    + Environment.StackTrace);

                Environment.FailFast(message);
            }

            if (! SlotDefinedBefore(sourceSlot, ownerSlot))
            {
                string message = "PatchData.ResolvePredicates: collection '"
                    + GetCollectionNameFromIndex(index) + "' field '" + entry.OwnerFieldName
                    + "' (slot " + ownerSlot + ") predicate '" + entry.RawPredicate
                    + "' names source field '" + sourceName + "' at slot " + sourceSlot
                    + ", which is not decoded before the gated field for patchLevel=" + PatchLevel
                    + " -- broken patch definition, aborting";

                DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                    + Environment.StackTrace);

                Environment.FailFast(message);
            }

            predicate.SourceSlot = sourceSlot;

            // This is a local reference, so mutates the stored structure in place.
            definitions[ownerSlot.Index].Predicate = predicate;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SlotDefinedBefore
    //
    // Determines if one slot was defined before another.  This is an existence check during
    // hot-path iteration to make sure that slot dependencies can be childCollection.
    //
    // Parameters:
    //   a, b:   The slots to compare.  Slot 'a' is checked to see if it was defined before 'b'
    //
    // Returns:
    //   True if a is non-local or defined earlier in a collection
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private bool SlotDefinedBefore(SlotId a, SlotId b)
    {
        if (a.Collection != b.Collection)
        {
            // TODO:  really need an ancestor check
            return true;
        }

        if (a.Index < b.Index)
        {
            return true;
        }

        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetBaseOpcode
    //
    // Returns the version 1 PatchOpcode for the given opcode name in this patch, or
    // PatchOpcode.None if the name is not present.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode GetBaseOpcode(string opcodeName)
    {
        OpcodeHandle opcodeHandle;
        bool found = _opcodeHandlesByName.TryGetValue(opcodeName, out opcodeHandle);
        if (found == false)
        {
            return PatchOpcode.None;
        }

        PatchOpcode loaded = _patchOpcodeByOpcodeHandle[opcodeHandle];
        PatchOpcode versionOne = new PatchOpcode(loaded.Level, loaded.Value, 1);
        return versionOne;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeCollection
    //
    // Returns the top-level CollectionHandle bound to the given opcode in this patch.
    //
    // Parameters:
    //   patchOpcode  - The opcode whose top-level collection to resolve.
    //
    // Returns:
    //   The opcode's top-level CollectionHandle, or CollectionHandle.None when the opcode is
    //   the None sentinel, is not present in this patch, or its record carries no collection.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public CollectionHandle GetOpcodeCollection(PatchOpcode patchOpcode)
    {
        OpcodeHandle opcodeHandle;
        bool found = _opcodeHandlesByPatchOpcode.TryGetValue(patchOpcode, out opcodeHandle);
        if (found == false)
        {
            return CollectionHandle.None;
        }

        CollectionHandle collectionHandle = _opcodes[opcodeHandle].CollectionHandle;
        return collectionHandle;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetGateDefinition
    //
    // Returns the GateDefinition for the given GateDefinitionHandle in this patch, by value.
    //
    // The handle must be a real handle issued by this PatchData instance.  GateDefinitionHandle.None
    // and any value outside the gate array are schema or programming corruption and bring
    // the process down with the evidence preserved.
    //
    // Parameters:
    //   gateHandle  - The gate to look up.
    //
    // Returns:
    //   The GateDefinition stored at the handle.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public GateDefinition GetGateDefinition(GateDefinitionHandle gateHandle)
    {
        if (gateHandle.Exists == false)
        {
            string noneFailure = "PatchData.GetGateDefinition: GateDefinitionHandle.None passed for patchLevel="
                + PatchLevel;
            DebugLog.Write(LogChannel.Fields, noneFailure);
            Environment.FailFast(noneFailure);
        }

        if (gateHandle >= _gate.Length)
        {
            string rangeFailure = "PatchData.GetGateDefinition: gate handle " + gateHandle
                + " out of range, " + _gate.Length + " gate(s) loaded for patchLevel="
                + PatchLevel;
            DebugLog.Write(LogChannel.Fields, rangeFailure);
            Environment.FailFast(rangeFailure);
        }

        DebugLog.Write(LogChannel.Fields, "PatchData.GetGateDefinition: handle " + gateHandle
            + " resolves to gate '" + _gate[gateHandle].Name + "'");
        return _gate[gateHandle];
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetCollectionNameFromIndex
    //
    // Returns the collection name for the given CollectionIndex in this patch.
    //
    // Parameters:
    //   CollectionIndex - The Collection whose name to return.
    //
    // Returns:
    //   The collection name (e.g. "OP_PlayerProfile").
    ///////////////////////////////////////////////////////////////////////////////////////////
    
    public string GetCollectionNameFromIndex(CollectionIndex index)
    {
        if (! index.Exists)
        {
            return ("Unknown");
        }

        if (index >= _collections.Length)
        {
            string indexFailure = "GetCollectionNameFromIndex: Invalid CollectionIndex " + index + " passed in";
            DebugLog.WriteMultiline(LogChannel.Fields, indexFailure + Environment.NewLine
                + Environment.StackTrace);
            Environment.FailFast(indexFailure);
        }

        return _collections[index].Name;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetCollectionHandleFromName
    //
    // Returns the hanle of the named collection in this patch.
    //
    // Parameters:
    //   collectionName - The name of the collection to lookup.
    //
    // Returns:
    //   The handle if found, or CollectionHandle.None
    ///////////////////////////////////////////////////////////////////////////////////////////
    public CollectionHandle GetCollectionHandleFromName(string collectionName)
    {
        CollectionIndex index;

        if (_collectionIndexByCollectionName.TryGetValue(collectionName, out index) == false)
        {
            return CollectionHandle.None;  
        }

        return _collectionHandleByIndex[index];
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetSlotCount
    //
    // Returns the slot capacity a bag for the given collection requires.
    //
    // index:  The collection to query.
    //
    // Returns:  The collection's slot count.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public ushort GetSlotCount(CollectionIndex index)
    {
        return _collections[index].SlotCount;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetArenaCapacity
    //
    // Returns the recommended arena capacity in bytes for a bag for the given collection.
    //
    // index:  The collection to query.
    //
    // Returns:  The collection's recommended arena capacity.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public uint GetArenaCapacity(CollectionIndex index)
    {
        return _collections[index].ArenaEstimate;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeName
    //
    // Returns the name of the given Opcode in this patch.
    //
    // Parameters:
    //   patchOpcode  - The Opcode whose name to return.
    //
    // Returns:
    //   The opcode name (e.g. "OP_PlayerProfile").
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string GetOpcodeName(PatchOpcode patchOpcode)
    {
        OpcodeHandle handle;
        if (_opcodeHandlesByPatchOpcode.TryGetValue(patchOpcode, out handle))
        {
            return _opcodeNamesByOpcodeHandle[handle];
        }

        return "Unknown_" + patchOpcode.Value;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeCount
    //
    // Returns the number of opcodes loaded for this patch.  Equal to the length of the
    // opcodeHandle-indexed arrays and the count of PatchOpcode rows for this patch.
    //
    // Returns:
    //   The number of opcodes in this patch.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int GetOpcodeCount()
    {
        return _patchOpcodeByOpcodeHandle.Length;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetFieldDefinitions
    //
    // Returns the FieldDefinition array for the given CollectionIndex, or null if the
    // collection has no fields loaded for this patch.
    //
    // Parameters:
    //   collection  - The index of the Collection whose field definitions to return.
    //
    // Returns:
    //   The FieldDefinition array, or null if absent.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public FieldDefinition[] GetFieldDefinitions(CollectionIndex collection)
    {
        return _collections[collection].Fields;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeGate
    //
    // Returns the top-level GateDefinitionHandle for the given opcode in this patch.  The gate is the
    // entry point for extracting the opcode's payload.
    //
    // Parameters:
    //   opcode  - The opcode whose top-level gate to look up.
    //
    // Returns:
    //   The opcode's top-level GateDefinitionHandle, or GateDefinitionHandle.None when the opcode is not in this
    //   patch or its row named no loaded gate.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public GateDefinitionHandle GetOpcodeGate(PatchOpcode opcode)
    {
        OpcodeHandle opcodeHandle;
        bool found = _opcodeHandlesByPatchOpcode.TryGetValue(opcode, out opcodeHandle);
        if (found == false)
        {
            DebugLog.Write(LogChannel.Opcodes, "PatchData.GetOpcodeGate: opcode "
                + opcode + " not in patchLevel=" + PatchLevel + ", returning GateDefinitionHandle.None");
            return GateDefinitionHandle.None;
        }

        GateDefinitionHandle gate = _opcodes[opcodeHandle].Gate;
        DebugLog.Write(LogChannel.Opcodes, "PatchData.GetOpcodeGate: opcode "
            + opcode + " resolves to gate handle " + gate);
        return gate;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // IndexOfField
    //
    // Returns the SlotId of the named field within the collection's field definitions.
    // Cold path; callers resolve an index once and retain it.
    //
    // Returns SlotId.None if the collection has no fields loaded for this patch, or if
    // the named field is not present in the loaded definitions.
    //
    // Parameters:
    //   collection  - The index of the collection to search.
    //   fieldName   - The field_name column value to look up.
    //
    // Returns:
    //   The SlotId of the named field, or SlotId.None if not found.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SlotId IndexOfField(CollectionIndex collection, string fieldName)
    {
        FieldDefinition[] definitions = GetFieldDefinitions(collection);
        if (definitions.Length == 0)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.IndexOfField: no field definitions for collection '"
                + GetCollectionNameFromIndex(collection) + "' in patchLevel=" + PatchLevel
                + ", returning None");
            return SlotId.None;
        }

        for (uint fieldIndex = 0; fieldIndex < definitions.Length; fieldIndex++)
        {
            if (definitions[fieldIndex].Name == fieldName)
            {
                CollectionHandle handle = _collectionHandleByIndex[collection];
                return new SlotId(handle, fieldIndex);
            }
        }

        DebugLog.Write(LogChannel.Fields, "PatchData.IndexOfField: field '" + fieldName
            + "' not in definitions for collection '" + GetCollectionNameFromIndex(collection)
            + "' in patchLevel=" + PatchLevel + ", returning None");
        return SlotId.None;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetFieldPosition
    //
    // Returns the BitOffset of a slot.  Used for field rows whose
    // BitOffset carries position-like metadata that ExtractCollection does not consume — for
    // example, csv_token rows where BitOffset is the 1-based index of the token within
    // a comma-separated payload.
    //
    // collection:   The collection containing the field
    // slot:     The slot to query.
    //
    // Returns the slot's BitOffset.  Throws if the collection handle is invalid
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint GetFieldPosition(CollectionIndex collectionIndex, SlotId slot)
    {
        FieldDefinition[] definitions = GetFieldDefinitions(collectionIndex);

        if (definitions.Length == 0)
        {
            string name = GetCollectionNameFromIndex(collectionIndex);
            throw new InvalidOperationException("PatchData.GetFieldPosition: no fields for collection " + name);
        }

        if (slot.Index >= definitions.Length)
        {
            string name = GetCollectionNameFromIndex(collectionIndex);
            throw new ArgumentOutOfRangeException(nameof(slot),
                "PatchData.GetFieldPosition: field id " + slot.Index + " out of range for collection " + name);
        }

        FieldDefinition definition = definitions[slot.Index];
        return definition.BitOffset - 1;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetEncodingStrings
    //
    // Returns the encoding strings recognized by this PatchData.  Used by the patch data
    // editor to populate encoding dropdowns.
    //
    // Returns:
    //   The encoding strings, in dictionary enumeration order.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string[] GetEncodingStrings()
    {
        string[] result = new string[_encodingsByString.Count];
        _encodingsByString.Keys.CopyTo(result, 0);
        return result;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ParsePredicate
    //
    // Parses a raw predicate string of the form "name OP operand" into its three parts: the
    // source field name on the left, the comparison operator, and the unsigned operand on the
    // right.  The operator is matched longest-first so a two-character operator is not split
    // into a one-character one.  The operand is read as hexadecimal when prefixed with 0x or
    // 0X, otherwise as decimal.  Whitespace around any part is optional and trimmed.
    //
    // Op and operand are final once parsed; the source name still requires resolution to a
    // slot index by the caller.  This method performs no resolution and reads no payload.
    //
    // Parameters:
    //   raw         - The raw predicate string from the PacketField predicate column.
    //   sourceName  - Receives the trimmed source field name on success, empty on failure.
    //   op          - Receives the parsed PredicateOp on success, None on failure.
    //   operand     - Receives the parsed operand on success, 0 on failure.
    //
    // Returns:
    //   true when the string parsed into all three parts; false when the operator is missing
    //   or unrecognized, the source name is empty, or the operand is not a valid number.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private bool ParsePredicate(string raw, out string sourceName, out FieldPredicate predicate)
    {
        sourceName = "";
        predicate = default;

        if (raw == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: null predicate string, parse failed");
            return false;
        }

        string trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: empty predicate string, parse failed");
            return false;
        }

        string operatorToken = "";
        int operatorIndex = -1;

        string[] twoCharOperators = new string[] { "==", "!=", ">=", "<=" };
        for (int candidateIndex = 0; candidateIndex < twoCharOperators.Length; candidateIndex++)
        {
            int foundIndex = trimmed.IndexOf(twoCharOperators[candidateIndex], StringComparison.Ordinal);
            if (foundIndex >= 0)
            {
                operatorToken = twoCharOperators[candidateIndex];
                operatorIndex = foundIndex;
                break;
            }
        }

        if (operatorIndex < 0)
        {
            string[] oneCharOperators = new string[] { ">", "<", "&" };
            for (int candidateIndex = 0; candidateIndex < oneCharOperators.Length; candidateIndex++)
            {
                int foundIndex = trimmed.IndexOf(oneCharOperators[candidateIndex], StringComparison.Ordinal);
                if (foundIndex >= 0)
                {
                    operatorToken = oneCharOperators[candidateIndex];
                    operatorIndex = foundIndex;
                    break;
                }
            }
        }

        if (operatorIndex < 0)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: no recognized operator in '"
                + trimmed + "', parse failed");
            return false;
        }

        string leftPart = trimmed.Substring(0, operatorIndex).Trim();
        string rightPart = trimmed.Substring(operatorIndex + operatorToken.Length).Trim();

        if (leftPart.Length == 0)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: empty source name in '"
                + trimmed + "', parse failed");
            return false;
        }

        if (rightPart.Length == 0)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: empty operand in '"
                + trimmed + "', parse failed");
            return false;
        }

        PredicateOp parsedOp;
        if (operatorToken == "&")
        {
            parsedOp = PredicateOp.BitmaskNonZero;
        }
        else if (operatorToken == "==")
        {
            parsedOp = PredicateOp.Equal;
        }
        else if (operatorToken == "!=")
        {
            parsedOp = PredicateOp.NotEqual;
        }
        else if (operatorToken == ">=")
        {
            parsedOp = PredicateOp.GreaterOrEqual;
        }
        else if (operatorToken == "<=")
        {
            parsedOp = PredicateOp.LessOrEqual;
        }
        else if (operatorToken == ">")
        {
            parsedOp = PredicateOp.Greater;
        }
        else if (operatorToken == "<")
        {
            parsedOp = PredicateOp.Less;
        }
        else
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: unhandled operator '"
                + operatorToken + "' in '" + trimmed + "', parse failed");
            return false;
        }

        uint parsedOperand;
        int parsedSignedOperand;

        if (rightPart.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == true)
        {
            string hexDigits = rightPart.Substring(2);
            bool hexParsed = uint.TryParse(hexDigits, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out parsedOperand);
            if (hexParsed == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: hex operand '" + rightPart
                    + "' in '" + trimmed + "' is not a valid 32-bit value, parse failed");
                return false;
            }
            parsedSignedOperand = unchecked((int)parsedOperand);
        }
        else if (rightPart.StartsWith("-", StringComparison.Ordinal) == true)
        {
            bool signedParsed = int.TryParse(rightPart, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out parsedSignedOperand);
            if (signedParsed == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: signed operand '" + rightPart
                    + "' in '" + trimmed + "' is not a valid 32-bit signed value, parse failed");
                return false;
            }
            parsedOperand = unchecked((uint)parsedSignedOperand);
        }
        else
        {
            bool unsignedParsed = uint.TryParse(rightPart, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out parsedOperand);
            if (unsignedParsed == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: operand '" + rightPart
                    + "' in '" + trimmed + "' is not a valid unsigned number, parse failed");
                return false;
            }
            parsedSignedOperand = unchecked((int)parsedOperand);
        }

        sourceName = leftPart;
        predicate.Op = parsedOp;
        predicate.Operand = parsedOperand;
        predicate.SignedOperand = parsedSignedOperand;

/*        DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: parsed '" + trimmed
            + "' to source='" + sourceName + "' op=" + predicate.Op + " operand=" + predicate.Operand
            + " signedOperand=" + predicate.SignedOperand);*/
        return true;
    }
}