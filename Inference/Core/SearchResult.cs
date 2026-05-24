using System.Collections.Generic;
using Inference.Models;

namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// SearchResult
//
// Carries the lists produced by a search operation plus the count of
// matches those lists represent.  MatchCount is not derivable from list
// lengths: a single match can produce multiple highlight ranges, and a
// single matching row can contain multiple matches.
///////////////////////////////////////////////////////////////////////////////////////////////
public class SearchResult
{
    public List<HighlightRange>? Ranges { get; set; }
    public List<OpcodeTraceRow>? Rows { get; set; }
    public int MatchCount { get; set; }
}
