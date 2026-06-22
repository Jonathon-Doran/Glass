namespace Inference.Core;

using Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// SearchMatch
//
// Locates one search match within one region of a row.  Region tags which region the match is
// in; the remaining fields are a region-specific address interpreted according to that tag.
//
// For every region, Start is a character offset whose meaning depends on the region: an offset
// into the summary cell's string for the summary regions, into the hex-dump string for Hex, and
// into the matched node's Text for Field.
//
// Node is meaningful only when Region is Field, where the match's address space is
// two-dimensional: Node identifies which FieldDisplayNode in the row's tree the match lies in,
// and Start is the offset within that node's Text.  Node is null for every other region, whose
// address space is the single Start offset.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly struct SearchMatch
{
    public HighlightRegionType Region { get; }
    public int Start { get; }
    public FieldDisplayNode? Node { get; }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SearchMatch (constructor)
    //
    // Builds a match in a non-Field region, whose address is the single Start offset.  Node is
    // null.
    //
    // region:  The region the match is in.
    // start:   The character offset of the match within the region's string.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SearchMatch(HighlightRegionType region, int start)
    {
        Region = region;
        Start = start;
        Node = null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SearchMatch (constructor)
    //
    // Builds a Field-region match, whose address is the node together with the offset within
    // that node's Text.
    //
    // node:   The FieldDisplayNode the match lies in.
    // start:  The character offset of the match within the node's Text.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SearchMatch(FieldDisplayNode node, int start)
    {
        Region = HighlightRegionType.Field;
        Start = start;
        Node = node;
    }
}