namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// SearchMode
//
// Selects the scope of an Opcode Trace search.  Threaded through the
// presenter's Find methods and the per-row predicate so the same code
// path serves both a cheap visible-only sweep and a thorough all-rows
// sweep.
//
// Fast:  Summary fields are searched on every row regardless of
//        expansion state.  Body text (hex dump, extracted field text)
//        is searched only on rows that are currently expanded.  No
//        unpopulated rows are populated and no rows are auto-expanded.
//        Intended for interactive find-as-you-go where the user is
//        browsing visible content.
//
// Deep:  Summary fields and body text are searched on every row,
//        populating rows on demand via PopulateRowDetail.  Matched
//        rows are auto-expanded so highlights become visible.
//        Intended for "find everywhere" sweeps where the user accepts
//        the cost of touching every row.
//
// "No search active" is signalled by an empty or null query on the
// presenter, not by a mode value -- there is no None.
///////////////////////////////////////////////////////////////////////////////////////////////
public enum SearchMode
{
    Fast,
    Deep
}