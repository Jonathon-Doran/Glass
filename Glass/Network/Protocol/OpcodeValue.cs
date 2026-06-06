namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeValue
//
// Hot-path handle into a PatchData's flat opcode arrays. Wraps a uint so that the compiler
// can distinguish opcode handles from other integers in method signatures, while implicit
// conversion to uint keeps array indexing and other uint-typed call sites free of .Value
// noise. Construction from a raw uint requires an explicit cast, which keeps the origin of
// every handle deliberate.
//
// Value uint.MaxValue is reserved to mean "no opcode": returned when an opcode name or wire
// value is not present in this patch.  None exposes the reserved value and Exists tests
// against it; call sites check Exists before indexing, since the reserved value is not a
// valid array position.
//
// Handles are assigned by PatchData at load time (0..N-1, dense) and are valid only for the
// PatchData instance that issued them. They are not interchangeable across patch levels.
///////////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct OpcodeValue(ushort Value)
{
    public static implicit operator uint(OpcodeValue handle) => handle.Value;
    public static explicit operator OpcodeValue(ushort value) => new(value);
    public static explicit operator OpcodeValue(int value) => new((ushort)(value & 0xFFFF));
    public const ushort NoneValue = ushort.MaxValue;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // None
    //
    // The reserved handle meaning "no opcode".
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static OpcodeValue None
    {
        get { return new OpcodeValue(NoneValue); }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Exists
    //
    // True when this handle is not the reserved "no opcode" value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool Exists
    {
        get { return Value != NoneValue; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CompareTo
    //
    // Orders this opcode value against another by ascending wire value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int CompareTo(OpcodeValue other)
    {
        return Value.CompareTo(other.Value);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ToString
    //
    // Renders the opcode value as a four-digit hexadecimal string with a "0x" prefix.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public override string ToString()
    {
        return "0x" + Value.ToString("X4");
    }
}