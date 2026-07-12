using Glass.Core.Logging;
using Glass.Core.Memory;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Inference.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using static Glass.Network.Protocol.SoeConstants;

namespace Inference.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeTraceRow
//
// Per-row view model for the Opcode Trace list.  One instance per visible
// CatalogedPacket on the most recent Refresh.  Built by OpcodeTracePresenter
// from a CatalogedPacket plus a resolved character name at render time.
//
// All summary columns are immutable for the lifetime of the row; a new
// Refresh produces a new set of rows rather than mutating existing ones.
// Color is the one mutable property and raises PropertyChanged so per-row
// or per-opcode highlight changes repaint without rebuilding the list.
//
// The Payload reference is kept for expand-to-hex-dump in a later cut.
// The first cut does not read it.
///////////////////////////////////////////////////////////////////////////////////////////////
public class OpcodeTraceRow : INotifyPropertyChanged
{
    private uint _color;
    private bool _isExpanded;
    private bool _isHidden;
    private string? _fieldText;
    private string? _hexDumpText;
    private List<HighlightRange> _highlights;
    private List<SearchMatch> _matches;
    private bool _isCursorRow;

    public event PropertyChangedEventHandler? PropertyChanged;
    public uint PacketIndex { get; }
    public string TimestampLocal { get; }
    public string OpcodeHex { get; }
    public OpcodeValue OpcodeValue { get; }
    public string OpcodeName { get; }
    public StreamId Channel { get; }
    public ushort SourcePort { get; }
    public ushort DestPort { get; }
    public string CharacterName { get; }
    public int Length { get; }
    public RetainedBuffer Payload { get; }
    private FieldDisplayNode? _fieldTree;
    private FieldDisplayNode? _timestampElement;
    private FieldDisplayNode? _opcodeHexElement;
    private FieldDisplayNode? _opcodeNameElement;
    private FieldDisplayNode? _hexDumpElement;

