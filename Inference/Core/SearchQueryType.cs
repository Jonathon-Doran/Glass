namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// SearchQueryType
//
// Classification of the active search query.  Set by SetSearchQuery once
// per query change and read by the per-row predicate and highlight
// recompute so neither has to re-parse the query.
//
// Empty:  No active query.  _searchQuery is null or whitespace.
// Hex:    Query parses as a hex byte sequence.  Byte scans run against
//         the parsed bytes.
// Ascii:  Query does not parse as hex.  Byte scans run against the
//         ASCII encoding of the query string.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum SearchQueryType
{
    Empty,
    Hex,
    Ascii
}