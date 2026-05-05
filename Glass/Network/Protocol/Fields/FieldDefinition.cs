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
// FlagMask gates optional fields.  Zero means the field is required and read unconditionally.
// Nonzero means the field is optional: the extractor ANDs FlagMask against the value of
// the field literally named "flags" earlier in the same packet, and reads only if the
// result is nonzero.  Optional fields are read sequentially with a running bit offset
// computed at extract time, so their BitOffset is meaningless and is set to zero by
// PatchData.
//
// This is plain data — no methods, no validation.  Validation happens at load time in the
// handler's load routine and at the FieldExtractor's string-table lookup.
///////////////////////////////////////////////////////////////////////////////////////////////
public struct FieldDefinition
{
    public string Name;
    public uint BitOffset;
    public uint BitLength;
    public FieldEncoding Encoding;
    public uint FlagMask;
}