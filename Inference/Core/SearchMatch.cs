namespace Inference.Core;

public readonly struct SearchMatch
{
    public HighlightRegionType Region { get; }
    public int Start { get; }

    public SearchMatch(HighlightRegionType region, int start)
    {
        Region = region;
        Start = start;
    }
}
