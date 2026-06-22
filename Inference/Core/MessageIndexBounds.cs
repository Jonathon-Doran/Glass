namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// MessageIndexBounds
//
// The lowest and highest message index present in a row collection, with a flag marking the
// empty case where neither bound is meaningful.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct MessageIndexBounds
{
    public uint Lowest { get; }
    public uint Highest { get; }
    public bool HasRows { get; }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MessageIndexBounds (constructor)
    //
    // Captures the lowest and highest present message index and whether any rows were present.
    //
    // lowest:   The lowest message index present, or 0 when no rows.
    // highest:  The highest message index present, or 0 when no rows.
    // hasRows:  True when at least one row contributed to the bounds.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public MessageIndexBounds(uint lowest, uint highest, bool hasRows)
    {
        Lowest = lowest;
        Highest = highest;
        HasRows = hasRows;
    }
}