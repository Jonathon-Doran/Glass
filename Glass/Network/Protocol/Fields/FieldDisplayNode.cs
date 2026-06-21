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
    private readonly List<FieldDisplayNode> _children;

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
}