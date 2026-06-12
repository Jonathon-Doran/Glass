namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeHandle
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
public readonly record struct OpcodeHandle(uint Value)
{
    public static implicit operator uint(OpcodeHandle handle) => handle.Value;
    public static explicit operator OpcodeHandle(uint value) => new(value);
    public const uint NoneValue = uint.MaxValue;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // None
    //
    // The reserved handle meaning "no opcode".
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static OpcodeHandle None
    {
        get { return new OpcodeHandle(NoneValue); }
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

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ToString
    //
    // Renders the handle as its numeric value, or "None" for the sentinel.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override string ToString()
    {
        if (Exists == false)
        {
            return "None";
        }
        return Value.ToString();
    }
}