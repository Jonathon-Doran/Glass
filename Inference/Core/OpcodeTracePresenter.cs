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
    private readonly HashSet<OpcodeValue> _hiddenOpcodes;
    private readonly Dictionary<OpcodeValue, uint> _colorByOpcode;
    private readonly Dictionary<uint, uint> _colorByPacketIndex;
    public ObservableCollection<OpcodeTraceRow> Rows => _rows;
    private readonly Dictionary<int, string> _characterNameCache;
    private readonly object _characterNameCacheLock;
    private readonly Action<SignalSessionAdded> _sessionAddedHandler;
    private uint _maxHexBytes;
    private string _searchQuery;
    private byte[]? _searchQueryBytes;
    private int _matchCount;
    private SearchQueryType _searchQueryType;
    private OpcodeTraceRow? _cursorRow;
    private int _cursorMatchIndex;
    private int _cursorOrdinal;
    private bool _searchWrap;
    private readonly HighlightGenerationMap _highlightGenerationMap;
    private ArgbColor _activeHighlightColor;

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
        _cursorRow = null;
        _cursorOrdinal = 0;
        _cursorMatchIndex = -1;
        _highlightGenerationMap = new HighlightGenerationMap();
        _activeHighlightColor = new ArgbColor(0u);

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
    public OpcodeTraceRow? CursorRow
    {
        get { return _cursorRow; }
        set
        {
            _cursorRow = value;
            _cursorMatchIndex = -1;
        }
    }
    public int CursorMatchIndex
    {
        get { return _cursorMatchIndex; }
    }
    public int CursorOrdinal
    {
        get { return _cursorOrdinal; }
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

        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.Detach: unsubscribed from SignalSessionAdded", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OnSessionAdded
    //
    // SignalSessionAdded subscriber.  Writes the session id and character
    // name into the presenter's name cache so future Refresh calls can
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
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.OnSessionAdded: null signal, ignoring", LogLevel.Warn);
            return;
        }

        lock (_characterNameCacheLock)
        {
            _characterNameCache[signal.SessionId] = signal.CharacterName;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
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
        }

        DebugLog.Write(LogChannel.InferenceDebug,
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

        DebugLog.Write(LogChannel.InferenceDebug,
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
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.SetPacketColor: cleared override for index="
                + packetIndex, LogLevel.Trace);
        }
        else
        {
            _colorByPacketIndex[packetIndex] = argb;
            DebugLog.Write(LogChannel.InferenceDebug,
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
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.SetOpcodeColor: cleared override for opcode=0x"
                + opcode, LogLevel.Trace);
        }
        else
        {
            _colorByOpcode[opcode] = argb;
            DebugLog.Write(LogChannel.InferenceDebug,
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
        row.HexDumpText = HexDumpFormatter.Format(payload, _maxHexBytes);

        CatalogedPacket? packet = _catalog.PacketAt(row.PacketIndex);

        if (packet == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.PopulateRowDetail: packetIndex " + row.PacketIndex
                + " not in catalog", LogLevel.Warn);
            return;
        }

        PacketMetadata metadata = packet.Value.Metadata;

        FieldDisplayNode? root = OpcodeDispatch.Instance.Describe(payload, metadata);
        row.FieldTree = root;

        if (root == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.PopulateRowDetail: no handler for " + metadata.Opcode
                + ", no field tree", LogLevel.Trace);
        }
        else
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.PopulateRowDetail: built field tree for " + metadata.Opcode, LogLevel.Trace);
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
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.SetMaxHexBytes: unchanged at " + maxBytes, LogLevel.Trace);
            return;
        }

        _maxHexBytes = maxBytes;
        DebugLog.Write(LogChannel.InferenceDebug,
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
            row.HexDumpText = HexDumpFormatter.Format(payload, _maxHexBytes);
            repopulated++;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.SetMaxHexBytes: re-formatted " + repopulated
            + " populated rows out of " + _rows.Count, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LocateHexDumpHighlights
    //
    // Scans the row's payload for the active query bytes and appends HighlightRanges
    // and SearchMatches to the row's lists.  For each payload match, one SearchMatch
    // is appended carrying the character offset of the first hex column character of
    // the match, and two HighlightRanges are appended per line the match spans (one
    // over the hex column, one over the ASCII gutter).  Every HighlightRange emitted
    // for a single match carries the same MatchIndex, which is the index that
    // SearchMatch occupies in the row's Matches list.  ASCII queries fold 'A'-'Z'
    // to 'a'-'z' on both sides during comparison.  In Fast mode collapsed rows are
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
            DebugLog.Write(LogChannel.InferenceDebug,
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

            int matchByteInLine = matchStart - firstLine * 16;
            int matchHexStartInLine = hexColumnOffset + (matchByteInLine * 3);
            if (matchByteInLine >= 8)
            {
                matchHexStartInLine += 1;
            }
            int matchCharOffset = firstLine * lineWidth + matchHexStartInLine;

            int matchIndex = row.Matches.Count;
            row.Matches.Add(new SearchMatch(HighlightRegionType.Hex, matchCharOffset));

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

                row.Highlights.Add(new HighlightRange(HighlightRegionType.Hex,
                    lineStartInString + hexStartInLine, hexLength, matchIndex, null));

                int asciiStartInLine = asciiColumnOffset + withinLineByteIndex;
                row.Highlights.Add(new HighlightRange(HighlightRegionType.Hex,
                    lineStartInString + asciiStartInLine, sliceLength, matchIndex, null));
            }
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
    // Scans one node's Text for every case-insensitive occurrence of the active query, stamping
    // a HighlightSpan on the node and appending a node-addressed SearchMatch to the row for each,
    // then recurses into the node's children.
    //
    // node:  The node to scan.
    // row:   The row receiving the SearchMatches.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ScanFieldNode(FieldDisplayNode node, OpcodeTraceRow row)
    {
        string text = node.Text;
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
                node.AddSpan(highlightSpan, _highlightGenerationMap.CurrentGeneration);
                row.Matches.Add(new SearchMatch(node, found));

                DebugLog.Write(LogChannel.Fields,
                    "OpcodeTracePresenter.ScanFieldNode: match at offset " + found
                    + " in '" + text + "'", LogLevel.Trace);

                scanPos = found + _searchQuery.Length;
            }
        }

        for (int childIndex = 0; childIndex < node.Children.Count; childIndex++)
        {
            ScanFieldNode(node.Children[childIndex], row);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LocateSummaryHighlights
    //
    // Tests each of the row's summary cells against the active query and appends a
    // full-cell HighlightRange and a SearchMatch to the row's lists for each cell
    // that matches case-insensitively.  Each emitted HighlightRange carries the
    // index it occupies in the row's Matches list so the view can later identify
    // which range belongs to which SearchMatch.
    //
    // row:  The row whose summary cells are checked.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void LocateSummaryHighlights(OpcodeTraceRow row)
    {
        if (row.TimestampLocal.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
        {
            int matchIndex = row.Matches.Count;
            row.Highlights.Add(new HighlightRange(HighlightRegionType.SummaryTimestamp, 0,
                row.TimestampLocal.Length, matchIndex, null));
            row.Matches.Add(new SearchMatch(HighlightRegionType.SummaryTimestamp, 0));
        }

        if (row.OpcodeHex.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
        {
            int matchIndex = row.Matches.Count;
            row.Highlights.Add(new HighlightRange(HighlightRegionType.SummaryOpcodeHex, 0,
                row.OpcodeHex.Length, matchIndex, null));
            row.Matches.Add(new SearchMatch(HighlightRegionType.SummaryOpcodeHex, 0));
        }

        if (row.OpcodeName.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
        {
            int matchIndex = row.Matches.Count;
            row.Highlights.Add(new HighlightRange(HighlightRegionType.SummaryOpcodeName, 0,
                row.OpcodeName.Length, matchIndex, null));
            row.Matches.Add(new SearchMatch(HighlightRegionType.SummaryOpcodeName, 0));
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ApplyCursorHighlightColor
    //
    // Visually distinguishes the cursor's match from the rest by giving the
    // HighlightRange that belongs to the cursor's match a per-range color
    // override, and clearing any override on the row the cursor previously
    // occupied.
    //
    // When previousCursorRow is non-null and is the same row as the new
    // cursor row, the row is touched once: every range with an override is
    // rebuilt to clear it, then the first range whose MatchIndex equals
    // _cursorMatchIndex is rebuilt with the cursor color.  Otherwise the
    // previous row has its overrides cleared in one pass and the new row
    // has the cursor color applied in a second pass.
    //
    // In both passes the row's Highlights list is reassigned through the
    // row's setter so the bound HighlightTextBehavior.Ranges dependency
    // property sees a value change and rebuilds the TextBlock inlines.
    //
    // previousCursorRow:  The row the cursor was on before the current
    //                     advance, or null when no prior cursor existed.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ApplyCursorHighlightColor(OpcodeTraceRow? previousCursorRow)
    {
        const uint cursorColor = 0x800000FF;

        OpcodeTraceRow? newCursorRow = _cursorRow;

        if (previousCursorRow != null
            && !object.ReferenceEquals(previousCursorRow, newCursorRow))
        {
            List<HighlightRange> cleared = new List<HighlightRange>(previousCursorRow.Highlights.Count);
            for (int i = 0; i < previousCursorRow.Highlights.Count; i++)
            {
                HighlightRange r = previousCursorRow.Highlights[i];
                if (r.OverrideColor.HasValue)
                {
                    cleared.Add(new HighlightRange(r.Region, r.Start, r.Length, r.MatchIndex, null));
                }
                else
                {
                    cleared.Add(r);
                }
            }
            previousCursorRow.Highlights = cleared;
        }

        if (newCursorRow == null || _cursorMatchIndex < 0)
        {
            return;
        }

        List<HighlightRange> rebuilt = new List<HighlightRange>(newCursorRow.Highlights.Count);
        bool colorApplied = false;
        for (int i = 0; i < newCursorRow.Highlights.Count; i++)
        {
            HighlightRange r = newCursorRow.Highlights[i];

            if (object.ReferenceEquals(previousCursorRow, newCursorRow) && r.OverrideColor.HasValue)
            {
                r = new HighlightRange(r.Region, r.Start, r.Length, r.MatchIndex, null);
            }

            if (!colorApplied && r.MatchIndex == _cursorMatchIndex)
            {
                rebuilt.Add(new HighlightRange(r.Region, r.Start, r.Length, r.MatchIndex, cursorColor));
                colorApplied = true;
            }
            else
            {
                rebuilt.Add(r);
            }
        }
        newCursorRow.Highlights = rebuilt;

        if (!colorApplied)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.ApplyCursorHighlightColor: no range matched cursor matchIndex="
                + _cursorMatchIndex, LogLevel.Error);
        }
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
            DebugLog.Write(LogChannel.InferenceDebug, "TryParseHexQuery: empty query", LogLevel.Error);
            return null;
        }

        string[] tokens = query.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length < 2)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "TryParseHexQuery: single-token query '" + query + "' treated as ASCII", LogLevel.Warn);
            return null;
        }

        byte[] result = new byte[tokens.Length];

        for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
        {
            string token = tokens[tokenIndex];

            if (token.Length != 2)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "TryParseHexQuery: token '" + token + "' is not 2 chars, query treated as ASCII", LogLevel.Warn);
                return null;
            }

            if (!byte.TryParse(token, System.Globalization.NumberStyles.HexNumber, null, out byte parsed))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "TryParseHexQuery: token '" + token + "' is not hex, query treated as ASCII", LogLevel.Warn);
                return null;
            }

            result[tokenIndex] = parsed;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
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
    // invisible to the cursor walks in FindNext, FindPrevious, and
    // FindAll, regardless of mode.
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

        if (mode == SearchMode.Deep && row.FieldText == null)
        {
            PopulateRowDetail(row);
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
    // Assigns the active search query and clears the cursor match index.
    //
    // An empty or whitespace-only query resets the query state to Empty,
    // discards the byte form, and clears every row's highlights and
    // matches.  A non-empty query is classified as Hex or Ascii and the
    // byte form is set accordingly; row highlights and matches are left in
    // place to be overwritten by the next recompute.
    //
    // query:  The new search query, or null/whitespace to clear.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void SetSearchQuery(string? query)
    {
        _cursorMatchIndex = -1;
        _matchCount = 0;
        _cursorOrdinal = 0;

        if (string.IsNullOrWhiteSpace(query))
        {
            _searchQuery = string.Empty;
            _searchQueryType = SearchQueryType.Empty;
            _searchQueryBytes = null;
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.SetSearchQuery: cleared", LogLevel.Trace);
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

        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.SetSearchQuery: query='" + query + "' type=" + _searchQueryType
            + " bytes=" + _searchQueryBytes.Length, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetSearchWrap
    //
    // Sets whether FindNext and FindPrevious wrap around at the end of the list.  When
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

        DebugLog.Write(LogChannel.InferenceDebug,
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

        DebugLog.Write(LogChannel.InferenceDebug,
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
        DebugLog.Write(LogChannel.InferenceDebug,
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
        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.ClearHiddenOpcodes: cleared " + count + " opcode(s)", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindNext
    //
    // Advances the search cursor forward by one match and returns the
    // SearchMatch at the new cursor position, or null when no further
    // match exists.
    //
    // A search is active when _cursorMatchIndex is non-negative.  When
    // active, iteration starts at the match immediately after the cursor,
    // on the cursor's row when _cursorRow is non-null and present in
    // _rows, otherwise on row 0.  When not active, every row's Matches
    // list is recomputed against the current query using mode, and
    // iteration starts at the first match on selectedRow when non-null
    // and present in _rows, otherwise on row 0.
    //
    // Iteration walks rows forward.  With _searchWrap true, iteration
    // continues from row 0 after reaching the end and stops once every
    // row has been visited.  With _searchWrap false, iteration stops at
    // the end of _rows.
    //
    // On a non-null return the cursor is updated.  On a null return the
    // cursor is left unchanged.  Does not modify the list view's
    // selection.
    //
    // selectedRow:  Row to seed iteration when no search is active.  May
    //               be null.  Ignored when a search is active.
    // mode:         Search mode passed to RecomputeRowHighlights when
    //               this call triggers the initial search.
    //
    // returns:      The SearchMatch at the new cursor position, or null.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SearchMatch? FindNext(OpcodeTraceRow? selectedRow, SearchMode mode)
    {
        int rowCount = _rows.Count;
        if (rowCount == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.FindNext: no rows", LogLevel.Warn);
            return null;
        }

        int startRowIndex;
        int startMatchIndex;

        // Pick the starting position for iteration.
        if (_cursorMatchIndex >= 0)
        {
            // Search is active.  Resume from the row and match after the cursor.
            startRowIndex = 0;
            if (_cursorRow != null)
            {
                int found = _rows.IndexOf(_cursorRow);
                if (found >= 0)
                {
                    startRowIndex = found;
                }
            }

            if (_cursorMatchIndex == -1)
            {
                startMatchIndex = 0;
            }
            else
            {
                startMatchIndex = _cursorMatchIndex + 1;
            }
        }
        else
        {
            // No search is active.  Recompute every row's matches against
            // the current query, then seed iteration at the selected row.
            int totalMatches = 0;
            for (int i = 0; i < rowCount; i++)
            {
                totalMatches += RecomputeRowHighlights(_rows[i], mode);
            }
            _matchCount = totalMatches;

            startRowIndex = 0;
            if (selectedRow != null)
            {
                int found = _rows.IndexOf(selectedRow);
                if (found >= 0)
                {
                    startRowIndex = found;
                }
            }
            startMatchIndex = 0;
        }

        // With wrap on, visit every row in turn starting from startRowIndex.
        // Without wrap, visit only from startRowIndex to the end of _rows.
        int rowsToVisit;
        if (_searchWrap == true)
        {
            rowsToVisit = rowCount + 1;
        }
        else
        {
            rowsToVisit = rowCount - startRowIndex;
        }

        // Walk rows forward, returning the first match found.  The very
        // first row's first candidate is startMatchIndex; on every later
        // row the first candidate is index 0.
        for (int step = 0; step < rowsToVisit; step++)
        {
            int rowIndex = (startRowIndex + step) % rowCount;
            OpcodeTraceRow row = _rows[rowIndex];

            int firstCandidate;
            if (step == 0)
            {
                firstCandidate = startMatchIndex;
            }
            else
            {
                firstCandidate = 0;
            }

            // increment with wrap
            if (firstCandidate < row.Matches.Count)
            {
                OpcodeTraceRow? previousCursorRow = _cursorRow;
                _cursorRow = row;
                _cursorMatchIndex = firstCandidate;
                int ordinalCount = 0;
                for (int walkIndex = 0; walkIndex < rowIndex; walkIndex++)
                {
                    ordinalCount = ordinalCount + _rows[walkIndex].Matches.Count;
                }
                ordinalCount = ordinalCount + firstCandidate + 1;
                _cursorOrdinal = ordinalCount;
                ApplyCursorHighlightColor(previousCursorRow);
                return row.Matches[firstCandidate];
            }
        }

        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindPrevious
    //
    // Advances the search cursor backward by one match and returns the
    // SearchMatch at the new cursor position, or null when no further
    // match exists.
    //
    // A search is active when _cursorMatchIndex is non-negative.  When
    // active, iteration starts at the match immediately before the cursor,
    // on the cursor's row when _cursorRow is non-null and present in
    // _rows, otherwise on the last row.  When not active, every row's
    // Matches list is recomputed against the current query using mode,
    // and iteration starts at the last match on selectedRow when non-null
    // and present in _rows, otherwise on the last row.
    //
    // Iteration walks rows backward.  With _searchWrap true, iteration
    // continues from the last row after reaching row 0 and stops once
    // every row has been visited.  With _searchWrap false, iteration
    // stops at row 0.
    //
    // On a non-null return the cursor is updated.  On a null return the
    // cursor is left unchanged.  Does not modify the list view's
    // selection.
    //
    // selectedRow:  Row to seed iteration when no search is active.  May
    //               be null.  Ignored when a search is active.
    // mode:         Search mode passed to RecomputeRowHighlights when
    //               this call triggers the initial search.
    //
    // returns:      The SearchMatch at the new cursor position, or null.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SearchMatch? FindPrevious(OpcodeTraceRow? selectedRow, SearchMode mode)
    {
        int rowCount = _rows.Count;
        if (rowCount == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.FindPrevious: no rows", LogLevel.Warn);
            return null;
        }

        int startRowIndex;
        int startMatchIndex;

        // Pick the starting position for iteration.
        if (_cursorMatchIndex >= 0)
        {
            // Search is active.  Resume from the row and match before the cursor.
            startRowIndex = rowCount - 1;
            if (_cursorRow != null)
            {
                int found = _rows.IndexOf(_cursorRow);
                if (found >= 0)
                {
                    startRowIndex = found;
                }
            }
            if (_cursorMatchIndex == -1)
            {
                startMatchIndex = int.MaxValue;
            }
            else
            {
                startMatchIndex = _cursorMatchIndex - 1;
            }
        }
        else
        {
            // No search is active.  Recompute every row's matches against
            // the current query, then seed iteration at selectedRow.
            int totalMatches = 0;
            for (int i = 0; i < rowCount; i++)
            {
                totalMatches += RecomputeRowHighlights(_rows[i], mode);
            }
            _matchCount = totalMatches;
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.FindPrevious: recompute located " + totalMatches
                + " matches across " + rowCount + " rows", LogLevel.Trace);

            startRowIndex = rowCount - 1;
            if (selectedRow != null)
            {
                int found = _rows.IndexOf(selectedRow);
                if (found >= 0)
                {
                    startRowIndex = found;
                }
            }
            startMatchIndex = int.MaxValue;
        }
        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.FindPrevious: starting at rowIndex=" + startRowIndex
            + " matchIndex=" + startMatchIndex, LogLevel.Trace);

        // With wrap on, visit every row in turn starting from startRowIndex
        // going backward, plus one extra to revisit the start row.  Without
        // wrap, visit only from startRowIndex down to row 0.
        int rowsToVisit;
        if (_searchWrap == true)
        {
            rowsToVisit = rowCount + 1;
        }
        else
        {
            rowsToVisit = startRowIndex + 1;
        }
        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.FindPrevious: wrap=" + _searchWrap
            + " rowsToVisit=" + rowsToVisit, LogLevel.Trace);

        // Walk rows backward, returning the first match found.  On the
        // first row the candidate is min(startMatchIndex, Matches.Count - 1);
        // on every later row the candidate is Matches.Count - 1.  When the
        // resulting candidate is negative the row has nothing to offer in
        // the backward direction and we move on.
        for (int step = 0; step < rowsToVisit; step++)
        {
            int rowIndex = ((startRowIndex - step) % rowCount + rowCount) % rowCount;
            OpcodeTraceRow row = _rows[rowIndex];
            int matchCount = row.Matches.Count;

            int candidate;
            if (step == 0)
            {
                candidate = startMatchIndex;
                if (candidate > matchCount - 1)
                {
                    candidate = matchCount - 1;
                }
            }
            else
            {
                candidate = matchCount - 1;
            }

            // decrement with wrap
            if (candidate >= 0)
            {
                OpcodeTraceRow? previousCursorRow = _cursorRow;
                _cursorRow = row;
                _cursorMatchIndex = candidate;
                int ordinalCount = 0;
                for (int walkIndex = 0; walkIndex < rowIndex; walkIndex++)
                {
                    ordinalCount = ordinalCount + _rows[walkIndex].Matches.Count;
                }
                ordinalCount = ordinalCount + candidate + 1;
                _cursorOrdinal = ordinalCount;
                ApplyCursorHighlightColor(previousCursorRow);
                DebugLog.Write(LogChannel.InferenceDebug,
                    "OpcodeTracePresenter.FindPrevious: cursor at rowIndex=" + rowIndex
                    + " matchIndex=" + candidate
                    + " ordinal=" + _cursorOrdinal, LogLevel.Trace);
                return row.Matches[candidate];
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.FindPrevious: no match, cursor unchanged", LogLevel.Trace);
        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindAll
    //
    // Recomputes match highlights across every row in _rows, sums the total
    // match count into _matchCount, then walks the rows forward from
    // selectedRow looking for the first row with matches.  When one is
    // found the cursor is parked at its first match (match index 0,
    // ordinal 1) and the SearchMatch at that position is returned;
    // otherwise null is returned and the cursor is left at -1.
    //
    // The row walk visits every row exactly once, wrapping around the end
    // of _rows back to row 0 if necessary, so a positioned cursor is
    // guaranteed whenever the recompute found at least one match.
    //
    // selectedRow:  Row to seed the cursor walk.  May be null, in which
    //               case iteration starts at row 0.
    // mode:         Search mode for the per-row recompute.  Fast skips body
    //               scans on collapsed rows.
    //
    // returns:      The SearchMatch at the new cursor position, or null
    //               when the trace contains no matches.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SearchMatch? FindAll(OpcodeTraceRow? selectedRow, SearchMode mode)
    {
        if (_searchQueryType == SearchQueryType.Empty)
        {
            _matchCount = 0;
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.FindAll: empty query", LogLevel.Warn);
            return null;
        }

        int rowCount = _rows.Count;
        if (rowCount == 0)
        {
            _matchCount = 0;
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.FindAll: no rows", LogLevel.Warn);
            return null;
        }

        int totalMatches = 0;
        for (int i = 0; i < rowCount; i++)
        {
            totalMatches += RecomputeRowHighlights(_rows[i], mode);
        }
        _matchCount = totalMatches;
        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.FindAll: recompute located " + totalMatches
            + " matches across " + rowCount + " rows", LogLevel.Trace);

        if (totalMatches == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.FindAll: no matches, cursor unchanged", LogLevel.Trace);
            return null;
        }

        int startRowIndex = 0;
        if (selectedRow != null)
        {
            int found = _rows.IndexOf(selectedRow);
            if (found >= 0)
            {
                startRowIndex = found;
            }
        }
        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.FindAll: startRowIndex=" + startRowIndex, LogLevel.Trace);

        for (int step = 0; step < rowCount; step++)
        {
            int rowIndex = (startRowIndex + step) % rowCount;
            OpcodeTraceRow row = _rows[rowIndex];
            if (row.Matches.Count > 0)
            {
                OpcodeTraceRow? previousCursorRow = _cursorRow;
                _cursorRow = row;
                _cursorMatchIndex = 0;
                int ordinalCount = 0;
                for (int walkIndex = 0; walkIndex < rowIndex; walkIndex++)
                {
                    ordinalCount = ordinalCount + _rows[walkIndex].Matches.Count;
                }
                ordinalCount = ordinalCount + 1;
                _cursorOrdinal = ordinalCount;
                ApplyCursorHighlightColor(previousCursorRow);
                DebugLog.Write(LogChannel.InferenceDebug,
                    "OpcodeTracePresenter.FindAll: cursor at rowIndex=" + rowIndex
                    + " matchIndex=0 ordinal=1", LogLevel.Trace);
                return row.Matches[0];
            }
        }

        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Clear
    //
    // Resets every field to its initial state.  Must be called on the UI thread.
    ///////////////////////////////////////////////////////////////////////////////////////
    public void Clear()
    {
        _rows.Clear();
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
        _cursorRow = null;
        _cursorOrdinal = 0;
        _cursorMatchIndex = -1;
        _searchWrap = false;

        DebugLog.Write(LogChannel.InferenceDebug, "OpcodeTracePresenter.Clear: cleared", LogLevel.Trace);
    }
}
