///////////////////////////////////////////////////////////////////////////////////////////
// HighlightRange
//
// A contiguous span within a formatted text string that should be visually highlighted.
// Returned by the presenter's locate helpers and consumed by the view when constructing
// inline runs for the trace row's text columns.
//
// Region:        The region of the row template this range applies to.
// Start:         Zero-based character offset into the formatted string.
// Length:        Number of characters covered by the highlight.
// MatchIndex:    Index into the row's Matches list of the SearchMatch this range
//                belongs to.  Multiple ranges can share a MatchIndex when one match
//                emits several ranges (e.g. hex column and ASCII gutter for one byte
//                match).  -1 when the range is not tied to a SearchMatch.
// OverrideColor: ARGB color to render this range with, or null to use the default
//                match brush.  Lets the presenter visually distinguish the cursor's
//                match without changing the default appearance of other matches.
///////////////////////////////////////////////////////////////////////////////////////////
using Inference.Core;

public readonly struct HighlightRange
{
    public HighlightRegionType Region { get; }
    public int Start { get; }
    public int Length { get; }
    public int MatchIndex { get; }
    public uint? OverrideColor { get; }

    public HighlightRange(HighlightRegionType region, int start, int length,
        int matchIndex, uint? overrideColor)
    {
        Region = region;
        Start = start;
        Length = length;
        MatchIndex = matchIndex;
        OverrideColor = overrideColor;
    }
}
