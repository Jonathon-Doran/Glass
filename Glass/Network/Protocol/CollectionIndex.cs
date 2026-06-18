namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// CollectionIndex
//
// Local subscript into a single PatchData's collection array, identifying a FieldCollection
// within that one PatchData.  Wraps a uint so method signatures and field definitions
// distinguish a local collection index from other integers and from a CollectionHandle;
// implicit conversion to uint keeps array indexing clean.  Construction from a raw uint is
// explicit, keeping the origin of every index deliberate.
//
// Index value uint.MaxValue is reserved to mean "no collection".  Real collections are
// assigned dense indices starting at 0.  None exposes the reserved value and Exists tests
// against it; call sites check Exists before indexing, since the reserved value is not a
// valid array position.
//
// A CollectionIndex is valid only for the PatchData instance that issued it.  It is known
// only to PatchData and PatchRegistry; it never crosses a public PatchData boundary as a
// global identity.  The system-unique identity is CollectionHandle, which the registry maps
// to a (PatchData, CollectionIndex) pair.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct CollectionIndex(uint Value)
{
    public static implicit operator uint(CollectionIndex index) => index.Value;
    public static explicit operator CollectionIndex(uint value) => new(value);
    public const uint NoneValue = uint.MaxValue;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // None
    //
    // The reserved index meaning "no collection".
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static CollectionIndex None
    {
        get { return new CollectionIndex(NoneValue); }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Exists
    //
    // True when this index is not the reserved "no collection" value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool Exists
    {
        get { return Value != NoneValue; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ToString
    //
    // Renders the index as its numeric value, or "None" for the sentinel.
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