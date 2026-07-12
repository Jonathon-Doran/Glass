using Glass.Core.Logging;
using Glass.Core.Memory;
using Glass.Data;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Microsoft.Data.Sqlite;

namespace Glass.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// PatchRegistry
//
// Owns the cache of PatchData instances keyed by PatchLevel.  Loads a PatchData on first
// request for its patch level and returns the cached instance on subsequent requests.
//
// One instance is constructed at application startup and held by GlassContext.  Glass
// holds the registry for the active session's patch; Inference may load several when
// comparing patches.
///////////////////////////////////////////////////////////////////////////////////////////
public class PatchRegistry
{
    private readonly List<PatchData> _loadedPatches;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CollectionEntry
    //
    // Resolves a system-unique CollectionHandle to the PatchData that owns the collection and
    // the CollectionIndex of that collection within the owner's local collection array.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private struct CollectionEntry
    {
        public PatchData Owner;
        public CollectionIndex Index;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // _collectionEntries
    //
    // Resolves a system-unique CollectionHandle to its owning PatchData and the CollectionIndex
    // of the collection within that owner.  A CollectionHandle is a position in this list,
    // assigned in registration order across every PatchData that registers a collection.
    // Append-only over the registry's lifetime.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private readonly List<CollectionEntry> _collectionEntries;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PatchRegistry (constructor)
    //
    // Builds an empty cache.  No patches are loaded here; callers invoke LoadPatchLevel or
    // LoadLatestPatchLevel before any handler that needs the patch is constructed.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public PatchRegistry()
    {
        _loadedPatches = new List<PatchData>();
        _collectionEntries = new List<CollectionEntry>();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LoadPatchLevel
    //
    // Ensures the PatchData for the given patch level is loaded and cached.  If the patch
    // is already in the cache, returns immediately.  Otherwise constructs a PatchData
    // (which runs the database queries to populate its opcode map and field definitions)
    // and stores it.
    //
    // Idempotent: calling twice for the same patch level is safe and cheap on the second
    // call.
    //
    // Parameters:
    //   patchLevel  - The patch identifier to load.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void LoadPatchLevel(PatchLevel patchLevel)
    {
        PatchData? existing = TryFindPatchData(patchLevel);
        if (existing != null)
        {
            return;
        }

        PatchData patchData = new PatchData(patchLevel);
        _loadedPatches.Add(patchData);

        DebugLog.Write(LogChannel.Network, "PatchRegistry.LoadPatchLevel: loaded patchLevel "
            + patchLevel, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LoadLatestPatchLevel
    //
    // Resolves the most recent patch_date in the database for the given server type,
    // constructs the corresponding PatchLevel identifier, ensures its PatchData is loaded
    // and cached, and returns the identifier.
    //
    // Throws InvalidOperationException via ResolveLatestPatchDate if no patches exist for
    // the server type.
    //
    // Parameters:
    //   serverType  - The server_type column value (e.g. "live", "test").
    //
    // Returns:
    //   The PatchLevel identifier for the loaded patch.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public PatchLevel LoadLatestPatchLevel(string serverType)
    {
        string latestDate = ResolveLatestPatchDate(serverType);
        PatchLevel patchLevel = new PatchLevel(latestDate, serverType);
        LoadPatchLevel(patchLevel);

        DebugLog.Write(LogChannel.Network, "PatchRegistry.LoadLatestPatchLevel: returning "
            + patchLevel, LogLevel.Trace);
        return patchLevel;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ResolveLatestPatchDate
    //
    // Queries PatchOpcode for the maximum patch_date for the given server_type.  Used by
    // LoadLatestPatchLevel.  patch_date is stored as an ISO-8601 string and sorts
    // lexicographically, which is why MAX() is correct here.
    //
    // Throws InvalidOperationException if the query returns no rows for the server type.
    //
    // Parameters:
    //   serverType  - The server_type column value to filter on.
    //
    // Returns:
    //   The most recent patch_date string for that server type.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static string ResolveLatestPatchDate(string serverType)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(patch_date)"
            + " FROM PatchOpcode"
            + " WHERE server_type = @serverType";
        cmd.Parameters.AddWithValue("@serverType", serverType);

        object? result = cmd.ExecuteScalar();
        if (result == null || result == DBNull.Value)
        {
            DebugLog.Write(LogChannel.Network, "no patches for servertype '" + serverType + '"', LogLevel.Error);
            throw new InvalidOperationException("PatchRegistry.ResolveLatestPatchDate: "
                + "no patches for serverType='" + serverType + "'");
        }

        string latestDate = (string)result;
        DebugLog.Write(LogChannel.Network, "PatchRegistry.ResolveLatestPatchDate: latest "
            + "patch_date for serverType=" + serverType + " is " + latestDate, LogLevel.Trace);
        return latestDate;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetAllPatchLevels
    //
    // Reads the distinct (patch_date, server_type) pairs from PatchOpcode and returns a
    // PatchLevel for each, ordered by patch_date then server_type.  Enumerates every patch
    // level present in the database, whether or not its PatchData is loaded; this is a
    // discovery query, not a cache read.
    //
    // Returns:
    //   The patch levels present in PatchOpcode, possibly empty if the table has no rows.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static List<PatchLevel> GetAllPatchLevels()
    {
        List<PatchLevel> patchLevels = new List<PatchLevel>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT patch_date, server_type"
            + " FROM PatchOpcode"
            + " ORDER BY patch_date, server_type";

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string patchDate = reader.GetString(0);
            string serverType = reader.GetString(1);
            PatchLevel patchLevel = new PatchLevel(patchDate, serverType);
            patchLevels.Add(patchLevel);
        }

        return patchLevels;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetGateDefinitionHandle
    //
    // Resolves a gate's name to its GateDefinitionHandle in the given patch level by locating the
    // patch's PatchData and delegating the name lookup to it.
    //
    // patchLevel:  The patch level whose gate definitions are searched.
    // gateName:    The gate name to resolve.
    //
    // Returns:     The GateDefinitionHandle for the named gate, or GateDefinitionHandle.None when no
    //              gate of that name is loaded for the patch level.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public CollectionHandle GetCollectionHandle(PatchLevel patchLevel, string collectionName)
    {
        PatchData patchData = FindPatchData(patchLevel);
        return patchData.GetCollectionHandleFromName(collectionName);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetGateDefinitionHandle
    //
    // Resolves a gate's name to its GateDefinitionHandle in the given patch level by locating the
    // patch's PatchData and delegating the name lookup to it.
    //
    // patchLevel:  The patch level whose gate definitions are searched.
    // gateName:    The gate name to resolve.
    //
    // Returns:     The GateDefinitionHandle for the named gate, or GateDefinitionHandle.None when no
    //              gate of that name is loaded for the patch level.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public GateDefinitionHandle GetGateDefinitionHandle(PatchLevel patchLevel, string gateName)
    {
        PatchData patchData = FindPatchData(patchLevel);
        return patchData.GetGateDefinitionHandleFromName(gateName);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TryFindPatchData
    //
    // Returns the loaded PatchData matching the given patch level, or null if no loaded
    // patch matches.  Linear scan over _loadedPatches; the registry typically holds one
    // or two patches at runtime, so the scan is faster than a dictionary lookup keyed by
    // struct (which would hash the PatchLevel and compare strings on every call).
    //
    // Used by LoadPatchLevel to detect the already-loaded case without throwing, and by
    // FindPatchData as the underlying scan.
    //
    // Parameters:
    //   patchLevel  - The patch identifier to look up.
    //
    // Returns:
    //   The loaded PatchData for the given patch level, or null if not loaded.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private PatchData? TryFindPatchData(PatchLevel patchLevel)
    {
        for (int patchIndex = 0; patchIndex < _loadedPatches.Count; patchIndex++)
        {
            PatchData candidate = _loadedPatches[patchIndex];
            if (candidate.PatchLevel.Equals(patchLevel) == true)
            {
                return candidate;
            }
        }

        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindPatchData
    //
    // Returns the loaded PatchData matching the given patch level.  Wraps TryFindPatchData
    // and throws if the lookup fails — the right shape for hot-path callers that already
    // know the patch should be loaded by virtue of how they were constructed.
    //
    // Throws InvalidOperationException if no loaded PatchData has the given patch level.
    // Callers must invoke LoadPatchLevel (or LoadLatestPatchLevel) for the patch before
    // any code that reaches this method runs.
    //
    // Parameters:
    //   patchLevel  - The patch identifier to look up.
    //
    // Returns:
    //   The loaded PatchData for the given patch level.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private PatchData FindPatchData(PatchLevel patchLevel)
    {
        PatchData? patchData = TryFindPatchData(patchLevel);
        if (patchData == null)
        {
            throw new InvalidOperationException("PatchRegistry.FindPatchData: patchLevel "
                + patchLevel + " is not loaded");
        }

        return patchData;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TryGetGateDefinition
    //
    // Resolves the GateDefinition for the given GateDefinitionHandle in the given patch
    // level, by value.  Looks the patch up in the loaded set and delegates to its PatchData.
    // A None handle is rejected here rather than delegated, since PatchData has no
    // representation for "no gate".
    //
    // Throws InvalidOperationException if the patch level is not loaded.
    //
    // Parameters:
    //   patchLevel      - The patch identifier.  Must already be loaded.
    //   gateHandle      - The gate to look up.
    //   gateDefinition  - Receives the GateDefinition on success; default on failure.
    //
    // Returns:
    //   true when the handle resolved to a gate; false when the handle is None.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool TryGetGateDefinition(PatchLevel patchLevel, GateDefinitionHandle gateHandle,
        out GateDefinition gateDefinition)
    {
        if (gateHandle.Exists == false)
        {
            DebugLog.Write(LogChannel.Fields, "PatchRegistry.TryGetGateDefinition: "
                + "GateDefinitionHandle.None passed for patchLevel " + patchLevel, LogLevel.Warn);
            gateDefinition = default;
            return false;
        }

        PatchData patchData = FindPatchData(patchLevel);
        gateDefinition = patchData.GetGateDefinition(gateHandle);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeGateDefinition
    //
    // Resolves the given opcode's top-level gate directly to its GateDefinitionHandle, by value.
    //
    // An opcode that is not in this patch, or whose row named no loaded gate, is schema
    // corruption and brings the process down with the evidence preserved.
    //
    // Parameters:
    //   opcode  - The opcode whose top-level gate to resolve.
    //
    // Returns:
    //   The handle of the opcode's top-level gate definition.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public GateDefinitionHandle GetOpcodeGateDefinition(PatchOpcode patchOpcode)
    {
        if (patchOpcode.Exists == false)
        {
            string failure = "PatchRegistry.GetOpcodeGateDefinition: PatchOpcode.None passed";
            DebugLog.Write(LogChannel.Fields, failure + Environment.NewLine + Environment.StackTrace, LogLevel.Error);
            Environment.FailFast(failure);
        }

        PatchData patchData = FindPatchData(patchOpcode.Level);
        return patchData.GetOpcodeGate(patchOpcode);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RegisterCollection
    //
    // Mints a system-unique CollectionHandle for a collection owned by the given PatchData at
    // the given local CollectionIndex, recording the owner and index for later resolution.
    //
    // Parameters:
    //   owner  - The PatchData that owns the collection.
    //   index  - The collection's CollectionIndex within the owner's local collection array.
    //
    // Returns:
    //   The newly minted CollectionHandle.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public CollectionHandle RegisterCollection(PatchData owner, CollectionIndex index)
    {
        CollectionEntry entry;
        entry.Owner = owner;
        entry.Index = index;
        _collectionEntries.Add(entry);

        CollectionHandle handle = (CollectionHandle)(uint)(_collectionEntries.Count - 1);
        return handle;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeCollection
    //
    // A CollectionHandle defines a set of named fields in the given patch level.  This returns
    // the top-level collection for an opcode.  This is where packets begin extraction.
    //
    // Throws InvalidOperationException if the patch level is not loaded.
    //
    // Parameters:
    //   patchOpcode:  The opcode to query
    //
    // Returns:
    //   The CollectionHandle for the named collection, or CollectionHandle.None if absent.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public CollectionHandle GetOpcodeCollection(PatchOpcode patchOpcode)
    {
        PatchData patchData = FindPatchData(patchOpcode.Level);

        return patchData.GetOpcodeCollection(patchOpcode);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeName
    //
    // Returns the logical opcode name for the given wire opcode value in the given patch
    // level.  Looks up the patch in the loaded set and delegates to the PatchData.
    //
    // Throws InvalidOperationException if the patch level is not loaded.
    //
    // Returns "Unknown" if the wire value is not in the patch.
    //
    // Parameters:
    //   patchLevel   - The patch identifier.  Must already be loaded.
    //   opcodeValue  - The wire opcode value (e.g. 0x6FA1).
    //
    // Returns:
    //   The opcode name (e.g. "OP_PlayerProfile"), or "Unknown" if not in the patch.
    ///////////////////////////////////////////////////////////////////////////////////////////

    public string GetOpcodeName(PatchOpcode patchOpcode)
    {
        PatchData patchData = FindPatchData(patchOpcode.Level);
        return patchData.GetOpcodeName(patchOpcode);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetBaseOpcode
    //
    // Returns the base PatchOpcode for the given opcode name in the given patch level, or
    // PatchOpcode.None if the name is not present.  Looks up the patch in the loaded set and
    // delegates to the PatchData.
    //
    // Note:  Callers typically obtain the base and modify the version number separately.
    //
    // Throws InvalidOperationException if the patch level is not loaded.
    //
    // Parameters:
    //   patchLevel  - The patch identifier.  Must already be loaded.
    //   opcodeName  - The logical name (e.g. "OP_PlayerProfile").
    //
    // Returns:
    //   The base PatchOpcode for the named opcode, or PatchOpcode.None if not in the patch.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode GetBaseOpcode(PatchLevel patchLevel, string opcodeName)
    {
        PatchData patchData = FindPatchData(patchLevel);
        return patchData.GetBaseOpcode(opcodeName);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetCollectionName
    //
    // Returns the number of opcodes loaded for the given patch level.  Looks up the patch
    // in the loaded set and delegates to the PatchData.
    //
    // Throws InvalidOperationException if the patch level is not loaded.
    //
    // Parameters:
    //   patchLevel  - The patch identifier.  Must already be loaded.
    //
    // Returns:
    //   The number of opcodes in the patch.
    ///////////////////////////////////////////////////////////////////////////////////////////

    public string GetCollectionName(CollectionHandle collectionHandle)
    {
        if (collectionHandle.Exists == false || collectionHandle.Value >= _collectionEntries.Count)
        {
            throw new InvalidOperationException("PatchRegistry.GetCollectionName: invalid collection handle " + collectionHandle);

        }

        CollectionIndex index = _collectionEntries[(int)collectionHandle.Value].Index;

        return _collectionEntries[(int) collectionHandle.Value].Owner.GetCollectionNameFromIndex(index);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetSlotCount
    //
    // Returns the slot capacity a bag for the given collection requires.  Looks up the
    // patch in the loaded set and delegates to the PatchData.
    //
    // Throws InvalidOperationException if the patch level is not loaded.
    //
    // Parameters:
    //   collectionHandle  - The collection to query.
    //
    // Returns:
    //   The collection's slot count.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public ushort GetSlotCount(CollectionHandle collectionHandle)
    {
        if (collectionHandle.Exists == false || collectionHandle.Value >= _collectionEntries.Count)
        {
            throw new InvalidOperationException("PatchRegistry.GetCollectionName: invalid collection handle " + collectionHandle);

        }

        CollectionIndex index = _collectionEntries[(int)collectionHandle.Value].Index;
        return _collectionEntries[(int)collectionHandle.Value].Owner.GetSlotCount(index);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetArenaCapacity
    //
    // Returns the recommended arena capacity in bytes for a bag for the given collection.
    // Looks up the patch in the loaded set and delegates to the PatchData.
    //
    // Throws InvalidOperationException if the patch level is not loaded.
    //
    // Parameters:
    //   collectionHandle  - The collection to query.
    //
    // Returns:
    //   The collection's recommended arena capacity.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public uint GetArenaCapacity(PatchLevel patchLevel, CollectionHandle collectionHandle)
    {
        if (collectionHandle.Exists == false || collectionHandle.Value >= _collectionEntries.Count)
        {
            throw new InvalidOperationException("PatchRegistry.GetCollectionName: invalid collection handle " + collectionHandle);

        }

        CollectionIndex index = _collectionEntries[(int)collectionHandle.Value].Index;
        return _collectionEntries[(int)collectionHandle.Value].Owner.GetArenaCapacity(index);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeCount
    //
    // Returns the number of opcodes loaded for the given patch level.  Looks up the patch
    // in the loaded set and delegates to the PatchData.
    //
    // Throws InvalidOperationException if the patch level is not loaded.
    //
    // Parameters:
    //   patchLevel  - The patch identifier.  Must already be loaded.
    //
    // Returns:
    //   The number of opcodes in the patch.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int GetOpcodeCount(PatchLevel patchLevel)
    {
        PatchData patchData = FindPatchData(patchLevel);
        return patchData.GetOpcodeCount();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetEncodingStrings
    //
    // Returns the encoding strings recognized by the given patch level's PatchData.
    // Used by the patch data editor to populate encoding dropdowns.
    //
    // Throws InvalidOperationException if the patch level is not loaded.
    //
    // Parameters:
    //   patchLevel  - The patch identifier.  Must already be loaded.
    //
    // Returns:
    //   The encoding strings recognized by the patch's PatchData.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string[] GetEncodingStrings(PatchLevel patchLevel)
    {
        PatchData patchData = FindPatchData(patchLevel);
        return patchData.GetEncodingStrings();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // IndexOfField
    //
    // Returns the SlotId of the named field within the CollectionHandle's field
    // definitions in the given patch level.
    //
    // Throws InvalidOperationException if the patch level is not loaded.
    //
    // Parameters:
    //   patchLevel  - The patch identifier.  Must already be loaded.
    //   collection  - The CollectionHandle whose field definitions to search.
    //   fieldName   - The field_name column value to look up.
    //
    // Returns:
    //   The SlotId of the named field, or SlotId.None if not found.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SlotId IndexOfField(CollectionHandle collectionHandle, string fieldName)
    {
        if (collectionHandle.Exists == false || collectionHandle.Value >= _collectionEntries.Count)
        {
            throw new InvalidOperationException("PatchRegistry.GetCollectionName: invalid collection handle " + collectionHandle);

        }

        CollectionIndex index = _collectionEntries[(int)collectionHandle.Value].Index;
        return _collectionEntries[(int)collectionHandle.Value].Owner.IndexOfField(index, fieldName);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetFields
    //
    // Returns the FieldDefinition array for the given CollectionHandle in the given patch
    // level.  Looks up the patch in the loaded set and delegates to the PatchData.
    //
    // Throws InvalidOperationException if the patch level is not loaded.
    //
    // Returns null if the collection has no fields loaded for this patch.
    //
    // Parameters:
    //   collection  - The CollectionHandle whose field definitions to return.
    //
    // Returns:
    //   The FieldDefinition array, or null if absent.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public FieldDefinition[]? GetFields(CollectionHandle collectionHandle)
    {
        if (collectionHandle.Exists == false || collectionHandle.Value >= _collectionEntries.Count)
        {
            throw new InvalidOperationException("PatchRegistry.GetCollectionName: invalid collection handle " + collectionHandle);

        }

        CollectionIndex index = _collectionEntries[(int)collectionHandle.Value].Index;
        return _collectionEntries[(int)collectionHandle.Value].Owner.GetFieldDefinitions(index);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetFieldPosition
    //
    // Returns the position value stored on a field's definition (its BitOffset).  Used by
    // handlers that need a position-like value from a field row that ExtractCollection does not
    // process — for example, csv_token rows where BitOffset carries the 1-based index of
    // the token within a comma-separated payload.
    //
    // patchLevel:  The patch level whose PatchData holds the field definitions.
    // collection:  The collection containing the field
    // slot:        The slot queried.
    //
    // Returns the field's BitOffset as an int, or -1 if the patch level, handle, or
    // field id cannot be resolved.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint GetFieldPosition(CollectionHandle collectionHandle, SlotId slot)
    {
        if (collectionHandle.Exists == false || collectionHandle.Value >= _collectionEntries.Count)
        {
            throw new InvalidOperationException("PatchRegistry.GetFieldPosition: invalid collection handle " + collectionHandle);
        }

        CollectionIndex index = _collectionEntries[(int)collectionHandle.Value].Index;
        return _collectionEntries[(int)collectionHandle.Value].Owner.GetFieldPosition(index, slot);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LogPoolStatistics
    //
    // Writes the bag pool's lifetime counters to the log by delegating to the pool's
    // LogStatistics.  The pool is private to this registry, so this is the only path by
    // which its counters reach the log.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void LogPoolStatistics()
    {
    }
}
