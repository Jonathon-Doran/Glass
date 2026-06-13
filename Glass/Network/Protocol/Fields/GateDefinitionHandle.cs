namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// GateDefinitionHandle
//
// Handle into a PatchData's per-patch gate definition array, identifying the Gate of
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
public readonly record struct GateDefinitionHandle(uint Value)
{
    public static implicit operator uint(GateDefinitionHandle handle) => handle.Value;
    public static explicit operator GateDefinitionHandle(uint value) => new(value);
    public const uint NoneValue = uint.MaxValue;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // None
    //
    // The reserved handle meaning "no gate".
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static GateDefinitionHandle None
    {
        get { return new GateDefinitionHandle(NoneValue); }
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

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ToString
    //
    // Renders the handle as its numeric value, or "None" for the sentinel.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public override string ToString()
    {
        if (Exists == false)
        {
            return "None";
        }
        return Value.ToString();
    }
}
