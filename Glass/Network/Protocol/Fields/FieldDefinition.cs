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
// Predicate is the field-level presence condition.  When its Op is PredicateOp.None the field is
// unconditionally present and the extractor decodes it without evaluating any condition.  When Op
// is any other value the field is decoded only when the predicate holds against an earlier field's
// value in the same collection; otherwise the slot is left empty.  The predicate's SourceSlot is
// resolved from a field name to a slot index at load time, the same way RelativeToSlot and gate
// count fields are resolved.
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
    public uint? OptionalGroupId;
    public GateDefinitionHandle Gate;
    public FieldPredicate Predicate;
    public uint Sequence;
}