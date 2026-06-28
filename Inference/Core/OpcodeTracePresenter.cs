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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

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
public partial class OpcodeTracePresenter
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
    private readonly HighlightGenerationMap _highlightGenerationMap;
    private ArgbColor _activeHighlightColor;
    private readonly List<SearchMatch> _matches;
    private readonly System.Windows.Controls.ListView _opcodeTraceList;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeTracePresenter (constructor)
    //
    // Captures the catalog reference for snapshotting on Refresh.
    //
    // catalog:  The PacketCatalog the presenter reads on Refresh.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeTracePresenter(PacketCatalog catalog, System.Windows.Controls.ListView opcodeTraceList)
    {
        _catalog = catalog;
        _opcodeTraceList = opcodeTraceList;
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
        _searchCursor = new SearchCursor(this);
        _highlightGenerationMap = new HighlightGenerationMap();
        _activeHighlightColor = new ArgbColor(0u);
        _matches = new List<SearchMatch>();

        // The FieldHighlightBehavior needs to know the generations so that it can clean up old highlights
        FieldHighlightBehavior.SetGenerationMap(_highlightGenerationMap);

        _sessionAddedHandler = OnSessionAdded;
        GlassContext.SignalBus.Subscribe(_sessionAddedHandler);
    }
    ///////////////////////////////////////////////////////////////////////////////////////////
    // NumCurrentMatches
    //
    // The number of matches for the active search.  This is cleared at the start of each new search
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int NumCurrentMatches
    {
        get
        {
            return _matchCount;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CursorMessage
    //
    // The message index of the row the cursor sits on, or None when the cursor holds no row.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public MessageIndex CursorMessage
    {
        get
        {
            return _searchCursor.CurrentMessage;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CursorOrdinal
    //
    // The 1-based position of the cursor's match within the match list, for display as
    // "ordinal of total".  Returns 0 when the cursor is OnRow, meaning no match is current.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public uint CursorOrdinal
    {
        get
        {
            if (_searchCursor.State != SearchCursor.CursorState.OnMatch)
            {
                return 0u;
            }

            return _searchCursor.MatchIndex + 1u;
        }
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
        List<CatalogedPacket> filteredPackets = new List<CatalogedPacket>();
        for (int i = 0; i < opcodes.Length; i++)
        {
            OpcodeValue opcode = opcodes[i];
            if (_hiddenOpcodes.Contains(opcode))
            {
                continue;
            }
            List<CatalogedPacket> bucket = _catalog.PacketsFor(opcode, null, null, int.MaxValue);
            filteredPackets.AddRange(bucket);
        }

        filteredPackets.Sort((a, b) => a.PacketIndex.CompareTo(b.PacketIndex));

        _rows.Clear();
        _rowByPacketIndex.Clear();
        _matches.Clear();
        _matchCount = 0;

        for (int packetIndex = 0; packetIndex < filteredPackets.Count; packetIndex++)
        {
            OpcodeValue opcodeToDisplay;

            CatalogedPacket packet = filteredPackets[packetIndex];
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

            // create an OTR for the packet.  One OTR for each packet in filteredPackets
            OpcodeTraceRow row = new OpcodeTraceRow(packet.PacketIndex,
                            timestampLocal, opcodeToDisplay, opcodeName, packet.Metadata.Channel,
                            characterName, length, packet.Payload,
                            (ushort)packet.Metadata.SourcePort, (ushort)packet.Metadata.DestPort);
            row.Color = GetPacketColor(packet.PacketIndex, packet.Opcode);
            _rows.Add(row);
            _rowByPacketIndex[packet.PacketIndex] = row;
        }

        // No active search.  Reset the cursor.  If there are no rows then it probably does not matter.
        // A PacketIndex of 0 may not make any sense.
        if (_rows.Count > 0)
        {
            DebugLog.Write(LogChannel.Opcodes, "Refresh call#1 to MoveToMessage", LogLevel.Info);
            _searchCursor.MoveToMessage(_rows[0].PacketIndex);
        }
        else
        {
            DebugLog.Write(LogChannel.Opcodes, "Refresh call#2 to MoveToMessage", LogLevel.Info);
            _searchCursor.MoveToMessage(0u);
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
    // GetPacketColor
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
    private uint GetPacketColor(uint packetIndex, OpcodeValue opcode)
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
    // PacketIndex has its Color recomputed via GetPacketColor so the visible
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

        _rowByPacketIndex[packetIndex].Color = GetPacketColor(packetIndex, _rowByPacketIndex[packetIndex].OpcodeValue);
    }
    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetOpcodeColor
    //
    // Sets or clears the per-opcode color override for the given wire
    // opcode value.  Writing 0 removes the entry (zero is the no-override
    // sentinel).  After the map update, every visible row with the matching
    // OpcodeValue has its Color recomputed via GetPacketColor so per-packet
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
                row.Color = GetPacketColor(row.PacketIndex, row.OpcodeValue);
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
            row.HexDumpElement.PacketIndex = row.PacketIndex;
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

        if (root != null)
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
    // MoveCursorToMessage
    //
    // Moves the search cursor to the row named by the given message index.
    //
    // messageIndex:  The message index of the row to move the cursor to.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void MoveCursorToMessage(uint messageIndex)
    {
        _searchCursor.MoveToMessage(messageIndex);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScrollMatchIntoView
    //
    // Brings a SearchMatch into the visible area of the trace list with context around it, then
    // runs the supplied settled action once the scroll's layout has completed.  The match's row
    // is centered in the outer list viewport so it does not land at an edge.  When the match's
    // element is displayed inside an inner per-row ScrollViewer (the expanded Field or Hex
    // detail), the anchor span's character offset is centered within that inner viewport; a
    // summary-cell element has no inner ScrollViewer and needs no offset scroll because centering
    // the row already brings it into view.
    //
    // The work runs in three layout phases, each its own dispatcher turn at Loaded priority, so
    // the layout each phase depends on has settled before the next runs: the first turn waits for
    // the row container and any expanded inner ScrollViewers to realize; the second centers the
    // row in the outer list, which re-realizes containers; the third measures the now-settled
    // container, finds the element's TextBlock, and scrolls the anchor offset into the inner
    // viewport.  The settled action runs at the end of the third turn, after the anchor is in
    // view, so a caller can defer work such as cursor painting until scrolling has finished and
    // the viewport no longer moves.
    //
    // Returns silently with a log entry when the row's container or the element's TextBlock is not
    // realized; the settled action still runs in those cases so a caller's deferred work is not
    // lost when only the inner offset scroll could not be performed.  Does not modify the list
    // view's selection.
    //
    // row:        The row containing the match.
    // match:      The SearchMatch to bring into view.
    // onSettled:  Action invoked at the end of the final layout phase, or null for no action.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ScrollMatchIntoView(OpcodeTraceRow row, SearchMatch match, Action? onSettled)
    {
        FieldDisplayNode element = match.Element;
        int anchorOffset = match.Anchor.Start;

        DebugLog.Write(LogChannel.Opcodes, "ScrollMatchIntoView: row " + row.PacketIndex +
            ", match for row " + match.PacketIndex, LogLevel.Info);

        _opcodeTraceList.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new System.Action(() =>
        {
            CenterRowInList(row);

            _opcodeTraceList.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new System.Action(() =>
            {
                DependencyObject? container = _opcodeTraceList.ItemContainerGenerator.ContainerFromItem(row);
                if (container == null)
                {
                    DebugLog.Write(LogChannel.Opcodes,
                        "OpcodeTracePresenter.ScrollMatchIntoView: container not realized for row, ignoring", LogLevel.Warn);

                    if (onSettled != null)
                    {
                        onSettled();
                    }
                    return;
                }

                TextBlock? elementBlock = FindElementTextBlock(container, element);
                if (elementBlock == null)
                {
                    DebugLog.Write(LogChannel.Opcodes,
                        "OpcodeTracePresenter.ScrollMatchIntoView: no TextBlock for element under container, ignoring",
                        LogLevel.Warn);

                    if (onSettled != null)
                    {
                        onSettled();
                    }
                    return;
                }

                ScrollViewer? outerScroller = FindDescendantScrollViewer(_opcodeTraceList);
                ScrollViewer? elementScroller = FindAncestorScrollViewer(elementBlock);

                if (elementScroller != null)
                {
                    DebugLog.Write(LogChannel.Opcodes, "element scroller exists", LogLevel.Info);
                }
                else
                {
                    DebugLog.Write(LogChannel.Opcodes, "element scroller DOES NOT EXIST", LogLevel.Info);
                }

                if (outerScroller != null)
                {
                    DebugLog.Write(LogChannel.Opcodes, "outer scroller exists", LogLevel.Info);
                }
                else
                {
                    DebugLog.Write(LogChannel.Opcodes, "outer scroller DOES NOT EXIST", LogLevel.Info);
                }

                if (elementScroller != null && !object.ReferenceEquals(elementScroller, outerScroller))
                {
                    ScrollOffsetIntoView(elementBlock, anchorOffset);
                }

                if (onSettled != null)
                {
                    onSettled();
                }
            }));
        }));
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CenterRowInList
    //
    // Scrolls the Opcode Trace list so the given row sits vertically centered in the outer
    // viewport, giving the match context above and below rather than landing it at an edge.  The
    // list is item-scrolling (CanContentScroll defaults true for a ListView), so the outer
    // ScrollViewer's offsets and viewport are in item units, not pixels: centering is the row's
    // item index minus half the viewport's item count, clamped to the scrollable range.
    //
    // Returns silently with a log entry when the row is not in the list or the outer ScrollViewer
    // cannot be found.
    //
    // row:  The row to center.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void CenterRowInList(OpcodeTraceRow row)
    {
        int rowIndex = _opcodeTraceList.Items.IndexOf(row);
        if (rowIndex < 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.CenterRowInList: row not in list, ignoring", LogLevel.Warn);
            return;
        }

        ScrollViewer? outerScroller = FindDescendantScrollViewer(_opcodeTraceList);
        if (outerScroller == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.CenterRowInList: no outer ScrollViewer, ignoring", LogLevel.Warn);
            return;
        }

        double viewportItems = outerScroller.ViewportHeight;
        double targetOffset = rowIndex - (viewportItems / 2.0);

        if (targetOffset < 0)
        {
            targetOffset = 0;
        }
        double maxOffset = outerScroller.ScrollableHeight;
        if (targetOffset > maxOffset)
        {
            targetOffset = maxOffset;
        }

        outerScroller.ScrollToVerticalOffset(targetOffset);

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.CenterRowInList: centered packetIndex=" + row.PacketIndex
            + " rowIndex=" + rowIndex + " viewportItems=" + viewportItems
            + " targetOffset=" + targetOffset, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindDescendantScrollViewer
    //
    // Walks down the visual tree from a starting element and returns the first ScrollViewer
    // found in breadth-first order.  Used to reach a ListView's own template ScrollViewer.
    // Returns null when no ScrollViewer exists beneath the starting element.
    //
    // root:  The element from which to begin the downward walk.
    //
    // returns:  The first descendant ScrollViewer, or null when none exists.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);

            ScrollViewer? asScroller = child as ScrollViewer;
            if (asScroller != null)
            {
                return asScroller;
            }

            ScrollViewer? found = FindDescendantScrollViewer(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindElementTextBlock
    //
    // Walks the visual tree under a container and returns the first TextBlock whose
    // FieldHighlightBehavior.FDN attached property is the requested element.  Returns null when no
    // such TextBlock is realized under the container, which happens when the container has not yet
    // been laid out or when the element belongs to a collapsed or unscrolled part of the row.
    //
    // container:  The realized container for the row of interest.
    // element:    The FieldDisplayNode whose displaying TextBlock to locate.
    //
    // returns:    The matching TextBlock, or null when none is found.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static TextBlock? FindElementTextBlock(DependencyObject container, FieldDisplayNode element)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(container);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(container, i);

            TextBlock? asTextBlock = child as TextBlock;
            if (asTextBlock != null)
            {
                FieldDisplayNode? childElement = FieldHighlightBehavior.GetFDN(asTextBlock);
                if (object.ReferenceEquals(childElement, element))
                {
                    return asTextBlock;
                }
            }

            TextBlock? found = FindElementTextBlock(child, element);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindAncestorScrollViewer
    //
    // Walks up the visual tree from a starting element and returns the nearest ScrollViewer
    // ancestor.  Returns null when no ScrollViewer is found above the starting element.
    //
    // start:    The element from which to begin the upward walk.  May be null, in which case
    //           null is returned.
    //
    // returns:  The nearest enclosing ScrollViewer, or null when none exists above start.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject? start)
    {
        if (start == null)
        {
            return null;
        }

        DependencyObject? current = VisualTreeHelper.GetParent(start);
        while (current != null)
        {
            ScrollViewer? asScroller = current as ScrollViewer;
            if (asScroller != null)
            {
                return asScroller;
            }
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScrollOffsetIntoView
    //
    // Scrolls the ScrollViewer ancestor of a TextBlock so the character at the given offset is
    // inside the viewport with a 20-pixel margin.  The character rectangle is obtained by
    // resolving the character offset to a TextPointer through the TextBlock's Inlines, then asking
    // that TextPointer for its forward character rect and transforming the rect into the
    // ScrollViewer's coordinate space.  Both vertical and horizontal offsets are adjusted so the
    // rectangle is visible.
    //
    // Returns silently with a log entry when the TextBlock has no ScrollViewer ancestor, when the
    // offset is outside the TextBlock's rendered text, or when the resulting character rectangle
    // is empty.
    //
    // text:    The TextBlock containing the match.
    // offset:  Zero-based character offset within the TextBlock's rendered text.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void ScrollOffsetIntoView(TextBlock text, int offset)
    {
        const double margin = 20.0;

        ScrollViewer? scroller = FindAncestorScrollViewer(text);
        if (scroller == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.ScrollOffsetIntoView: no ScrollViewer ancestor, ignoring", LogLevel.Warn);
            return;
        }

        TextPointer? pointer = GetTextPointerAtCharacterOffset(text, offset);
        if (pointer == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.ScrollOffsetIntoView: offset " + offset + " could not be resolved to a TextPointer", LogLevel.Error);
            return;
        }

        Rect charRect = pointer.GetCharacterRect(LogicalDirection.Forward);
        if (charRect.IsEmpty || double.IsInfinity(charRect.Top) || double.IsInfinity(charRect.Left))
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.ScrollOffsetIntoView: empty character rect at offset " + offset, LogLevel.Warn);
            return;
        }

        GeneralTransform toScroller = text.TransformToAncestor(scroller);
        Rect rectInScroller = toScroller.TransformBounds(charRect);

        double currentVertical = scroller.VerticalOffset;
        double viewportHeight = scroller.ViewportHeight;
        double rectTop = rectInScroller.Top + currentVertical;
        double rectCenter = rectTop + (rectInScroller.Height / 2.0);

        double newVertical = rectCenter - (viewportHeight / 2.0);
        if (newVertical < 0)
        {
            newVertical = 0;
        }
        double maxVertical = scroller.ScrollableHeight;
        if (newVertical > maxVertical)
        {
            newVertical = maxVertical;
        }

        double currentHorizontal = scroller.HorizontalOffset;
        double viewportWidth = scroller.ViewportWidth;
        double rectLeft = rectInScroller.Left + currentHorizontal;
        double rectRight = rectInScroller.Right + currentHorizontal;
        double newHorizontal = currentHorizontal;

        if (rectLeft - margin < currentHorizontal)
        {
            newHorizontal = rectLeft - margin;
            if (newHorizontal < 0)
            {
                newHorizontal = 0;
            }
        }
        else if (rectRight + margin > currentHorizontal + viewportWidth)
        {
            newHorizontal = rectRight + margin - viewportWidth;
        }

        if (newVertical != currentVertical)
        {
            scroller.ScrollToVerticalOffset(newVertical);
        }
        if (newHorizontal != currentHorizontal)
        {
            scroller.ScrollToHorizontalOffset(newHorizontal);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetTextPointerAtCharacterOffset
    //
    // Returns a TextPointer at the given character offset into a TextBlock's rendered text.  The
    // TextBlock's text may be composed of multiple Run inlines (as produced by
    // HighlightTextBehavior); the offset is into the concatenated visible text, not into the
    // TextBlock's element-symbol space.
    //
    // Walks the TextBlock's Inlines, accumulating text length per Run.  When the cumulative length
    // reaches the requested offset, returns a TextPointer at the position inside that Run.  Returns
    // null when the offset is past the end of the rendered text or when the TextBlock contains
    // inline types other than Run that this helper does not know how to traverse.
    //
    // text:    The TextBlock whose rendered text is being indexed.
    // offset:  Zero-based character offset into the concatenated rendered text.
    //
    // returns: A TextPointer at the requested character position, or null when the offset is out
    //          of range.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static TextPointer? GetTextPointerAtCharacterOffset(TextBlock text, int offset)
    {
        if (offset < 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.GetTextPointerAtCharacterOffset: negative offset " + offset, LogLevel.Error);
            return null;
        }

        int totalRunLength = 0;
        foreach (Inline countInline in text.Inlines)
        {
            Run? countRun = countInline as Run;
            if (countRun != null)
            {
                totalRunLength += countRun.Text.Length;
            }
        }

        int remaining = offset;
        foreach (Inline inline in text.Inlines)
        {
            Run? run = inline as Run;
            if (run == null)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "OpcodeTracePresenter.GetTextPointerAtCharacterOffset: non-Run inline encountered, cannot traverse", LogLevel.Error);
                return null;
            }

            int runLength = run.Text.Length;
            if (remaining <= runLength)
            {
                TextPointer? pointer = run.ContentStart.GetPositionAtOffset(remaining, LogicalDirection.Forward);
                if (pointer == null)
                {
                    DebugLog.Write(LogChannel.Opcodes,
                        "OpcodeTracePresenter.GetTextPointerAtCharacterOffset: Run.ContentStart returned null for offset "
                        + remaining + " within run of length " + runLength, LogLevel.Error);
                }
                return pointer;
            }

            remaining = remaining - runLength;
        }

        return null;
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
        element.PacketIndex = row.PacketIndex;

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
    // RebuildIfChanged
    //
    // Rebuilds the match list when the supplied query differs from the cached query.  Every row
    // is recomputed against the query, repopulating _matches and _matchCount, and the supplied
    // query becomes the cached query.  When the query is unchanged the existing match list is
    // left intact.
    //
    // query:  The current find-box query to compare against the cached query.
    // mode:   Search mode passed through to the per-row recompute.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void RebuildIfChanged(string query, SearchMode mode)
    {
        if (query == _searchQuery)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.RebuildIfChanged: query unchanged, matches kept",
                LogLevel.Trace);
            return;
        }

        SetSearchQuery(query);

        int rowCount = _rows.Count;
        int totalMatches = 0;
        for (int i = 0; i < rowCount; i = i + 1)
        {
            totalMatches = totalMatches + RecomputeRowHighlights(_rows[i], mode);
        }
        _matchCount = totalMatches;

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.RebuildIfChanged: rebuilt, " + _matches.Count
            + " matches across " + rowCount + " rows", LogLevel.Trace);
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
        int startingMatchCount = _matches.Count;

        if (row.IsHidden)
        {
            return 0;
        }

        if (_searchQueryType == SearchQueryType.Empty)
        {
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

        int currentRowMatches = _matches.Count - startingMatchCount;

        if (mode == SearchMode.Deep && currentRowMatches > 0 && !row.IsExpanded)
        {
            row.IsExpanded = true;
        }

        return currentRowMatches;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetSearchQuery
    //
    // Assigns the active search query and its derived byte form.  An empty or whitespace-only
    // query resets the query state to Empty and discards the byte form.  A non-empty query is
    // classified as Hex or Ascii, the byte form is set accordingly, and the active highlight
    // color's generation is bumped so the prior search's spans go stale.  Does not touch the
    // cursor; cursor parking is the caller's responsibility.
    //
    // query:  The new search query, or null/whitespace to clear.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void SetSearchQuery(string? query)
    {
        _matchCount = 0;

        if (string.IsNullOrWhiteSpace(query))
        {
            _searchQuery = string.Empty;
            _searchQueryType = SearchQueryType.Empty;
            _searchQueryBytes = null;
            DebugLog.Write(LogChannel.Opcodes,
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

        // invalidate the current color's highlights.
        _highlightGenerationMap.Bump(_activeHighlightColor);

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.SetSearchQuery: query='" + query + "' type=" + _searchQueryType
            + " bytes=" + _searchQueryBytes.Length, LogLevel.Trace);
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
        _searchCursor.SetWrap(wrap);
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
    // Advances the search cursor forward to the next live match and returns it, or null when no
    // live match exists.
    //
    // RebuildIfChanged repopulates _matches when the supplied query differs from the cached
    // query; an unchanged query walks the existing list.  The forward walk, stale pruning, wrap
    // handling, scroll-into-view, and cursor painting are all owned by the cursor's
    // AdvanceForward.  After the advance the cursor is OnMatch when it landed on a live match and
    // the match at its index is returned; any other state means no live match was found and null
    // is returned.
    //
    // query:  The current find-box query, compared against the cached query to decide rebuild.
    // mode:   Search mode passed to the per-row recompute when this call rebuilds.
    //
    // returns:  The SearchMatch the cursor landed on, or null when no live match exists.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SearchMatch? FindNext(string query, SearchMode mode)
    {
        RebuildIfChanged(query, mode);

        _searchCursor.AdvanceForward();

        if (_searchCursor.State != SearchCursor.CursorState.OnMatch)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.FindNext: no live match found", LogLevel.Trace);
            return null;
        }

        SearchMatch match = _matches[(int)_searchCursor.MatchIndex];

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.FindNext: cursor at match index " + _searchCursor.MatchIndex
            + " of " + _matches.Count, LogLevel.Trace);
        return match;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindPrevious
    //
    // Advances the search cursor backward to the previous live match and returns it, or null when
    // no live match exists.
    //
    // RebuildIfChanged repopulates _matches when the supplied query differs from the cached
    // query; an unchanged query walks the existing list.  The backward walk, stale pruning, wrap
    // handling, scroll-into-view, and cursor painting are all owned by the cursor's
    // AdvanceBackward.  After the advance the cursor is OnMatch when it landed on a live match and
    // the match at its index is returned; any other state means no live match was found and null
    // is returned.
    //
    // query:  The current find-box query, compared against the cached query to decide rebuild.
    // mode:   Search mode passed to the per-row recompute when this call rebuilds.
    //
    // returns:  The SearchMatch the cursor landed on, or null when no live match exists.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SearchMatch? FindPrevious(string query, SearchMode mode)
    {
        RebuildIfChanged(query, mode);

        _searchCursor.AdvanceBackward();

        if (_searchCursor.State != SearchCursor.CursorState.OnMatch)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.FindPrevious: no live match found", LogLevel.Trace);
            return null;
        }

        SearchMatch match = _matches[(int)_searchCursor.MatchIndex];

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.FindPrevious: cursor at match index " + _searchCursor.MatchIndex
            + " of " + _matches.Count, LogLevel.Trace);
        return match;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindFirst
    //
    // Runs a fresh forward search from the start of the trace.  The cursor is parked OnRow at the
    // first row so the forward walk resolves from the top regardless of any prior cursor position,
    // then FindNext rebuilds against the query if needed and lands on the first live match.  When
    // there are no rows the cursor is parked at packet index zero and FindNext returns null over
    // the empty match list.  Returns the first match, or null when no live match exists.
    //
    // query:  The current find-box query, passed through to FindNext.
    // mode:   Search mode passed through to FindNext.
    //
    // returns:  The SearchMatch at the first live match, or null when no live match exists.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public SearchMatch? FindFirst(string query, SearchMode mode)
    {
        if (_rows.Count > 0)
        {
            DebugLog.Write(LogChannel.Opcodes, "FindFirst call#1 to MoveToMessage", LogLevel.Info);
            _searchCursor.MoveToMessage(_rows[0].PacketIndex);
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.FindFirst: parked cursor OnRow at first row packetIndex "
                + _rows[0].PacketIndex + ", running fresh forward search", LogLevel.Trace);
        }
        else
        {
            DebugLog.Write(LogChannel.Opcodes, "FindFirst call#2 to MoveToMessage", LogLevel.Info);
            _searchCursor.MoveToMessage(0u);
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.FindFirst: no rows, parked cursor at packet index 0", LogLevel.Trace);
        }

        return FindNext(query, mode);
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
    // RemoveMatchIfStale
    //
    // Removes the match at the given index when it is stale, leaving a live match in place.  A
    // match is stale when its anchor span's generation no longer equals the current generation
    // for that span's color; a stale match is removed from _matches in place, shifting the tail
    // down by one.
    //
    // index:  The subscript into _matches to test.  Must be in range; callers walk only indices
    //         they have bounds-checked.
    //
    // returns:  True when the match was stale and removed; false when it was live and left in
    //           place.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private bool RemoveMatchIfStale(uint index)
    {
        SearchMatch candidate = _matches[(int)index];
        HighlightSpan anchor = candidate.Anchor;
        uint currentGeneration = _highlightGenerationMap.CurrentGeneration(anchor.OverrideColor);

        if (anchor.Generation == currentGeneration)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.RemoveMatchIfStale: live match at index " + index
                + ", left in place", LogLevel.Trace);
            return false;
        }

        _matches.RemoveAt((int)index);
        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.RemoveMatchIfStale: pruned stale match at index " + index
            + ", " + _matches.Count + " remaining", LogLevel.Trace);
        return true;
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
    // CenterRowForPacketIndex
    //
    // Scrolls the Opcode Trace list so the row carrying the given message index sits vertically
    // centered in the viewport, giving it context above and below rather than landing it at an
    // edge.  Resolves the index to its row through the packet-index map, then defers the centering
    // to a Loaded-priority dispatcher turn so the row's container has realized before the scroll
    // offset is computed.  Does nothing but log when the index resolves to no row.
    //
    // packetIndex:  The message index of the row to center.
    //
    // returns:  True when a row was resolved and centering was scheduled; false when no row
    //           carries the index.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool CenterRowForPacketIndex(uint packetIndex)
    {
        OpcodeTraceRow? row;
        if (!_rowByPacketIndex.TryGetValue(packetIndex, out row))
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.CenterRowForPacketIndex: no row for index " + packetIndex
                + ", nothing centered", LogLevel.Warn);
            return false;
        }

        _opcodeTraceList.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            CenterRowInList(row);
        }));

        DebugLog.Write(LogChannel.Opcodes,
            "OpcodeTracePresenter.CenterRowForPacketIndex: scheduled centering for index "
            + packetIndex, LogLevel.Trace);
        return true;
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

        DebugLog.Write(LogChannel.Opcodes, "OpcodeTracePresenter.Clear: cleared", LogLevel.Trace);
    }
}
