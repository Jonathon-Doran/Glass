using Glass.Core;
using Glass.Core.Logging;
using Glass.Core.Signals;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Inference.Models;
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
    private readonly HashSet<ushort> _hiddenOpcodes;
    private readonly Dictionary<ushort, uint> _colorByOpcode;
    private readonly Dictionary<uint, uint> _colorByPacketIndex;
    public ObservableCollection<OpcodeTraceRow> Rows => _rows;
    private readonly Dictionary<int, string> _characterNameCache;
    private readonly object _characterNameCacheLock;
    private readonly Action<SignalSessionAdded> _sessionAddedHandler;
    private int _maxHexBytes;
    private string _searchQuery;
    private string _lastAppliedQuery;
    private byte[]? _searchQueryBytes;
    private int _matchCount;
    private SearchQueryType _searchQueryType;
    private OpcodeTraceRow? _cursorRow;
    private int _cursorMatchIndex;
    private int _cursorOrdinal;
    private bool _searchWrap;

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
        _hiddenOpcodes = new HashSet<ushort>();
        _colorByOpcode = new Dictionary<ushort, uint>();
        _colorByPacketIndex = new Dictionary<uint, uint>();
        _characterNameCache = new Dictionary<int, string>();
        _characterNameCacheLock = new object();
        _maxHexBytes = 64;
        _searchQuery = string.Empty;
        _lastAppliedQuery = string.Empty;
        _searchQueryBytes = null;
        _matchCount = 0;
        _searchQueryType = SearchQueryType.Empty;
        _cursorRow = null;
        _cursorOrdinal = 0;
        _cursorMatchIndex = -1;

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
            "OpcodeTracePresenter.Detach: unsubscribed from SignalSessionAdded");
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
                "OpcodeTracePresenter.OnSessionAdded: null signal, ignoring");
            return;
        }

        lock (_characterNameCacheLock)
        {
            _characterNameCache[signal.SessionId] = signal.CharacterName;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.OnSessionAdded: cached sessionId="
            + signal.SessionId + " character=" + signal.CharacterName);
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
        ushort[] opcodes = _catalog.KnownOpcodes();
        List<CatalogedPacket> accumulated = new List<CatalogedPacket>();
        for (int i = 0; i < opcodes.Length; i++)
        {
            ushort opcode = opcodes[i];
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
            CatalogedPacket packet = accumulated[i];
            string timestampLocal = packet.Metadata.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
            string opcodeHex = "0x" + packet.Opcode.ToString("x4");
            string opcodeName = GlassContext.PatchRegistry.GetOpcodeName(GlassContext.CurrentPatchLevel, packet.Opcode);
            string characterName = ResolveCharacterName(packet.Metadata.SessionId);
            int length = packet.Payload.Length;

            OpcodeTraceRow row = new OpcodeTraceRow(packet.PacketIndex,
                timestampLocal, packet.Opcode, opcodeName, packet.Metadata.Channel,
                characterName, length, packet.Payload);
            row.Color = ResolveColor(packet.PacketIndex, packet.Opcode);
            _rows.Add(row);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.Refresh: rebuilt " + _rows.Count + " rows from "
            + opcodes.Length + " known opcodes (" + _hiddenOpcodes.Count + " hidden)");
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
    private uint ResolveColor(uint packetIndex, ushort opcode)
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
                + packetIndex);
        }
        else
        {
            _colorByPacketIndex[packetIndex] = argb;
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.SetPacketColor: set index="
                + packetIndex + " color=0x" + argb.ToString("x8"));
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
    public void SetOpcodeColor(ushort opcode, uint argb)
    {
        if (argb == 0)
        {
            _colorByOpcode.Remove(opcode);
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.SetOpcodeColor: cleared override for opcode=0x"
                + opcode.ToString("x4"));
        }
        else
        {
            _colorByOpcode[opcode] = argb;
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.SetOpcodeColor: set opcode=0x"
                + opcode.ToString("x4") + " color=0x" + argb.ToString("x8"));
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
    // Runs the field extractor against the row's payload and stores the formatted
    // field text on the row, and formats the row's payload as a hex dump into the
    // row's HexDumpText.  The hex dump is capped at the presenter's current
    // _maxHexBytes.
    //
    // Rows whose opcode is not in the active patch produce an empty FieldText but
    // still receive a HexDumpText so the user can examine the bytes regardless of
    // whether a field schema is available.
    //
    // row:  The row to populate.
    ///////////////////////////////////////////////////////////////////////////////////
    public void PopulateRowDetail(OpcodeTraceRow row)
    {
        ReadOnlySpan<byte> payload = row.Payload.AsReadOnlySpan();
        row.HexDumpText = FormatHexDump(payload, _maxHexBytes);

        OpcodeHandle handle = GlassContext.PatchRegistry.GetOpcodeHandle(GlassContext.CurrentPatchLevel, row.OpcodeValue);
        if ((int)handle == -1)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.PopulateRowDetail: opcode=" + row.OpcodeHex
                + " not in patch, no fields to extract; hex dump length=" + row.HexDumpText.Length);
            row.FieldText = string.Empty;
            return;
        }

        FieldBag bag = GlassContext.PatchRegistry.Rent(GlassContext.CurrentPatchLevel, handle);
        try
        {
            GlassContext.FieldExtractor.Extract(GlassContext.CurrentPatchLevel, handle, payload, bag);

            StringBuilder sb = new StringBuilder();
            BagWalker walker = bag.Walk();
            FieldBinding? binding = walker.Next();
            while (binding != null)
            {
                FieldBinding b = binding.Value;
                sb.Append(b.Name);
                sb.Append(" = ");
                sb.Append(b.Value);
                sb.Append('\n');
                binding = walker.Next();
            }
            row.FieldText = sb.ToString();
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.PopulateRowDetail: opcode=" + row.OpcodeHex
                + " field text length=" + row.FieldText.Length
                + " hex dump length=" + row.HexDumpText.Length);
        }
        finally
        {
            bag.Release();
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
    public void SetMaxHexBytes(int maxBytes)
    {
        if (maxBytes <= 0 && maxBytes != int.MaxValue)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.SetMaxHexBytes: ignoring non-positive value " + maxBytes);
            return;
        }

        if (maxBytes == _maxHexBytes)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.SetMaxHexBytes: unchanged at " + maxBytes);
            return;
        }

        _maxHexBytes = maxBytes;
        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.SetMaxHexBytes: cap set to " + maxBytes);

        int repopulated = 0;
        for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            OpcodeTraceRow row = _rows[rowIndex];
            if (row.HexDumpText == null)
            {
                continue;
            }

            ReadOnlySpan<byte> payload = row.Payload.AsReadOnlySpan();
            row.HexDumpText = FormatHexDump(payload, _maxHexBytes);
            repopulated++;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.SetMaxHexBytes: re-formatted " + repopulated
            + " populated rows out of " + _rows.Count);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FormatHexDump
    //
    // Formats a packet payload as a hex dump string in the same layout used by
    // the Analysis tab: an 8-hex-digit offset, two columns of 8 bytes each
    // separated by an extra space, then an ASCII gutter showing printable bytes
    // and '.' for everything else.  Highlighting is not applied (the opcode
    // trace renders into a plain TextBox).
    //
    // The dump is capped at maxBytes.  When the payload is longer than maxBytes,
    // the trailing line "[showing first N of M bytes]" is appended.  When
    // maxBytes is int.MaxValue the cap is treated as effectively unlimited.
    //
    // payload:   The bytes to format.
    // maxBytes:  Maximum bytes to render.  Pass int.MaxValue for no cap.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static string FormatHexDump(ReadOnlySpan<byte> payload, int maxBytes)
    {
        int payloadLength = payload.Length;
        int displayLength;
        if (maxBytes == int.MaxValue || payloadLength <= maxBytes)
        {
            displayLength = payloadLength;
        }
        else
        {
            displayLength = maxBytes;
        }

        StringBuilder sb = new StringBuilder(displayLength * 4 + 80);

        int offset = 0;
        while (offset < displayLength)
        {
            int bytesThisRow = Math.Min(16, displayLength - offset);

            sb.Append(offset.ToString("x8"));
            sb.Append("  ");

            for (int i = 0; i < 16; i++)
            {
                if (i == 8)
                {
                    sb.Append(' ');
                }

                if (i < bytesThisRow)
                {
                    sb.Append(payload[offset + i].ToString("x2"));
                    sb.Append(' ');
                }
                else
                {
                    sb.Append("   ");
                }
            }

            sb.Append('|');
            for (int i = 0; i < bytesThisRow; i++)
            {
                byte b = payload[offset + i];
                char c;
                if (b >= 0x20 && b <= 0x7e)
                {
                    c = (char)b;
                }
                else
                {
                    c = '.';
                }
                sb.Append(c);
            }
            sb.Append('|');
            sb.Append('\n');

            offset = offset + 16;
        }

        if (displayLength < payloadLength)
        {
            sb.Append("[showing first ");
            sb.Append(displayLength);
            sb.Append(" of ");
            sb.Append(payloadLength);
            sb.Append(" bytes]\n");
        }

        return sb.ToString();
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
        int payloadLength = payload.Length;

        if (queryBytes.Length > payloadLength)
        {
            return;
        }

        bool caseInsensitive = _searchQueryType == SearchQueryType.Ascii;

        int lineWidth = 8 + 2 + (16 * 3) + 1 + 1 + 16 + 1 + 1;
        int hexColumnOffset = 8 + 2;
        int asciiColumnOffset = 8 + 2 + (16 * 3) + 1 + 1;

        int searchEnd = payloadLength - queryBytes.Length + 1;
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
    // Scans the row's field text for case-insensitive occurrences of the active
    // query, appending a HighlightRange and a SearchMatch per occurrence to the
    // row's lists.  Each emitted HighlightRange carries the index it occupies in
    // the row's Matches list so the view can later identify which range belongs
    // to which SearchMatch.  In Fast mode collapsed rows are skipped.
    //
    // row:   The row whose field text is scanned.
    // mode:  Search mode gating body scans for collapsed rows.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void LocateFieldHighlights(OpcodeTraceRow row, SearchMode mode)
    {
        if (mode == SearchMode.Fast && !row.IsExpanded)
        {
            return;
        }

        string? fieldText = row.FieldText;
        if (string.IsNullOrEmpty(fieldText))
        {
            return;
        }

        int scanPos = 0;
        while (scanPos <= fieldText.Length - _searchQuery.Length)
        {
            int found = fieldText.IndexOf(_searchQuery, scanPos, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                break;
            }
            int matchIndex = row.Matches.Count;
            row.Highlights.Add(new HighlightRange(HighlightRegionType.Field, found,
                _searchQuery.Length, matchIndex, null));
            row.Matches.Add(new SearchMatch(HighlightRegionType.Field, found));
            scanPos = found + _searchQuery.Length;
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
                + _cursorMatchIndex);
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
            DebugLog.Write(LogChannel.InferenceDebug, "TryParseHexQuery: empty query");
            return null;
        }

        string[] tokens = query.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length < 2)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "TryParseHexQuery: single-token query '" + query + "' treated as ASCII");
            return null;
        }

        byte[] result = new byte[tokens.Length];

        for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
        {
            string token = tokens[tokenIndex];

            if (token.Length != 2)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "TryParseHexQuery: token '" + token + "' is not 2 chars, query treated as ASCII");
                return null;
            }

            if (!byte.TryParse(token, System.Globalization.NumberStyles.HexNumber, null, out byte parsed))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "TryParseHexQuery: token '" + token + "' is not hex, query treated as ASCII");
                return null;
            }

            result[tokenIndex] = parsed;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "TryParseHexQuery: parsed " + tokens.Length + " hex bytes");
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
    // row:    The row to recompute against the active query.
    // mode:   Search mode passed through to the Locate helpers.
    //
    // returns: The number of matches located on the row.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private int RecomputeRowHighlights(OpcodeTraceRow row, SearchMode mode)
    {
        row.Highlights.Clear();
        row.Matches.Clear();

        if (_searchQueryType == SearchQueryType.Empty)
        {
            row.Highlights = new List<HighlightRange>();
            row.Matches = new List<SearchMatch>();
            return 0;
        }

        LocateSummaryHighlights(row);
        LocateFieldHighlights(row, mode);
        LocateHexDumpHighlights(row, mode);

        List<HighlightRange> rebuiltHighlights = new List<HighlightRange>(row.Highlights);
        List<SearchMatch> rebuiltMatches = new List<SearchMatch>(row.Matches);
        row.Highlights = rebuiltHighlights;
        row.Matches = rebuiltMatches;

        return row.Matches.Count;
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
        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.ClearHighlights: clearing highlights on " + _rows.Count + " rows");
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
            ClearHighlights();
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.SetSearchQuery: cleared");
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
        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.SetSearchQuery: query='" + query + "' type=" + _searchQueryType
            + " bytes=" + _searchQueryBytes.Length);
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
        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.SetSearchWrap: wrap=" + wrap);
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
                "OpcodeTracePresenter.FindNext: no rows");
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
            startMatchIndex = _cursorMatchIndex + 1;
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.FindNext: active search, resuming at rowIndex="
                + startRowIndex + " matchIndex=" + startMatchIndex);
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
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.FindNext: recompute located " + totalMatches
                + " matches across " + rowCount + " rows");

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
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.FindNext: no active search, starting at rowIndex="
                + startRowIndex + " matchIndex=" + startMatchIndex);
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
        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.FindNext: wrap=" + _searchWrap
            + " rowsToVisit=" + rowsToVisit);

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
                _cursorOrdinal = _cursorOrdinal + 1;
                if (_cursorOrdinal > _matchCount)
                {
                    _cursorOrdinal = 1;
                }
                ApplyCursorHighlightColor(previousCursorRow);
                DebugLog.Write(LogChannel.InferenceDebug,
                    "OpcodeTracePresenter.FindNext: cursor at rowIndex=" + rowIndex
                    + " matchIndex=" + firstCandidate
                    + " ordinal=" + _cursorOrdinal);
                return row.Matches[firstCandidate];
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.FindNext: no match, cursor unchanged");
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
    // and iteration starts at the last match on the last row of _rows.
    // The selectedRow parameter is ignored.
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
    // selectedRow:  Ignored.  Present for symmetry with FindNext.
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
                "OpcodeTracePresenter.FindPrevious: no rows");
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
            startMatchIndex = _cursorMatchIndex - 1;
        }
        else
        {
            // No search is active.  Recompute every row's matches against
            // the current query, then seed iteration at the last row.
            int totalMatches = 0;
            for (int i = 0; i < rowCount; i++)
            {
                totalMatches += RecomputeRowHighlights(_rows[i], mode);
            }
            _matchCount = totalMatches;
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeTracePresenter.FindPrevious: recompute located " + totalMatches
                + " matches across " + rowCount + " rows");

            startRowIndex = rowCount - 1;
            startMatchIndex = int.MaxValue;
        }
        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.FindPrevious: starting at rowIndex=" + startRowIndex
            + " matchIndex=" + startMatchIndex);

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
            + " rowsToVisit=" + rowsToVisit);

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
                _cursorOrdinal = _cursorOrdinal - 1;
                if (_cursorOrdinal < 1)
                {
                    _cursorOrdinal = _matchCount;
                }
                ApplyCursorHighlightColor(previousCursorRow);
                DebugLog.Write(LogChannel.InferenceDebug,
                    "OpcodeTracePresenter.FindPrevious: cursor at rowIndex=" + rowIndex
                    + " matchIndex=" + candidate
                    + " ordinal=" + _cursorOrdinal);
                return row.Matches[candidate];
            }
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.FindPrevious: no match, cursor unchanged");
        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindAll
    //
    // Walks every row, recomputing highlights and accumulating the total match count
    // and the first matching row into _matchCount and _firstMatch.  Memoized against
    // _lastAppliedQuery: when the active query has not changed since the previous
    // accumulation, the walk is skipped.
    //
    // mode:  Search mode to apply at the per-row highlight recompute.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void FindAll(SearchMode mode)
    {
        if (_searchQueryType == SearchQueryType.Empty)
        {
            _matchCount = 0;
            //_firstMatch = null;
            _lastAppliedQuery = _searchQuery;
            return;
        }

        if (string.Equals(_searchQuery, _lastAppliedQuery, StringComparison.Ordinal))
        {
            return;
        }

        _matchCount = 0;
       // _firstMatch = null;

        for (int i = 0; i < _rows.Count; i++)
        {
            OpcodeTraceRow row = _rows[i];
            int rowMatches = RecomputeRowHighlights(row, mode);
            if (rowMatches > 0)
            {
                _matchCount += rowMatches;
/*                if (_firstMatch == null)
                {
                    _firstMatch = row;
                }*/
            }
        }

        _lastAppliedQuery = _searchQuery;
    }
}
