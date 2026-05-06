using Glass.Core.Logging;
using Glass.Data;
using Microsoft.Data.Sqlite;
using Glass.Network.Protocol;

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
    private readonly Dictionary<PatchLevel, PatchData> _loadedPatches;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PatchRegistry (constructor)
    //
    // Builds an empty cache.  No patches are loaded here; callers invoke LoadPatchLevel or
    // LoadLatestPatchLevel before any handler that needs the patch is constructed.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public PatchRegistry()
    {
        _loadedPatches = new Dictionary<PatchLevel, PatchData>();
        DebugLog.Write(LogChannel.Network, "PatchRegistry ctor: empty cache");
    }

    public PatchRegistry(PatchLevel patchLevel)
        : this()
    {
        LoadPatchLevel(patchLevel);
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
        if (_loadedPatches.ContainsKey(patchLevel) == true)
        {
            DebugLog.Write(LogChannel.Network, "PatchRegistry.LoadPatchLevel: patchLevel "
                + patchLevel + " already loaded, returning");
            return;
        }

        PatchData patchData = new PatchData(patchLevel);
        _loadedPatches[patchLevel] = patchData;

        DebugLog.Write(LogChannel.Network, "PatchRegistry.LoadPatchLevel: loaded patchLevel "
            + patchLevel);
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
        DebugLog.Write(LogChannel.Network, "PatchRegistry.LoadLatestPatchLevel: serverType="
            + serverType);

        string latestDate = ResolveLatestPatchDate(serverType);
        PatchLevel patchLevel = new PatchLevel(latestDate, serverType);
        LoadPatchLevel(patchLevel);

        DebugLog.Write(LogChannel.Network, "PatchRegistry.LoadLatestPatchLevel: returning "
            + patchLevel);
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
            throw new InvalidOperationException("PatchRegistry.ResolveLatestPatchDate: "
                + "no patches for serverType='" + serverType + "'");
        }

        string latestDate = (string)result;
        DebugLog.Write(LogChannel.Network, "PatchRegistry.ResolveLatestPatchDate: latest "
            + "patch_date for serverType=" + serverType + " is " + latestDate);
        return latestDate;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeValue
    //
    // Returns the wire opcode value for the given logical name in the given patch level.
    // Looks up the patch in the loaded set and delegates to the PatchData.
    //
    // Throws InvalidOperationException if the patch level is not loaded.  Callers must
    // invoke LoadPatchLevel (or LoadLatestPatchLevel) for the patch before any code that
    // reaches this method runs.
    //
    // Returns 0 if the opcode name is unknown to the patch.  Handlers check for 0 and
    // skip their own dispatch registration if their opcode is missing — the expected
    // case when a patch genuinely lacks an opcode the handler knows about.
    //
    // Parameters:
    //   patchLevel  - The patch identifier.  Must already be loaded.
    //   opcodeName  - The logical name (e.g. "OP_PlayerProfile").
    //
    // Returns:
    //   The opcode value from the appropriate patch.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public ushort GetOpcodeValue(PatchLevel patchLevel, string opcodeName)
    {
        PatchData patchData;
        bool found = _loadedPatches.TryGetValue(patchLevel, out patchData!);
        if (found == false)
        {
            throw new InvalidOperationException("PatchRegistry.GetOpcodeValue: patchLevel "
                + patchLevel + " is not loaded");
        }

        return patchData.GetOpcodeValue(opcodeName);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetOpcodeHandle
    //
    // Returns the OpcodeHandle for the given opcode name in the given patch level.  Looks
    // up the patch in the loaded set and delegates to the PatchData.
    //
    // Throws InvalidOperationException if the patch level is not loaded.  Callers must
    // invoke LoadPatchLevel (or LoadLatestPatchLevel) for the patch before any code that
    // reaches this method runs.
    //
    // Returns (OpcodeHandle)(-1) if the opcode name is unknown to the patch.
    //
    // Parameters:
    //   patchLevel  - The patch identifier.  Must already be loaded.
    //   opcodeName  - The logical name (e.g. "OP_PlayerProfile").
    //
    // Returns:
    //   The OpcodeHandle for the named opcode, or (OpcodeHandle)(-1) if not in the patch.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeHandle GetOpcodeHandle(PatchLevel patchLevel, string opcodeName)
    {
        PatchData patchData;
        bool found = _loadedPatches.TryGetValue(patchLevel, out patchData!);
        if (found == false)
        {
            throw new InvalidOperationException("PatchRegistry.GetOpcodeHandle: patchLevel "
                + patchLevel + " is not loaded");
        }

        return patchData.GetOpcodeHandle(opcodeName);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // IndexOfField
    //
    // Returns the FieldIndex of the named field within the OpcodeHandle's field
    // definitions in the given patch level.  Looks up the patch in the loaded set and
    // delegates to the PatchData.
    //
    // Throws InvalidOperationException if the patch level is not loaded.
    //
    // Returns (FieldIndex)(-1) if the named field is not present.
    //
    // Parameters:
    //   patchLevel  - The patch identifier.  Must already be loaded.
    //   handle      - The OpcodeHandle whose field definitions to search.
    //   fieldName   - The field_name column value to look up.
    //
    // Returns:
    //   The FieldIndex of the named field, or (FieldIndex)(-1) if not found.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public FieldIndex IndexOfField(PatchLevel patchLevel, OpcodeHandle handle, string fieldName)
    {
        PatchData patchData;
        bool found = _loadedPatches.TryGetValue(patchLevel, out patchData!);
        if (found == false)
        {
            throw new InvalidOperationException("PatchRegistry.IndexOfField: patchLevel "
                + patchLevel + " is not loaded");
        }

        return patchData.IndexOfField(handle, fieldName);
    }
}
