namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// PatchLevel
//
// Identifies one patch by its (PatchDate, ServerType) pair.  A small immutable value
// that is cheap to construct, compare, and use as a dictionary key.  Carries no data —
// the loaded opcode map and field definitions for a patch live inside FieldExtractor's
// per-patch PatchData and are accessed by passing this identifier to extractor lookup
// methods.
//
// PatchDate is an ISO-8601 string (yyyy-MM-dd).  Lexicographic comparison sorts
// chronologically, which is why MAX(patch_date) on the database column gives the right
// answer for "latest patch."
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct PatchLevel(string PatchDate, string ServerType);