    private bool _enableTrace = false;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeTraceRow (constructor)
    //
    // Captures the display values for one packet at the moment of row build.
    // Subsequent registry updates (e.g. late character identification) are
    // not reflected on the row; the next Refresh rebuilds the rows and
    // picks up the current state.
    //
    // packetIndex:     Arrival index from the originating CatalogedPacket.
    //                  Used by the presenter for per-entry color lookup.
    // timestampLocal:  Pre-formatted local-time timestamp string.
    // opcodeHex:       Hex-formatted wire opcode (e.g. "0xdb56").
    // opcodeName:      Resolved name or "<Unknown>".
    // channel:         Stream the packet arrived on.
    // characterName:   Resolved character name, or empty string when
    //                  identification has not yet associated the session
    //                  with a character.
    // length:          Payload length in bytes.
    // payload:         RetainedBuffer over the long-lived payload copy.
    //                  Kept for hex-dump expansion in a later cut.
    // sourcePort:      UDP source port from the packet's metadata.
    // destPort:        UDP destination port from the packet's metadata.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeTraceRow(uint packetIndex, string timestampLocal, OpcodeValue opcodeValue,
        string opcodeName, StreamId channel, string characterName, int length,
        RetainedBuffer payload, ushort sourcePort, ushort destPort)
    {
        PacketIndex = packetIndex;
        TimestampLocal = timestampLocal;
        OpcodeValue = opcodeValue;
        OpcodeHex = opcodeValue.ToString();
        OpcodeName = opcodeName;
        Channel = channel;
        CharacterName = characterName;
        Length = length;
        Payload = payload;
        SourcePort = sourcePort;
        DestPort = destPort;
        _color = 0;
        _isExpanded = false;
        _isHidden = false;
        _fieldText = null;          // TODO: remove
        _fieldTree = null;
        _hexDumpText = null;
        _isCursorRow = false;

        _timestampElement = null;
        _opcodeHexElement = null;
        _opcodeNameElement = null;
        _hexDumpElement = null;

        _highlights = new List<HighlightRange>();
        _matches = new List<SearchMatch>();

        /*
        if (packetIndex == 59)
        {
            EnableTrace = true;
        }
        */
    }

    public string ChannelAbbrev
    {
        get
        {
            return StreamAbbrev[Channel];
        }
    }
    public string PortsText
    {
        get
        {
            return SourcePort + " \u2192 " + DestPort;
        }
    }

    public bool EnableTrace
    {
        get { return _enableTrace; }
        set { _enableTrace = value; }
    }
    public uint Color
    {
        get { return _color; }
        set
        {
            _color = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color)));
        }
    }
    public bool IsExpanded
    {
        get { return _isExpanded; }
        set
        {
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }
    public bool IsHidden
    {
        get { return _isHidden; }
        set
        {
            _isHidden = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHidden)));
        }
    }

    public string? FieldText
    {
        get { return _fieldText; }
        set
        {
            _fieldText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FieldText)));
        }
    }

    public string? HexDumpText
    {
        get { return _hexDumpText; }
        set
        {
            _hexDumpText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HexDumpText)));
        }
    }

    public List<HighlightRange> Highlights
    {
        get { return _highlights; }
        set
        {
            _highlights = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Highlights)));
        }
    }

    public List<SearchMatch> Matches
    {
        get { return _matches; }
        set
        {
            _matches = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Matches)));
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // IsCursorRow
    //
    // True when this row is the one the search cursor currently designates.  Exactly one row in
    // the collection is the cursor row at a time; the presenter clears the flag on the prior
    // cursor row and sets it on the new one as the cursor moves.  Bound by the row container
    // style to paint the cursor border, independent of selection or focus.
    //
    // Raises PropertyChanged so the bound border repaints when the designation moves.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool IsCursorRow
    {
        get { return _isCursorRow; }
        set
        {
            _isCursorRow = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCursorRow)));
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FieldTree
    //
    // Root node of the field region's tree representation, or null when the packet's opcode
    // has no registered handler.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public FieldDisplayNode? FieldTree
    {
        get { return _fieldTree; }
        set
        {
            _fieldTree = value;
            if (EnableTrace && (value != null))
            {
                _fieldTree!.EnableTrace = true;
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FieldTree)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FieldTreeRoots)));
        }
    }
    ///////////////////////////////////////////////////////////////////////////////////////////
    // FieldTreeRoots
    //
    // The field tree's root wrapped in a single-element list, or an empty list when there is
    // no field tree.
    //
    // Note:  This exists because we needed something enumerable for the XAML
    ///////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<FieldDisplayNode> FieldTreeRoots
    {
        get
        {
            if (_fieldTree == null)
            {
                return System.Array.Empty<FieldDisplayNode>();
            }

            return new FieldDisplayNode[] { _fieldTree };
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TimestampElement
    //
    // The timestamp summary cell as a single span-owning display element.  Its Text is the
    // formatted local timestamp; its Spans carry that cell's search highlights.  Built on first
    // access from TimestampLocal so a row that is never searched pays nothing.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public FieldDisplayNode TimestampElement
    {
        get
        {
            if (_timestampElement == null)
            {
                _timestampElement = new FieldDisplayNode(TimestampLocal);
                if (EnableTrace)
                {
                    _timestampElement.EnableTrace = true;
                }
                DebugLog.Write(LogChannel.Fields,
                    "OpcodeTraceRow.TimestampElement: built element for '" + TimestampLocal + "'",
                    LogLevel.Trace);
            }

            return _timestampElement;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHexElement
    //
    // The opcode-hex summary cell as a single span-owning display element.  Its Text is the
    // hex-formatted wire opcode; its Spans carry that cell's search highlights.  Built on first
    // access from OpcodeHex so a row that is never searched pays nothing.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public FieldDisplayNode OpcodeHexElement
    {
        get
        {
            if (_opcodeHexElement == null)
            {
                _opcodeHexElement = new FieldDisplayNode(OpcodeHex);

                DebugLog.Write(LogChannel.Fields,
                    "OpcodeTraceRow.OpcodeHexElement: built element for '" + OpcodeHex + "'",
                    LogLevel.Trace);
            }

            if (EnableTrace)
            {
                _opcodeHexElement.EnableTrace = true;
            }

            return _opcodeHexElement;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeNameElement
    //
    // The opcode-name summary cell as a single span-owning display element.  Its Text is the
    // resolved opcode name; its Spans carry that cell's search highlights.  Built on first
    // access from OpcodeName so a row that is never searched pays nothing.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public FieldDisplayNode OpcodeNameElement
    {
        get
        {
            if (_opcodeNameElement == null)
            {
                _opcodeNameElement = new FieldDisplayNode(OpcodeName);

                DebugLog.Write(LogChannel.Fields,
                    "OpcodeTraceRow.OpcodeNameElement: built element for '" + OpcodeName + "'",
                    LogLevel.Trace);
            }

            if (EnableTrace)
            {
                _opcodeNameElement.EnableTrace = true;
            }

            return _opcodeNameElement;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HexDumpElement
    //
    // The hex-dump cell as a single span-owning display element.  Its Text is the formatted hex
    // dump; its Spans carry that cell's search highlights.  Unlike the summary elements this is
    // not built lazily from an existing string: the dump does not exist until detail is
    // populated, and it is re-formatted when the hex-length cap changes, so the presenter sets
    // and updates this element's Text directly.  Null until detail is first populated.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public FieldDisplayNode? HexDumpElement
    {
        get
        {
            return _hexDumpElement;
        }
        set
        {
            _hexDumpElement = value;

            if (EnableTrace && (_hexDumpElement != null))
            {
                _hexDumpElement.EnableTrace = true;
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HexDumpElement)));
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Equals
    //
    // Compares this row to another object for equality on PacketIndex.  PacketIndex is the
    // durable per-message identity assigned at row build and is unique across the row set, so it
    // serves as the row's equality key.
    //
    // obj:      The object to compare against; equal only when it is an OpcodeTraceRow carrying
    //           the same PacketIndex.
    //
    // returns:  True when obj is an OpcodeTraceRow whose PacketIndex equals this row's
    //           PacketIndex; false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public override bool Equals(object? obj)
    {
        OpcodeTraceRow? other = obj as OpcodeTraceRow;
        if (other == null)
        {
            return false;
        }

        bool equal = other.PacketIndex == PacketIndex;
        return equal;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetHashCode
    //
    // Produces the row's hash from PacketIndex, the row's equality key, so equal rows hash equal.
    //
    // returns:  The hash code of this row's PacketIndex.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public override int GetHashCode()
    {
        return PacketIndex.GetHashCode();
    }
}
