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

    public event PropertyChangedEventHandler? PropertyChanged;

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

        _highlights = new List<HighlightRange>();
        _matches = new List<SearchMatch>();
    }

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FieldTree)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FieldTreeRoots)));
        }
    }
    ///////////////////////////////////////////////////////////////////////////////////////////
    // FieldTreeRoots
    //
    // The field tree's root wrapped in a single-element list, or an empty list when there is
    // no field tree.
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
}
