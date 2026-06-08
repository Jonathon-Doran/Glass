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
    private readonly Dictionary<string, OpcodeValue> _opcodeValuesByName;
    private readonly Dictionary<OpcodeValue, string> _opcodeNamesByValue;
    private readonly Dictionary<string, FieldEncoding> _encodingsByString;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _patchOpcodeByOpcodeHandle
    //
    // Flat array of every PatchOpcode loaded for this patch level, indexed by Opcode.
    // Allocated once in the constructor sized to the number of rows returned by
    // LoadPatchOpcodes; never resized.  An Opcode is a position in this array, valid
    // only for this PatchData instance.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private readonly PatchOpcode[] _patchOpcodeByOpcodeHandle;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _opcodeFields
    //
    // Parallel array to _patchOpcodeByOpcodeHandle: _opcodeFields[opcodeHandle] is the FieldDefinition[] for
    // _patchOpcodeByOpcodeHandle[opcodeHandle], or null if that opcode has no fields defined for this patch
    //
    // Allocated once in the constructor sized to the number of rows returned by
    // LoadPatchOpcodes; never resized.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private readonly FieldDefinition[]?[] _opcodeFields;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _collectionFields
    //
    // The field definitions of a collection, indexed by CollectionHandle.
    //
    // Allocated once in the constructor sized to the collection count; never resized.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private readonly FieldDefinition[]?[] _collectionFields;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // _opcodeOptionalGroups
    //
    // Parallel array to _patchOpcodeByOpcodeHandle: _opcodeOptionalGroups[opcodeHandle] is the OptionalGroup
    // for the opcode at that opcodeHandle, or null if the opcode has no optional block defined
    // for this patch.  Most opcodes have no optional block; those slots stay null.
    //
    // Allocated once in the constructor sized to the number of rows returned by
    // LoadPatchOpcodes; never resized.
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private readonly Dictionary<uint, OptionalGroup> _optionalGroupsById;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // _collectionNamesByHandle
    //
    // Parallel array to _patchOpcodeByOpcodeHandle: _collectionNamesByHandle[opcodeHandle] is the
    // collection_name string for _patchOpcodeByOpcodeHandle[opcodeHandle].  
    ///////////////////////////////////////////////////////////////////////////////////////////
    private readonly string[] _collectionNamesByHandle;
    private readonly Dictionary<string, CollectionHandle> _collectionHandleByCollectionName;
    private readonly Dictionary<OpcodeHandle, CollectionHandle> _collectionHandleByOpcodeHandle;
    private readonly Dictionary<string, CollectionHandle> _collectionHandleByOpcodeName;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // _optionalGroupIdsByName
    //
    // Maps an optional group's name to its PacketOptionalGroup id.  Built once before the
    // per-opcode field loop from every PacketOptionalGroup row for this patch.  Consulted
    // during field loading when a PacketField encoding string is not a known scalar
    // encoding: the string is looked up here to resolve it to a group id.  Cold path only;
    // not used after construction.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private Dictionary<string, int>? _optionalGroupIdsByName;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // _opcodeHandlesByPatchOpcode
    //
    // Maps the PatchOpcode to the OpcodeHandle assigned at load time.  Used on the
    // hot path by the dispatcher to resolve an incoming PatchOpcode to the opcodeHandle that
    // indexes its handler array.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private readonly Dictionary<PatchOpcode, OpcodeHandle> _opcodeHandlesByPatchOpcode;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // _opcodeNamesByOpcodeHandle
    //
    // Parallel array to _patchOpcodeByOpcodeHandle: _opcodeNamesByOpcodeHandle[opcodeHandle] is the opcode_name string
    // for _patchOpcodeByOpcodeHandle[opcodeHandle].  Allocated once in the constructor sized to the number
    // of rows returned by LoadPatchOpcodes; never resized.  Used by GetOpcodeName and by
    // diagnostic log lines that need to render a opcodeHandle as a human-readable name.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private readonly string[] _opcodeNamesByOpcodeHandle;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // _opcodeHandlesByName
    //
    // Reverse map of _opcodeNamesByOpcodeHandle: opcode_name string to Opcode.  Used by
    // GetOpcodeHandle(string) when handlers resolve the opcodes they care about at
    // construction time.  Cold path — handlers call this once per opcode they register.
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
    // Flat array of childCollection Gate structures for this patch level, indexed by GateHandle.
    // Sized by CountGates and filled incrementally as each gate is childCollection at its
    // referrer's load time, in resolution order.  A GateHandle is a position in this array.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private readonly Gate[] _gate;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // _nextGateHandle
    //
    // The next GateHandle to assign, advanced by one each time a gate is childCollection and stored
    // in _gate.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private uint _nextGateHandle;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PendingGateField
    //
    // Load-time record pairing a gate awaiting field resolution with the field name to resolve
    // and the collection that name resolves against.  Created when a gate carrying a field name
    // is loaded and consumed by the gate field-resolution pass, which looks the name up within
    // the parent collection's fields and writes the resulting SlotId onto the gate.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    internal struct PendingGateField
    {
        public GateHandle Gate;
        public string FieldName;
        public CollectionHandle ParentCollection;
    }

    private List<PendingGateField>? _pendingGateFields;

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
    internal struct PendingFieldPredicate
    {
        public string OwnerFieldName;
        public string RawPredicate;
    }

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
        _opcodeValuesByName = new Dictionary<string, OpcodeValue>();
        _opcodeNamesByValue = new Dictionary<OpcodeValue, string>();
        _optionalGroupsById = new Dictionary<uint, OptionalGroup>();

        _pendingGateFields = new List<PendingGateField>();
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
        _opcodeFields = new FieldDefinition[opcodeCount][];
        _collectionFields = new FieldDefinition[collectionCount][];
        _opcodeNamesByOpcodeHandle = new string[opcodeCount];
        _opcodeHandlesByName = new Dictionary<string, OpcodeHandle>(opcodeCount);
        _opcodeHandlesByPatchOpcode = new Dictionary<PatchOpcode, OpcodeHandle>(opcodeCount);
        _pendingRelativeNames = new List<string?>();

        _collectionNamesByHandle = new string[collectionCount];
        _collectionHandleByCollectionName = new Dictionary<string, CollectionHandle>(collectionCount);
        _gate = new Gate[gateCount];
        _nextGateHandle = 0;
        _collectionHandleByOpcodeHandle = new Dictionary<OpcodeHandle, CollectionHandle>(opcodeCount);
        _collectionHandleByOpcodeName = new Dictionary<string, CollectionHandle>(collectionCount);
        LoadOpcodeMap(conn);
        LoadPatchOpcodes(conn);
        LoadPatchCollections(conn);
        LoadOptionalGroupNames(conn);

        // may be deprecated
        for (uint handleIndex = 0; handleIndex < opcodeCount; handleIndex++)
        {
            OpcodeHandle handle = (OpcodeHandle) handleIndex;
            PatchOpcode patchOpcode = _patchOpcodeByOpcodeHandle[handle];
            string name = _opcodeNamesByOpcodeHandle[handle];

            CollectionHandle collectionHandle = CollectionHandleForOpcode(handle, conn);
            _collectionHandleByOpcodeName[name] = collectionHandle;
            _collectionHandleByOpcodeHandle[handle] = collectionHandle;
        }

        for (uint collectionIndex = 0; collectionIndex < collectionCount; collectionIndex++)
        {
            CollectionHandle handle = (CollectionHandle)collectionIndex;
            DebugLog.Write(LogChannel.Fields, "PatchData ctor: loading fields for collection opcodeHandle="
                + collectionIndex + " name=" + _collectionNamesByHandle[handle]);
            LoadFields(handle, conn);
        }
        ResolveGates();
        _pendingRelativeNames = null;
        _pendingGateFields = null;
        _pendingFieldPredicates = null;

        DebugLog.Write(LogChannel.Opcodes, "PatchData ctor: loaded " +
            _opcodeValuesByName.Count + " opcode name(s) and " + _patchOpcodeByOpcodeHandle.Length + " opcode(s) for patch " + PatchLevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CollectionHandleForOpcode
    //
    // Resolves an opcode to the CollectionHandle of the collection it decodes, by reading the
    // opcode's gate binding and the gates's child collection from the database, then mapping
    // that collection name to its opcodeHandle.  Temporary: bridges the opcode field loader to the
    // collection-keyed field store until the loader is collection-native.
    //
    // Parameters:
    //   opcodeHandle  - The opcode whose collection to resolve.
    //   conn          - An open database connection, owned by the caller.
    //
    // Returns:
    //   The CollectionHandle for the opcode's collection, or CollectionHandle.None if the
    //   binding or collection cannot be childCollection.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private CollectionHandle CollectionHandleForOpcode(OpcodeHandle opcodeHandle, SqliteConnection conn)
    {
        string opcodeName = _opcodeNamesByOpcodeHandle[opcodeHandle];

        string? childCollection = null;
        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT m.child_collection"
                + " FROM PatchOpcode po"
                + " JOIN Gate m"
                + " ON m.name = po.gate_name"
                + " AND m.patch_date = po.patch_date"
                + " AND m.server_type = po.server_type"
                + " WHERE po.patch_date = @patchDate"
                + " AND po.server_type = @serverType"
                + " AND po.opcode_name = @opcodeName";
            cmd.Parameters.AddWithValue("@patchDate", PatchLevel.PatchDate);
            cmd.Parameters.AddWithValue("@serverType", PatchLevel.ServerType);
            cmd.Parameters.AddWithValue("@opcodeName", opcodeName);

            object? result = cmd.ExecuteScalar();
            if (result != null && result != DBNull.Value)
            {
                childCollection = (string)result;
            }
        }

        if (childCollection == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.CollectionHandleForOpcode: no gate/collection "
                + "binding for opcode '" + opcodeName + "' in patchLevel=" + PatchLevel
                + ", returning None");
            return CollectionHandle.None;
        }

        CollectionHandle handle;
        bool found = _collectionHandleByCollectionName.TryGetValue(childCollection, out handle);
        if (found == false)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.CollectionHandleForOpcode: opcode '"
                + opcodeName + "' gate names collection '" + childCollection
                + "' that is not loaded in patchLevel=" + PatchLevel + ", returning None");
            return CollectionHandle.None;
        }

        return handle;
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
        _encodingsByString.Add("optional_group", FieldEncoding.OptionalGroup);
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

        DebugLog.Write(LogChannel.Opcodes, "PatchData.CountPatchOpcodes: " + count
            + " PatchOpcode row(s) for patchLevel=" + PatchLevel);
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

        DebugLog.Write(LogChannel.Opcodes, "PatchData.CountCollections: " + count
            + " FieldCollection row(s) for patchLevel=" + PatchLevel);
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

        DebugLog.Write(LogChannel.Fields, "PatchData.CountGates: " + count
            + " Gate row(s) for patchLevel=" + PatchLevel);
        return count;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadPatchOpcodes
    //
    // Reads every PatchOpcode row for this patch level and populates the opcodeHandle-indexed
    // structures: _patchOpcodeByOpcodeHandle, _opcodeNamesByOpcodeHandle, and _opcodeHandlesByName.  Row order from the
    // database determines opcodeHandle assignment — the first row read becomes opcodeHandle 0, the
    // second opcodeHandle 1, and so on.
    //
    // The arrays and dictionary must be allocated by the constructor before this method
    // runs.  The size is determined by CountPatchOpcodes, which uses the same WHERE clause
    // and so produces a count consistent with what this method will read.
    //
    // Parameters:
    //   conn    - An open database connection, owned by the caller. 
    //
    // Returns
    //   The number of opcodes read
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private uint LoadPatchOpcodes(SqliteConnection conn)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT opcode_value, version, opcode_name"
            + " FROM PatchOpcode"
            + " WHERE patch_date = @patchDate"
            + " AND server_type = @serverType";
        cmd.Parameters.AddWithValue("@patchDate", PatchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", PatchLevel.ServerType);

        using SqliteDataReader reader = cmd.ExecuteReader();
        uint handleIndex = 0;
        while (reader.Read())
        {
            int opcodeValueRaw = reader.GetInt32(0);
            uint version = (uint) reader.GetInt32(1);
            string opcodeName = reader.GetString(2);
            OpcodeValue opcodeValue = (OpcodeValue) opcodeValueRaw;
            OpcodeHandle opcodeHandle = (OpcodeHandle)handleIndex;
            PatchLevel currentPatch = GlassContext.CurrentPatchLevel;
            PatchOpcode patchOpcode = new PatchOpcode(currentPatch, opcodeValue, version);
            DebugLog.Write(LogChannel.Opcodes, "Register opcode " + patchOpcode +
                " as handle " + opcodeHandle);
            _patchOpcodeByOpcodeHandle[opcodeHandle] = patchOpcode;
            _opcodeNamesByOpcodeHandle[opcodeHandle] = opcodeName;
            _opcodeHandlesByName[opcodeName] = opcodeHandle;
            _opcodeHandlesByPatchOpcode[patchOpcode] = opcodeHandle;

            handleIndex = handleIndex + 1;
        }

        DebugLog.Write(LogChannel.Opcodes, "PatchData.LoadPatchOpcodes: loaded "
            + handleIndex + " PatchOpcode(s) for patchLevel=" + PatchLevel);
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

        using SqliteDataReader reader = cmd.ExecuteReader();
        uint handleIndex = 0;
        while (reader.Read())
        {
            string collectionName = reader.GetString(0);
            CollectionHandle handle = (CollectionHandle)handleIndex;

            _collectionNamesByHandle[handle] = collectionName;
            _collectionHandleByCollectionName[collectionName] = handle;

            handleIndex = handleIndex + 1;
        }

        DebugLog.Write(LogChannel.Fields, "PatchData.LoadPatchCollections: loaded "
            + handleIndex + " FieldCollection(s) for patchLevel=" + PatchLevel);
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
            OpcodeValue opcodeValue = (OpcodeValue) reader.GetInt32(1);
            _opcodeValuesByName[opcodeName] = opcodeValue;
            _opcodeNamesByValue[opcodeValue] = opcodeName;
            rowCount++;
        }

        DebugLog.Write(LogChannel.Opcodes, "PatchData.LoadOpcodeMap: read " + rowCount
            + " row(s), map now has " + _opcodeValuesByName.Count + " entries");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadFields
    //
    // Reads the PacketField rows for the collection at the given opcodeHandle, ordered by bit_offset,
    // builds their FieldDefinition array, stores it in _collectionFields, and resolves relative
    // anchors.  A collection with no PacketField rows leaves the entry null.
    //
    // Unrecognized encoding strings are stored as FieldEncoding.Unknown.  The relative_to value
    // of each row is appended to _pendingRelativeNames for ResolveRelativeAnchors, which is
    // cleared before this method returns.
    //
    // Parameters:
    //   opcodeHandle  - The CollectionHandle whose fields to load.
    //   conn    - An open database connection, owned by the caller.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadFields(CollectionHandle handle, SqliteConnection conn)
    {
        string collectionName = _collectionNamesByHandle[handle];
        List<FieldDefinition> fields = new List<FieldDefinition>();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT field_name, bit_offset, bit_length, encoding, divisor, relative_to, predicate"
                    + " FROM PacketField"
                    + " WHERE patch_date = @patchDate"
                    + " AND server_type = @serverType"
                    + " AND collection_name = @collectionName"
                    + " ORDER BY bit_offset";
        cmd.Parameters.AddWithValue("@patchDate", PatchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", PatchLevel.ServerType);
        cmd.Parameters.AddWithValue("@collectionName", collectionName);

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
                GateHandle gate = GateHandle.None;

                if (encodingString.StartsWith("Gate") == true)
                {
                    gate = LoadGate(encodingString, handle, conn);
                    encoding = FieldEncoding.Gate;
                }
                else
                {
                    bool encodingFound = _encodingsByString.TryGetValue(encodingString, out encoding);
                    if (encodingFound == false)
                    {
                        DebugLog.Write(LogChannel.Fields, "PatchData.LoadFields: unrecognized encoding '"
                            + encodingString + "' for collection='" + collectionName
                            + "' fieldName='" + fieldName + "', storing as Unknown");
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

                FieldDefinition definition;
                definition.Name = fieldName;
                definition.BitOffset = bitOffset;
                definition.BitLength = bitLength;
                definition.Encoding = encoding;
                definition.Divisor = divisor;
                definition.RelativeToSlot = null;
                definition.OptionalGroupId = null;
                definition.Gate = gate;
                definition.Predicate = default;
                fields.Add(definition);
                _pendingRelativeNames!.Add(relativeToName);

                if (predicateString != null)
                {
                    PendingFieldPredicate pendingPredicate;
                    pendingPredicate.OwnerFieldName = fieldName;
                    pendingPredicate.RawPredicate = predicateString;
                    _pendingFieldPredicates!.Add(pendingPredicate);
                    DebugLog.Write(LogChannel.Fields, "PatchData.LoadFields: collection='"
                        + collectionName + "' field='" + fieldName
                        + "' carries predicate '" + predicateString + "', pending resolution");
                }
            }
        }

        if (fields.Count == 0)
        {
            _pendingRelativeNames!.Clear();
            return;
        }

        _collectionFields[handle] = fields.ToArray();
        ResolveRelativeAnchors(handle);
        ResolvePredicates(handle);

        _pendingRelativeNames!.Clear();
        _pendingFieldPredicates!.Clear();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // LoadGate
    //
    // Reads the single Gate row named by gateField for this patch level, builds its Gate
    // struct, stores it at the next GateHandle, and returns that opcodeHandle.  The child_collection
    // name is looked up to a CollectionHandle; kind is parsed to MultiplicityKind.  When the row's
    // field_name is non-null, a PendingGateField is appended pairing this gate with that name
    // and the parent collection, for the field-resolution step to resolve to a SlotId.
    //
    // An unparsable kind is stored as Always and logged.  A child_collection naming no loaded
    // collection stores CollectionHandle.None and logs.  A missing gate row logs and stores a
    // gate with CollectionHandle.None so the returned opcodeHandle stays valid.
    //
    // Parameters:
    //   gateField         - The Gate name to read.
    //   parentCollection  - The collection this gate is referenced from, recorded on any
    //                       PendingGateField for later field resolution.
    //   conn              - An open database connection, owned by the caller.
    //
    // Returns:
    //   The GateHandle assigned to the loaded gate.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private GateHandle LoadGate(string gateField, CollectionHandle parentCollection, SqliteConnection conn)
    {
        string kindString = "";
        string childCollectionName = "";
        string? fieldName = null;
        bool rowFound = false;

        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT kind, child_collection, field_name"
                + " FROM Gate"
                + " WHERE patch_date = @patchDate"
                + " AND server_type = @serverType"
                + " AND name = @name";
            cmd.Parameters.AddWithValue("@patchDate", PatchLevel.PatchDate);
            cmd.Parameters.AddWithValue("@serverType", PatchLevel.ServerType);
            cmd.Parameters.AddWithValue("@name", gateField);

            using SqliteDataReader reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                rowFound = true;
                kindString = reader.GetString(0);
                childCollectionName = reader.GetString(1);
                if (reader.IsDBNull(2) == false)
                {
                    fieldName = reader.GetString(2);
                }
            }
        }

        if (rowFound == false)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.LoadGate: no Gate row named '"
                + gateField + "' for patchLevel=" + PatchLevel + ", storing gate with no child collection");
        }

        MultiplicityKind kind = MultiplicityKind.Once;
        if (rowFound == true)
        {
            bool kindParsed = Enum.TryParse<MultiplicityKind>(kindString, out kind);
            if (kindParsed == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.LoadGate: gate '" + gateField
                    + "' has unparsable kind '" + kindString + "', storing as Always");
                kind = MultiplicityKind.Once;
            }
        }

        CollectionHandle childCollection = CollectionHandle.None;
        if (childCollectionName.Length > 0)
        {
            bool collectionFound = _collectionHandleByCollectionName.TryGetValue(childCollectionName, out childCollection);
            if (collectionFound == false)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.LoadGate: gate '" + gateField
                    + "' names child collection '" + childCollectionName
                    + "' that is not loaded for patchLevel=" + PatchLevel + ", storing CollectionHandle.None");
            }
        }

        GateHandle handle = (GateHandle)_nextGateHandle;
        _nextGateHandle = _nextGateHandle + 1;

        Gate gate;
        gate.Name = gateField;
        gate.Kind = kind;
        gate.ChildCollection = childCollection;
        gate.FieldSlot = SlotId.None;

        _gate[handle] = gate;

        if (fieldName != null)
        {
            PendingGateField pending;
            pending.Gate = handle;
            pending.FieldName = fieldName;
            pending.ParentCollection = parentCollection;
            _pendingGateFields!.Add(pending);
            DebugLog.Write(LogChannel.Fields, "PatchData.LoadGate: gate '" + gateField
                + "' field '" + fieldName + "' pending resolution against collection opcodeHandle "
                + (uint)parentCollection);
        }

        DebugLog.Write(LogChannel.Fields, "PatchData.LoadGate: loaded gate '" + gateField
            + "' kind=" + kind + " childCollection=" + (uint)childCollection
            + " at opcodeHandle " + (uint)handle + " for patchLevel=" + PatchLevel);

        return handle;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LoadOptionalGroupNames
    //
    // Reads every PacketOptionalGroup row for this patch level and builds the name-to-id
    // map used during field loading to resolve a PacketField encoding string that names a
    // substructure.  Rows with an empty name are skipped and logged; an unnamed group
    // cannot be referenced by a field.  A duplicate name is logged and the first occurrence
    // is kept.
    //
    // Parameters:
    //   conn    - An open database connection, owned by the caller.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void LoadOptionalGroupNames(SqliteConnection conn)
    {
        _optionalGroupIdsByName = new Dictionary<string, int>();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pog.id, pog.name"
            + " FROM PacketOptionalGroup pog"
            + " JOIN PatchOpcode po ON pog.patch_opcode_id = po.id"
            + " WHERE po.patch_date = @patchDate"
            + " AND po.server_type = @serverType";
        cmd.Parameters.AddWithValue("@patchDate", PatchLevel.PatchDate);
        cmd.Parameters.AddWithValue("@serverType", PatchLevel.ServerType);

        using SqliteDataReader reader = cmd.ExecuteReader();
        int rowCount = 0;
        while (reader.Read())
        {
            int groupId = reader.GetInt32(0);
            string groupName = reader.GetString(1);
            rowCount++;

            if (groupName.Length == 0)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.LoadOptionalGroupNames: group id "
                    + groupId + " has empty name, skipping; it cannot be referenced by a field");
                continue;
            }

            if (_optionalGroupIdsByName.ContainsKey(groupName) == true)
            {
                DebugLog.Write(LogChannel.Fields, "PatchData.LoadOptionalGroupNames: duplicate group "
                    + "name '" + groupName + "' (id " + groupId + "), keeping first occurrence");
                continue;
            }

            _optionalGroupIdsByName[groupName] = groupId;
        }

        DebugLog.Write(LogChannel.Fields, "PatchData.LoadOptionalGroupNames: read " + rowCount
            + " group row(s), map has " + _optionalGroupIdsByName.Count + " named group(s) for patchLevel="
            + PatchLevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ResolveRelativeAnchors
    //
    // Reorders _collectionFields[opcodeHandle] so that every relative field appears after the field it
    // anchors on, then resolves each anchor name to a slot index and writes it into
    // FieldDefinition.RelativeToSlot.  The ordering is produced by Kahn's algorithm over the
    // name-anchor graph and handles chains of any depth.
    //
    // _pendingRelativeNames supplies the anchor name for each field, parallel to
    // _collectionFields[opcodeHandle].
    //
    // An anchor name not present in the field list logs and leaves RelativeToSlot null on
    // that field.  A cycle in the graph logs each unresolved field and appends the affected
    // fields in their original order.
    //
    // Parameters:
    //   opcodeHandle  - The CollectionHandle whose field array to reorder and resolve.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ResolveRelativeAnchors(CollectionHandle handle)
    {
        FieldDefinition[]? definitions = _collectionFields[handle];
        if (definitions == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.ResolveRelativeAnchors: no field "
                + "definitions for collection='" + _collectionNamesByHandle[handle]
                + "', nothing to resolve");
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
                    + _collectionNamesByHandle[handle] + "' field='" + ownName
                    + "' references unknown anchor '" + anchorName
                    + "', will be left absolute");
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
                        + _collectionNamesByHandle[handle] + "' field='"
                        + definitions[leftoverIndex].Name
                        + "' has unresolved anchor dependency, appending in original order");
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
                    + _collectionNamesByHandle[handle] + "' field='" + entry.Name
                    + "' references unknown anchor '" + anchorName
                    + "', leaving RelativeToSlot null");
                continue;
            }

            entry.RelativeToSlot = resolvedIndex;
            ordered[resolveIndex] = entry;
        }

        _collectionFields[handle] = ordered.ToArray();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ResolveGates
    //
    // Resolves each pending gate field name to a SlotId within its parent collection and
    // writes it onto the gate's FieldSlot.  Run once after every collection's fields are loaded,
    // so a name can resolve against any parent collection.
    //
    // A name not present in its parent collection is a broken patch definition: the failure is
    // logged with a stack trace and the process is terminated, since a gate decoding against a
    // field its collection does not define must never run.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ResolveGates()
    {
        List<PendingGateField> pending = _pendingGateFields!;
        for (int pendingIndex = 0; pendingIndex < pending.Count; pendingIndex++)
        {
            PendingGateField entry = pending[pendingIndex];
            SlotId slot = IndexOfField(entry.ParentCollection, entry.FieldName);
            if (slot.Exists == false)
            {
                string message = "PatchData.ResolveGates: gate opcodeHandle " + (uint)entry.Gate
                    + " references field '" + entry.FieldName + "' not present in collection '"
                    + _collectionNamesByHandle[entry.ParentCollection] + "' for patchLevel="
                    + PatchLevel + " -- broken patch definition, aborting";

                DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                    + Environment.StackTrace);

                Environment.FailFast(message);
            }
            _gate[entry.Gate].FieldSlot = slot;
        }

        DebugLog.Write(LogChannel.Fields, "PatchData.ResolveGates: childCollection "
            + pending.Count + " pending gate field(s) for patchLevel=" + PatchLevel);
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
    //   opcodeHandle  - The CollectionHandle whose pending predicates to resolve.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ResolvePredicates(CollectionHandle handle)
    {
        List<PendingFieldPredicate> pending = _pendingFieldPredicates!;
        FieldDefinition[]? definitions = _collectionFields[handle];

        if (definitions == null)
        {
            if (pending.Count > 0)
            {
                string message = "PatchData.ResolvePredicates: collection '"
                    + _collectionNamesByHandle[handle] + "' has " + pending.Count
                    + " pending predicate(s) but no field array for patchLevel=" + PatchLevel
                    + " -- broken patch definition, aborting";

                DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                    + Environment.StackTrace);

                Environment.FailFast(message);
            }

            DebugLog.Write(LogChannel.Fields, "PatchData.ResolvePredicates: collection '"
                + _collectionNamesByHandle[handle] + "' has no field array and no pending "
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
                    + _collectionNamesByHandle[handle] + "' field '" + entry.OwnerFieldName
                    + "' predicate '" + entry.RawPredicate + "' failed to parse for patchLevel="
                    + PatchLevel + " -- broken patch definition, aborting";

                DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                    + Environment.StackTrace);

                Environment.FailFast(message);
            }

            SlotId sourceSlot = IndexOfField(handle, sourceName);
            if (sourceSlot.Exists == false)
            {
                string message = "PatchData.ResolvePredicates: collection '"
                    + _collectionNamesByHandle[handle] + "' field '" + entry.OwnerFieldName
                    + "' predicate '" + entry.RawPredicate + "' names source field '"
                    + sourceName + "' not present in the collection for patchLevel=" + PatchLevel
                    + " -- broken patch definition, aborting";

                DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                    + Environment.StackTrace);

                Environment.FailFast(message);
            }


            SlotId ownerSlot = IndexOfField(handle, entry.OwnerFieldName);
            if (ownerSlot.Exists == false)
            {
                string message = "PatchData.ResolvePredicates: collection '"
                    + _collectionNamesByHandle[handle] + "' predicate owner field '"
                    + entry.OwnerFieldName + "' not present in the collection for patchLevel="
                    + PatchLevel + " -- broken patch definition, aborting";

                DebugLog.WriteMultiline(LogChannel.Fields, message + Environment.NewLine
                    + Environment.StackTrace);

                Environment.FailFast(message);
            }

            if (! SlotDefinedBefore(sourceSlot, ownerSlot))
            {
                string message = "PatchData.ResolvePredicates: collection '"
                    + _collectionNamesByHandle[handle] + "' field '" + entry.OwnerFieldName
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

            DebugLog.Write(LogChannel.Fields, "PatchData.ResolvePredicates: collection '"
                            + _collectionNamesByHandle[handle] + "' field '" + entry.OwnerFieldName
                            + "' (slot " + ownerSlot + ") predicate childCollection to source slot "
                            + predicate.SourceSlot + " op=" + predicate.Op + " operand=" + predicate.Operand
                            + " signedOperand=" + predicate.SignedOperand);
        }

        DebugLog.Write(LogChannel.Fields, "PatchData.ResolvePredicates: childCollection "
            + pending.Count + " pending predicate(s) for collection '"
            + _collectionNamesByHandle[handle] + "' in patchLevel=" + PatchLevel);
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
            DebugLog.Write(LogChannel.Opcodes, "PatchData.GetBaseOpcode: unknown opcode name '"
                + opcodeName + "' in patchLevel=" + PatchLevel + ", returning None");
            return PatchOpcode.None;
        }

        PatchOpcode loaded = _patchOpcodeByOpcodeHandle[opcodeHandle];
        PatchOpcode versionOne = new PatchOpcode(loaded.Level, loaded.Value, 1);
        return versionOne;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeHandle
    //
    // Returns the Opcode for the given opcode in this patch.  
    //
    // Parameters:
    //   opcode  - The opcode to lookup
    //
    // Returns:
    //   The Opcode for the wire value, or Opcode.None if not in this patch.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeHandle GetOpcodeHandle(PatchOpcode opcode)
    {
        OpcodeHandle handle;
        bool found = _opcodeHandlesByPatchOpcode.TryGetValue(opcode, out handle);
        if (found == false)
        {
            return OpcodeHandle.None;
        }
        return handle;
    }

    public CollectionHandle GetOpcodeCollection(string opcodeName)
    {
        CollectionHandle collectionHandle;
        bool found = _collectionHandleByOpcodeName.TryGetValue(opcodeName, out collectionHandle);
        if (found == false)
        {
            return CollectionHandle.None;
        }

        return collectionHandle;
    }
    public CollectionHandle GetCollectionHandleFromOpcode(OpcodeHandle opcodeHandle)
    {
        return _collectionHandleByOpcodeHandle[opcodeHandle];
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeName
    //
    // Returns the opcode name for the given Opcode in this patch.
    //
    // Parameters:
    //   opcodeHandle  - The Opcode whose name to return.
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

        return "Unknown";
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeName
    //
    // Returns the opcode name for the given wire opcode value in this patch.
    // Returns "<Unknown>" when the wire value is not present in this patch's
    // opcode map.  Never returns null or an empty string.
    //
    // opcodeValue:  The wire opcode value to look up.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string GetOpcodeName(OpcodeValue opcodeValue)
    {
        string name;
        if (_opcodeNamesByValue.TryGetValue(opcodeValue, out name!))
        {
            return name;
        }
        return "<Unknown>";
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
    // Returns the FieldDefinition array for the given CollectionHandle, or null if the
    // collection has no fields loaded for this patch.
    //
    // Parameters:
    //   collection  - The CollectionHandle whose field definitions to return.
    //
    // Returns:
    //   The FieldDefinition array, or null if absent.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public FieldDefinition[]? GetFieldDefinitions(CollectionHandle collection)
    {
        return _collectionFields[collection];
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOptionalGroup
    //
    // Returns the OptionalGroup for the given PacketOptionalGroup id, or null if no group
    // with that id was loaded for this patch.
    //
    // Parameters:
    //   groupId  - The PacketOptionalGroup id, as carried on FieldDefinition.OptionalGroupId.
    //
    // Returns:
    //   The OptionalGroup, or null if no group with that id is loaded.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OptionalGroup? GetOptionalGroup(uint groupId)
    {
        OptionalGroup? group;
        if (_optionalGroupsById.TryGetValue(groupId, out group) == true)
        {
            return group;
        }

        DebugLog.Write(LogChannel.Fields, "PatchData.GetOptionalGroup: no group with id "
            + groupId + " in patchLevel=" + PatchLevel + ", returning null");
        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // IndexOfField
    //
    // Returns the SlotId of the named field within the FieldDefinition list for the
    // given Opcode.  Called by handlers at construction time to cache field indices
    // for hot-path reads from FieldBags.  Cold path.
    //
    // Parameters:
    //   opcodeHandle - The Opcode whose field definitions to search.
    //   fieldName - The field_name column value to look up.
    //
    // Returns:
    //   The SlotId of the named field, or SlotId.None if not found.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SlotId IndexOfField(OpcodeHandle opcode, string fieldName)
    {
        if (! opcode.Exists)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.IndexOfField: opcode handle '" + opcode +
                  "' in patchLevel=" + PatchLevel + "), does not exist.  Returning slot.None");
            return SlotId.None;
        }

        FieldDefinition[]? definitions = _opcodeFields[opcode];
        CollectionHandle collectionHandle = GetCollectionHandleFromOpcode(opcode);
        if (definitions == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.IndexOfField: no field definitions for opcode '" +
                _opcodeNamesByOpcodeHandle[opcode] + "' in patchLevel=" + PatchLevel + "), returning -1");
            return SlotId.None;
        }

        for (uint fieldIndex = 0; fieldIndex < definitions.Length; fieldIndex++)
        {
            if (definitions[fieldIndex].Name == fieldName)
            {
                return new SlotId(collectionHandle, fieldIndex);
            }
        }

        DebugLog.Write(LogChannel.Fields, "PatchData.IndexOfField: field '" + fieldName
            + "' not in definitions for opcode '" + _opcodeNamesByOpcodeHandle[opcode] + 
            "' in patchLevel=" + PatchLevel + "), returning -1");
        return SlotId.None;
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
    //   collection  - The CollectionHandle whose field definitions to search.
    //   fieldName   - The field_name column value to look up.
    //
    // Returns:
    //   The SlotId of the named field, or SlotId.None if not found.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SlotId IndexOfField(CollectionHandle collection, string fieldName)
    {
        FieldDefinition[]? definitions = _collectionFields[collection];
        if (definitions == null)
        {
            DebugLog.Write(LogChannel.Fields, "PatchData.IndexOfField: no field definitions for collection '"
                + _collectionNamesByHandle[collection] + "' in patchLevel=" + PatchLevel
                + ", returning None");
            return SlotId.None;
        }

        for (uint fieldIndex = 0; fieldIndex < definitions.Length; fieldIndex++)
        {
            if (definitions[fieldIndex].Name == fieldName)
            {
                return new SlotId(collection, fieldIndex);
            }
        }

        DebugLog.Write(LogChannel.Fields, "PatchData.IndexOfField: field '" + fieldName
            + "' not in definitions for collection '" + _collectionNamesByHandle[collection]
            + "' in patchLevel=" + PatchLevel + ", returning None");
        return SlotId.None;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetFieldPosition
    //
    // Returns the BitOffset of a slot.  Used for field rows whose
    // BitOffset carries position-like metadata that Extract does not consume — for
    // example, csv_token rows where BitOffset is the 1-based index of the token within
    // a comma-separated payload.
    //
    // collection:   The collection containing the field
    // slot:     The slot to query.
    //
    // Returns the slot's BitOffset.  Throws if the collection handle is invalid
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint GetFieldPosition(CollectionHandle collectionHandle, SlotId slot)
    {
        FieldDefinition[]? definitions = _collectionFields[collectionHandle];

        if (definitions == null)
        {
            throw new InvalidOperationException("PatchData.GetFieldPosition: no fields for collection " + collectionHandle);
        }

        if (slot.Index >= definitions.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(slot),
                "PatchData.GetFieldPosition: field id " + slot.Index + " out of range for collection " + collectionHandle);
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

        DebugLog.Write(LogChannel.Fields, "PatchData.ParsePredicate: parsed '" + trimmed
            + "' to source='" + sourceName + "' op=" + predicate.Op + " operand=" + predicate.Operand
            + " signedOperand=" + predicate.SignedOperand);
        return true;
    }
}

