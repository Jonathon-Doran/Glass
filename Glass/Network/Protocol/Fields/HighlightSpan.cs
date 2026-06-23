using Glass.Core.Logging;
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

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SpanText
    //
    // Returns the text this span covers, sliced out of the supplied node Text.
    // If Start/Length fall outside the bounds of text, an out-of-range marker is returned
    // instead of throwing, so the logging path is safe against a stale span written under an earlier Text.
    //
    // text:  The node Text string this span indexes into.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string SpanText(string text)
    {
        if (text == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "MatchText: text is null");
            return "<null>";
        }

        if (Start < 0 || Length < 0 || Start > text.Length || Start + Length > text.Length)
        {
            DebugLog.Write(LogChannel.Opcodes, "MatchText: span out of range start=" + Start + " len=" + Length + " textLen=" + text.Length);
            return "<out-of-range>";
        }

        return text.Substring(Start, Length);
    }
}