using Glass.UI;

namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// HighlightSpan
//
// A highlighted span within a node's display Text, given as a start character offset and a
// length, together with the ARGB color to paint it and the highlight generation it was written
// under.  Start and Length index the node's Text string, not the payload bytes; payload byte
// association is carried separately by the node's ByteRange list.  OverrideColor is null to
// paint with the consumer's default match color.  Generation stamps the span for the
// generation-bump clear mechanism: a span is live only while its Generation equals the
// consumer's current highlight generation, and is otherwise inert.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct HighlightSpan
{
    public readonly int Start;
    public readonly int Length;
    public readonly ArgbColor OverrideColor;
    public readonly uint Generation;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HighlightSpan (constructor)
    //
    // Stores the span's offset, length, color, and generation stamp.
    //
    // start:          First character offset into the node's Text.
    // length:         Number of characters covered.
    // overrideColor:  ARGB color to paint the span, or null for the consumer's default.
    // generation:     Highlight generation this span was written under.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public HighlightSpan(int start, int length, ArgbColor overrideColor, uint generation)
    {
        Start = start;
        Length = length;
        OverrideColor = overrideColor;
        Generation = generation;
    }
}