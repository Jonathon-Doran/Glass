using Glass.Core;
using Glass.Core.Logging;
using Glass.Core.Signals;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.UI;
using Inference.Models;
using Inference.UI;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeTracePresenter
//
// UI-side owner of the Opcode Trace list's row collection.  Cold path:
// rows are rebuilt on demand when Refresh is called from the toolbar
// button, not subscribed to the bus.
//
// Holds the ObservableCollection bound to OpcodeTraceList plus three
// ephemeral session-scoped maps:
//   - hidden opcodes (per-opcode hide set)
//   - per-opcode color overrides
//   - per-arrival-index color overrides
// Per-arrival-index wins over per-opcode at render time.
//
// All mutation runs on the UI thread.  The catalog is read under its own
// lock via PacketsFor; the presenter does not hold the catalog lock past
// those calls.
///////////////////////////////////////////////////////////////////////////////////////////////
public class OpcodeTracePresenter
{
    private readonly PacketCatalog _catalog;
    private readonly ObservableCollection<OpcodeTraceRow> _rows;
    public ObservableCollection<OpcodeTraceRow> Rows => _rows;
    private readonly HashSet<OpcodeValue> _hiddenOpcodes;
    private readonly Dictionary<OpcodeValue, uint> _colorByOpcode;
    private readonly Dictionary<uint, uint> _colorByPacketIndex;

