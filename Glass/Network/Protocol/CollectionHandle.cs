namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// CollectionHandle
//
// Handle into a PatchData's per-patch collection list, identifying a FieldCollection.
// Wraps a uint so method signatures and field definitions distinguish a collection handle
// from other integers; implicit conversion to uint keeps list indexing clean.  Construction
// from a raw uint is explicit, keeping the origin of every handle deliberate.
//
// Handle value uint.MaxValue is reserved to mean "no collection".  Real collections are
// assigned dense handles starting at 0.  None exposes the reserved value and Exists tests
// against it; call sites check Exists before indexing, since the reserved value is not a
// valid list position.
//
// Handles are assigned by PatchData at load time and are valid only for the PatchData
// instance that issued them.  They are not interchangeable across patch levels.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct CollectionHandle(uint Value)
{
    public static implicit operator uint(CollectionHandle handle) => handle.Value;
    public static explicit operator CollectionHandle(uint value) => new(value);
    public const uint NoneValue = uint.MaxValue;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // None
    //
    // The reserved handle meaning "no collection".
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static CollectionHandle None
    {
        get { return new CollectionHandle(NoneValue); }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Exists
    //
    // True when this handle is not the reserved "no collection" value.
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
