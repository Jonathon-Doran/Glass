namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// GateHandle
//
// Handle into a PatchData's per-patch gate array, identifying the Multiplicity a field decodes.
// Wraps a uint so method signatures and field definitions distinguish a gate handle from
// other integers; implicit conversion to uint keeps array indexing clean.  Construction from
// a raw uint is explicit, keeping the origin of every handle deliberate.
//
// Handle value uint.MaxValue is reserved to mean "no gate": a leaf field carries a GateHandle
// whose Value is uint.MaxValue.  Real gates are assigned dense handles starting at 0.  None
// exposes the reserved value and Exists tests against it; call sites check Exists before
// indexing, since the reserved value is not a valid array position.
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
    // The reserved handle meaning "no gate".  A leaf field carries this value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static GateHandle None
    {
        get { return new GateHandle(NoneValue); }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Exists
    //
    // True when this handle is not the reserved "no gate" value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool Exists
    {
        get { return Value != NoneValue; }
    }
}
