using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.UI;
using Inference.Core;
using Inference.Models;
using Inference.UI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using static Glass.Network.Protocol.SoeConstants;

namespace Inference.Dialogs;

///////////////////////////////////////////////////////////////////////////////////////////////
// PacketDetailWindow
//
// Modeless display window for a single captured packet.  Snapshots the
// payload and metadata at construction and renders a header strip with
// the packet's identifying details, a left pane with the field
// decoding produced by the field extractor against the active patch
// level, and a right pane with the full uncapped hex dump.  The two
// panes are independent ScrollViewers separated by a GridSplitter.
// The contents are read-only but selectable so the user can copy text
// out of either pane.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class PacketDetailWindow : Window
{
    private FieldDisplayNode? _dragAnchor;
    private List<FieldDisplayNode> _visibleNodes = new List<FieldDisplayNode>();
    private bool _anchorWasHighlighted = false;
    private bool _dragOccurred = false;
    private string _hexText = string.Empty;
    private List<HighlightSpan> _hexSpans = new List<HighlightSpan>();

    // Hand-painted spans, kept apart from the find spans so no generation bump can reach them.
    // These are removed only by an explicit erase.
    private List<HighlightSpan> _manualSpans = new List<HighlightSpan>();

    // Ranges currently painted by the find and the find cursor, in dump-text character offsets.
    // These are the only ranges an erase of the find highlighting is allowed to touch.
    private List<HighlightSpan> _findPaintedSpans = new List<HighlightSpan>();

    private HighlightGenerationMap _hexGenerationMap = new HighlightGenerationMap();
    private ArgbColor _activeHighlightColor;
    private List<ByteRange> _findMatches = new List<ByteRange>();
    private uint _findCursorByte;
    private bool _findCursorValid;
    private byte[] _payload = System.Array.Empty<byte>();

    // Never null after construction; the constructor arms a swatch before anything can read it.
    private Border _selectedSwatch = null!;

    // The cursor's own highlight color, deliberately outside the sixteen-swatch palette so the
    // current match can never be confused with a match painted in a selected color.
    private static readonly ArgbColor HexCursorColor = new ArgbColor(0xFFFF0000u);

    // Character offset in the dump text at which each line begins, ascending.
    private List<int> _lineStartOffsets = new List<int>();

    // Text position at which each line begins, parallel to _lineStartOffsets.
    private List<TextPointer> _lineStartPointers = new List<TextPointer>();

    // The query text the current match list was built from, so a re-run of the same query can be
    // told from a new one.
    private string _lastFindQuery = string.Empty;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PacketDetailWindow (constructor)
    //
    // Captures the packet's identifying details into the header strip,
    // runs the field extractor against the active patch level and
    // writes the formatted result into the left pane, and formats the
    // full payload as an uncapped hex dump into the right pane.  Rows
    // whose opcode is not in the active patch leave the field pane
    // empty; the hex pane is always populated.
    //
    // packet:  The cataloged packet to display.  Retained by the
    //          window's bindings for the lifetime of the window.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public PacketDetailWindow(CatalogedPacket packet)
    {
        InitializeComponent();
        SelectHexColorSwatch(HexPatchYellow);

        ReadOnlySpan<byte> payload = packet.Payload.AsReadOnlySpan();
        int payloadLength = payload.Length;

        _payload = payload.ToArray();

        // note on version:  This usage is ok because we do not need to know the exact version
        // when obtaining the opcode name.  V1's output is identical to all other versions.

        PatchOpcode patchOpcode = new PatchOpcode(GlassContext.CurrentPatchLevel, packet.Opcode);
        string opcodeName = GlassContext.PatchRegistry.GetOpcodeName(patchOpcode);
        string opcodeHex = "0x" + packet.Opcode;
        string characterName = GlassContext.SessionRegistry.CharacterNameFromMetadata(packet.Metadata);

        Title = "Packet " + packet.PacketIndex + " — " + opcodeName;

        HeaderPacketIndex.Text = packet.PacketIndex.ToString();
        HeaderTimestamp.Text = packet.Metadata.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
        HeaderLength.Text = payloadLength.ToString() + " bytes";
        HeaderOpcode.Text = opcodeHex + "  " + opcodeName;
        HeaderChannel.Text = StreamAbbrev[packet.Metadata.Channel];
        HeaderCharacter.Text = characterName;

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow: opening packetIndex=" + packet.PacketIndex
            + " opcode=" + opcodeHex + " (" + opcodeName + ")"
            + " length=" + payloadLength, LogLevel.Trace);

        FieldDisplayNode? fieldRoot = BuildFieldTree(packet.Metadata, payload);
        if (fieldRoot != null)
        {
            FieldTree.ItemsSource = new FieldDisplayNode[] { fieldRoot };
        }
        else
        {
            FieldTree.ItemsSource = null;
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow: no field tree for " + packet.Metadata.Opcode, LogLevel.Trace);
        }
        _hexText = HexDumpFormatter.Format(payload, int.MaxValue);
        BuildHexDocument();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Window_PreviewKeyDown
    //
    // Window-level key handler for the find bar's show and hide gestures.  Ctrl+F reveals the bar
    // and moves focus to the find text box with any existing text selected, so a second Ctrl+F
    // replaces the previous query by typing.  Escape hides the bar and returns focus to the hex
    // pane.  Every other key is left alone, so the field tree's own Ctrl+C handling is unaffected.
    //
    // sender:  The key event source.
    // e:       The key event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            HexFindBar.Visibility = Visibility.Visible;
            TextBoxHexFind.Focus();
            TextBoxHexFind.SelectAll();
            e.Handled = true;

            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.Window_PreviewKeyDown: find bar shown", LogLevel.Trace);
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (HexFindBar.Visibility != Visibility.Visible)
            {
                return;
            }

            HexFindBar.Visibility = Visibility.Collapsed;
            HexDumpBox.Focus();
            e.Handled = true;

            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.Window_PreviewKeyDown: find bar hidden", LogLevel.Trace);
            return;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Toggle_HexColorSelected_Click
    //
    // Momentary apply-then-reset toggle that colors the hex pane's current text selection in the
    // armed swatch's color.  The toggle is returned to its unchecked state on every path so it
    // behaves as a button rather than a mode.  An empty selection is not an error; there is simply
    // nothing to color.
    //
    // The colored range is painted directly and recorded in the manual span list, which is never
    // cleared by a find, so hand coloring outlives every subsequent query in any color and is
    // restored underneath whenever find highlighting that covered it is removed.  The generation
    // stamped on the recorded span is zero and is not read; it is present only because the span
    // type carries the field.
    //
    // Offsets are taken against the dump text by normalizing the flow document's line breaks:
    // TextRange.Text reports a LineBreak as a carriage return and line feed pair, while the stored
    // dump text holds a single line feed, so each pair is collapsed before measuring.
    //
    // sender:  The ToggleButton that raised the event.
    // e:       Standard routed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Toggle_HexColorSelected_Click(object sender, RoutedEventArgs e)
    {
        ToggleButton? toggle = sender as ToggleButton;
        if (toggle == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.Toggle_HexColorSelected_Click: sender was not a ToggleButton, "
                + "ignoring", LogLevel.Error);
            return;
        }

        toggle.IsChecked = false;

        TextSelection selection = HexDumpBox.Selection;
        if (selection.IsEmpty)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.Toggle_HexColorSelected_Click: selection is empty, nothing to "
                + "color", LogLevel.Warn);
            return;
        }

        Paragraph? paragraph = HexDumpBox.Document.Blocks.FirstBlock as Paragraph;
        if (paragraph == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.Toggle_HexColorSelected_Click: hex document has no paragraph, "
                + "ignoring", LogLevel.Error);
            return;
        }

        TextRange prefix = new TextRange(paragraph.ContentStart, selection.Start);
        int start = prefix.Text.Replace("\r\n", "\n").Length;
        int length = selection.Text.Replace("\r\n", "\n").Length;

        if (length <= 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.Toggle_HexColorSelected_Click: selection measured zero length, "
                + "ignoring", LogLevel.Warn);
            return;
        }

        if (!PaintHexRange(start, length, _activeHighlightColor))
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.Toggle_HexColorSelected_Click: could not paint range start="
                + start + " length=" + length + ", nothing recorded", LogLevel.Warn);
            return;
        }

        _manualSpans.Add(new HighlightSpan(start, length, _activeHighlightColor, 0u));

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.Toggle_HexColorSelected_Click: colored selection start=" + start
            + " length=" + length + " color=0x" + _activeHighlightColor.ToString()
            + ", manual span count now " + _manualSpans.Count, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildFieldTree
    //
    // Asks OpcodeDispatch for the display tree the packet's handler builds for the payload.
    // Returns the root node, or null when no handler is registered for the opcode.
    //
    // metadata:  The packet's metadata.
    // payload:   Bytes to decode.
    //
    // Returns:   The root FieldDisplayNode, or null.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static FieldDisplayNode? BuildFieldTree(PacketMetadata metadata, ReadOnlySpan<byte> payload)
    {
        FieldDisplayNode? root = OpcodeDispatch.Instance.Describe(payload, metadata);

        if (root == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.BuildFieldTree: no handler for " + metadata.Opcode
                + ", no field tree", LogLevel.Trace);
        }

        return root;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FieldTree_PreviewKeyDown
    //
    // Copies the selected field nodes to the clipboard when Ctrl+C is pressed over the field
    // tree.  Walks the bound roots depth-first, appending each node whose IsSelected flag is set
    // as an indented line.  Copies nothing when no node is selected.  Ignores all other keys.
    //
    // sender:  The key event source.
    // e:       The key event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void FieldTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (FieldTree.ItemsSource == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "PacketDetailWindow.FieldTree_PreviewKeyDown: no field tree to copy", LogLevel.Warn);
            return;
        }

        StringBuilder builder = new StringBuilder();
        foreach (FieldDisplayNode root in (FieldDisplayNode[])FieldTree.ItemsSource)
        {
            AppendSelectedNodes(root, 0u, false, builder);
        }

        if (builder.Length == 0u)
        {
            DebugLog.Write(LogChannel.Opcodes, "PacketDetailWindow.FieldTree_PreviewKeyDown: no nodes selected", LogLevel.Trace);
            return;
        }

        Clipboard.SetText(builder.ToString());
        e.Handled = true;
        DebugLog.Write(LogChannel.Opcodes, "PacketDetailWindow.FieldTree_PreviewKeyDown: copied " + builder.Length + " chars", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FieldTree_PreviewMouseLeftButtonDown
    //
    // Records the node under the pointer as the drag anchor when the left button goes down over
    // the field tree, captures the anchor's highlight state before any drag can alter it, and
    // clears the drag-occurred flag so button-up can distinguish a plain click from a drag.
    // Does nothing when the pointer is over the expander toggle or not over a node.
    //
    // sender:  The mouse event source.
    // e:       The mouse event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void FieldTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? hitSource = e.OriginalSource as DependencyObject;
        DependencyObject? walk = hitSource;

        while (walk != null)
        {
            if (walk is System.Windows.Controls.Primitives.ToggleButton)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "PacketDetailWindow.FieldTree_PreviewMouseLeftButtonDown: click on expander, ignoring. Severity=Trace",
                    LogLevel.Trace);
                return;
            }
            walk = VisualTreeHelper.GetParent(walk);
        }

        TreeViewItem? item = FindAncestorTreeViewItem(hitSource);
        if (item == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.FieldTree_PreviewMouseLeftButtonDown: no item under pointer. Severity=Trace",
                LogLevel.Trace);
            return;
        }

        FieldDisplayNode? node = item.DataContext as FieldDisplayNode;
        if (node == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.FieldTree_PreviewMouseLeftButtonDown: item has no node. Severity=Warn",
                LogLevel.Warn);
            return;
        }

        _visibleNodes = new List<FieldDisplayNode>();
        foreach (FieldDisplayNode root in (FieldDisplayNode[])FieldTree.ItemsSource)
        {
            FlattenVisibleNodes(root, FieldTree, _visibleNodes);
        }

        _dragAnchor = node;
        _anchorWasHighlighted = node.IsHighlighted;
        _dragOccurred = false;

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.FieldTree_PreviewMouseLeftButtonDown: anchor='" + node.Text
            + "' anchorWasHighlighted=" + _anchorWasHighlighted
            + " visibleCount=" + _visibleNodes.Count + ". Severity=Trace",
            LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FieldTree_PreviewMouseLeftButtonUp
    //
    // Ends a highlight gesture.  When a drag occurred, the range highlight applied during
    // mouse move is left in place.  When no drag occurred, the gesture is a plain click:
    // all highlights are cleared, then the anchor's subtree is highlighted unless the anchor
    // was already highlighted before the click, making the click a toggle.  Clears the drag
    // anchor in all cases.
    //
    // sender:  The mouse event source.
    // e:       The mouse event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void FieldTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FieldTree.IsMouseCaptured == true)
        {
            FieldTree.ReleaseMouseCapture();
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.FieldTree_PreviewMouseLeftButtonUp: released mouse capture. Severity=Trace",
                LogLevel.Trace);
        }

        if (_dragAnchor == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.FieldTree_PreviewMouseLeftButtonUp: no drag anchor. Severity=Trace",
                LogLevel.Trace);
            return;
        }

        if (_dragOccurred == true)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.FieldTree_PreviewMouseLeftButtonUp: drag ended, range highlight retained. Severity=Trace",
                LogLevel.Trace);
            _dragAnchor = null;
            return;
        }

        bool newState = !_anchorWasHighlighted;
        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.FieldTree_PreviewMouseLeftButtonUp: click on anchor='" + _dragAnchor.Text
            + "' anchorWasHighlighted=" + _anchorWasHighlighted + " newState=" + newState + ". Severity=Trace",
            LogLevel.Trace);

        foreach (FieldDisplayNode root in (FieldDisplayNode[])FieldTree.ItemsSource)
        {
            SetHighlightDescendants(root, false);
        }

        if (newState == true)
        {
            SetHighlightDescendants(_dragAnchor, true);
        }

        _dragAnchor = null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AppendSelectedNodes
    //
    // Appends the given node's subtree to the builder when its IsSelected flag is set: the node
    // and all its descendants are emitted as indented lines regardless of the descendants' own
    // flags.  When the node is not selected it contributes no line, but its children are still
    // visited so a selected descendant is found.  Indentation is two spaces per depth level.
    //
    // node:      The node to test and append.
    // depth:     The node's depth in the tree, used for indentation.
    // selected:  True when an ancestor was already selected, forcing this node to emit.
    // builder:   The builder receiving the text lines.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void AppendSelectedNodes(FieldDisplayNode node, uint depth, bool selected, StringBuilder builder)
    {
        if (node == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "PacketDetailWindow.AppendSelectedNodes: null node at depth " + depth, LogLevel.Warn);
            return;
        }

        bool emit = node.IsHighlighted;

        if (emit == true)
        {
            builder.Append(' ', (int)(depth * 2u));
            builder.Append(node.Text);
            builder.Append('\n');
        }

        foreach (FieldDisplayNode child in node.Children)
        {
            AppendSelectedNodes(child, depth + 1u, emit, builder);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HexColorPatch_MouseLeftButtonUp
    //
    // Click handler shared by every color swatch above the hex pane.  Arms the clicked swatch as
    // the selected color and does nothing else; the selected color is what a find paints its
    // matches in and what the color-selected action applies to a text selection.  Clicking a swatch
    // never alters the document, so switching colors is always safe.
    //
    // sender:  The Border that raised the event.
    // e:       Standard mouse event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void HexColorPatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Border? swatch = sender as Border;
        if (swatch == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.HexColorPatch_MouseLeftButtonUp: sender was not a Border, "
                + "ignoring", LogLevel.Error);
            return;
        }

        SelectHexColorSwatch(swatch);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HexColorPatch_MouseRightButtonUp
    //
    // Right-click handler shared by every color swatch above the hex pane.  Erases every
    // hand-painted span whose color matches the clicked swatch, everywhere in the dump, and
    // removes those spans from the manual span list.  Spans in other colors are untouched, and
    // the armed swatch is not changed, so right-clicking is purely an erase gesture.
    //
    // A span whose paint cannot be cleared is kept in the list, so the record never claims a
    // range is uncolored while it still shows color.  A swatch whose Tag cannot be read as a
    // packed ARGB literal erases nothing.
    //
    // sender:  The Border that raised the event.
    // e:       Standard mouse event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void HexColorPatch_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        Border? swatch = sender as Border;
        if (swatch == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.HexColorPatch_MouseRightButtonUp: sender was not a Border, "
                + "ignoring", LogLevel.Error);
            return;
        }
        string? tagText = swatch.Tag as string;
        if (tagText == null || tagText.Length < 3)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.HexColorPatch_MouseRightButtonUp: swatch Tag missing or too "
                + "short, nothing erased", LogLevel.Error);
            return;
        }
        uint raw;
        if (!uint.TryParse(tagText.Substring(2),
            System.Globalization.NumberStyles.HexNumber, null, out raw))
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.HexColorPatch_MouseRightButtonUp: could not parse Tag '"
                + tagText + "' as hex uint, nothing erased", LogLevel.Error);
            return;
        }
        List<HighlightSpan> kept = new List<HighlightSpan>();
        int cleared = 0;
        for (int i = 0; i < _manualSpans.Count; i++)
        {
            HighlightSpan span = _manualSpans[i];
            if (span.OverrideColor.Value != raw)
            {
                kept.Add(span);
                continue;
            }
            if (PaintHexRange(span.Start, span.Length, null))
            {
                cleared++;
            }
            else
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "PacketDetailWindow.HexColorPatch_MouseRightButtonUp: could not clear span "
                    + "start=" + span.Start + " length=" + span.Length + ", span kept",
                    LogLevel.Warn);
                kept.Add(span);
            }
        }
        _manualSpans = kept;
        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.HexColorPatch_MouseRightButtonUp: color=0x"
            + raw.ToString("x8") + " cleared " + cleared + " span(s), "
            + _manualSpans.Count + " remain", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SelectHexColorSwatch
    //
    // Arms a color swatch and adopts its color as the window's selected highlight color.  The
    // swatches behave as a radio group with one armed at all times, so the previously armed
    // swatch's border is returned to its unarmed appearance before the new one is marked.  This is
    // the only method that writes either the armed swatch or the selected color, so the two cannot
    // disagree.  Arming changes no document content; it only determines the color a find paints its
    // matches in and the color the color-selected action applies.
    //
    // A swatch whose Tag cannot be read as a packed ARGB literal is rejected and the already-armed
    // swatch stays armed, so a swatch is armed from construction onward without exception.
    //
    // swatch:  The swatch Border to arm.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void SelectHexColorSwatch(Border swatch)
    {
        string? tagText = swatch.Tag as string;
        if (tagText == null || tagText.Length < 3)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.SelectHexColorSwatch: swatch Tag missing or too short, "
                + "arming unchanged", LogLevel.Error);
            return;
        }

        uint raw;
        if (!uint.TryParse(tagText.Substring(2),
            System.Globalization.NumberStyles.HexNumber, null, out raw))
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.SelectHexColorSwatch: could not parse Tag '" + tagText
                + "' as hex uint, arming unchanged", LogLevel.Error);
            return;
        }

        if (_selectedSwatch != null)
        {
            _selectedSwatch.BorderBrush = Brushes.Gray;
            _selectedSwatch.BorderThickness = new Thickness(1);
        }

        swatch.BorderBrush = Brushes.White;
        swatch.BorderThickness = new Thickness(2);
        _selectedSwatch = swatch;
        _activeHighlightColor = new ArgbColor(raw);

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.SelectHexColorSwatch: armed swatch tag='" + tagText
            + "' color=0x" + _activeHighlightColor.ToString(), LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FieldTree_MouseMove
    //
    // Extends a range highlight as the pointer drags over the field tree with the left button
    // held.  Hit-tests the pointer position to find the node under it, flattens the visible
    // nodes in display order, and sets IsHighlighted true across the inclusive range from the
    // drag anchor to that node and false elsewhere.  Marks the gesture as a drag once the
    // pointer reaches a node other than the anchor.  Does nothing when the left button is up,
    // no anchor is set, or the pointer is not over a node.
    //
    // sender:  The mouse event source.
    // e:       The mouse event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void FieldTree_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (_dragAnchor == null)
        {
            return;
        }

        Point point = e.GetPosition(FieldTree);
        HitTestResult hit = VisualTreeHelper.HitTest(FieldTree, point);
        if (hit == null)
        {
            return;
        }

        TreeViewItem? item = FindAncestorTreeViewItem(hit.VisualHit);
        if (item == null)
        {
            return;
        }

        FieldDisplayNode? current = item.DataContext as FieldDisplayNode;
        if (current == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.FieldTree_MouseMove: item has no node. Severity=Warn",
                LogLevel.Warn);
            return;
        }

        if (current != _dragAnchor)
        {
            _dragOccurred = true;
        }

        if (_dragOccurred == false)
        {
            return;
        }

        int anchorIndex = _visibleNodes.IndexOf(_dragAnchor);
        int currentIndex = _visibleNodes.IndexOf(current);
        if (anchorIndex < 0 || currentIndex < 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.FieldTree_MouseMove: anchor or current not visible"
                + " anchor='" + _dragAnchor.Text + "' current='" + current.Text + "'. Severity=Warn",
                LogLevel.Warn);
            return;
        }

        uint low = (uint)System.Math.Min(anchorIndex, currentIndex);
        uint high = (uint)System.Math.Max(anchorIndex, currentIndex);

        for (uint i = 0u; i < (uint)_visibleNodes.Count; i++)
        {
            _visibleNodes[(int)i].IsHighlighted = (i >= low && i <= high);
        }

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.FieldTree_MouseMove: highlighted [" + low + ".." + high + "]. Severity=Trace",
            LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindAncestorTreeViewItem
    //
    // Walks up the visual tree from the given element and returns the nearest TreeViewItem
    // ancestor, or null when none is found above it.
    //
    // start:    The element to walk up from.  May be null, in which case null is returned.
    //
    // Returns:  The nearest enclosing TreeViewItem, or null when none exists above start.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static TreeViewItem? FindAncestorTreeViewItem(DependencyObject? start)
    {
        if (start == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "PacketDetailWindow.FindAncestorTreeViewItem: null start", LogLevel.Trace);
            return null;
        }

        DependencyObject? current = start;
        while (current != null)
        {
            TreeViewItem? item = current as TreeViewItem;
            if (item != null)
            {
                return item;
            }
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MeasureHexPageWidth
    //
    // Returns the width in device-independent pixels needed to lay out the widest line of the
    // supplied hex dump without wrapping, measured in the hex pane's own typeface and size.  The
    // FlowDocument hosting the dump wraps at its PageWidth, so the document is given this width
    // to force a horizontal scrollbar instead of folding 77-character lines.  A small trailing
    // pad is added so the closing '|' is not clipped by the pane edge.  Empty text measures as
    // zero.
    //
    // text:  The formatted hex dump whose widest line is measured.
    //
    // Returns the required page width in device-independent pixels.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private double MeasureHexPageWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.MeasureHexPageWidth: empty text, width 0", LogLevel.Warn);
            return 0.0;
        }

        string[] lines = text.Split('\n');
        string widest = string.Empty;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length > widest.Length)
            {
                widest = lines[i];
            }
        }

        if (widest.Length == 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.MeasureHexPageWidth: no non-empty line, width 0",
                LogLevel.Warn);
            return 0.0;
        }

        Typeface typeface = new Typeface(
            HexDumpBox.FontFamily,
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        FormattedText formatted = new FormattedText(
            widest,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            HexDumpBox.FontSize,
            Brushes.Black,
            pixelsPerDip);

        double width = formatted.WidthIncludingTrailingWhitespace + 16.0;

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.MeasureHexPageWidth: widest line " + widest.Length
            + " chars over " + lines.Length + " line(s), pixelsPerDip=" + pixelsPerDip
            + ", page width " + width, LogLevel.Trace);

        return width;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildHexDocument
    //
    // Builds the hex pane's FlowDocument once from the stored dump text, uncolored, and records the
    // character offset and text position at which each line begins.  Called during construction and
    // at no other time, so the pane's scroll position and text selection are never disturbed after
    // the window opens.
    //
    // One Run is emitted per line with a LineBreak between lines, rather than leaving newline
    // characters inside a Run, so line breaks do not depend on how the flow layout treats a newline
    // character.  A line that is empty contributes no Run, only its LineBreak.  Each newline
    // consumes exactly one character offset either way, so offsets measured against the dump text
    // address the document directly.
    //
    // The two recorded lists are parallel and ascending, and are what turns a character offset into
    // a text position without walking the whole inline collection.  The document is given an
    // explicit PageWidth so 77-character lines scroll horizontally instead of wrapping, and zero
    // page and paragraph padding so the dump is not double-spaced.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void BuildHexDocument()
    {
        _lineStartOffsets = new List<int>();
        _lineStartPointers = new List<TextPointer>();

        FlowDocument document = new FlowDocument();
        document.PagePadding = new Thickness(0);

        double pageWidth = MeasureHexPageWidth(_hexText);
        if (pageWidth > 0.0)
        {
            document.PageWidth = pageWidth;
        }
        else
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.BuildHexDocument: no measurable width, leaving PageWidth at "
                + "its default", LogLevel.Warn);
        }

        Paragraph paragraph = new Paragraph();
        paragraph.Margin = new Thickness(0);
        document.Blocks.Add(paragraph);

        if (_hexText.Length == 0)
        {
            HexDumpBox.Document = document;

            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.BuildHexDocument: empty dump text, empty document",
                LogLevel.Warn);
            return;
        }

        string[] lines = _hexText.Split('\n');
        int offset = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            _lineStartOffsets.Add(offset);

            if (lines[i].Length > 0)
            {
                Run run = new Run(lines[i]);
                paragraph.Inlines.Add(run);
                _lineStartPointers.Add(run.ContentStart);
            }
            else
            {
                LineBreak placeholder = new LineBreak();
                paragraph.Inlines.Add(placeholder);
                _lineStartPointers.Add(placeholder.ContentStart);

                DebugLog.Write(LogChannel.Opcodes,
                    "PacketDetailWindow.BuildHexDocument: line " + i + " is empty, no Run emitted",
                    LogLevel.Trace);

                offset = offset + 1;
                continue;
            }

            offset = offset + lines[i].Length;

            if (i < lines.Length - 1)
            {
                paragraph.Inlines.Add(new LineBreak());
                offset = offset + 1;
            }
        }

        HexDumpBox.Document = document;

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.BuildHexDocument: " + lines.Length + " line(s) over "
            + _hexText.Length + " character(s), " + _lineStartPointers.Count
            + " line position(s) recorded", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RunHexFind
    //
    // Re-runs the find over the retained payload from the current contents of the find text box
    // and records the results as the window's match list.  An empty or whitespace-only query
    // leaves the match list empty, which is how a cleared find is expressed.
    //
    // The cursor is dropped when the query text differs from the one the previous match list was
    // built from, because a position taken from the old query's hits means nothing among the new
    // one's.  A re-run of the same text keeps the cursor, so repeatedly starting the search walks
    // through the matches instead of returning to the first one every time.
    //
    // The query is scanned in both byte forms: its parsed hex bytes when it satisfies the strict
    // hex rule, and always its ASCII bytes.  The whole payload is in scope because this window's
    // dump is uncapped.
    //
    // Matches are recorded but not painted; painting is a separate step.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void RunHexFind()
    {
        string query = TextBoxHexFind.Text;

        if (query != _lastFindQuery)
        {
            _findCursorValid = false;
            _findCursorByte = 0u;

            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.RunHexFind: query changed from '" + _lastFindQuery + "' to '"
                + query + "', cursor dropped", LogLevel.Trace);
        }

        _lastFindQuery = query;
        _findMatches.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.RunHexFind: empty query, match list cleared", LogLevel.Warn);

            UpdateHexFindStatus();
            return;
        }

        byte[]? hexPattern = HexDumpSearch.TryParseHexQuery(query);
        byte[] asciiPattern = Encoding.ASCII.GetBytes(query);

        _findMatches = HexDumpSearch.FindMatches(
            _payload, _payload.Length, hexPattern, asciiPattern);

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.RunHexFind: query='" + query + "' hexPattern="
            + (hexPattern == null ? "none" : hexPattern.Length + " byte(s)")
            + " asciiPattern=" + asciiPattern.Length + " byte(s), "
            + _findMatches.Count + " match(es) in " + _payload.Length + " payload byte(s)",
            LogLevel.Trace);

        UpdateHexFindStatus();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // InitiateHexFind
    //
    // Runs the query currently in the find box, paints its matches, moves the cursor to the
    // neighbouring match in the requested direction, and scrolls that match into view.  This is the
    // whole of what starting a search means, so every control that starts one goes through here and
    // none of them re-implement part of it.
    //
    // The query is always re-run rather than compared against a previous one: starting a search is
    // an instruction to search, and the cursor is a byte offset that survives the rebuilt match list
    // so direction still means what the user expects.
    //
    // forward:  True to land on the next match from the cursor, false for the previous one.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void InitiateHexFind(bool forward)
    {
        RunHexFind();
        PaintHexFindMatches();

        AdvanceHexFind(forward);
        PaintHexCursor();
        ScrollHexCursorIntoView();

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.InitiateHexFind: searched "
            + (forward ? "forward" : "backward") + ", " + _findMatches.Count + " match(es)",
            LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PaintHexFindMatches
    //
    // Makes the pane's find highlighting equal the current match list.  The previous query's
    // highlighting is removed first, so a match that is no longer in the list stops being painted,
    // and every recorded match is then painted in the armed color.  Each match contributes one
    // hex-column range and one ASCII-gutter range per dump line it covers.  Every range painted is
    // recorded so a later query can find and remove it.
    //
    // Hand-painted spans are not part of this and survive; where a match overlaps one, the match is
    // painted over it and the hand coloring is restored when this highlighting is next cleared.
    //
    // An empty match list still clears, which is how a cleared query stops showing its old hits.
    //
    // The generation handed to the span builder is zero and is not read; the ranges it returns are
    // consumed for their offsets alone.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void PaintHexFindMatches()
    {
        ClearHexFindPaint();

        int painted = 0;

        for (int i = 0; i < _findMatches.Count; i++)
        {
            List<HighlightSpan> spans =
                HexDumpSearch.BuildSpans(_findMatches[i], _activeHighlightColor, 0u);

            if (spans.Count == 0)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "PacketDetailWindow.PaintHexFindMatches: match at byte "
                    + _findMatches[i].Start + " produced no ranges, skipped", LogLevel.Warn);
                continue;
            }

            for (int s = 0; s < spans.Count; s++)
            {
                if (PaintHexRange(spans[s].Start, spans[s].Length, _activeHighlightColor))
                {
                    _findPaintedSpans.Add(spans[s]);
                    painted++;
                }
                else
                {
                    DebugLog.Write(LogChannel.Opcodes,
                        "PacketDetailWindow.PaintHexFindMatches: could not paint range start="
                        + spans[s].Start + " length=" + spans[s].Length, LogLevel.Warn);
                }
            }
        }

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.PaintHexFindMatches: " + _findMatches.Count
            + " match(es) painted as " + painted + " range(s) in color 0x"
            + _activeHighlightColor.ToString(), LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FlattenVisibleNodes
    //
    // Appends the given node and its visible descendants to the list in display order: a node is
    // added, then its children are visited only when the corresponding TreeViewItem container is
    // expanded.  Produces the ordered run of nodes a range selection spans between two endpoints.
    //
    // node:     The node to append and descend from.
    // parent:   The ItemsControl whose container generator resolves this node's TreeViewItem.
    // visible:  The list receiving the nodes in display order.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void FlattenVisibleNodes(FieldDisplayNode node, ItemsControl parent, List<FieldDisplayNode> visible)
    {
        if (node == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "PacketDetailWindow.FlattenVisibleNodes: null node", LogLevel.Warn);
            return;
        }

        visible.Add(node);

        TreeViewItem? container = parent.ItemContainerGenerator.ContainerFromItem(node) as TreeViewItem;
        if (container == null || container.IsExpanded == false)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.FlattenVisibleNodes: node='" + node.Text + "' not expanded or no container",
                LogLevel.Trace);
            return;
        }

        foreach (FieldDisplayNode child in node.Children)
        {
            FlattenVisibleNodes(child, container, visible);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_HexFindAll_Click
    //
    // Find button on the hex find bar.  Re-runs the query and paints every match, leaving the
    // cursor invalid so a subsequent next or previous starts from the top of the dump rather than
    // from wherever an earlier query had left off.  Highlighting only; the view is not scrolled.
    //
    // sender:  The button that raised the event.
    // e:       Standard routed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_HexFindAll_Click(object sender, RoutedEventArgs e)
    {
        InitiateHexFind(true);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_HexFindNext_Click
    //
    // Down arrow on the hex find bar.  Moves the find cursor to the next match and scrolls it into
    // view.  Operates on the match list as it stands, so the query must already have been run;
    // pressing this with no matches recorded does nothing.
    //
    // sender:  The button that raised the event.
    // e:       Standard routed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_HexFindNext_Click(object sender, RoutedEventArgs e)
    {
        InitiateHexFind(true);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_HexFindPrevious_Click
    //
    // Up arrow on the hex find bar.  Moves the find cursor to the previous match and scrolls it
    // into view.  Operates on the match list as it stands, so the query must already have been run;
    // pressing this with no matches recorded does nothing.
    //
    // sender:  The button that raised the event.
    // e:       Standard routed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_HexFindPrevious_Click(object sender, RoutedEventArgs e)
    {
        InitiateHexFind(false);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_HexFindClear_Click
    //
    // Clear button on the hex find bar.  Empties the find text box and re-runs the find, which with
    // no query bumps the selected color's generation and records no matches, so the previous query's
    // highlights are pruned on the rebuild while hand-painted spans in other colors survive.  Focus
    // returns to the text box so a new query can be typed immediately.
    //
    // sender:  The button that raised the event.
    // e:       Standard routed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_HexFindClear_Click(object sender, RoutedEventArgs e)
    {
        TextBoxHexFind.Text = string.Empty;

        RunHexFind();
        PaintHexFindMatches();

        TextBoxHexFind.Focus();

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.Button_HexFindClear_Click: query cleared", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_HexFindClose_Click
    //
    // Close button on the hex find bar.  Hides the bar and returns focus to the hex pane, leaving
    // the query text, the match list, and the painted highlights as they are so reopening the bar
    // resumes where it left off.
    //
    // sender:  The button that raised the event.
    // e:       Standard routed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_HexFindClose_Click(object sender, RoutedEventArgs e)
    {
        HexFindBar.Visibility = Visibility.Collapsed;
        HexDumpBox.Focus();

    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TextBoxHexFind_KeyDown
    //
    // Key handler for the find text box.  Enter moves the find cursor to the next match and
    // Shift+Enter to the previous, each scrolling the result into view, so navigation is available
    // without leaving the keyboard.  Both operate on the match list as it stands.  Every other key
    // is left alone so the box behaves normally while typing, and Escape in particular falls
    // through to the window handler that hides the bar.
    //
    // sender:  The key event source.
    // e:       The key event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void TextBoxHexFind_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        bool forward = (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift;

        InitiateHexFind(forward);

        e.Handled = true;

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.TextBoxHexFind_KeyDown: Enter moved cursor "
            + (forward ? "forward" : "backward"), LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetHighlightDescendants
    //
    // Sets IsHighlighted to the given value on the node and all its descendants.
    //
    // node:      The root of the subtree to update.
    // highlight: The value to assign to IsHighlighted.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void SetHighlightDescendants(FieldDisplayNode node, bool highlight)
    {
        node.IsHighlighted = highlight;

        foreach (FieldDisplayNode child in node.Children)
        {
            SetHighlightDescendants(child, highlight);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AdvanceHexFind
    //
    // Moves the find cursor to the neighbouring match in the requested direction and leaves it
    // valid.  Going forward, the target is the first match starting beyond the cursor; going
    // backward, the last match starting before it.  An invalid cursor means no match has been
    // visited yet, so a forward move starts at the first match and a backward move at the last.
    // When no match lies in the requested direction, the move wraps to the opposite end if the
    // Wrap box is checked and otherwise leaves the cursor where it was.  An empty match list moves
    // nothing.
    //
    // Matches are compared by start offset only, so two matches beginning at the same byte — which
    // happens when both byte forms of a query hit the same position — are treated as one stop.
    //
    // forward:  True to move toward the end of the payload, false to move toward the start.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void AdvanceHexFind(bool forward)
    {
        if (_findMatches.Count == 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.AdvanceHexFind: no matches, nothing to advance to",
                LogLevel.Warn);
            return;
        }

        bool wrap = CheckBoxHexFindWrap.IsChecked == true;
        int target = -1;

        if (!_findCursorValid)
        {
            if (forward)
            {
                target = 0;
            }
            else
            {
                target = _findMatches.Count - 1;
            }

            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.AdvanceHexFind: cursor was invalid, starting at match "
                + target, LogLevel.Trace);
        }
        else if (forward)
        {
            for (int i = 0; i < _findMatches.Count; i++)
            {
                if (_findMatches[i].Start > _findCursorByte)
                {
                    target = i;
                    break;
                }
            }

            if (target < 0 && wrap)
            {
                target = 0;

                DebugLog.Write(LogChannel.Opcodes,
                    "PacketDetailWindow.AdvanceHexFind: past last match, wrapped to first",
                    LogLevel.Trace);
            }
        }
        else
        {
            for (int i = _findMatches.Count - 1; i >= 0; i--)
            {
                if (_findMatches[i].Start < _findCursorByte)
                {
                    target = i;
                    break;
                }
            }

            if (target < 0 && wrap)
            {
                target = _findMatches.Count - 1;

                DebugLog.Write(LogChannel.Opcodes,
                    "PacketDetailWindow.AdvanceHexFind: before first match, wrapped to last",
                    LogLevel.Trace);
            }
        }

        if (target < 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.AdvanceHexFind: no match "
                + (forward ? "after" : "before") + " byte " + _findCursorByte
                + " and wrap is off, cursor unchanged", LogLevel.Warn);
            return;
        }

        _findCursorByte = _findMatches[target].Start;
        _findCursorValid = true;

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.AdvanceHexFind: cursor moved "
            + (forward ? "forward" : "backward") + " to match " + target + " of "
            + _findMatches.Count + " at byte " + _findCursorByte, LogLevel.Trace);

        UpdateHexFindStatus();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetHexTextPointer
    //
    // Returns a TextPointer at the given character offset into the hex dump text, or null when the
    // offset cannot be resolved.  The line holding the offset is found by binary search over the
    // recorded line start offsets, and the remainder is applied as a step forward from that line's
    // recorded text position, so the cost does not grow with the size of the dump.
    //
    // An offset that lands exactly on a line's newline resolves to the end of that line's text
    // rather than to a position inside the following line, so a range ending at a newline stops at
    // the visible end of the line.  An empty line holds no text, so the only offset it can resolve
    // is its own start.
    //
    // charOffset:  Zero-based character offset into the dump text.
    //
    // Returns the pointer at that offset, or null when it cannot be resolved.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private TextPointer? GetHexTextPointer(int charOffset)
    {
        if (charOffset < 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.GetHexTextPointer: negative offset " + charOffset,
                LogLevel.Error);
            return null;
        }

        if (_lineStartOffsets.Count == 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.GetHexTextPointer: no line positions recorded",
                LogLevel.Error);
            return null;
        }

        if (charOffset > _hexText.Length)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.GetHexTextPointer: offset " + charOffset
                + " is past the end of the dump text, length " + _hexText.Length, LogLevel.Warn);
            return null;
        }

        int low = 0;
        int high = _lineStartOffsets.Count - 1;
        int lineIndex = 0;

        while (low <= high)
        {
            int middle = low + ((high - low) / 2);

            if (_lineStartOffsets[middle] <= charOffset)
            {
                lineIndex = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        int lineStart = _lineStartOffsets[lineIndex];
        int lineTextLength;

        if (lineIndex + 1 < _lineStartOffsets.Count)
        {
            lineTextLength = _lineStartOffsets[lineIndex + 1] - lineStart - 1;
        }
        else
        {
            lineTextLength = _hexText.Length - lineStart;
        }

        if (lineTextLength < 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.GetHexTextPointer: line " + lineIndex
                + " measured negative length " + lineTextLength, LogLevel.Error);
            return null;
        }

        int delta = charOffset - lineStart;

        if (delta > lineTextLength)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.GetHexTextPointer: offset " + charOffset
                + " falls past the text of line " + lineIndex + ", clamping to its end",
                LogLevel.Warn);
            delta = lineTextLength;
        }

        TextPointer lineStartPointer = _lineStartPointers[lineIndex];

        if (lineTextLength == 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.GetHexTextPointer: offset " + charOffset + " is on empty line "
                + lineIndex, LogLevel.Trace);
            return lineStartPointer;
        }

        TextPointer? pointer = AdvanceOverText(lineStartPointer, delta);
        if (pointer == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.GetHexTextPointer: GetPositionAtOffset returned null for delta "
                + delta + " on line " + lineIndex + " of text length " + lineTextLength,
                LogLevel.Error);
            return null;
        }

        return pointer;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ScrollHexCursorIntoView
    //
    // Scrolls the hex pane so the match at the find cursor is visible, in both axes, leaving the
    // scroll position alone on an axis where the match already sits inside the viewport.  The
    // target character is the first digit of the match's hex column on its first line, obtained by
    // asking HexDumpSearch for the match's spans so the geometry is not duplicated here; the colour
    // and generation handed to that call are irrelevant because only the span's offset is read.
    // Does nothing when the cursor is invalid, when no recorded match starts at the cursor, or when
    // the offset cannot be resolved to a position in the document.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ScrollHexCursorIntoView()
    {
        if (!_findCursorValid)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.ScrollHexCursorIntoView: cursor invalid, nothing to scroll to",
                LogLevel.Warn);
            return;
        }

        int matchIndex = CursorMatchIndex();
        if (matchIndex < 0)
        {
            return;
        }

        List<HighlightSpan> spans = HexDumpSearch.BuildSpans(
            _findMatches[matchIndex], _activeHighlightColor, 0u);
        if (spans.Count == 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.ScrollHexCursorIntoView: match at byte " + _findCursorByte
                + " produced no spans", LogLevel.Warn);
            return;
        }
        HexDumpBox.UpdateLayout();
        TextPointer? pointer = GetHexTextPointer(spans[0].Start);
        if (pointer == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.ScrollHexCursorIntoView: could not resolve character offset "
                + spans[0].Start, LogLevel.Warn);
            return;
        }

        Rect rect = pointer.GetCharacterRect(LogicalDirection.Forward);
        if (rect.IsEmpty)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.ScrollHexCursorIntoView: character rect is empty, document may "
                + "not be laid out yet", LogLevel.Warn);
            return;
        }

        double centeredOffset = HexDumpBox.VerticalOffset + rect.Top
                    - ((HexDumpBox.ViewportHeight - rect.Height) / 2.0);
        HexDumpBox.ScrollToVerticalOffset(centeredOffset);

        double horizontalMargin = rect.Height * 2.0;
        if (rect.Left < 0.0)
        {
            HexDumpBox.ScrollToHorizontalOffset(
                HexDumpBox.HorizontalOffset + rect.Left - horizontalMargin);
        }
        else if (rect.Right > HexDumpBox.ViewportWidth)
        {
            HexDumpBox.ScrollToHorizontalOffset(HexDumpBox.HorizontalOffset + rect.Right
                - HexDumpBox.ViewportWidth + horizontalMargin);
        }

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.ScrollHexCursorIntoView: match " + matchIndex + " at byte "
            + _findCursorByte + " scrolled into view", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // UpdateHexFindStatus
    //
    // Rewrites the status bar from the current find state.  The cursor field shows the payload byte
    // the cursor sits on, formatted as the dump formats its line offsets so the two can be compared
    // by eye, and reads as having no cursor until a match has been visited.  The match field shows
    // the cursor's position within the match list and the list's size, or the size alone while no
    // match has been visited, or that there are none.
    //
    // The ordinal is the index of the first match starting at the cursor byte, so two matches that
    // begin at the same byte — which happens when both byte forms of a query hit one position —
    // report as the same stop.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void UpdateHexFindStatus()
    {
        if (_findCursorValid)
        {
            StatusHexCursor.Text = "Byte 0x" + _findCursorByte.ToString("x8");
        }
        else
        {
            StatusHexCursor.Text = "No cursor";
        }

        if (_findMatches.Count == 0)
        {
            StatusHexMatches.Text = "No matches";

            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.UpdateHexFindStatus: no matches", LogLevel.Trace);
            return;
        }

        if (!_findCursorValid)
        {
            StatusHexMatches.Text = _findMatches.Count + " matches";

            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.UpdateHexFindStatus: " + _findMatches.Count
                + " match(es), cursor not yet placed", LogLevel.Trace);
            return;
        }

        int ordinal = CursorMatchIndex() + 1;

        StatusHexMatches.Text = ordinal + "/" + _findMatches.Count + " matches";

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.UpdateHexFindStatus: match " + ordinal + " of "
            + _findMatches.Count + " at byte " + _findCursorByte, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CursorMatchIndex
    //
    // Returns the index in the match list of the match the find cursor sits on, or -1 when the
    // cursor is not placed or no recorded match starts at it.  Matches are located by start offset,
    // so when two matches begin at the same byte — which happens when both byte forms of a query hit
    // one position — the earlier one in the list is reported.
    //
    // Returns the match index, or -1 when there is none.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private int CursorMatchIndex()
    {
        if (!_findCursorValid)
        {
            return -1;
        }

        for (int i = 0; i < _findMatches.Count; i++)
        {
            if (_findMatches[i].Start == _findCursorByte)
            {
                return i;
            }
        }

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.CursorMatchIndex: no match starts at cursor byte "
            + _findCursorByte, LogLevel.Error);
        return -1;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PaintHexCursor
    //
    // Paints the match the find cursor sits on in the cursor color, so the current stop is
    // distinguishable from the other hits.  The ranges painted are recorded alongside the match
    // ranges, so clearing the find highlighting removes the cursor with it.
    //
    // No earlier cursor has to be removed here, because the match highlighting is repainted in full
    // immediately before this runs and has already covered wherever the cursor previously sat.
    // Painting after that is what puts the cursor color on top of the match color they share.
    //
    // With no cursor placed there is nothing to paint and the pane keeps the match highlighting
    // alone.
    //
    // The generation handed to the span builder is zero and is not read; the ranges it returns are
    // consumed for their offsets alone.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void PaintHexCursor()
    {
        int matchIndex = CursorMatchIndex();
        if (matchIndex < 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.PaintHexCursor: no cursor match, nothing painted",
                LogLevel.Trace);
            return;
        }

        List<HighlightSpan> spans = HexDumpSearch.BuildSpans(
            _findMatches[matchIndex], HexCursorColor, 0u);

        int painted = 0;

        for (int i = 0; i < spans.Count; i++)
        {
            if (PaintHexRange(spans[i].Start, spans[i].Length, HexCursorColor))
            {
                _findPaintedSpans.Add(spans[i]);
                painted++;
            }
            else
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "PacketDetailWindow.PaintHexCursor: could not paint range start="
                    + spans[i].Start + " length=" + spans[i].Length, LogLevel.Warn);
            }
        }

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.PaintHexCursor: match " + matchIndex + " at byte "
            + _findCursorByte + " painted as " + painted + " cursor range(s)", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PaintHexRange
    //
    // Applies or removes the background of one run of characters in the hex pane in place, without
    // rebuilding the document.  A color paints that background across the range together with a
    // contrast foreground so the text stays legible on a dark patch; no color removes the
    // background and returns the foreground to the pane's own, which is what an erase looks like.
    //
    // The range is addressed by character offset into the dump text, the same offsets the search
    // and the hand-coloring both produce, so no caller has to hold a text position.
    //
    // start:   Zero-based character offset of the first character to paint.
    // length:  Number of characters to paint.  A non-positive length paints nothing.
    // color:   The background color to apply, or null to remove the background.
    //
    // Returns true when the range was painted, false when it could not be resolved.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private bool PaintHexRange(int start, int length, ArgbColor? color)
    {
        if (length <= 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.PaintHexRange: non-positive length " + length + " at start="
                + start + ", nothing painted", LogLevel.Warn);
            return false;
        }

        TextPointer? startPointer = GetHexTextPointer(start);
        if (startPointer == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.PaintHexRange: could not resolve start offset " + start,
                LogLevel.Warn);
            return false;
        }

        TextPointer? endPointer = GetHexTextPointer(start + length);
        if (endPointer == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.PaintHexRange: could not resolve end offset "
                + (start + length), LogLevel.Warn);
            return false;
        }

        TextRange range = new TextRange(startPointer, endPointer);

        if (color.HasValue)
        {
            uint argb = color.Value.Value;
            SolidColorBrush background = new SolidColorBrush(Color.FromArgb(
                (byte)((argb >> 24) & 0xFF),
                (byte)((argb >> 16) & 0xFF),
                (byte)((argb >> 8) & 0xFF),
                (byte)(argb & 0xFF)));
            background.Freeze();

            range.ApplyPropertyValue(TextElement.BackgroundProperty, background);
            range.ApplyPropertyValue(TextElement.ForegroundProperty,
                FieldHighlightBehavior.ContrastForeground(color.Value));

            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.PaintHexRange: painted start=" + start + " length=" + length
                + " color=0x" + color.Value.ToString(), LogLevel.Trace);
            return true;
        }

        range.ApplyPropertyValue(TextElement.BackgroundProperty, null);
        range.ApplyPropertyValue(TextElement.ForegroundProperty, HexDumpBox.Foreground);

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.PaintHexRange: cleared start=" + start + " length=" + length,
            LogLevel.Trace);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ClearHexFindPaint
    //
    // Removes the background from every range the find and the find cursor have painted, and
    // repaints the hand-painted spans afterwards so any hand coloring that lay under a find
    // highlight is restored rather than erased along with it.  Leaves the painted-range list empty,
    // which is the state a fresh query starts from.
    //
    // The hand-painted spans are all repainted rather than only those that overlap a cleared range,
    // because the list is short and repainting a span that was already correct costs one property
    // application over a few characters.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ClearHexFindPaint()
    {
        int cleared = 0;

        for (int i = 0; i < _findPaintedSpans.Count; i++)
        {
            HighlightSpan span = _findPaintedSpans[i];

            if (PaintHexRange(span.Start, span.Length, null))
            {
                cleared++;
            }
            else
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "PacketDetailWindow.ClearHexFindPaint: could not clear range start="
                    + span.Start + " length=" + span.Length, LogLevel.Warn);
            }
        }

        _findPaintedSpans.Clear();

        int repainted = 0;

        for (int i = 0; i < _manualSpans.Count; i++)
        {
            HighlightSpan span = _manualSpans[i];

            if (PaintHexRange(span.Start, span.Length, span.OverrideColor))
            {
                repainted++;
            }
            else
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "PacketDetailWindow.ClearHexFindPaint: could not repaint manual span start="
                    + span.Start + " length=" + span.Length, LogLevel.Warn);
            }
        }

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.ClearHexFindPaint: " + cleared + " find range(s) cleared, "
            + repainted + " manual span(s) repainted", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AdvanceOverText
    //
    // Returns the position reached by moving forward over a given number of text characters from
    // the supplied position, stepping over element boundaries without counting them.  This is what
    // makes an offset measured against the dump text address the document correctly after a range
    // has been given a background, because applying a property splits the Run it covers and every
    // resulting boundary would otherwise be counted as a character.
    //
    // Line breaks are elements and are stepped over rather than counted, so the walk is only
    // meaningful within a single line; a count that would run past the end of the line's text
    // continues into the next line's text and is the caller's error.
    //
    // start:  The position to walk forward from.
    // count:  Number of text characters to move over.  Zero returns the starting position.
    //
    // Returns the position reached, or null when the document ends before the count is satisfied.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static TextPointer? AdvanceOverText(TextPointer start, int count)
    {
        if (start == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.AdvanceOverText: null start position", LogLevel.Error);
            return null;
        }

        if (count < 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.AdvanceOverText: negative count " + count, LogLevel.Error);
            return null;
        }

        if (count == 0)
        {
            return start;
        }

        TextPointer? current = start;
        int remaining = count;

        while (current != null && remaining > 0)
        {
            TextPointerContext context = current.GetPointerContext(LogicalDirection.Forward);

            if (context == TextPointerContext.None)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "PacketDetailWindow.AdvanceOverText: document ended with " + remaining
                    + " character(s) still to move over", LogLevel.Warn);
                return null;
            }

            if (context == TextPointerContext.Text)
            {
                int runLength = current.GetTextRunLength(LogicalDirection.Forward);

                if (runLength >= remaining)
                {
                    return current.GetPositionAtOffset(remaining, LogicalDirection.Forward);
                }

                current = current.GetPositionAtOffset(runLength, LogicalDirection.Forward);
                remaining = remaining - runLength;
                continue;
            }

            current = current.GetNextContextPosition(LogicalDirection.Forward);
        }

        if (current == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.AdvanceOverText: walk ran off the end of the document",
                LogLevel.Warn);
        }

        return current;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RemoveManualSpanRange
    //
    // Subtracts a character range from the manual span list.  A span entirely inside the range
    // is removed; a span overlapping one end is trimmed to the part outside the range; a span
    // the range splits down the middle is replaced by its two remaining pieces.  Spans that do
    // not touch the range are left alone.  The document is not repainted here; only the record
    // of what is hand-painted changes.  The generation stamped on a replacement piece is zero
    // and is not read; it is present only because the span type carries the field.
    //
    // start:   Zero-based character offset of the first character of the removed range.
    // length:  Number of characters in the removed range.  A non-positive length removes
    //          nothing.
    //
    // Returns the number of spans removed, trimmed, or split.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private int RemoveManualSpanRange(int start, int length)
    {
        if (length <= 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.RemoveManualSpanRange: non-positive length " + length
                + ", nothing removed", LogLevel.Warn);
            return 0;
        }
        int end = start + length;
        List<HighlightSpan> kept = new List<HighlightSpan>();
        int touched = 0;
        for (int i = 0; i < _manualSpans.Count; i++)
        {
            HighlightSpan span = _manualSpans[i];
            int spanEnd = span.Start + span.Length;
            if (spanEnd <= start || span.Start >= end)
            {
                kept.Add(span);
                continue;
            }
            touched++;
            if (span.Start < start)
            {
                kept.Add(new HighlightSpan(span.Start, start - span.Start,
                    span.OverrideColor, 0u));
                DebugLog.Write(LogChannel.Opcodes,
                    "PacketDetailWindow.RemoveManualSpanRange: span start=" + span.Start
                    + " length=" + span.Length + " kept left piece of "
                    + (start - span.Start) + " character(s)", LogLevel.Trace);
            }
            if (spanEnd > end)
            {
                kept.Add(new HighlightSpan(end, spanEnd - end, span.OverrideColor, 0u));
                DebugLog.Write(LogChannel.Opcodes,
                    "PacketDetailWindow.RemoveManualSpanRange: span start=" + span.Start
                    + " length=" + span.Length + " kept right piece of "
                    + (spanEnd - end) + " character(s)", LogLevel.Trace);
            }
        }
        _manualSpans = kept;
        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.RemoveManualSpanRange: range start=" + start + " length="
            + length + " touched " + touched + " span(s), " + _manualSpans.Count
            + " remain", LogLevel.Trace);
        return touched;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Toggle_HexUncolorSelected_Click
    //
    // Momentary apply-then-reset toggle that removes hand coloring from the hex pane's current
    // text selection.  The toggle is returned to its unchecked state on every path so it behaves
    // as a button rather than a mode.  An empty selection is not an error; there is simply
    // nothing to uncolor.
    //
    // The selected range's background is removed in place and the range is subtracted from the
    // manual span list, so a manual span partly covered by the selection keeps its uncovered
    // pieces both on screen and in the record.
    //
    // Offsets are taken against the dump text by normalizing the flow document's line breaks:
    // TextRange.Text reports a LineBreak as a carriage return and line feed pair, while the
    // stored dump text holds a single line feed, so each pair is collapsed before measuring.
    //
    // sender:  The ToggleButton that raised the event.
    // e:       Standard routed event args; not inspected.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Toggle_HexUncolorSelected_Click(object sender, RoutedEventArgs e)
    {
        ToggleButton? toggle = sender as ToggleButton;
        if (toggle == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.Toggle_HexUncolorSelected_Click: sender was not a "
                + "ToggleButton, ignoring", LogLevel.Error);
            return;
        }
        toggle.IsChecked = false;
        TextSelection selection = HexDumpBox.Selection;
        if (selection.IsEmpty)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.Toggle_HexUncolorSelected_Click: selection is empty, "
                + "nothing to uncolor", LogLevel.Warn);
            return;
        }
        Paragraph? paragraph = HexDumpBox.Document.Blocks.FirstBlock as Paragraph;
        if (paragraph == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.Toggle_HexUncolorSelected_Click: hex document has no "
                + "paragraph, ignoring", LogLevel.Error);
            return;
        }
        TextRange prefix = new TextRange(paragraph.ContentStart, selection.Start);
        int start = prefix.Text.Replace("\r\n", "\n").Length;
        int length = selection.Text.Replace("\r\n", "\n").Length;
        if (length <= 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.Toggle_HexUncolorSelected_Click: selection measured zero "
                + "length, ignoring", LogLevel.Warn);
            return;
        }
        if (!PaintHexRange(start, length, null))
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.Toggle_HexUncolorSelected_Click: could not clear range "
                + "start=" + start + " length=" + length + ", spans unchanged", LogLevel.Warn);
            return;
        }
        int touched = RemoveManualSpanRange(start, length);
        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.Toggle_HexUncolorSelected_Click: uncolored selection start="
            + start + " length=" + length + ", " + touched + " manual span(s) touched, "
            + _manualSpans.Count + " remain", LogLevel.Trace);
    }
}
