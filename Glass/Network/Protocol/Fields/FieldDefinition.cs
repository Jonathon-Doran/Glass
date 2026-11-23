namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldDefinition
//
// One row of metadata describing a single field within a packet's payload.  Handlers load
// these once at construction time from the PacketField table and pass an array of them to
// FieldExtractor.ExtractCollection on every packet.
//
// Divisor is used to scale integer values to create sign-magnitude floats.
//
// RelativeToSlot, when non-null, names another field's slot index.  This field's BitOffset
// is then measured from the end of that slot's payload rather than from packet start.  Null
// means absolute (anchored at packet start).  Resolution from the database string to the
// slot index happens in a second pass during load.
//
// Sequence is the display order of this field within its collection.
//
// Gate, when it Exists, is the handle of the gate definition this field decodes through; the gate
// names the child collection and the multiplicity rule.  GateDefinitionHandle.None means the field
// is an ordinary scalar, not a gate reference.
//
// Predicate is the field-level presence condition.  When its Steps is null the field is
// unconditionally present and the extractor decodes it without evaluating any condition.
// Otherwise the field is decoded only when the program's comparisons, combined by its And, Or,
// and Not steps, hold against earlier fields' values in the same collection; otherwise the slot
// is left empty.  Each Comparison step's SourceSlot is resolved from a field name to a slot
// index at load time, the same way RelativeToSlot and gate count fields are resolved.
//
// ByteCount is the fixed size in bytes of a Blob-typed field.  Zero for all other field types.
//
// This is plain data — no methods, no validation.  Validation happens at load time in the
// handler's load routine and at the FieldExtractor's string-table lookup.
///////////////////////////////////////////////////////////////////////////////////////////////
public struct FieldDefinition
{
    public string Name;
    public uint BitOffset;
    public uint BitLength;
    public float Divisor;
    public uint? RelativeToSlot;
    public FieldEncoding Encoding;
    public uint ByteCount;
    public GateDefinitionHandle Gate;
    public PredicateProgram Predicate;
    public uint Sequence;
}