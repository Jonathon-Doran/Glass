namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// GateHandle
//
// Handle into a FieldExtractor's gate list, identifying one Gate — a node in the tree of
// collection instances built during a single extraction.
//
// Wraps a uint so method signatures and field definitions distinguish a handle from
// other integers; implicit conversion to uint keeps list indexing clean.  Construction from
// a raw uint is explicit, keeping the origin of every handle deliberate.
//
// Handle value uint.MaxValue is reserved to mean "no gate".
//
// Handles are assigned by a FieldExtractor as gates are created during an extraction and are
// valid only until that extraction's gate list is reset.  They are not interchangeable across
// extractions.
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
