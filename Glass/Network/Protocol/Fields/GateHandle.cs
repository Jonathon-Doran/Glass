namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// GateHandle
//
// Handle into a PatchData's per-patch gate array, identifying the Gate of
// a child collection.  Gates take us from one collection to another.
//
// Wraps a uint so method signatures and field definitions distinguish a handle from
// other integers; implicit conversion to uint keeps array indexing clean.  Construction from
// a raw uint is explicit, keeping the origin of every handle deliberate.
//
// Handle value uint.MaxValue is reserved to mean "no gate".
//
// Handles are assigned by PatchData at load time and are valid only for the PatchData
// instance that issued them.  They are not interchangeable across patch levels.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct GateHandle(uint Value)
{
    public static implicit operator uint(GateHandle handle) => handle.Value;
    public static explicit operator GateHandle(uint value) => new(value);
    public const uint NoneValue = uint.MaxValue;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // None
    //
    // The reserved handle meaning "no gate".
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static GateHandle None
    {
        get { return new GateHandle(NoneValue); }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Exists
    //
    // True when this gate exists.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool Exists
    {
        get { return Value != NoneValue; }
    }
}
