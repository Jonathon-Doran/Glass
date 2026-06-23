namespace Inference.Core;

using Glass.Network.Protocol.Fields;
using System.Collections.Generic;

///////////////////////////////////////////////////////////////////////////////////////////////
// SearchMatch
//
// One located search hit: a place the find cursor can stop on, move to, and scroll into view.
// A match is a navigation target, not a paint instruction — painting is done by the
// HighlightSpans the match holds, which also live on the element (a FieldDisplayNode) whose Text
// contains the hit.  A single match can be made of several spans: a composite field assembled
// from non-contiguous parts of the Text (an x,y,z position drawn from three separated runs) is
// one hit made of three spans; a hex-dump hit is one hit made of two, the hex column and the
// ASCII gutter.  "One match, one span" is the common case, not the rule.
//
//   PacketIndex  The arrival index of the row whose payload produced the hit.  Carried on the
//                match so navigation can resolve the owning row and report the message number
//                without consulting a separate cursor.
//
//   Element  The FieldDisplayNode whose Text contains the hit.  This is the paint target and the
//            scroll target: the realized TextBlock currently displaying this element is what the
//            view recolors for the cursor and scrolls into view.  The element, not a region tag,
//            is the address.
//
//   Spans    The highlight components that make up the hit, each a HighlightSpan carrying its own
//            Start, Length, color, and generation.  Usually one; more for a compound hit.  The
//            match holds these directly, so it is self-describing: its scroll offset is the
//            anchor span's Start (see Anchor), and its liveness is read off a span's generation
//            without dereferencing the element's current span list.  The element holds its own
//            copies for painting; these are the navigation view of the same hit.
//
//   Anchor   The span the cursor scrolls to and the current-match coloring centers on — the
//            first element of Spans.  Its Start is the character offset, within Element's Text,
//            that the find code resolves to a character rectangle and scrolls into the viewport,
//            landing the user's eye on the hit.  Start is a character offset into Text, not a
//            payload byte position; the element separately carries the ByteRanges its Text was
//            decoded from, and keeping the two coordinates distinct is what will let a later
//            feature correlate a field with its bytes and a byte with its field.
//
// Liveness:  highlights are cleared by bumping a color's generation in the HighlightGenerationMap,
// an O(1) operation that touches nothing.  A match is live only while the current generation for
// its anchor span's color still equals that span's generation; once bumped past, the match is
// inactive — painted as nothing, counted as nothing — though it physically lingers in the match
// list until a sweep removes it.  Liveness is this by-value generation test, never a dereference
// of the element's current spans.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly struct SearchMatch
{
    public uint PacketIndex { get; }
    public FieldDisplayNode Element { get; }
    public IReadOnlyList<HighlightSpan> Spans { get; }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SearchMatch (constructor)
    //
    // Builds a match over one element and the span list that paints the hit.  The first span is
    // the anchor: its Start is the scroll offset and the current-match coloring centers on it.
    //
    // packetIndex:  The arrival index of the row whose payload produced the hit.
    // element:      The FieldDisplayNode whose Text contains the hit.
    // spans:        The hit's highlight components, anchor first.  Must hold at least one span.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SearchMatch(uint packetIndex, FieldDisplayNode element, IReadOnlyList<HighlightSpan> spans)
    {
        PacketIndex = packetIndex;
        Element = element;
        Spans = spans;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Anchor
    //
    // The span the cursor scrolls to and the current-match coloring centers on: the first span
    // of the hit.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public HighlightSpan Anchor
    {
        get
        {
            return Spans[0];
        }
    }
}