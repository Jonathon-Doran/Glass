namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldIndex
//
// Index of a field within an opcode's FieldDefinition list, used by handlers to read
// extracted values from a FieldBag.  Wraps a uint so method signatures distinguish field
// indices from other integers; implicit conversion to uint keeps array indexing clean.
// Construction from a raw uint is explicit, keeping the origin of every index deliberate.
//
// Value uint.MaxValue is reserved to mean "not found": returned when a field name is not
// present in the opcode's definitions.  None exposes the reserved value and Exists tests
// against it; call sites check Exists before indexing, since the reserved value is not a
// valid list position.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct FieldIndex(uint Value)
{
    public static implicit operator uint(FieldIndex index) => index.Value;
    public static explicit operator FieldIndex(uint value) => new(value);
    public const uint NoneValue = uint.MaxValue;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // None
    //
    // The reserved index meaning "not found".
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static FieldIndex None
    {
        get { return new FieldIndex(NoneValue); }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Exists
    //
    // True when this index is not the reserved "not found" value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool Exists
    {
        get { return Value != NoneValue; }
    }
}