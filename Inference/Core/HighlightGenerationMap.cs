using Glass.Core.Logging;
using Glass.UI;
using System.Collections.Generic;

namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// HighlightGenerationMap
//
// Tracks the current highlight generation for each color independently.  A HighlightSpan is live
// only while its Generation equals the current generation for its color; a span whose generation
// is behind is stale and paints as nothing.
//
// Clearing a color's highlights is a single Bump of that color's generation: every span already
// written under the prior generation becomes stale at once with no per-node walk.  Stamping a new
// span reads CurrentGeneration for the span's color so the span is born live.  Realization and
// span-append paths prune spans behind the current generation for their color.
//
// All access is on the UI thread; the map holds no lock.
///////////////////////////////////////////////////////////////////////////////////////////////
public sealed class HighlightGenerationMap
{
    private readonly Dictionary<ArgbColor, uint> _generationByColor;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HighlightGenerationMap (constructor)
    //
    // Starts with no colors recorded; an unrecorded color reads as generation zero.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public HighlightGenerationMap()
    {
        _generationByColor = new Dictionary<ArgbColor, uint>();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CurrentGeneration
    //
    // Returns the current generation for a color.  A color that has never been bumped reads as
    // generation zero.
    //
    // color:  The color to query.
    //
    // returns:  The color's current generation.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public uint CurrentGeneration(ArgbColor color)
    {
        uint generation;
        if (_generationByColor.TryGetValue(color, out generation))
        {
            return generation;
        }

        return 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Bump
    //
    // Advances a color's generation by one and returns the new value, marking every span
    // previously written under that color stale.  A color bumped for the first time advances from
    // zero to one.
    //
    // color:  The color to advance.
    //
    // returns:  The color's new current generation.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public uint Bump(ArgbColor color)
    {
        uint current;
        if (!_generationByColor.TryGetValue(color, out current))
        {
            current = 0;
        }

        uint next = current + 1;
        _generationByColor[color] = next;

        DebugLog.Write(LogChannel.Fields,
            "HighlightGenerationMap.Bump: color=0x" + color.Value.ToString("x8")
            + " generation now " + next, LogLevel.Trace);

        return next;
    }
}