///////////////////////////////////////////////////////////////////////////////////////////
// HighlightRange
//
// A contiguous span within a formatted text string that should be visually highlighted.
// Returned by LocateHexDumpHighlights and consumed by the view when constructing
// inline runs for the hex dump display.
//
// Start:  Zero-based character offset into the formatted string.
// Length: Number of characters covered by the highlight.
///////////////////////////////////////////////////////////////////////////////////////////
public readonly struct HighlightRange
{
    public int Start { get; }
    public int Length { get; }

    public HighlightRange(int start, int length)
    {
        Start = start;
        Length = length;
    }
}
