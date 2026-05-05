namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeHandle
//
// Hot-path integer handle into a PatchData's flat opcode arrays. Wraps an int so that the
// compiler can distinguish opcode handles from other integers in method signatures, while
// implicit conversion to int keeps array indexing and other int-typed call sites free of
// .Value noise. Construction from a raw int requires an explicit cast, which keeps the
// origin of every handle deliberate.
//
// Handles are assigned by PatchData at load time (0..N-1, dense) and are valid only for the
// PatchData instance that issued them. They are not interchangeable across patch levels.
///////////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct OpcodeHandle(int Value)
{
    // Implicit OpcodeHandle -> int. Lets handles index arrays and feed int parameters cleanly.
    public static implicit operator int(OpcodeHandle handle) => handle.Value;

    // Explicit int -> OpcodeHandle. Forces deliberate construction at callers that synthesize
    // a handle from a raw integer; ordinary code should obtain handles from PatchData.
    public static explicit operator OpcodeHandle(int value) => new(value);
}