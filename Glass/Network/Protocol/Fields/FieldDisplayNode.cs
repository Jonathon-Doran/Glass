namespace Glass.Network.Protocol.Fields;

using Glass.Core.Logging;
using System.Collections.Generic;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldDisplayNode
//
// One node in a hierarchical display model.  Carries a display string, a list of payload byte
// ranges the node was decoded from, and a list of child nodes.  Children are appended after
// construction via AddChild; byte ranges via AddByteRange.
///////////////////////////////////////////////////////////////////////////////////////////////
public sealed class FieldDisplayNode
{
    private string _text;
    private readonly List<ByteRange> _ranges;
    private readonly List<HighlightSpan> _spans;
    private readonly List<FieldDisplayNode> _children;
    private event System.Action? _spansChanged;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FieldDisplayNode (constructor)
    //
    // Stores the display string, and starts empty range and child lists.
    //
    // text:  The display string for the node.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public FieldDisplayNode(string text)
    {
        _text = text;
        _ranges = new List<ByteRange>();
        _spans = new List<HighlightSpan>();
        _children = new List<FieldDisplayNode>();

        DebugLog.Write(LogChannel.Fields,
            "FieldDisplayNode: created text='" + text + "'", LogLevel.Trace);
    }
    ///////////////////////////////////////////////////////////////////////////////////////////
    // FieldDisplayNode (constructor)
    //
    // Creates an empty FieldDisplayNode
    ///////////////////////////////////////////////////////////////////////////////////////////
    public FieldDisplayNode() : this(string.Empty)
    {
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Text
    //
    // The node's display string.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string Text
    {
        get { return _text; }
        set
        {
            _text = value;
            DebugLog.Write(LogChannel.Fields,
                "FieldDisplayNode.Text: set to '" + value + "'", LogLevel.Trace);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Ranges
    //
    // The payload byte ranges the node was decoded from.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<ByteRange> Ranges
    {
        get { return _ranges; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Spans
    //
    // The highlighted spans recorded on the node's Text.  May include spans from an earlier
    // highlight generation that have not yet been pruned; consumers paint only those whose
    // Generation matches the current highlight generation.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<HighlightSpan> Spans
    {
        get { return _spans; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SpansChanged
    //
    // Raised after the node's span list changes.  A subscriber rebuilds whatever it renders from
    // Spans; the event carries no arguments because the current span list is read from Spans.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public event System.Action? SpansChanged
    {
        add { _spansChanged += value; }
        remove { _spansChanged -= value; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Children
    //
    // The node's child nodes.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<FieldDisplayNode> Children
    {
        get { return _children; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddByteRange
    //
    // Appends a byte range.
    //
    // byteRange:  The byte range to append.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void AddByteRange(ByteRange byteRange)
    {
        _ranges.Add(byteRange);

        DebugLog.Write(LogChannel.Fields,
            "FieldDisplayNode.AddByteRange: appended start=" + byteRange.Start + " end=" + byteRange.End
            + " under '" + _text + "', range count now " + _ranges.Count, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddChild
    //
    // Appends a child node.  Throws on a null child.
    //
    // child:  The child node to append.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void AddChild(FieldDisplayNode child)
    {
        if (child == null)
        {
            DebugLog.Write(LogChannel.Fields,
                "FieldDisplayNode.AddChild: null child for text='" + _text + "'", LogLevel.Error);
            throw new System.InvalidOperationException(
                "FieldDisplayNode.AddChild: child is null for text '" + _text + "'");
        }

        _children.Add(child);

        DebugLog.Write(LogChannel.Fields,
            "FieldDisplayNode.AddChild: appended '" + child.Text + "' under '" + _text
            + "', child count now " + _children.Count, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddSpan
    //
    // Appends a highlighted span to the node, first removing spans the supplied lookup reports as
    // stale so the node retains only live spans across generations.  Pruning here is opportunistic
    // cleanup performed while the node is already being touched; it bounds span growth on a node
    // that is re-highlighted across many generations without a separate walk.
    //
    // span:                        The span to append.
    // currentGenerationForColor:   Returns the current generation for a given color, used to drop
    //                              stale spans before the append.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void AddSpan(HighlightSpan span, System.Func<Glass.UI.ArgbColor, uint> currentGenerationForColor)
    {
        RemoveStaleSpans(currentGenerationForColor);

        _spans.Add(span);

        DebugLog.Write(LogChannel.Fields,
            "FieldDisplayNode.AddSpan: appended start=" + span.Start + " length=" + span.Length
            + " generation=" + span.Generation + " under '" + _text + "', span count now "
            + _spans.Count, LogLevel.Trace);

        // Notify subscribers that the span list changed so they can rebuild from Spans.  Only a
        // node bound to a realized element has a subscriber; an unrealized node invokes an empty
        // delegate list and nothing happens.
        _spansChanged?.Invoke();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RemoveStaleSpans
    //
    // Removes every span whose generation is below the current generation for that span's own
    // color, as given by the supplied lookup.  Each color is an independent lane: a span survives
    // only while its generation is at or above its color's current generation.  Raises
    // SpansChanged when any span is removed so a renderer rebuilds.
    //
    // currentGenerationForColor:  Returns the current generation for a given color.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void RemoveStaleSpans(System.Func<Glass.UI.ArgbColor, uint> currentGenerationForColor)
    {
        int removed = _spans.RemoveAll(existing =>
            existing.Generation < currentGenerationForColor(existing.OverrideColor));

        if (removed > 0)
        {
            DebugLog.Write(LogChannel.Fields,
                "FieldDisplayNode.RemoveStaleSpans: removed " + removed + " stale span(s) under '"
                + _text + "', span count now " + _spans.Count, LogLevel.Trace);
        }
    }
}