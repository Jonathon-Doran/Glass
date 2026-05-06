namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldIndex
//
// Index of a field within an opcode's FieldDefinition list, used by handlers to read
// extracted values from a FieldBag.  Wraps an int so method signatures distinguish field
// indices from other integers; implicit conversion to int keeps array indexing clean.
// Construction from a raw int is explicit, keeping the origin of every index deliberate.
//
// (FieldIndex)(-1) is the not-found sentinel returned when a field name is not present in
// the opcode's definitions.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct FieldIndex(int Value)
{
    public static implicit operator int(FieldIndex index) => index.Value;
    public static explicit operator FieldIndex(int value) => new(value);
}