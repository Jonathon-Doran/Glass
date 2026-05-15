namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldDefinition
//
// One row of metadata describing a single field within a packet's payload.  Handlers load
// these once at construction time from the PacketField table and pass an array of them to
// FieldExtractor.Extract on every packet.
//
// Encoding is stored as the resolved FieldEncoding enum, not the database string.  The
// resolution happens once during the handler's load routine via FieldExtractor.GetEncodingId.
// The hot path dispatches on the enum directly with no string comparison.
//
// BitOffset and BitLength are unsigned because they are non-negative by definition.  The
// database column type (SQLite INTEGER, signed under the hood) is cast to uint at load
// time in the handler's load routine.  Within the extractor, no defensive checks for
// negative values are needed.
//
// Divisor is used to scale integer values to create sign-magnitude floats.
//
// RelativeToSlot, when non-null, names another field's slot index.  This field's BitOffset
// is then measured from the end of that slot's payload rather than from packet start.  Null
// means absolute (anchored at packet start).  Resolution from the database string to the
// slot index happens in a second pass during load.
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
}