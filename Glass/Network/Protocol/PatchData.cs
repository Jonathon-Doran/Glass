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
    // PredicateTokenKind
    //
    // The kind of one token produced when a predicate expression is split.  Comparison is the
    // text of a single comparison.  Open and Close are parentheses.  And and Or are the && and
    // || operators.  Not is a ! that applies to a parenthesized group.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private enum PredicateTokenKind
    {
        None = 0,
        Comparison,
        Open,
        Close,
        And,
        Or,
        Not
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PredicateToken
    //
    // One token of a predicate expression.  Text holds the trimmed comparison text when Kind is
    // Comparison, and the operator or parenthesis characters for every other kind.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private struct PredicateToken
    {
        public PredicateTokenKind Kind;
        public string Text;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PredicateParseState
    //
    // Working state shared by every level of one predicate expression parse.  Tokens is the
    // split expression and Position is the index of the next token to read.  Steps and
    // SourceNames receive the program in postfix order, parallel to each other: a Comparison
    // step's entry in SourceNames is its source field name, and an operator step's entry is
    // null.  Depth is the number of results the evaluation stack would hold after the steps
    // emitted so far, and MaxDepth is the greatest value Depth has reached.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private sealed class PredicateParseState
    {
        public List<PredicateToken> Tokens = new List<PredicateToken>();
        public int Position;
        public List<PredicateStep> Steps = new List<PredicateStep>();
        public List<string?> SourceNames = new List<string?>();
        public uint Depth;
        public uint MaxDepth;
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
    //      definitions from PacketField,
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

        DebugLog.Write(LogChannel.General, "Loading patch data for patch " + patchLevel, LogLevel.Info);
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
        _encodingsByString.Add("int64", FieldEncoding.Int64);
        _encodingsByString.Add("uint64", FieldEncoding.UInt64);
        _encodingsByString.Add("uint_msb", FieldEncoding.UIntMsb);
        _encodingsByString.Add("uint_masked", FieldEncoding.UIntMasked);
        _encodingsByString.Add("float", FieldEncoding.Float);
        _encodingsByString.Add("double", FieldEncoding.Double);
        _encodingsByString.Add("signmag_msb", FieldEncoding.SignMagnitudeMsb);
        _encodingsByString.Add("signmag_lsb", FieldEncoding.SignMagnitudeLsb);
        _encodingsByString.Add("opt_signmag_msb", FieldEncoding.OptSignMagnitudeMsb);
        _encodingsByString.Add("csv_token", FieldEncoding.CsvToken);
        _encodingsByString.Add("string_null_terminated", FieldEncoding.StringNullTerminated);
        _encodingsByString.Add("string_length_prefixed", FieldEncoding.StringLengthPrefixed);
        _encodingsByString.Add("blob", FieldEncoding.Blob);
        _encodingsByString.Add("array_uint", FieldEncoding.UIntArray);
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
    // Reads the PacketField rows for the collection at the given opcodeHandle, ordered by sequence
    // with unsequenced rows last and the row id breaking ties,
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
                    + " ORDER BY sequence IS NULL, sequence, id";
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
                definition.ByteCount = bitLength / 8u;
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

        if (index == 7)
        {
            DebugLog.Write(LogChannel.Fields, "about to reorder strides", LogLevel.Trace);
        }
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
    // name-anchor graph and handles chains of any depth.  Among the fields whose anchors are
    // already placed, the one loaded first is placed next, so the load order is kept wherever
    // the anchors allow.
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

        PriorityQueue<int, int> ready = new PriorityQueue<int, int>();
        for (int readyIndex = 0; readyIndex < definitionCount; readyIndex++)
        {
            if (inDegree[readyIndex] == 0)
            {
                ready.Enqueue(readyIndex, readyIndex);
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
                    ready.Enqueue(dependentIndex, dependentIndex);
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
    // the raw string is parsed into a predicate program, the source field name of every
    // Comparison step is resolved to a slot index, and the program is written onto the owner
    // field's Predicate.
    //
    // The owner is located by name because the reorder invalidated the read-loop position.
    // The write indexes the backing array directly so it lands on the stored struct rather
    // than a copy.
    //
    // A predicate string that fails to parse, an owner name not present in the collection, or
    // a Comparison whose source name is not present in the collection, is not decoded before
    // the owner, or has operands that do not fit the source's encoding is a broken patch
    // definition: the failure is logged with a stack trace and the process is terminated,
    // since a field whose presence depends on an unresolvable predicate must never decode.
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
                    + Environment.StackTrace, LogLevel.Error);

                Environment.FailFast(message);
            }

            DebugLog.Write(LogChannel.Fields, "PatchData.ResolvePredicates: collection '"
                + GetCollectionNameFromIndex(index) + "' has no fields and no pending "
                + "predicates, nothing to resolve", LogLevel.Warn);
            return;
        }

        for (int pendingIndex = 0; pendingIndex < pending.Count; pendingIndex++)
        {
            PendingFieldPredicate entry = pending[pendingIndex];

            PredicateProgram program;
            bool parsed = ParsePredicateExpression(entry.RawPredicate, out program);
            if (parsed == false)
            {
                string message = "PatchData.ResolvePredicates: collection '"
                    + GetCollectionNameFromIndex(index) + "' field '" + entry.OwnerFieldName
                    + "' predicate '" + entry.RawPredicate + "' failed to parse for patchLevel="
                    + PatchLevel + " -- broken patch definition, aborting";

                DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                    + Environment.StackTrace, LogLevel.Error);

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
                    + Environment.StackTrace, LogLevel.Error);

                Environment.FailFast(message);
            }

            PredicateStep[] steps = program.Steps!;
            string?[] sourceNames = program.SourceNames!;
            uint comparisonCount = 0;

            for (int stepIndex = 0; stepIndex < steps.Length; stepIndex++)
            {
                if (steps[stepIndex].Kind != PredicateStepKind.Comparison)
                {
                    continue;
                }

                string? sourceName = sourceNames[stepIndex];
                if (sourceName == null)
                {
                    string message = "PatchData.ResolvePredicates: collection '"
                        + GetCollectionNameFromIndex(index) + "' field '" + entry.OwnerFieldName
                        + "' predicate '" + entry.RawPredicate + "' step " + stepIndex
                        + " is a Comparison with no source name for patchLevel=" + PatchLevel
                        + " -- broken predicate program, aborting";

                    DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                        + Environment.StackTrace, LogLevel.Error);

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
                        + Environment.StackTrace, LogLevel.Error);

                    Environment.FailFast(message);
                }

                if (!SlotDefinedBefore(sourceSlot, ownerSlot))
                {
                    string message = "PatchData.ResolvePredicates: collection '"
                        + GetCollectionNameFromIndex(index) + "' field '" + entry.OwnerFieldName
                        + "' (slot " + ownerSlot + ") predicate '" + entry.RawPredicate
                        + "' names source field '" + sourceName + "' at slot " + sourceSlot
                        + ", which is not decoded before the gated field for patchLevel=" + PatchLevel
                        + " -- broken patch definition, aborting";

                    DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                        + Environment.StackTrace, LogLevel.Error);

                    Environment.FailFast(message);
                }

                FieldEncoding sourceEncoding = definitions[sourceSlot.Index].Encoding;
                bool operandsFit = OperandsFitSource(sourceEncoding, steps[stepIndex].Comparison,
                    entry.OwnerFieldName);
                if (operandsFit == false)
                {
                    string message = "PatchData.ResolvePredicates: collection '"
                        + GetCollectionNameFromIndex(index) + "' field '" + entry.OwnerFieldName
                        + "' predicate '" + entry.RawPredicate + "' does not fit source field '"
                        + sourceName + "' encoding " + sourceEncoding + " for patchLevel=" + PatchLevel
                        + " -- broken patch definition, aborting";

                    DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                        + Environment.StackTrace, LogLevel.Error);

                    Environment.FailFast(message);
                }

                // Array element access, so this mutates the stored step in place.
                steps[stepIndex].Comparison.SourceSlot = sourceSlot;
                comparisonCount++;

                DebugLog.Write(LogChannel.Fields, "PatchData.ResolvePredicates: collection '"
                    + GetCollectionNameFromIndex(index) + "' field '" + entry.OwnerFieldName
                    + "' step " + stepIndex + " source '" + sourceName + "' resolved to slot "
                    + sourceSlot, LogLevel.Trace);
            }

            // This is a local reference, so mutates the stored structure in place.
            definitions[ownerSlot.Index].Predicate = program;

            DebugLog.Write(LogChannel.Fields, "PatchData.ResolvePredicates: collection '"
                + GetCollectionNameFromIndex(index) + "' field '" + entry.OwnerFieldName
                + "' predicate '" + entry.RawPredicate + "' resolved with " + comparisonCount
                + " comparison(s) in " + steps.Length + " step(s)", LogLevel.Trace);
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
    // TryGetGateDefinition
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
            string noneFailure = "PatchData.TryGetGateDefinition: GateDefinitionHandle.None passed for patchLevel="
                + PatchLevel;
            DebugLog.Write(LogChannel.Fields, noneFailure, LogLevel.Error);
            Environment.FailFast(noneFailure);
        }

        if (gateHandle >= _gate.Length)
        {
            string rangeFailure = "PatchData.TryGetGateDefinition: gate handle " + gateHandle
                + " out of range, " + _gate.Length + " gate(s) loaded for patchLevel="
                + PatchLevel;
            DebugLog.Write(LogChannel.Fields, rangeFailure, LogLevel.Trace);
            Environment.FailFast(rangeFailure);
        }

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
                + Environment.StackTrace, LogLevel.Error);
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

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetGateDefinitionHandleFromName
    //
    // Resolves a gate's name to the GateDefinitionHandle assigned to it at load time.  The lookup
    // is against the name-to-handle map built when this patch's Gate rows were read.
    //
    // gateName:  The gate name to resolve.
    //
    // Returns:   The GateDefinitionHandle for the named gate, or GateDefinitionHandle.None when no
    //            gate of that name is loaded for this patch level.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public GateDefinitionHandle GetGateDefinitionHandleFromName(string gateName)
    {
        GateDefinitionHandle handle;
        if (_gateHandlesByName.TryGetValue(gateName, out handle) == false)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PatchData.GetGateHandleFromName: no gate named '" + gateName
                + "' in patchLevel=" + PatchLevel + ", returning None", LogLevel.Warn);
            return GateDefinitionHandle.None;
        }
        return handle;
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
                + ", returning None", LogLevel.Warn);
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
    // Parses a raw predicate string into its source field name and a FieldPredicate holding
    // the operator, negation, and typed operands.  Two forms are accepted, each optionally
    // preceded by ! to negate the result:
    //
    //   name OP literal      OP is one of == != >= <= > < &, with one literal operand.
    //   name in [list]       The bracketed list holds one or more literals or low..high
    //                        integer ranges, separated by commas.
    //
    // Two-character operators are matched before one-character ones so a two-character
    // operator is not split.  The operator and bracket searches stop at the first double
    // quote, so text inside a string literal is never read as an operator or a list.
    // Whitespace around any part is optional and trimmed.
    //
    // The source name still requires resolution to a slot by the caller.  This method
    // performs no resolution and reads no payload.
    //
    // Parameters:
    //   raw         - The raw predicate string from the PacketField predicate column.
    //   sourceName  - Receives the trimmed source field name on success, empty on failure.
    //   predicate   - Receives the parsed operator, negation, and operands on success,
    //                 default on failure.
    //
    // Returns:
    //   true when the string parsed; false when the string is empty, the operator is missing
    //   or unrecognized, the source name is empty, the in keyword is malformed, or an operand
    //   or operand list fails to parse.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static bool ParsePredicate(string raw, out string sourceName, out FieldPredicate predicate)
    {
        sourceName = "";
        predicate = default;

        if (raw == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: null predicate string, parse failed",
                LogLevel.Warn);
            return false;
        }

        string trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: empty predicate string, parse failed",
                LogLevel.Warn);
            return false;
        }

        bool negated = false;
        if (trimmed[0] == '!')
        {
            negated = true;
            trimmed = trimmed.Substring(1).Trim();
            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: leading ! in '" + raw.Trim()
                + "', predicate negated", LogLevel.Trace);

            if (trimmed.Length == 0)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: nothing follows ! in '"
                    + raw.Trim() + "', parse failed", LogLevel.Warn);
                return false;
            }
        }

        int firstQuote = trimmed.IndexOf('"');
        string searchRegion;
        if (firstQuote >= 0)
        {
            searchRegion = trimmed.Substring(0, firstQuote);
        }
        else
        {
            searchRegion = trimmed;
        }

        int bracketIndex = searchRegion.IndexOf('[');
        if (bracketIndex >= 0)
        {
            string head = trimmed.Substring(0, bracketIndex).TrimEnd();
            bool hasInKeyword = (head.Length > 2)
                && (head.EndsWith("in", StringComparison.Ordinal) == true)
                && (char.IsWhiteSpace(head[head.Length - 3]) == true);
            if (hasInKeyword == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: list in '" + trimmed
                    + "' is not preceded by 'name in', parse failed", LogLevel.Warn);
                return false;
            }

            string inSourceName = head.Substring(0, head.Length - 2).Trim();
            if (inSourceName.Length == 0)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: empty source name in '"
                    + trimmed + "', parse failed", LogLevel.Warn);
                return false;
            }

            PredicateOperand[] listOperands;
            bool listParsed = ParseOperandList(trimmed.Substring(bracketIndex), out listOperands);
            if (listParsed == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: operand list in '" + trimmed
                    + "' failed to parse", LogLevel.Warn);
                return false;
            }

            sourceName = inSourceName;
            predicate.Op = PredicateOp.In;
            predicate.Negated = negated;
            predicate.Operands = listOperands;

            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: parsed '" + raw.Trim()
                + "' to source='" + sourceName + "' op=In negated=" + negated + " operands="
                + listOperands.Length, LogLevel.Trace);
            return true;
        }

        string operatorToken = "";
        int operatorIndex = -1;

        string[] twoCharOperators = new string[] { "==", "!=", ">=", "<=" };
        for (int candidateIndex = 0; candidateIndex < twoCharOperators.Length; candidateIndex++)
        {
            int foundIndex = searchRegion.IndexOf(twoCharOperators[candidateIndex], StringComparison.Ordinal);
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
                int foundIndex = searchRegion.IndexOf(oneCharOperators[candidateIndex], StringComparison.Ordinal);
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
                + trimmed + "', parse failed", LogLevel.Warn);
            return false;
        }

        string leftPart = trimmed.Substring(0, operatorIndex).Trim();
        string rightPart = trimmed.Substring(operatorIndex + operatorToken.Length).Trim();

        if (leftPart.Length == 0)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: empty source name in '"
                + trimmed + "', parse failed", LogLevel.Warn);
            return false;
        }

        if (rightPart.Length == 0)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: empty operand in '"
                + trimmed + "', parse failed", LogLevel.Warn);
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
                + operatorToken + "' in '" + trimmed + "', parse failed", LogLevel.Warn);
            return false;
        }

        PredicateOperand singleOperand;
        bool operandParsed = ParseOperand(rightPart, out singleOperand);
        if (operandParsed == false)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: operand '" + rightPart
                + "' in '" + trimmed + "' failed to parse", LogLevel.Warn);
            return false;
        }

        sourceName = leftPart;
        predicate.Op = parsedOp;
        predicate.Negated = negated;
        predicate.Operands = new PredicateOperand[] { singleOperand };

        DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: parsed '" + raw.Trim()
            + "' to source='" + sourceName + "' op=" + parsedOp + " negated=" + negated
            + " operandKind=" + singleOperand.Kind, LogLevel.Trace);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ParseOperand
    //
    // Parses one predicate constant into a typed PredicateOperand.  The literal's form selects
    // its kind: text in double quotes is a String; a value prefixed with 0x or 0X is
    // hexadecimal and unsigned; a value containing a decimal point is a Double; a value with a
    // leading minus sign is signed; any other value is unsigned decimal.  An integer that fits
    // in 32 bits is stored as the 32-bit kind (UInt or Int), otherwise as the 64-bit kind
    // (UInt64 or Int64).  Whitespace around the literal is trimmed.
    //
    // Parameters:
    //   literal  - The text of one constant from the predicate string.
    //   operand  - Receives the parsed operand on success, default on failure.
    //
    // Returns:
    //   true when the literal parsed; false when it is empty, an unterminated string, or not
    //   a valid number of its form.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool ParseOperand(string literal, out PredicateOperand operand)
    {
        operand = default;

        string trimmed = literal.Trim();
        if (trimmed.Length == 0)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperand: empty literal, parse failed",
                LogLevel.Warn);
            return false;
        }

        if (trimmed[0] == '"')
        {
            if ((trimmed.Length < 2) || (trimmed[trimmed.Length - 1] != '"'))
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperand: unterminated string literal '"
                    + trimmed + "', parse failed", LogLevel.Warn);
                return false;
            }

            operand.Kind = PredicateOperandKind.String;
            operand.Bits = 0;
            operand.Text = trimmed.Substring(1, trimmed.Length - 2);

            DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperand: '" + trimmed
                + "' parsed as String", LogLevel.Trace);
            return true;
        }

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == true)
        {
            string hexDigits = trimmed.Substring(2);
            ulong hexValue;
            bool hexParsed = ulong.TryParse(hexDigits, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out hexValue);
            if (hexParsed == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperand: hex literal '" + trimmed
                    + "' is not a valid 64-bit value, parse failed", LogLevel.Warn);
                return false;
            }

            if (hexValue <= uint.MaxValue)
            {
                operand.Kind = PredicateOperandKind.UInt;
            }
            else
            {
                operand.Kind = PredicateOperandKind.UInt64;
            }
            operand.Bits = hexValue;
            operand.Text = null;

            DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperand: '" + trimmed + "' parsed as "
                + operand.Kind, LogLevel.Trace);
            return true;
        }

        if (trimmed.Contains('.') == true)
        {
            double doubleValue;
            bool doubleParsed = double.TryParse(trimmed, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out doubleValue);
            if (doubleParsed == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperand: floating literal '" + trimmed
                    + "' is not a valid double, parse failed", LogLevel.Warn);
                return false;
            }

            operand.Kind = PredicateOperandKind.Double;
            operand.Bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(doubleValue));
            operand.Text = null;

            DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperand: '" + trimmed
                + "' parsed as Double", LogLevel.Trace);
            return true;
        }

        if (trimmed[0] == '-')
        {
            long signedValue;
            bool signedParsed = long.TryParse(trimmed, System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out signedValue);
            if (signedParsed == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperand: signed literal '" + trimmed
                    + "' is not a valid 64-bit signed value, parse failed", LogLevel.Warn);
                return false;
            }

            if (signedValue >= int.MinValue)
            {
                operand.Kind = PredicateOperandKind.Int;
                operand.Bits = unchecked((uint)(int)signedValue);
            }
            else
            {
                operand.Kind = PredicateOperandKind.Int64;
                operand.Bits = unchecked((ulong)signedValue);
            }
            operand.Text = null;

            DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperand: '" + trimmed + "' parsed as "
                + operand.Kind, LogLevel.Trace);
            return true;
        }

        ulong unsignedValue;
        bool unsignedParsed = ulong.TryParse(trimmed, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out unsignedValue);
        if (unsignedParsed == false)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperand: literal '" + trimmed
                + "' is not a valid unsigned number, parse failed", LogLevel.Warn);
            return false;
        }

        if (unsignedValue <= uint.MaxValue)
        {
            operand.Kind = PredicateOperandKind.UInt;
        }
        else
        {
            operand.Kind = PredicateOperandKind.UInt64;
        }
        operand.Bits = unsignedValue;
        operand.Text = null;

        DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperand: '" + trimmed + "' parsed as "
            + operand.Kind, LogLevel.Trace);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ParseOperandList
    //
    // Parses the bracketed operand list of an in predicate, such as "[1..12, 128, 130]", into
    // an array of typed operands.  After trimming, the list must begin with '[' and end with
    // ']', and holds one or more elements separated by commas.  Commas inside double quotes
    // do not separate elements.  An element is either a single literal or an integer range
    // written low..high, which expands to every integer from low to high inclusive.  A
    // quoted element is always a single literal.  Operands appear in the order written, with
    // each range expanded in ascending order.
    //
    // Parameters:
    //   listText  - The bracketed list text from the predicate string.
    //   operands  - Receives the parsed operands on success, an empty array on failure.
    //
    // Returns:
    //   true when every element parsed; false when the brackets are missing, a quote is
    //   unterminated, an element is empty or fails to parse, or a range has a non-integer
    //   end, an end outside the signed 64-bit range, or a low end above its high end.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool ParseOperandList(string listText, out PredicateOperand[] operands)
    {
        operands = Array.Empty<PredicateOperand>();

        string trimmed = listText.Trim();
        if ((trimmed.Length < 2) || (trimmed[0] != '[') || (trimmed[trimmed.Length - 1] != ']'))
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperandList: list '" + trimmed
                + "' is not enclosed in brackets, parse failed", LogLevel.Warn);
            return false;
        }

        string inner = trimmed.Substring(1, trimmed.Length - 2);

        List<string> elements = new List<string>();
        bool insideQuotes = false;
        int elementStart = 0;
        for (int charIndex = 0; charIndex < inner.Length; charIndex++)
        {
            char current = inner[charIndex];
            if (current == '"')
            {
                insideQuotes = !insideQuotes;
            }
            else if ((current == ',') && (insideQuotes == false))
            {
                elements.Add(inner.Substring(elementStart, charIndex - elementStart));
                elementStart = charIndex + 1;
            }
        }
        elements.Add(inner.Substring(elementStart));

        if (insideQuotes == true)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperandList: list '" + trimmed
                + "' has an unterminated string, parse failed", LogLevel.Warn);
            return false;
        }

        List<PredicateOperand> parsed = new List<PredicateOperand>();

        for (int elementIndex = 0; elementIndex < elements.Count; elementIndex++)
        {
            string element = elements[elementIndex].Trim();
            if (element.Length == 0)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperandList: list '" + trimmed
                    + "' has an empty element at position " + elementIndex + ", parse failed",
                    LogLevel.Warn);
                return false;
            }

            int rangeIndex = -1;
            if (element[0] != '"')
            {
                rangeIndex = element.IndexOf("..", StringComparison.Ordinal);
            }

            if (rangeIndex < 0)
            {
                PredicateOperand single;
                bool singleParsed = ParseOperand(element, out single);
                if (singleParsed == false)
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperandList: element '" + element
                        + "' in list '" + trimmed + "' failed to parse", LogLevel.Warn);
                    return false;
                }

                parsed.Add(single);
                continue;
            }

            string[] endTexts = new string[] { element.Substring(0, rangeIndex), element.Substring(rangeIndex + 2) };
            long[] endValues = new long[2];

            for (int endIndex = 0; endIndex < 2; endIndex++)
            {
                PredicateOperand endOperand;
                bool endParsed = ParseOperand(endTexts[endIndex], out endOperand);
                if (endParsed == false)
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperandList: range '" + element
                        + "' has an end that failed to parse", LogLevel.Warn);
                    return false;
                }

                if (endOperand.Kind == PredicateOperandKind.UInt)
                {
                    endValues[endIndex] = (long)endOperand.Bits;
                }
                else if (endOperand.Kind == PredicateOperandKind.Int)
                {
                    endValues[endIndex] = unchecked((int)(uint)endOperand.Bits);
                }
                else if (endOperand.Kind == PredicateOperandKind.Int64)
                {
                    endValues[endIndex] = unchecked((long)endOperand.Bits);
                }
                else if (endOperand.Kind == PredicateOperandKind.UInt64)
                {
                    if (endOperand.Bits > long.MaxValue)
                    {
                        DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperandList: range '" + element
                            + "' has an end above the signed 64-bit range, parse failed", LogLevel.Warn);
                        return false;
                    }
                    endValues[endIndex] = (long)endOperand.Bits;
                }
                else
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperandList: range '" + element
                        + "' has a non-integer end of kind " + endOperand.Kind + ", parse failed",
                        LogLevel.Warn);
                    return false;
                }
            }

            if (endValues[0] > endValues[1])
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperandList: range '" + element
                    + "' has its low end above its high end, parse failed", LogLevel.Warn);
                return false;
            }

            for (long rangeValue = endValues[0]; ; rangeValue++)
            {
                string valueText = rangeValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                PredicateOperand rangeOperand;
                bool rangeParsed = ParseOperand(valueText, out rangeOperand);
                if (rangeParsed == false)
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperandList: range '" + element
                        + "' value " + valueText + " failed to parse", LogLevel.Error);
                    return false;
                }

                parsed.Add(rangeOperand);

                if (rangeValue == endValues[1])
                {
                    break;
                }
            }

            DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperandList: range '" + element
                + "' expanded from " + endValues[0] + " to " + endValues[1], LogLevel.Trace);
        }

        operands = parsed.ToArray();

        DebugLog.Write(LogChannel.Fields, "PatchData.ParseOperandList: list '" + trimmed
            + "' parsed to " + operands.Length + " operand(s)", LogLevel.Trace);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OperandsFitSource
    //
    // Checks that a parsed predicate's operator and every one of its operands can be applied
    // to a source field of the given encoding.  The rules, by source encoding:
    //
    //   UInt, Int, UInt64, Int64, UIntMsb, UIntMasked, SignMagnitudeLsb, SignMagnitudeMsb,
    //   OptSignMagnitudeMsb:  every operand must be an integer kind (UInt, Int, UInt64,
    //   Int64).  Every operator is allowed, except that & is allowed only on the unsigned
    //   encodings UInt, UInt64, UIntMsb, and UIntMasked.
    //
    //   Float, Double:  every operand must be an integer kind or Double.  Every operator
    //   except & is allowed.
    //
    //   StringNullTerminated, StringLengthPrefixed:  every operand must be String, and the
    //   operator must be ==, !=, or in.
    //
    //   Any other encoding:  no rule is applied; the predicate is accepted and a warning is
    //   logged.
    //
    // Parameters:
    //   sourceEncoding  - The encoding of the field the predicate tests.
    //   predicate       - The parsed predicate whose operator and operands to check.
    //   ownerName       - The predicated field's name, used in log messages.
    //
    // Returns:
    //   true when the operator and every operand fit the source encoding, or when no rule
    //   applies to the encoding; false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private bool OperandsFitSource(FieldEncoding sourceEncoding, FieldPredicate predicate, string ownerName)
    {
        if (predicate.Operands == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.OperandsFitSource: field '" + ownerName
                + "' predicate has no operands", LogLevel.Error);
            return false;
        }

        bool isIntegerSource = (sourceEncoding == FieldEncoding.UInt)
            || (sourceEncoding == FieldEncoding.Int)
            || (sourceEncoding == FieldEncoding.UInt64)
            || (sourceEncoding == FieldEncoding.Int64)
            || (sourceEncoding == FieldEncoding.UIntMsb)
            || (sourceEncoding == FieldEncoding.UIntMasked)
            || (sourceEncoding == FieldEncoding.SignMagnitudeLsb)
            || (sourceEncoding == FieldEncoding.SignMagnitudeMsb)
            || (sourceEncoding == FieldEncoding.OptSignMagnitudeMsb);

        bool isFloatingSource = (sourceEncoding == FieldEncoding.Float)
            || (sourceEncoding == FieldEncoding.Double);

        bool isStringSource = (sourceEncoding == FieldEncoding.StringNullTerminated)
            || (sourceEncoding == FieldEncoding.StringLengthPrefixed);

        if ((isIntegerSource == false) && (isFloatingSource == false) && (isStringSource == false))
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.OperandsFitSource: field '" + ownerName
                + "' predicate source encoding " + sourceEncoding
                + " has no operand rule, accepting", LogLevel.Warn);
            return true;
        }

        bool isUnsignedSource = (sourceEncoding == FieldEncoding.UInt)
            || (sourceEncoding == FieldEncoding.UInt64)
            || (sourceEncoding == FieldEncoding.UIntMsb)
            || (sourceEncoding == FieldEncoding.UIntMasked);

        if ((predicate.Op == PredicateOp.BitmaskNonZero) && (isUnsignedSource == false))
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.OperandsFitSource: field '" + ownerName
                + "' predicate uses & on source encoding " + sourceEncoding
                + ", which is not unsigned", LogLevel.Warn);
            return false;
        }

        if (isStringSource == true)
        {
            bool stringOpAllowed = (predicate.Op == PredicateOp.Equal)
                || (predicate.Op == PredicateOp.NotEqual)
                || (predicate.Op == PredicateOp.In);
            if (stringOpAllowed == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.OperandsFitSource: field '" + ownerName
                    + "' predicate operator " + predicate.Op + " is not allowed on string source encoding "
                    + sourceEncoding, LogLevel.Warn);
                return false;
            }
        }

        for (int operandIndex = 0; operandIndex < predicate.Operands.Length; operandIndex++)
        {
            PredicateOperandKind kind = predicate.Operands[operandIndex].Kind;

            bool isIntegerOperand = (kind == PredicateOperandKind.UInt)
                || (kind == PredicateOperandKind.Int)
                || (kind == PredicateOperandKind.UInt64)
                || (kind == PredicateOperandKind.Int64);

            bool operandFits;
            if (isIntegerSource == true)
            {
                operandFits = isIntegerOperand;
            }
            else if (isFloatingSource == true)
            {
                operandFits = (isIntegerOperand == true) || (kind == PredicateOperandKind.Double);
            }
            else
            {
                operandFits = (kind == PredicateOperandKind.String);
            }

            if (operandFits == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.OperandsFitSource: field '" + ownerName
                    + "' predicate operand " + operandIndex + " of kind " + kind
                    + " does not fit source encoding " + sourceEncoding, LogLevel.Warn);
                return false;
            }
        }

        DebugLog.Write(LogChannel.Fields, "PatchData.OperandsFitSource: field '" + ownerName
            + "' predicate op=" + predicate.Op + " with " + predicate.Operands.Length
            + " operand(s) fits source encoding " + sourceEncoding, LogLevel.Trace);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TokenizePredicate
    //
    // Splits a predicate expression into tokens.  Parentheses, &&, and || are structural
    // tokens.  A ! is a Not token only when the next character other than whitespace is an
    // opening parenthesis; any other ! is left in the comparison text, where it negates a
    // single comparison or forms the != operator.  A single & or | is left in the comparison
    // text.  Text between structural tokens is trimmed and emitted as one Comparison token
    // when it is not empty.
    //
    // Text inside double quotes and inside [ ] lists is never read as a structural token, so a
    // string literal or an operand list may hold any of these characters.
    //
    // This method checks only quoting and bracket balance.  Parenthesis balance, operator
    // placement, and the comparison text itself are not checked here.
    //
    // Parameters:
    //   raw     - The predicate expression text.
    //   tokens  - Receives the tokens in the order they appear.  Cleared before use.
    //
    // Returns:
    //   true when the text was split into one or more tokens; false when the text is null,
    //   holds an unterminated string literal, holds an unbalanced [ or ], or yields no tokens.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool TokenizePredicate(string raw, List<PredicateToken> tokens)
    {
        tokens.Clear();

        if (raw == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.TokenizePredicate: null predicate string, "
                + "tokenize failed", LogLevel.Warn);
            return false;
        }

        int position = 0;
        int comparisonStart = 0;
        bool inQuote = false;
        uint bracketDepth = 0;

        while (true)
        {
            bool atEnd = (position >= raw.Length);
            PredicateTokenKind structuralKind = PredicateTokenKind.None;
            int structuralWidth = 0;

            if (atEnd == true)
            {
                if (inQuote == true)
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.TokenizePredicate: unterminated string "
                        + "literal in '" + raw + "', tokenize failed", LogLevel.Warn);
                    return false;
                }

                if (bracketDepth > 0)
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.TokenizePredicate: " + bracketDepth
                        + " unclosed [ in '" + raw + "', tokenize failed", LogLevel.Warn);
                    return false;
                }
            }
            else
            {
                char current = raw[position];

                if (inQuote == true)
                {
                    if (current == '"')
                    {
                        inQuote = false;
                    }
                    position++;
                    continue;
                }

                if (current == '"')
                {
                    inQuote = true;
                    position++;
                    continue;
                }

                if (current == '[')
                {
                    bracketDepth++;
                    position++;
                    continue;
                }

                if (current == ']')
                {
                    if (bracketDepth == 0)
                    {
                        DebugLog.Write(LogChannel.Fields, "PatchData.TokenizePredicate: ] without "
                            + "matching [ at position " + position + " in '" + raw
                            + "', tokenize failed", LogLevel.Warn);
                        return false;
                    }
                    bracketDepth--;
                    position++;
                    continue;
                }

                if (bracketDepth > 0)
                {
                    position++;
                    continue;
                }

                bool hasNext = (position + 1 < raw.Length);

                if (current == '(')
                {
                    structuralKind = PredicateTokenKind.Open;
                    structuralWidth = 1;
                }
                else if (current == ')')
                {
                    structuralKind = PredicateTokenKind.Close;
                    structuralWidth = 1;
                }
                else if ((current == '&') && (hasNext == true) && (raw[position + 1] == '&'))
                {
                    structuralKind = PredicateTokenKind.And;
                    structuralWidth = 2;
                }
                else if ((current == '|') && (hasNext == true) && (raw[position + 1] == '|'))
                {
                    structuralKind = PredicateTokenKind.Or;
                    structuralWidth = 2;
                }
                else if (current == '!')
                {
                    int lookahead = position + 1;
                    while ((lookahead < raw.Length) && (char.IsWhiteSpace(raw[lookahead]) == true))
                    {
                        lookahead++;
                    }

                    if ((lookahead < raw.Length) && (raw[lookahead] == '('))
                    {
                        structuralKind = PredicateTokenKind.Not;
                        structuralWidth = 1;
                    }
                }

                if (structuralKind == PredicateTokenKind.None)
                {
                    position++;
                    continue;
                }
            }

            string comparisonText = raw.Substring(comparisonStart, position - comparisonStart).Trim();
            if (comparisonText.Length > 0)
            {
                PredicateToken comparisonToken;
                comparisonToken.Kind = PredicateTokenKind.Comparison;
                comparisonToken.Text = comparisonText;
                tokens.Add(comparisonToken);

                DebugLog.Write(LogChannel.Fields, "PatchData.TokenizePredicate: comparison '"
                    + comparisonText + "'", LogLevel.Trace);
            }

            if (atEnd == true)
            {
                break;
            }

            PredicateToken structuralToken;
            structuralToken.Kind = structuralKind;
            structuralToken.Text = raw.Substring(position, structuralWidth);
            tokens.Add(structuralToken);

            DebugLog.Write(LogChannel.Fields, "PatchData.TokenizePredicate: " + structuralKind
                + " at position " + position, LogLevel.Trace);

            position = position + structuralWidth;
            comparisonStart = position;
        }

        if (tokens.Count == 0)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.TokenizePredicate: no tokens in '" + raw
                + "', tokenize failed", LogLevel.Warn);
            return false;
        }

        DebugLog.Write(LogChannel.Fields, "PatchData.TokenizePredicate: '" + raw + "' split into "
            + tokens.Count + " token(s)", LogLevel.Trace);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ParsePredicateGroup
    //
    // Parses one level of a predicate expression, starting at the parse state's position, and
    // emits its steps in postfix order.  A level is one or more operands separated by && or
    // ||.  An operand is a comparison, a parenthesized group, or ! followed by a parenthesized
    // group.  A parenthesized group is parsed by a recursive call for the level inside it.
    //
    // Every operator within one level must be the same: a level that mixes && and || is
    // rejected, so the grouping of mixed operators is always stated by parentheses.  Operators
    // within a level are applied left to right.
    //
    // Parsing stops without consuming the token at a ) or at the end of the tokens.  Checking
    // that the stop point is the right one belongs to the level that opened the group.
    //
    // Each comparison's text is parsed by ParsePredicate, which supplies the comparison and
    // its source field name.
    //
    // Parameters:
    //   state  - The shared parse state.  Its position is advanced past every token consumed,
    //            and its steps, source names, depth, and maximum depth are updated for every
    //            step emitted.
    //
    // Returns:
    //   true when the level parsed; false when an operand is missing or malformed, a
    //   comparison fails to parse, a group is not closed, or && and || are mixed.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool ParsePredicateGroup(PredicateParseState state)
    {
        PredicateStepKind groupOperator = PredicateStepKind.None;
        PredicateStepKind pendingOperator = PredicateStepKind.None;
        bool expectOperand = true;

        while (state.Position < state.Tokens.Count)
        {
            PredicateToken token = state.Tokens[state.Position];

            if (expectOperand == false)
            {
                if (token.Kind == PredicateTokenKind.Close)
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateGroup: ) at token "
                        + state.Position + " ends the level", LogLevel.Trace);
                    break;
                }

                PredicateStepKind operatorKind;
                if (token.Kind == PredicateTokenKind.And)
                {
                    operatorKind = PredicateStepKind.And;
                }
                else if (token.Kind == PredicateTokenKind.Or)
                {
                    operatorKind = PredicateStepKind.Or;
                }
                else
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateGroup: expected &&, ||, "
                        + "or ) at token " + state.Position + " but found " + token.Kind + " '"
                        + token.Text + "', parse failed", LogLevel.Warn);
                    return false;
                }

                if (groupOperator == PredicateStepKind.None)
                {
                    groupOperator = operatorKind;
                }
                else if (groupOperator != operatorKind)
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateGroup: && and || mixed "
                        + "at token " + state.Position + " without parentheses, parse failed",
                        LogLevel.Warn);
                    return false;
                }

                pendingOperator = operatorKind;
                state.Position++;
                expectOperand = true;
                continue;
            }

            bool negateOperand = false;
            if (token.Kind == PredicateTokenKind.Not)
            {
                negateOperand = true;
                state.Position++;

                if ((state.Position >= state.Tokens.Count)
                    || (state.Tokens[state.Position].Kind != PredicateTokenKind.Open))
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateGroup: ! at token "
                        + (state.Position - 1) + " is not followed by (, parse failed", LogLevel.Warn);
                    return false;
                }

                token = state.Tokens[state.Position];
            }

            if (token.Kind == PredicateTokenKind.Comparison)
            {
                string sourceName;
                FieldPredicate comparison;
                bool parsed = ParsePredicate(token.Text, out sourceName, out comparison);
                if (parsed == false)
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateGroup: comparison '"
                        + token.Text + "' at token " + state.Position + " failed to parse",
                        LogLevel.Warn);
                    return false;
                }

                PredicateStep comparisonStep;
                comparisonStep.Kind = PredicateStepKind.Comparison;
                comparisonStep.Comparison = comparison;
                state.Steps.Add(comparisonStep);
                state.SourceNames.Add(sourceName);

                state.Depth++;
                if (state.Depth > state.MaxDepth)
                {
                    state.MaxDepth = state.Depth;
                }

                DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateGroup: emitted Comparison '"
                    + token.Text + "' as step " + (state.Steps.Count - 1) + ", depth now "
                    + state.Depth, LogLevel.Trace);

                state.Position++;
            }
            else if (token.Kind == PredicateTokenKind.Open)
            {
                int openPosition = state.Position;
                state.Position++;

                DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateGroup: ( at token "
                    + openPosition + " opens a group", LogLevel.Trace);

                if (ParsePredicateGroup(state) == false)
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateGroup: group opened at "
                        + "token " + openPosition + " failed to parse", LogLevel.Warn);
                    return false;
                }

                if ((state.Position >= state.Tokens.Count)
                    || (state.Tokens[state.Position].Kind != PredicateTokenKind.Close))
                {
                    DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateGroup: group opened at "
                        + "token " + openPosition + " is not closed, parse failed", LogLevel.Warn);
                    return false;
                }

                DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateGroup: ) at token "
                    + state.Position + " closes the group opened at token " + openPosition,
                    LogLevel.Trace);

                state.Position++;
            }
            else
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateGroup: expected a "
                    + "comparison or ( at token " + state.Position + " but found " + token.Kind
                    + " '" + token.Text + "', parse failed", LogLevel.Warn);
                return false;
            }

            if (negateOperand == true)
            {
                PredicateStep notStep = default;
                notStep.Kind = PredicateStepKind.Not;
                state.Steps.Add(notStep);
                state.SourceNames.Add(null);

                DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateGroup: emitted Not as step "
                    + (state.Steps.Count - 1) + ", depth now " + state.Depth, LogLevel.Trace);
            }

            if (pendingOperator != PredicateStepKind.None)
            {
                PredicateStep operatorStep = default;
                operatorStep.Kind = pendingOperator;
                state.Steps.Add(operatorStep);
                state.SourceNames.Add(null);
                state.Depth--;

                DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateGroup: emitted "
                    + pendingOperator + " as step " + (state.Steps.Count - 1) + ", depth now "
                    + state.Depth, LogLevel.Trace);

                pendingOperator = PredicateStepKind.None;
            }

            expectOperand = false;
        }

        if (expectOperand == true)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateGroup: level ends at token "
                + state.Position + " where a comparison or ( was expected, parse failed",
                LogLevel.Warn);
            return false;
        }

        DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateGroup: level parsed, stopped at "
            + "token " + state.Position, LogLevel.Trace);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ParsePredicateExpression
    //
    // Parses a raw predicate expression into a PredicateProgram in postfix order.  The
    // expression is one or more comparisons joined by && and ||, grouped by parentheses, with
    // ! applying to a parenthesized group.  Each comparison uses the form ParsePredicate
    // accepts.  && and || may not be mixed within one level without parentheses.  A single
    // comparison with no operators yields a program of one Comparison step.
    //
    // The program's source names still require resolution to slots by the caller.  This
    // method performs no resolution and reads no payload.
    //
    // Parameters:
    //   raw      - The raw predicate string from the PacketField predicate column.
    //   program  - Receives the parsed program on success, default on failure.
    //
    // Returns:
    //   true when the expression parsed; false when it fails to split into tokens, any level
    //   fails to parse, a ) has no matching (, or the steps do not reduce to a single result.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static bool ParsePredicateExpression(string raw, out PredicateProgram program)
    {
        program = default;

        PredicateParseState state = new PredicateParseState();

        if (TokenizePredicate(raw, state.Tokens) == false)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateExpression: '" + raw
                + "' failed to split into tokens", LogLevel.Warn);
            return false;
        }

        if (ParsePredicateGroup(state) == false)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateExpression: '" + raw
                + "' failed to parse", LogLevel.Warn);
            return false;
        }

        if (state.Position < state.Tokens.Count)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateExpression: '" + raw
                + "' has " + state.Tokens[state.Position].Kind + " '"
                + state.Tokens[state.Position].Text + "' at token " + state.Position
                + " with no matching (, parse failed", LogLevel.Warn);
            return false;
        }

        if (state.Depth != 1)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateExpression: '" + raw
                + "' leaves " + state.Depth + " result(s) on the stack instead of 1, parse failed",
                LogLevel.Error);
            return false;
        }

        program.Steps = state.Steps.ToArray();
        program.MaxDepth = state.MaxDepth;
        program.SourceNames = state.SourceNames.ToArray();

        DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicateExpression: parsed '" + raw
            + "' to " + program.Steps.Length + " step(s), max depth " + program.MaxDepth,
            LogLevel.Trace);
        return true;
    }
}