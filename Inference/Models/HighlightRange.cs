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
using Inference.Core;

public readonly struct HighlightRange
{
    public HighlightRegionType Region { get; }
    public int Start { get; }
    public int Length { get; }

    public HighlightRange(HighlightRegionType region, int start, int length)
    {
        Region = region;
        Start = start;
        Length = length;
    }
}
