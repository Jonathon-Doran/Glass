namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// MultiplicityHandle
//
// Handle into a PatchData's per-patch multiplicity array, identifying the Multiplicity of
// a child collection.
//
// Wraps a uint so method signatures and field definitions distinguish a handle from
// other integers; implicit conversion to uint keeps array indexing clean.  Construction from
// a raw uint is explicit, keeping the origin of every handle deliberate.
//
// Handle value uint.MaxValue is reserved to mean "no multiplicity".
//
// Handles are assigned by PatchData at load time and are valid only for the PatchData
// instance that issued them.  They are not interchangeable across patch levels.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct MultiplicityHandle(uint Value)
{
    public static implicit operator uint(MultiplicityHandle handle) => handle.Value;
    public static explicit operator MultiplicityHandle(uint value) => new(value);
    public const uint NoneValue = uint.MaxValue;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // None
    //
    // The reserved handle meaning "no multiplicity".
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static MultiplicityHandle None
    {
        get { return new MultiplicityHandle(NoneValue); }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Exists
    //
    // True when this handle exists.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool Exists
    {
        get { return Value != NoneValue; }
    }
}
