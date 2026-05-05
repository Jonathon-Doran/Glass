namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// PatchOpcode
//
// Identifies one opcode within one patch.  Carries the PatchLevel, the wire opcode value,
// and the version.  Replaces the older OpcodeId, which carried only (Opcode, Version) and
// relied on the surrounding PatchData instance to imply the PatchLevel.
//
// Including PatchLevel in the identifier itself lets cold-path code carry an opcode
// identity across boundaries — log lines, cross-patch collections (Inference comparing
// observations across patches), debugger watches — without needing a separate PatchLevel
// parameter that could drift out of sync.
//
// The name mirrors the PatchOpcode database table, which carries the same conceptual
// content per row: (patch_date, server_type, opcode_value, version) plus opcode_name.
// The struct is the in-memory identity; the row is the persistence form.  Lookups inside
// PatchData use this struct as a dictionary key and benefit from the value-equality and
// hashing record struct provides.
//
// Cold path only.  Hot-path access uses OpcodeHandle, which PatchData issues when an
// opcode is registered.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct PatchOpcode(PatchLevel Level, ushort Opcode, int Version)
{
    // Convenience constructor: most opcodes have only one version (Version = 1).  This
    // overload preserves the old OpcodeId(opcode) ergonomic so two-arg construction
    // sites do not need to spell out the default.
    public PatchOpcode(PatchLevel level, ushort opcode) : this(level, opcode, 1)
    {
    }
}