namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// ByteRange
//
// A contiguous span of payload bytes given as a half-open interval [Start, End): Start is the
// first byte, End is one past the last.  An empty span has Start == End.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct ByteRange
{
    public readonly uint Start;
    public readonly uint End;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ByteRange (constructor)
    //
    // Stores the half-open interval bounds.
    //
    // start:  The first byte offset.
    // end:    One past the last byte offset.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public ByteRange(uint start, uint end)
    {
        Start = start;
        End = end;
    }
}