    private readonly Dictionary<uint, OpcodeTraceRow> _rowByPacketIndex;
    private readonly Dictionary<int, string> _characterNameCache;
    private readonly object _characterNameCacheLock;
    private readonly Action<SignalSessionAdded> _sessionAddedHandler;
    private uint _maxHexBytes;
    private string _searchQuery;
    private byte[]? _searchQueryBytes;
    private int _matchCount;
    private SearchCursor _searchCursor;
    private SearchQueryType _searchQueryType;
    private bool _searchWrap;
    private readonly HighlightGenerationMap _highlightGenerationMap;
    private ArgbColor _activeHighlightColor;
    private readonly List<SearchMatch> _matches;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeTracePresenter (constructor)
    //
    // Captures the catalog reference for snapshotting on Refresh.
    //
    // catalog:  The PacketCatalog the presenter reads on Refresh.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeTracePresenter(PacketCatalog catalog)
    {
        _catalog = catalog;
        _rows = new ObservableCollection<OpcodeTraceRow>();
        _rowByPacketIndex = new Dictionary<uint, OpcodeTraceRow>();
        _hiddenOpcodes = new HashSet<OpcodeValue>();
        _colorByOpcode = new Dictionary<OpcodeValue, uint>();
        _colorByPacketIndex = new Dictionary<uint, uint>();
        _characterNameCache = new Dictionary<int, string>();
        _characterNameCacheLock = new object();
        _maxHexBytes = 64;
        _searchQuery = string.Empty;
        _searchQueryBytes = null;
        _matchCount = 0;
        _searchQueryType = SearchQueryType.Empty;
        _searchCursor = SearchCursor.Inactive;
        _highlightGenerationMap = new HighlightGenerationMap();
        _activeHighlightColor = new ArgbColor(0u);
        _matches = new List<SearchMatch>();

        // The FieldHighlightBehavior needs to know the generations so that it can clean up old highlights
        FieldHighlightBehavior.SetGenerationMap(_highlightGenerationMap);

        _sessionAddedHandler = OnSessionAdded;
        GlassContext.SignalBus.Subscribe(_sessionAddedHandler);
    }
    public int MatchCount
    {
        get
        {
            return _matchCount;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CurrentMatch
    //
    // The match the cursor is parked on, or null when the cursor is inactive.  Reads the match
    // at the cursor's index into the match list; returns null without indexing when the cursor
    // holds no position.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SearchMatch? CurrentMatch
    {
        get
        {
            if (_searchCursor.Active == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "OpcodeTracePresenter.CurrentMatch: cursor inactive, no current match", LogLevel.Trace);
                return null;
            }

            return _matches[(int)_searchCursor.Value];
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CursorRow
    //
    // The row whose current cursor match belongs to it, or null when the cursor is inactive.
    //
    // The getter resolves the cursor's match to its row through the packet-index map.  The
    // setter lands the cursor on the first match in the supplied row, found by walking the match
    // list for a match whose PacketIndex equals the row's; when the row carries no match, or the
    // supplied row is null, the cursor is left unchanged.  The walk is an acceptable cold-path
    // cost: the setter fires on list selection, not on a hot path.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeTraceRow? CursorRow
    {
        get
        {
            if (_searchCursor.Active == false)
            {
                return null;
            }

            uint matchPacketIndex = _matches[(int)_searchCursor.Value].PacketIndex;

            OpcodeTraceRow? row;
            if (_rowByPacketIndex.TryGetValue(matchPacketIndex, out row))
            {
                return row;
            }

            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.CursorRow: cursor match packetIndex " + matchPacketIndex
                + " has no row in map", LogLevel.Warn);
            return null;
        }
        set
        {
            if (value == null)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "OpcodeTracePresenter.CursorRow: null row, cursor left unchanged", LogLevel.Trace);
                return;
            }

            uint targetPacketIndex = value.PacketIndex;

            for (uint i = 0u; i < (uint)_matches.Count; i = i + 1u)
            {
                if (_matches[(int)i].PacketIndex == targetPacketIndex)
                {
                    _searchCursor = (SearchCursor)i;
                    DebugLog.Write(LogChannel.Opcodes,
                        "OpcodeTracePresenter.CursorRow: cursor set to match index " + i
                        + " for packetIndex " + targetPacketIndex, LogLevel.Trace);
                    return;
                }
            }

            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.CursorRow: packetIndex " + targetPacketIndex
                + " has no match, cursor left unchanged", LogLevel.Trace);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CursorOrdinal
    //
    // The 1-based position of the current cursor match within the match list, for display as
    // "ordinal of total".  Returns 0 when the cursor is inactive, meaning no match is current.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public uint CursorOrdinal
    {
        get
        {
            if (_searchCursor.Active == false)
            {
                return 0u;
            }

            return _searchCursor.Value + 1u;
        }
    }

    public string SearchQuery
    {
        get { return _searchQuery; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Detach
    //
    // Removes the presenter's SignalSessionAdded subscription.  Call before
    // discarding the presenter.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Detach()
    {
        GlassContext.SignalBus.Unsubscribe(_sessionAddedHandler);

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.Detach: unsubscribed from SignalSessionAdded", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OnSessionAdded
    //
    // SignalSessionAdded subscriber.  Writes the session id and character
    // name into the presenter's name cache so we can
    // resolve the name even after the session has disconnected and been
    // removed from SessionRegistry.
    //
    // Published on whatever thread SessionRegistry.IdentifyConnection ran
    // on (capture thread in live mode), not the UI thread.  The cache is
    // also read from the UI thread inside ResolveCharacterName, so all
    // access is serialized through _characterNameCacheLock.
    //
    // signal:  The SignalSessionAdded payload carrying the new session id
    //          and character name.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void OnSessionAdded(SignalSessionAdded signal)
    {
        if (signal == null)
        {
            DebugLog.Write(LogChannel.Sessions,
                "OpcodeTracePresenter.OnSessionAdded: null signal, ignoring", LogLevel.Warn);
            return;
        }

        lock (_characterNameCacheLock)
        {
            _characterNameCache[signal.SessionId] = signal.CharacterName;
        }

        DebugLog.Write(LogChannel.Sessions,
            "OpcodeTracePresenter.OnSessionAdded: cached sessionId="
            + signal.SessionId + " character=" + signal.CharacterName, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Refresh
    //
    // Rebuilds the row list from the catalog.  Walks every known opcode
    // except those in the hide set, accumulates the packets, sorts by
    // arrival index for absolute arrival order, resolves names and applies
    // colors, and replaces the bound collection.
    //
    // Must be called on the UI thread.  Catalog reads are short-locked
    // inside the per-opcode PacketsFor calls; the presenter does not hold
    // any lock across the build.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Refresh()
    {
        PatchLevel patchLevel = GlassContext.CurrentPatchLevel;
        OpcodeValue[] opcodes = _catalog.KnownOpcodes();
        List<CatalogedPacket> accumulated = new List<CatalogedPacket>();
        for (int i = 0; i < opcodes.Length; i++)
        {
            OpcodeValue opcode = opcodes[i];
            if (_hiddenOpcodes.Contains(opcode))
            {
                continue;
            }
            List<CatalogedPacket> bucket = _catalog.PacketsFor(opcode, null, null, int.MaxValue);
            accumulated.AddRange(bucket);
        }

        accumulated.Sort((a, b) => a.PacketIndex.CompareTo(b.PacketIndex));

        _rows.Clear();
        _rowByPacketIndex.Clear();

        for (int i = 0; i < accumulated.Count; i++)
        {
            OpcodeValue opcodeToDisplay;

            CatalogedPacket packet = accumulated[i];
            if (packet.Metadata.Opcode.Exists)
            {
                opcodeToDisplay = packet.Metadata.Opcode.Value;
            }
            else
            {
                opcodeToDisplay = packet.Metadata.WireValue;
            }
            string timestampLocal = packet.Metadata.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
            string opcodeName = GlassContext.PatchRegistry.GetOpcodeName(packet.Metadata.Opcode);
            string characterName = ResolveCharacterName(packet.Metadata.SessionId);
            int length = packet.Payload.Length;

            OpcodeTraceRow row = new OpcodeTraceRow(packet.PacketIndex,
                            timestampLocal, opcodeToDisplay, opcodeName, packet.Metadata.Channel,
                            characterName, length, packet.Payload,
                            (ushort)packet.Metadata.SourcePort, (ushort)packet.Metadata.DestPort);
            row.Color = ResolveColor(packet.PacketIndex, packet.Opcode);
            _rows.Add(row);
            _rowByPacketIndex[packet.PacketIndex] = row;
        }

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.Refresh: rebuilt " + _rows.Count + " rows from "
            + opcodes.Length + " known opcodes (" + _hiddenOpcodes.Count + " hidden)", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetActiveHighlightColor
    //
    // Stores the ARGB color that subsequent highlighting operations stamp their spans with.
    // Zero is the no-color-armed sentinel: while it is set, highlighting operations record
    // matches but paint nothing.  Arming a color does not repaint anything already
    // highlighted; the stored color affects only the next operation that writes spans.
    //
    // argb:  ARGB color to arm, or 0 to arm no color.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void SetActiveHighlightColor(ArgbColor argb)
    {
        _activeHighlightColor = argb;

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.SetActiveHighlightColor: armed color=0x" + argb.ToString(),
            LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ResolveCharacterName
    //
    // Returns the character name associated with the given session id, or
    // empty string if no name has been cached for it.  
    //
    // sessionId:  Session id from the packet's metadata.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private string ResolveCharacterName(int sessionId)
    {
        if (sessionId < 0)
        {
            return string.Empty;
        }

        lock (_characterNameCacheLock)
        {
            string? cached;
            if (_characterNameCache.TryGetValue(sessionId, out cached))
            {
                return cached;
            }
        }

        return string.Empty;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ResolveColor
    //
    // Returns the ARGB color to apply to a row, using the two-layer
    // override model: a per-arrival-index override wins, the per-opcode
    // override is the fallback, and 0 (no highlight) is the default.
    //
    // Both maps are session-scoped and mutated only on the UI thread; no
    // locking needed.
    //
    // packetIndex:  Arrival index from the originating CatalogedPacket.
    // opcode:       Wire opcode value, used for the per-opcode fallback.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private uint ResolveColor(uint packetIndex, OpcodeValue opcode)
    {
        uint color;
        if (_colorByPacketIndex.TryGetValue(packetIndex, out color))
        {
            return color;
        }
        if (_colorByOpcode.TryGetValue(opcode, out color))
        {
            return color;
        }
        return 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetPacketColor
    //
    // Sets or clears the per-packet color override for the given arrival
    // index.  Writing 0 removes the entry (zero is the no-override
    // sentinel).  After the map update, the row with the matching
    // PacketIndex has its Color recomputed via ResolveColor so the visible
    // background reflects the new override layer plus any per-opcode
    // fallback.
    //
    // packetIndex:  Arrival index of the target row.
    // argb:         ARGB color value, or 0 to clear the override.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void SetPacketColor(uint packetIndex, uint argb)
    {
        if (argb == 0)
        {
            _colorByPacketIndex.Remove(packetIndex);
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.SetPacketColor: cleared override for index="
                + packetIndex, LogLevel.Trace);
        }
        else
        {
            _colorByPacketIndex[packetIndex] = argb;
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.SetPacketColor: set index="
                + packetIndex + " color=0x" + argb.ToString("x8"), LogLevel.Trace);
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            OpcodeTraceRow row = _rows[i];
            if (row.PacketIndex == packetIndex)
            {
                row.Color = ResolveColor(row.PacketIndex, row.OpcodeValue);
                return;
            }
        }
    }
    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetOpcodeColor
    //
    // Sets or clears the per-opcode color override for the given wire
    // opcode value.  Writing 0 removes the entry (zero is the no-override
    // sentinel).  After the map update, every visible row with the matching
    // OpcodeValue has its Color recomputed via ResolveColor so per-packet
    // overrides still win where set.
    //
    // opcode:  Wire opcode value to apply the override to.
    // argb:    ARGB color value, or 0 to clear the override.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void SetOpcodeColor(OpcodeValue opcode, uint argb)
    {
        if (argb == 0)
        {
            _colorByOpcode.Remove(opcode);
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.SetOpcodeColor: cleared override for opcode=0x"
                + opcode, LogLevel.Trace);
        }
        else
        {
            _colorByOpcode[opcode] = argb;
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.SetOpcodeColor: set opcode=0x"
                + opcode + " color=0x" + argb.ToString("x8"), LogLevel.Trace);
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            OpcodeTraceRow row = _rows[i];
            if (row.OpcodeValue == opcode)
            {
                row.Color = ResolveColor(row.PacketIndex, row.OpcodeValue);
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////
    // PopulateRowDetail
    //
    // Formats the row's payload as a hex dump into HexDumpText, capped at the presenter's
    // current _maxHexBytes, then asks OpcodeDispatch for the field tree the packet's handler
    // builds and stores its root on the row.  Rows whose opcode has no registered handler
    // receive a null field tree but still receive a hex dump.
    //
    // row:  The row to populate.
    ///////////////////////////////////////////////////////////////////////////////////////
    public void PopulateRowDetail(OpcodeTraceRow row)
    {
        ReadOnlySpan<byte> payload = row.Payload.AsReadOnlySpan();
        string hexDump = HexDumpFormatter.Format(payload, _maxHexBytes);
        row.HexDumpText = hexDump;

        if (row.HexDumpElement == null)
        {
            row.HexDumpElement = new FieldDisplayNode(hexDump);
        }

        CatalogedPacket? packet = _catalog.PacketAt(row.PacketIndex);

        if (packet == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.PopulateRowDetail: packetIndex " + row.PacketIndex
                + " not in catalog", LogLevel.Warn);
            return;
        }

        PacketMetadata metadata = packet.Value.Metadata;

        // get the field FDN
        FieldDisplayNode? root = OpcodeDispatch.Instance.Describe(payload, metadata);
        row.FieldTree = root;

        if (root == null)
        {
            // this fires on every unhandled opcode!
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.PopulateRowDetail: no handler for " + metadata.Opcode
                + ", no field tree", LogLevel.Trace);
        }
        else
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.PopulateRowDetail: built field tree for " + metadata.Opcode +
                ", packetindex: " + row.PacketIndex, LogLevel.Trace);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetMaxHexBytes
    //
    // Updates the cap on bytes shown in expanded hex dumps and re-formats every
    // already-populated row in place.  Rows whose HexDumpText is null are left
    // alone; they will pick up the new cap when they are first expanded.
    //
    // Must be called on the UI thread because it mutates rows that are bound
    // to the list view.
    //
    // maxBytes:  Maximum bytes to render per row.  Pass int.MaxValue for no cap.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void SetMaxHexBytes(uint maxBytes)
    {
        if (maxBytes == _maxHexBytes)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.SetMaxHexBytes: unchanged at " + maxBytes, LogLevel.Trace);
            return;
        }

        _maxHexBytes = maxBytes;
        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.SetMaxHexBytes: cap set to " + maxBytes, LogLevel.Trace);

        int repopulated = 0;
        for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            OpcodeTraceRow row = _rows[rowIndex];
            if (row.HexDumpText == null)
            {
                continue;
            }

            ReadOnlySpan<byte> payload = row.Payload.AsReadOnlySpan();

            string hexDump = HexDumpFormatter.Format(payload, _maxHexBytes);
            row.HexDumpText = hexDump;

            // it is possible that we could have HexDumpSet without the HexDumpElement due to some
            // edge cases.  So guard against that.
            if (row.HexDumpElement != null)
            {
                row.HexDumpElement.Text = hexDump;
            }
            repopulated++;
        }

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.SetMaxHexBytes: re-formatted " + repopulated
            + " populated rows out of " + _rows.Count, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LocateHexDumpHighlights
    //
    // Scans the row's payload for the active query bytes and, for each payload match, stamps
    // HighlightSpans on the row's hex-dump element and appends one multi-span SearchMatch to the
    // row's Matches list.  A single payload match emits two spans per dump line it covers — one
    // over the hex column, one over the ASCII gutter — all collected into that match's span list.
    // The match's anchor is the hex-column span on its first line, so the cursor scrolls to the
    // start of the hex bytes.  ASCII queries fold 'A'-'Z' to 'a'-'z' on both sides during
    // comparison.  In Fast mode collapsed rows are skipped.  A row with no hex-dump element is
    // skipped.
    //
    // row:   The row whose payload is scanned.
    // mode:  Search mode gating body scans for collapsed rows.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void LocateHexDumpHighlights(OpcodeTraceRow row, SearchMode mode)
    {
        if (mode == SearchMode.Fast && !row.IsExpanded)
        {
            return;
        }

        FieldDisplayNode? hexElement = row.HexDumpElement;
        if (hexElement == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.LocateHexDumpHighlights: no hex-dump element, skipped", LogLevel.Trace);
            return;
        }

        byte[] queryBytes = _searchQueryBytes!;
        if (queryBytes.Length == 0)
        {
            return;
        }

        ReadOnlySpan<byte> payload = row.Payload.AsReadOnlySpan();
        uint payloadLength = (uint)payload.Length;

        uint displayLength;
        if (_maxHexBytes == int.MaxValue || payloadLength <= _maxHexBytes)
        {
            displayLength = payloadLength;
        }
        else
        {
            displayLength = _maxHexBytes;
        }

        int scanLength = (int)displayLength;

        if (queryBytes.Length > scanLength)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.LocateHexDumpHighlights: query longer than displayed "
                + scanLength + " byte(s), nothing to scan", LogLevel.Trace);
            return;
        }

        bool caseInsensitive = _searchQueryType == SearchQueryType.Ascii;

        int lineWidth = 8 + 2 + (16 * 3) + 1 + 1 + 16 + 1 + 1;
        int hexColumnOffset = 8 + 2;
        int asciiColumnOffset = 8 + 2 + (16 * 3) + 1 + 1;

        int searchEnd = scanLength - queryBytes.Length + 1;
        for (int scanPos = 0; scanPos < searchEnd; scanPos++)
        {
            bool matched = true;
            for (int q = 0; q < queryBytes.Length; q++)
            {
                byte payloadByte = payload[scanPos + q];
                byte queryByte = queryBytes[q];
                if (caseInsensitive)
                {
                    if (payloadByte >= (byte)'A' && payloadByte <= (byte)'Z')
                    {
                        payloadByte = (byte)(payloadByte + 32);
                    }
                    if (queryByte >= (byte)'A' && queryByte <= (byte)'Z')
                    {
                        queryByte = (byte)(queryByte + 32);
                    }
                }
                if (payloadByte != queryByte)
                {
                    matched = false;
                    break;
                }
            }

            if (!matched)
            {
                continue;
            }

            int matchStart = scanPos;
            int matchEnd = scanPos + queryBytes.Length;

            int firstLine = matchStart / 16;
            int lastLine = (matchEnd - 1) / 16;

            List<HighlightSpan> matchSpans = new List<HighlightSpan>();

            for (int lineIndex = firstLine; lineIndex <= lastLine; lineIndex++)
            {
                int lineByteStart = lineIndex * 16;
                int lineByteEnd = lineByteStart + 16;

                int sliceStart = Math.Max(matchStart, lineByteStart);
                int sliceEnd = Math.Min(matchEnd, lineByteEnd);
                int sliceLength = sliceEnd - sliceStart;

                int withinLineByteIndex = sliceStart - lineByteStart;

                int hexStartInLine = hexColumnOffset + (withinLineByteIndex * 3);
                if (withinLineByteIndex >= 8)
                {
                    hexStartInLine += 1;
                }

                int hexLength = (sliceLength * 3) - 1;

                int sliceLastByteIndex = withinLineByteIndex + sliceLength - 1;
                if (withinLineByteIndex < 8 && sliceLastByteIndex >= 8)
                {
                    hexLength += 1;
                }

                int lineStartInString = lineIndex * lineWidth;

                uint generation = _highlightGenerationMap.CurrentGeneration(_activeHighlightColor);

                HighlightSpan hexSpan = new HighlightSpan(
                    lineStartInString + hexStartInLine, hexLength, _activeHighlightColor, generation);
                hexElement.AddSpan(hexSpan, _highlightGenerationMap.CurrentGeneration);
                matchSpans.Add(hexSpan);

                int asciiStartInLine = asciiColumnOffset + withinLineByteIndex;
                HighlightSpan asciiSpan = new HighlightSpan(
                    lineStartInString + asciiStartInLine, sliceLength, _activeHighlightColor, generation);
                hexElement.AddSpan(asciiSpan, _highlightGenerationMap.CurrentGeneration);
                matchSpans.Add(asciiSpan);
            }

            SearchMatch match = new SearchMatch(row.PacketIndex, hexElement, matchSpans);
            row.Matches.Add(match);
            _matches.Add(match);

            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.LocateHexDumpHighlights: match at byte " + matchStart
                + ", " + matchSpans.Count + " span(s) across lines " + firstLine + "-" + lastLine,
                LogLevel.Trace);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LocateFieldHighlights
    //
    // Walks the row's field tree and, for each node whose Text contains the active query,
    // stamps a HighlightSpan on the node per occurrence and appends a node-addressed SearchMatch
    // to the row's Matches list.  Matching is case-insensitive.  In Fast mode a collapsed row
    // is skipped without walking its tree.  A row with no field tree is skipped.
    //
    // row:   The row whose field tree is scanned.
    // mode:  Search mode gating the scan for collapsed rows.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void LocateFieldHighlights(OpcodeTraceRow row, SearchMode mode)
    {
        if (mode == SearchMode.Fast && !row.IsExpanded)
        {
            DebugLog.Write(LogChannel.Fields,
                "OpcodeTracePresenter.LocateFieldHighlights: fast mode, collapsed row skipped",
                LogLevel.Trace);
            return;
        }

        FieldDisplayNode? root = row.FieldTree;
        if (root == null)
        {
            DebugLog.Write(LogChannel.Fields,
                "OpcodeTracePresenter.LocateFieldHighlights: no field tree, skipped", LogLevel.Trace);
            return;
        }

        int before = row.Matches.Count;
        ScanFieldNode(root, row);
        int added = row.Matches.Count - before;

        DebugLog.Write(LogChannel.Fields,
            "OpcodeTracePresenter.LocateFieldHighlights: added " + added + " field match(es)",
            LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScanFieldNode
    //
    // Scans one element's Text for every case-insensitive occurrence of the active query,
    // stamping a HighlightSpan on the element and appending a single-span SearchMatch to the row
    // for each, then recurses into the element's children.
    //
    // element:  The element to scan.
    // row:      The row receiving the SearchMatches.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ScanFieldNode(FieldDisplayNode element, OpcodeTraceRow row)
    {
        string text = element.Text;
        if (!string.IsNullOrEmpty(text))
        {
            int scanPos = 0;
            while (scanPos <= text.Length - _searchQuery.Length)
            {
                int found = text.IndexOf(_searchQuery, scanPos, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    break;
                }

                uint generation = _highlightGenerationMap.CurrentGeneration(_activeHighlightColor);
                HighlightSpan highlightSpan = new HighlightSpan(found, _searchQuery.Length, _activeHighlightColor, generation);
                element.AddSpan(highlightSpan, _highlightGenerationMap.CurrentGeneration);

                List<HighlightSpan> spans = new List<HighlightSpan>();
                spans.Add(highlightSpan);
                SearchMatch match = new SearchMatch(row.PacketIndex, element, spans);
                row.Matches.Add(match);
                _matches.Add(match);

                DebugLog.Write(LogChannel.Fields,
                    "OpcodeTracePresenter.ScanFieldNode: match at offset " + found
                    + " in '" + text + "'", LogLevel.Trace);

                scanPos = found + _searchQuery.Length;
            }
        }

        for (int childIndex = 0; childIndex < element.Children.Count; childIndex++)
        {
            ScanFieldNode(element.Children[childIndex], row);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LocateSummaryHighlights
    //
    // Scans each of the row's three summary cell elements — timestamp, opcode-hex, and
    // opcode-name — for every case-insensitive occurrence of the active query.  For each
    // occurrence a HighlightSpan covering the matched substring is stamped on that cell's element
    // and a single-span SearchMatch is appended to the row's Matches list.  A cell with no
    // occurrence contributes nothing.
    //
    // row:  The row whose summary cells are scanned.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void LocateSummaryHighlights(OpcodeTraceRow row)
    {
        ScanSummaryElement(row.TimestampElement, row);
        ScanSummaryElement(row.OpcodeHexElement, row);
        ScanSummaryElement(row.OpcodeNameElement, row);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScanSummaryElement
    //
    // Scans one summary cell element's Text for every case-insensitive occurrence of the active
    // query, stamping a HighlightSpan on the element and appending a single-span SearchMatch to
    // the row for each occurrence.  The span and the match share the matched substring's offset
    // and length; the span carries the active color and the current generation for that color.
    //
    // element:  The summary cell element to scan.
    // row:      The row receiving the SearchMatches.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ScanSummaryElement(FieldDisplayNode element, OpcodeTraceRow row)
    {
        string text = element.Text;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        int scanPos = 0;
        while (scanPos <= text.Length - _searchQuery.Length)
        {
            int found = text.IndexOf(_searchQuery, scanPos, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                break;
            }

            uint generation = _highlightGenerationMap.CurrentGeneration(_activeHighlightColor);
            HighlightSpan highlightSpan = new HighlightSpan(found, _searchQuery.Length, _activeHighlightColor, generation);
            element.AddSpan(highlightSpan, _highlightGenerationMap.CurrentGeneration);

            List<HighlightSpan> spans = new List<HighlightSpan>();
            spans.Add(highlightSpan);
            SearchMatch match = new SearchMatch(row.PacketIndex, element, spans);
            row.Matches.Add(match);
            _matches.Add(match);

            if (row.EnableTrace)
            {
                DebugLog.Write(LogChannel.Fields, "OTP.ScanSummaryElement adding span to row " + row.PacketIndex);
                DebugLog.Write(LogChannel.Fields,
                    " match at offset " + found + " in '" + highlightSpan.SpanText(text) + "'", LogLevel.Info);
                DebugLog.Write(LogChannel.Fields,
                    "highlight color is " + _activeHighlightColor + ", generation " + generation);
            }

            scanPos = found + _searchQuery.Length;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ApplyCursorHighlightColor
    //
    // Paints one match in the reserved cursor color, on top of that match's normal highlight, and
    // clears the previous cursor paint everywhere in one step.  The cursor color is its own
    // generation lane in the highlight generation map: bumping that lane makes every span
    // previously stamped in the cursor color stale at once, with no per-row bookkeeping, restoring
    // those matches to their underlying highlight color.  After the bump, a fresh cursor-color span
    // is stamped over every span of the supplied match at the lane's new generation, so the whole
    // hit — all lines of a hex match, all runs of a composite field — lights in the cursor color.
    // Spans paint last-wins, so the cursor color sits above the match color already present.
    //
    // Bumping before stamping is required: stamping first and bumping after would make the just-
    // stamped spans stale.  A null match still bumps, clearing the previous cursor paint and
    // leaving nothing cursor-colored.
    //
    // match:  The match to paint as the cursor, or null to only clear the previous cursor paint.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void ApplyCursorHighlightColor(SearchMatch? match)
    {
        ArgbColor cursorColor = new ArgbColor(0x800000FFu);

        _highlightGenerationMap.Bump(cursorColor);

        if (match == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.ApplyCursorHighlightColor: no match, cursor paint cleared",
                LogLevel.Trace);
            return;
        }

        SearchMatch value = match.Value;
        FieldDisplayNode element = value.Element;
        uint cursorGeneration = _highlightGenerationMap.CurrentGeneration(cursorColor);

        int stamped = 0;
        for (int i = 0; i < value.Spans.Count; i++)
        {
            HighlightSpan source = value.Spans[i];
            HighlightSpan cursorSpan = new HighlightSpan(
                source.Start, source.Length, cursorColor, cursorGeneration);
            element.AddSpan(cursorSpan, _highlightGenerationMap.CurrentGeneration);
            stamped++;
        }

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.ApplyCursorHighlightColor: stamped " + stamped
            + " cursor span(s)", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TryParseHexQuery
    //
    // Determines whether the user's query string should be interpreted as a hex byte sequence.
    // The rule is strict: the query must contain at least two whitespace-separated tokens,
    // and every token must be exactly two hexadecimal digits.  Single-token inputs like "BEEF"
    // are not hex — they fall through to the caller's ASCII interpretation.  This avoids the
    // ambiguity where a four-letter word could be a hex value or could be a real word in a
    // packet payload.
    //
    // The query is normalized by splitting on whitespace and ignoring empty tokens, so
    // multiple spaces between bytes are tolerated.
    //
    // query:  The raw user input from the find bar.
    //
    // Returns:  A byte array containing the parsed hex bytes, or null if the query is not
    //           a valid hex sequence.  An empty or whitespace-only query returns null.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static byte[]? TryParseHexQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            DebugLog.Write(LogChannel.Opcodes, "TryParseHexQuery: empty query", LogLevel.Error);
            return null;
        }

        string[] tokens = query.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length < 2)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "TryParseHexQuery: single-token query '" + query + "' treated as ASCII", LogLevel.Warn);
            return null;
        }

        byte[] result = new byte[tokens.Length];

        for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
        {
            string token = tokens[tokenIndex];

            if (token.Length != 2)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "TryParseHexQuery: token '" + token + "' is not 2 chars, query treated as ASCII", LogLevel.Warn);
                return null;
            }

            if (!byte.TryParse(token, System.Globalization.NumberStyles.HexNumber, null, out byte parsed))
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "TryParseHexQuery: token '" + token + "' is not hex, query treated as ASCII", LogLevel.Warn);
                return null;
            }

            result[tokenIndex] = parsed;
        }

        DebugLog.Write(LogChannel.Opcodes,
            "TryParseHexQuery: parsed " + tokens.Length + " hex bytes", LogLevel.Trace);
        return result;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RecomputeRowHighlights
    //
    // Recomputes the highlight ranges and match positions for a single row
    // against the active search query.  After the Locate helpers populate
    // the row's existing Highlights and Matches lists in place, the populated
    // lists are copied into fresh List instances and assigned through the
    // row's setters.  The setter assignment raises PropertyChanged with a
    // reference that differs from the prior dependency property value, so
    // the bound attached HighlightTextBehavior.Ranges property sees a value
    // change and its OnRangesChanged callback rebuilds the TextBlock's
    // inlines.
    //
    // Mutating the existing Highlights list in place and only raising
    // PropertyChanged is not sufficient.  WPF compares the new and old
    // values of a dependency property by reference for reference types;
    // when the reference is unchanged the change callback does not run, and
    // the rebuild is deferred until container recycling sets the DP back
    // from its default value.  A fresh List reference defeats that check.
    //
    // In Deep mode the row's FieldText and HexDumpText are populated when
    // missing so the body-scan helpers have content to scan, and a row
    // that produced matches is forced into the expanded state so the
    // matches are visible without further user action.
    //
    // Hidden rows are skipped entirely: highlights and matches are cleared
    // through the setters, and no Locate helper runs.  A hidden row
    // therefore never contributes to the total match count and is
    // invisible to the cursor walks regardless of mode.
    //
    // row:    The row to recompute against the active query.
    // mode:   Search mode passed through to the Locate helpers.
    //
    // returns: The number of matches located on the row.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private int RecomputeRowHighlights(OpcodeTraceRow row, SearchMode mode)
    {
        row.Highlights.Clear();
        row.Matches.Clear();

        if (row.IsHidden)
        {
            row.Highlights = new List<HighlightRange>();
            row.Matches = new List<SearchMatch>();
            return 0;
        }

        if (_searchQueryType == SearchQueryType.Empty)
        {
            row.Highlights = new List<HighlightRange>();
            row.Matches = new List<SearchMatch>();
            return 0;
        }

        if (mode == SearchMode.Deep && row.FieldTree == null)
        {
            if (row.EnableTrace)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "OpcodeTracePresenter.RecomputeRowHighlights: Calling PopulateRowDetail for packetIndex="
                    + row.PacketIndex, LogLevel.Info);
            }

            PopulateRowDetail(row);
        }
        else
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.RecomputeRowHighlights: reusing existing field tree for packetIndex="
                + row.PacketIndex, LogLevel.Trace);
        }

        LocateSummaryHighlights(row);
        LocateFieldHighlights(row, mode);
        LocateHexDumpHighlights(row, mode);

        List<HighlightRange> rebuiltHighlights = new List<HighlightRange>(row.Highlights);
        List<SearchMatch> rebuiltMatches = new List<SearchMatch>(row.Matches);
        row.Highlights = rebuiltHighlights;
        row.Matches = rebuiltMatches;

        int matchCount = row.Matches.Count;
        if (mode == SearchMode.Deep && matchCount > 0 && !row.IsExpanded)
        {
            row.IsExpanded = true;
        }

        return matchCount;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ClearHighlights
    //
    // Clears every row's highlight ranges and match positions by assigning
    // fresh empty List instances through the row's setters.  The setter
    // assignment raises PropertyChanged with a reference that differs from
    // the prior dependency property value, so the bound attached
    // HighlightTextBehavior.Ranges property sees a value change and its
    // OnRangesChanged callback rebuilds the TextBlock's inlines as a single
    // unhighlighted run.
    //
    // Calling Clear() on the existing Highlights and Matches lists is not
    // sufficient.  WPF compares the new and old values of a dependency
    // property by reference for reference types; when the reference is
    // unchanged the change callback does not run, and the visible
    // highlights persist until container recycling sets the DP back from
    // its default value.  A fresh List reference defeats that check.
    //
    // Must be called on the UI thread; mutates _rows entries that are
    // bound to the list view.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void ClearHighlights()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            OpcodeTraceRow row = _rows[i];
            row.Highlights = new List<HighlightRange>();
            row.Matches = new List<SearchMatch>();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetSearchQuery
    //
    // Assigns the active search query and resets the cursor to inactive.
    //
    // An empty or whitespace-only query resets the query state to Empty and discards the byte
    // form.  A non-empty query is classified as Hex or Ascii, the byte form is set accordingly,
    // and the active highlight color's generation is bumped so the prior search's spans go stale.
    //
    // query:  The new search query, or null/whitespace to clear.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void SetSearchQuery(string? query)
    {
        _searchCursor = SearchCursor.Inactive;
        _matchCount = 0;

        if (string.IsNullOrWhiteSpace(query))
        {
            _searchQuery = string.Empty;
            _searchQueryType = SearchQueryType.Empty;
            _searchQueryBytes = null;
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.SetSearchQuery: cleared, cursor inactive", LogLevel.Trace);
            return;
        }

        _searchQuery = query;
        byte[]? hexBytes = TryParseHexQuery(query);
        if (hexBytes != null)
        {
            _searchQueryType = SearchQueryType.Hex;
            _searchQueryBytes = hexBytes;
        }
        else
        {
            _searchQueryType = SearchQueryType.Ascii;
            _searchQueryBytes = Encoding.ASCII.GetBytes(query);
        }

        _highlightGenerationMap.Bump(_activeHighlightColor);

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.SetSearchQuery: query='" + query + "' type=" + _searchQueryType
            + " bytes=" + _searchQueryBytes.Length + ", cursor inactive", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetSearchWrap
    //
    // Sets whether the cursor wraps around at the end of the list.  When
    // true, running off the end restarts at the opposite end and continues toward the
    // starting position.  When false, running off the end stops without finding a match.
    //
    // wrap:  True to enable wrap, false to disable.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void SetSearchWrap(bool wrap)
    {
        _searchWrap = wrap;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetRowsHidden
    //
    // Sets row.IsHidden to the supplied value for each row in the
    // collection.  Rows already at the target state are skipped so the
    // PropertyChanged event does not fire redundantly.
    //
    // Hide state is per-row and lives on the row instance.  The next
    // Refresh rebuilds rows from the catalog and produces fresh instances
    // with IsHidden = false, so per-row hide is naturally transient
    // across Refresh.
    //
    // Must be called on the UI thread; mutates rows bound to the list
    // view.
    //
    // rows:    The rows to update.
    // hidden:  The target IsHidden value to assign.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void SetRowsHidden(IEnumerable<OpcodeTraceRow> rows, bool hidden)
    {
        int changed = 0;
        int unchanged = 0;
        foreach (OpcodeTraceRow row in rows)
        {
            if (row.IsHidden == hidden)
            {
                unchanged++;
                continue;
            }

            row.IsHidden = hidden;
            changed++;
        }

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.SetRowsHidden: set " + changed
            + " row(s) to IsHidden=" + hidden + ", " + unchanged + " unchanged", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetOpcodesHidden
    //
    // Adds or removes each wire opcode value in the collection from
    // _hiddenOpcodes and flips IsHidden on every row in _rows whose
    // OpcodeValue is in the supplied set.  Rows already at the target
    // IsHidden state are skipped so the PropertyChanged event does not
    // fire redundantly.
    //
    // _hiddenOpcodes is the per-opcode persistence layer consulted by
    // Refresh; updating it here keeps the hide state durable across
    // future Refresh calls.  _rows itself is never mutated; rows are
    // collapsed in the view solely through their IsHidden flag.
    //
    // Must be called on the UI thread; mutates rows bound to the list
    // view and _hiddenOpcodes.
    //
    // opcodes:  The wire opcode values to update.
    // hidden:   The target IsHidden value to assign.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void SetOpcodesHidden(IEnumerable<OpcodeValue> opcodes, bool hidden)
    {
        HashSet<OpcodeValue> targetSet = new HashSet<OpcodeValue>(opcodes);

        int setChanged = 0;
        foreach (OpcodeValue opcode in targetSet)
        {
            if (hidden)
            {
                if (_hiddenOpcodes.Add(opcode))
                {
                    setChanged++;
                }
            }
            else
            {
                if (_hiddenOpcodes.Remove(opcode))
                {
                    setChanged++;
                }
            }
        }

        int rowsChanged = 0;
        for (int i = 0; i < _rows.Count; i++)
        {
            OpcodeTraceRow row = _rows[i];
            if (!targetSet.Contains(row.OpcodeValue))
            {
                continue;
            }
            if (row.IsHidden == hidden)
            {
                continue;
            }
            row.IsHidden = hidden;
            rowsChanged++;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CollapseAllRows
    //
    // Sets IsExpanded = false on every row in _rows.  Rows already
    // collapsed are skipped so the PropertyChanged event does not fire
    // redundantly.  _rows itself is never mutated.
    //
    // Must be called on the UI thread; mutates rows bound to the list
    // view.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void CollapseAllRows()
    {
        int collapsed = 0;
        for (int i = 0; i < _rows.Count; i++)
        {
            OpcodeTraceRow row = _rows[i];
            if (!row.IsExpanded)
            {
                continue;
            }
            row.IsExpanded = false;
            collapsed++;
        }

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.CollapseAllRows: collapsed " + collapsed + " row(s)", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetHiddenOpcodes
    //
    // Returns a fresh HashSet copy of the wire opcode values currently in
    // the per-opcode hide set.  The caller may iterate or mutate the
    // returned set without affecting the presenter's state.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public HashSet<OpcodeValue> GetHiddenOpcodes()
    {
        HashSet<OpcodeValue> snapshot = new HashSet<OpcodeValue>(_hiddenOpcodes);
        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.GetHiddenOpcodes: returned snapshot of "
            + snapshot.Count + " opcode(s)", LogLevel.Trace);
        return snapshot;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ClearHiddenOpcodes
    //
    // Empties the per-opcode hide set.  Does not touch any row's
    // IsHidden flag; callers wanting to also make rows visible flip
    // those flags separately.
    //
    // Must be called on the UI thread.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void ClearHiddenOpcodes()
    {
        int count = _hiddenOpcodes.Count;
        _hiddenOpcodes.Clear();
        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.ClearHiddenOpcodes: cleared " + count + " opcode(s)", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindNext
    //
    // Advances the search cursor forward to the next live match and returns it, or null when the
    // trace holds no matches.
    //
    // When the cursor is inactive the match list is first rebuilt: every row is recomputed
    // against the active query, which repopulates _matches and _matchCount.  When the cursor is
    // already active the existing _matches list is walked without rebuilding.
    //
    // The forward walk over _matches is delegated to AdvanceCursorForward, which prunes stale
    // matches in place and honors the wrap setting.  The previous cursor paint is cleared and the
    // new match is painted in the cursor color by ApplyCursorHighlightColor.  On a null result
    // the cursor paint is cleared and the cursor is left inactive.
    //
    // mode:  Search mode passed to the per-row recompute when this call rebuilds.
    //
    // returns:  The SearchMatch at the new cursor position, or null when no match exists.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SearchMatch? FindNext(SearchMode mode)
    {
        if (_searchCursor.Active == false)
        {
            int rowCount = _rows.Count;
            int totalMatches = 0;
            for (int i = 0; i < rowCount; i++)
            {
                totalMatches = totalMatches + RecomputeRowHighlights(_rows[i], mode);
            }
            _matchCount = totalMatches;

            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.FindNext: rebuilt matches, " + _matches.Count
                + " located across " + rowCount + " rows", LogLevel.Trace);
        }
        else
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.FindNext: resuming active cursor at index " + _searchCursor.Value,
                LogLevel.Trace);
        }

        FieldDisplayNode? previousElement = null;
        if (_searchCursor.Active == true)
        {
            previousElement = _matches[(int)_searchCursor.Value].Element;
        }

        SearchCursor advanced = AdvanceCursorForward();

        if (advanced.Active == false)
        {
            _searchCursor = SearchCursor.Inactive;
            ApplyCursorHighlightColor(null);

            if (previousElement != null)
            {
                previousElement.NotifySpansChanged();
                DebugLog.Write(LogChannel.Opcodes,
                    "OpcodeTracePresenter.FindNext: no live match, repainted previous cursor element",
                    LogLevel.Trace);
            }

            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.FindNext: no live match, cursor cleared", LogLevel.Trace);
            return null;
        }

        _searchCursor = advanced;
        SearchMatch match = _matches[(int)advanced.Value];

        if (previousElement != null && object.ReferenceEquals(previousElement, match.Element) == false)
        {
            previousElement.NotifySpansChanged();
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.FindNext: repainted previous cursor element", LogLevel.Trace);
        }

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.FindNext: cursor at index " + advanced.Value
            + " of " + _matches.Count, LogLevel.Trace);
        return match;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindPrevious
    //
    // Advances the search cursor backward to the previous live match and returns it, or null when
    // the trace holds no matches.
    //
    // When the cursor is inactive the match list is first rebuilt: every row is recomputed
    // against the active query, which repopulates _matches and _matchCount.  When the cursor is
    // already active the existing _matches list is walked without rebuilding.
    //
    // The backward walk over _matches is delegated to AdvanceCursorBackward, which prunes stale
    // matches in place and honors the wrap setting.  The previous cursor paint is cleared and the
    // new match is painted in the cursor color by ApplyCursorHighlightColor.  On a null result
    // the cursor paint is cleared and the cursor is left inactive.
    //
    // mode:  Search mode passed to the per-row recompute when this call rebuilds.
    //
    // returns:  The SearchMatch at the new cursor position, or null when no match exists.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SearchMatch? FindPrevious(SearchMode mode)
    {
        if (_searchCursor.Active == false)
        {
            int rowCount = _rows.Count;
            int totalMatches = 0;
            for (int i = 0; i < rowCount; i++)
            {
                totalMatches = totalMatches + RecomputeRowHighlights(_rows[i], mode);
            }
            _matchCount = totalMatches;

            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.FindPrevious: rebuilt matches, " + _matches.Count
                + " located across " + rowCount + " rows", LogLevel.Trace);
        }
        else
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.FindPrevious: resuming active cursor at index " + _searchCursor.Value,
                LogLevel.Trace);
        }

        SearchCursor advanced = AdvanceCursorBackward();

        if (advanced.Active == false)
        {
            _searchCursor = SearchCursor.Inactive;
            ApplyCursorHighlightColor(null);

            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.FindPrevious: no live match, cursor cleared", LogLevel.Trace);
            return null;
        }

        _searchCursor = advanced;
        SearchMatch match = _matches[(int)advanced.Value];

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.FindPrevious: cursor at index " + advanced.Value
            + " of " + _matches.Count, LogLevel.Trace);
        return match;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindFirst
    //
    // Runs a fresh search from the start of the trace.  The cursor is forced inactive so the
    // forward walk rebuilds _matches against the active query and lands on the first live match,
    // regardless of any prior cursor position.  Returns the first match, or null when the trace
    // holds no matches.
    //
    // mode:  Search mode passed through to the forward walk's rebuild.
    //
    // returns:  The SearchMatch at the first live match, or null when no match exists.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SearchMatch? FindFirst(SearchMode mode)
    {
        _searchCursor = SearchCursor.Inactive;

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.FindFirst: cursor reset, running fresh forward search", LogLevel.Trace);

        return FindNext(mode);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RowForNearestPacketIndex
    //
    // Scans the row collection for the row whose PacketIndex is nearest the target and
    // returns it.  Distance is the unsigned magnitude of the difference, computed without
    // subtraction underflow.  On a tie the row with the lower PacketIndex is kept, so a
    // target falling exactly between two present messages resolves downward.  Returns null
    // only when the collection is empty.
    //
    // target:  The message number to seek.
    //
    // returns: The nearest row, or null when there are no rows.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeTraceRow? RowForNearestPacketIndex(uint target)
    {
        if (_rows.Count == 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.RowForNearestPacketIndex: no rows", LogLevel.Warn);
            return null;
        }

        OpcodeTraceRow best = _rows[0];
        uint bestDistance;
        if (best.PacketIndex >= target)
        {
            bestDistance = best.PacketIndex - target;
        }
        else
        {
            bestDistance = target - best.PacketIndex;
        }

        for (int i = 1; i < _rows.Count; i++)
        {
            OpcodeTraceRow row = _rows[i];

            uint distance;
            if (row.PacketIndex >= target)
            {
                distance = row.PacketIndex - target;
            }
            else
            {
                distance = target - row.PacketIndex;
            }

            if (distance < bestDistance)
            {
                best = row;
                bestDistance = distance;
            }
            else if (distance == bestDistance && row.PacketIndex < best.PacketIndex)
            {
                best = row;
            }
        }

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.RowForNearestPacketIndex: target=" + target
            + " resolved to PacketIndex=" + best.PacketIndex
            + " distance=" + bestDistance, LogLevel.Trace);

        return best;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RowForPacketIndex
    //
    // Returns the row whose PacketIndex equals the supplied value, or null when no row carries
    // that index.  The lookup is O(1) against the packet-index map maintained in lockstep with
    // the row collection.
    //
    // packetIndex:  The arrival index to resolve.
    //
    // returns:  The matching row, or null when no row carries that index.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeTraceRow? RowForPacketIndex(uint packetIndex)
    {
        OpcodeTraceRow? row;
        if (_rowByPacketIndex.TryGetValue(packetIndex, out row))
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.RowForPacketIndex: resolved index " + packetIndex
                + " to a row", LogLevel.Trace);
            return row;
        }

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.RowForPacketIndex: no row for index " + packetIndex, LogLevel.Warn);
        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AdvanceCursorForward
    //
    // Scans _matches forward from the position after the current cursor to the next live match,
    // deleting stale matches in place as they are encountered.  A match is stale when its anchor
    // span's generation no longer equals the current generation for that span's color; such a
    // match paints nothing and is removed from _matches so the list self-prunes during the walk.
    //
    // Deletion shifts the tail down by one, so after a delete the same index is re-examined
    // rather than advanced.  With _searchWrap set, reaching the end wraps to index 0 and the scan
    // continues.  With wrap off, reaching the end without a live match ahead parks on the last
    // remaining index: pruning has removed every stale entry up to the end, so that entry is
    // live, and the cursor holds there rather than going inactive.
    //
    // The returned cursor is inactive only when _matches holds no entries at all.  An
    // examined-count bound guarantees termination, so a list of only stale matches terminates
    // after pruning them all.
    //
    // returns:  The cursor at the next live match, or SearchCursor.Inactive only when _matches
    //           is empty.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private SearchCursor AdvanceCursorForward()
    {
        if (_matches.Count == 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.AdvanceCursorForward: empty match list, returning Inactive",
                LogLevel.Trace);
            return SearchCursor.Inactive;
        }

        uint index;
        if (_searchCursor.Active == false)
        {
            index = 0u;
        }
        else
        {
            index = _searchCursor.Value + 1u;
        }

        uint examined = 0u;
        uint limit = (uint)_matches.Count;

        while (examined < limit)
        {
            if (index >= (uint)_matches.Count)
            {
                if (_searchWrap == false)
                {
                    uint parkIndex = (uint)_matches.Count - 1u;
                    DebugLog.Write(LogChannel.Opcodes,
                        "OpcodeTracePresenter.AdvanceCursorForward: end reached, wrap off, parking on last index "
                        + parkIndex, LogLevel.Trace);
                    return (SearchCursor)parkIndex;
                }

                index = 0u;
                DebugLog.Write(LogChannel.Opcodes,
                    "OpcodeTracePresenter.AdvanceCursorForward: wrapped to index 0", LogLevel.Trace);
            }

            SearchMatch candidate = _matches[(int)index];
            HighlightSpan anchor = candidate.Anchor;
            uint currentGeneration = _highlightGenerationMap.CurrentGeneration(anchor.OverrideColor);

            if (anchor.Generation == currentGeneration)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "OpcodeTracePresenter.AdvanceCursorForward: live match at index " + index,
                    LogLevel.Trace);
                return (SearchCursor)index;
            }

            _matches.RemoveAt((int)index);
            examined = examined + 1u;
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.AdvanceCursorForward: pruned stale match at index " + index
                + ", " + _matches.Count + " remaining", LogLevel.Trace);
        }

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.AdvanceCursorForward: scan exhausted, parking on last index "
            + ((uint)_matches.Count - 1u), LogLevel.Trace);
        return (SearchCursor)((uint)_matches.Count - 1u);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AdvanceCursorBackward
    //
    // Scans _matches backward from the position before the current cursor to the next live
    // match, deleting stale matches in place as they are encountered.  A match is stale when its
    // anchor span's generation no longer equals the current generation for that span's color;
    // such a match paints nothing and is removed from _matches so the list self-prunes during
    // the walk.
    //
    // Deletion at an index does not move any earlier index, so after a delete the scan steps to
    // the index below.  With _searchWrap set, passing the start wraps to the last index and the
    // scan continues.  With wrap off, passing the start without a live match behind parks on
    // index 0: pruning has removed every stale entry down to the start, so that entry is live,
    // and the cursor holds there rather than going inactive.
    //
    // The returned cursor is inactive only when _matches holds no entries at all.  An
    // examined-count bound guarantees termination, so a list of only stale matches terminates
    // after pruning them all.
    //
    // returns:  The cursor at the next live match, or SearchCursor.Inactive only when _matches
    //           is empty.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private SearchCursor AdvanceCursorBackward()
    {
        if (_matches.Count == 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.AdvanceCursorBackward: empty match list, returning Inactive",
                LogLevel.Trace);
            return SearchCursor.Inactive;
        }

        uint index;
        if (_searchCursor.Active == false)
        {
            index = (uint)_matches.Count - 1u;
        }
        else if (_searchCursor.Value == 0u)
        {
            if (_searchWrap == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "OpcodeTracePresenter.AdvanceCursorBackward: at start, wrap off, parking on index 0",
                    LogLevel.Trace);
                return (SearchCursor)0u;
            }

            index = (uint)_matches.Count - 1u;
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.AdvanceCursorBackward: wrapped to last index " + index,
                LogLevel.Trace);
        }
        else
        {
            index = _searchCursor.Value - 1u;
        }

        uint examined = 0u;
        uint limit = (uint)_matches.Count;

        while (examined < limit)
        {
            SearchMatch candidate = _matches[(int)index];
            HighlightSpan anchor = candidate.Anchor;
            uint currentGeneration = _highlightGenerationMap.CurrentGeneration(anchor.OverrideColor);

            if (anchor.Generation == currentGeneration)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "OpcodeTracePresenter.AdvanceCursorBackward: live match at index " + index,
                    LogLevel.Trace);
                return (SearchCursor)index;
            }

            _matches.RemoveAt((int)index);
            examined = examined + 1u;
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.AdvanceCursorBackward: pruned stale match at index " + index
                + ", " + _matches.Count + " remaining", LogLevel.Trace);

            if (_matches.Count == 0)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "OpcodeTracePresenter.AdvanceCursorBackward: list emptied by pruning, returning Inactive",
                    LogLevel.Trace);
                return SearchCursor.Inactive;
            }

            if (index == 0u)
            {
                if (_searchWrap == false)
                {
                    DebugLog.Write(LogChannel.Opcodes,
                        "OpcodeTracePresenter.AdvanceCursorBackward: start reached after prune, wrap off, parking on index 0",
                        LogLevel.Trace);
                    return (SearchCursor)0u;
                }

                index = (uint)_matches.Count - 1u;
                DebugLog.Write(LogChannel.Opcodes,
                    "OpcodeTracePresenter.AdvanceCursorBackward: wrapped to last index " + index
                    + " after prune", LogLevel.Trace);
            }
            else
            {
                index = index - 1u;
            }
        }

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.AdvanceCursorBackward: scan exhausted, parking on index 0",
            LogLevel.Trace);
        return (SearchCursor)0u;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetMessageIndexBounds
    //
    // Scans the row collection for the lowest and highest PacketIndex present and returns them.
    // The collection is not assumed to be in index order, so both ends are found by a full
    // scan.  When the collection is empty the result carries HasRows = false and both bounds
    // are zero.
    //
    // returns: The lowest and highest present message index, with HasRows marking the empty
    //          case.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public MessageIndexBounds GetMessageIndexBounds()
    {
        if (_rows.Count == 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.GetMessageIndexBounds: no rows", LogLevel.Warn);
            return new MessageIndexBounds(0u, 0u, false);
        }

        uint lowest = _rows[0].PacketIndex;
        uint highest = _rows[0].PacketIndex;

        for (int i = 1; i < _rows.Count; i++)
        {
            uint index = _rows[i].PacketIndex;
            if (index < lowest)
            {
                lowest = index;
            }
            if (index > highest)
            {
                highest = index;
            }
        }

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.GetMessageIndexBounds: lowest=" + lowest
            + " highest=" + highest + " rows=" + _rows.Count, LogLevel.Trace);

        return new MessageIndexBounds(lowest, highest, true);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Clear
    //
    // Resets every field to its initial state.  Must be called on the UI thread.
    ///////////////////////////////////////////////////////////////////////////////////////
    public void Clear()
    {
        _rows.Clear();
        _rowByPacketIndex.Clear();  
        _hiddenOpcodes.Clear();
        _colorByOpcode.Clear();
        _colorByPacketIndex.Clear();
        lock (_characterNameCacheLock)
        {
            _characterNameCache.Clear();
        }
        _maxHexBytes = 64;
        _searchQuery = string.Empty;
        _searchQueryBytes = null;
        _matchCount = 0;
        _searchQueryType = SearchQueryType.Empty;
        _searchWrap = false;

        DebugLog.Write(LogChannel.Opcodes, "OpcodeTracePresenter.Clear: cleared", LogLevel.Trace);
    }
}
