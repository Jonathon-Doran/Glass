namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// BagHandle
//
// Handle into a GateTree's bag array, identifying one FieldBag — the decoded fields of
// one collection instance within a single extraction.
//
// Wraps a uint so method signatures and field definitions distinguish a handle from
// other integers; implicit conversion to uint keeps array indexing clean.  Construction from
// a raw uint is explicit, keeping the origin of every handle deliberate.
//
// Handle value uint.MaxValue is reserved to mean "no bag".
//
// Handles are assigned by a GateTree as bags are created during an extraction and are
// valid only until that GateTree is reset.  They are not interchangeable across
// extractions.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct BagHandle(uint Value)
{
    public static implicit operator uint(BagHandle handle) => handle.Value;
    public static explicit operator BagHandle(uint value) => new(value);
    public const uint NoneValue = uint.MaxValue;
    ///////////////////////////////////////////////////////////////////////////////////////////
    // None
    //
    // The reserved handle meaning "no bag".
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static BagHandle None
    {
        get { return new BagHandle(NoneValue); }
    }
    ///////////////////////////////////////////////////////////////////////////////////////////
    // Exists
    //
    // True when this bag exists.
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
