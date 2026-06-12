namespace Glass.Network.Protocol;

using Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// SlotId
//
// Identifies a field slot.  Collection names the FieldCollection the slot belongs to, or
// CollectionHandle.None to mean the current collection.  Index is the slot position within
// that collection's field list.
//
// Index value uint.MaxValue is reserved to mean "not found".  None exposes that reserved
// value and Exists tests against it.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct SlotId
{
    public const uint NoneValue = uint.MaxValue;

    public readonly CollectionHandle Collection;
    public readonly uint Index;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SlotId (constructor)
    //
    // Builds a slot id for the given collection and index.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SlotId(CollectionHandle collection, uint index)
    {
        Collection = collection;
        Index = index;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // None
    //
    // The reserved slot id meaning "not found".
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static SlotId None
    {
        get { return new SlotId(CollectionHandle.None, NoneValue); }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Exists
    //
    // True when this slot id is not the reserved "not found" value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool Exists
    {
        get { return Index != NoneValue; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ToString
    //
    // Renders the slot as "collection:index", or "None" for the sentinel.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public override string ToString()
    {
        if (Exists == false)
        {
            return "None";
        }
        return Collection.ToString() + ":" + Index;
    }
}