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
        RebuildHexDocument();
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
    // nothing to color.  The span is stamped with the current generation for the armed color, so it
    // stays live until that color's generation is bumped and is unaffected by bumps to other colors.
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

        uint generation = _hexGenerationMap.CurrentGeneration(_activeHighlightColor);
        _hexSpans.Add(new HighlightSpan(start, length, _activeHighlightColor, generation));

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.Toggle_HexColorSelected_Click: colored selection start=" + start
            + " length=" + length + " color=0x" + _activeHighlightColor.ToString()
            + " generation=" + generation + ", span count now " + _hexSpans.Count, LogLevel.Trace);

        RebuildHexDocument();
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
    // RebuildHexDocument
    //
    // Rebuilds the hex pane's FlowDocument from the stored dump text and span list.  Spans whose
    // generation is behind the current generation for their own color are dropped before
    // painting, so bumping one color's generation discards that color's spans and leaves
    // every other color untouched.
    //
    // Each character's color is the color of the last span covering it, so overlapping spans resolve
    // by last-writer-wins; one Run is emitted per maximal run of identical color, with an
    // uncolored Run for characters no span covers.  A colored Run also receives a contrast
    // foreground so text stays legible on dark patch colors.  Newlines in the dump text are
    // emitted as LineBreak elements rather than left inside a Run, so line breaks do not depend
    // on how the flow layout treats a newline character; the character offsets the spans index
    // are unaffected because each newline consumes exactly one offset either way.  The document
    // is given an explicit PageWidth so 77-character lines scroll horizontally instead of
    // wrapping, and zero page and paragraph padding so the dump is not double-spaced.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void RebuildHexDocument()
    {
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
                "PacketDetailWindow.RebuildHexDocument: no measurable width, leaving PageWidth "
                + "at its default", LogLevel.Warn);
        }

        Paragraph paragraph = new Paragraph();
        paragraph.Margin = new Thickness(0);

        int textLength = _hexText.Length;
        if (textLength == 0)
        {
            document.Blocks.Add(paragraph);
            HexDumpBox.Document = document;

            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.RebuildHexDocument: empty dump text, empty document",
                LogLevel.Warn);
            return;
        }

        int pruned = 0;
        for (int i = _hexSpans.Count - 1; i >= 0; i--)
        {
            HighlightSpan span = _hexSpans[i];
            uint currentGeneration = _hexGenerationMap.CurrentGeneration(span.OverrideColor);
            if (span.Generation != currentGeneration)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "PacketDetailWindow.RebuildHexDocument: pruning stale span start="
                    + span.Start + " color=0x" + span.OverrideColor.ToString()
                    + " generation=" + span.Generation + " current=" + currentGeneration,
                    LogLevel.Trace);

                _hexSpans.RemoveAt(i);
                pruned++;
            }
        }

        if (pruned > 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.RebuildHexDocument: pruned " + pruned + " stale span(s), "
                + _hexSpans.Count + " remaining", LogLevel.Trace);
        }

        ArgbColor?[] charColor = new ArgbColor?[textLength];
        int applied = 0;
        int skipped = 0;

        for (int i = 0; i < _hexSpans.Count; i++)
        {
            HighlightSpan span = _hexSpans[i];
            int spanStart = span.Start;
            int spanLength = span.Length;

            if (spanLength <= 0)
            {
                skipped++;
                DebugLog.Write(LogChannel.Opcodes,
                    "PacketDetailWindow.RebuildHexDocument: skipping span with non-positive "
                    + "length " + spanLength + " at start=" + spanStart, LogLevel.Warn);
                continue;
            }

            if (spanStart < 0 || spanStart >= textLength)
            {
                skipped++;
                DebugLog.Write(LogChannel.Opcodes,
                    "PacketDetailWindow.RebuildHexDocument: skipping out of range span, start="
                    + spanStart + " textLength=" + textLength, LogLevel.Warn);
                continue;
            }

            int spanEnd = spanStart + spanLength;
            if (spanEnd > textLength)
            {
                spanEnd = textLength;
            }

            for (int c = spanStart; c < spanEnd; c++)
            {
                charColor[c] = span.OverrideColor;
            }
            applied++;

            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.RebuildHexDocument: applied span start=" + spanStart
                + " end=" + spanEnd + " color=0x" + span.OverrideColor.ToString(),
                LogLevel.Trace);
        }

        int segStart = 0;
        int runCount = 0;
        while (segStart < textLength)
        {
            ArgbColor? segColor = charColor[segStart];
            int segEnd = segStart + 1;
            while (segEnd < textLength && FieldHighlightBehavior.NullableColorEquals(charColor[segEnd], segColor))
            {
                segEnd++;
            }

            SolidColorBrush? background = null;
            Brush? foreground = null;
            if (segColor.HasValue)
            {
                uint argb = segColor.Value.Value;
                background = new SolidColorBrush(Color.FromArgb(
                    (byte)((argb >> 24) & 0xFF),
                    (byte)((argb >> 16) & 0xFF),
                    (byte)((argb >> 8) & 0xFF),
                    (byte)(argb & 0xFF)));
                background.Freeze();
                foreground = FieldHighlightBehavior.ContrastForeground(segColor.Value);
            }

            string segText = _hexText.Substring(segStart, segEnd - segStart);
            string[] pieces = segText.Split('\n');
            for (int j = 0; j < pieces.Length; j++)
            {
                if (pieces[j].Length > 0)
                {
                    Run run = new Run(pieces[j]);
                    if (background != null)
                    {
                        run.Background = background;
                        run.Foreground = foreground;
                    }
                    paragraph.Inlines.Add(run);
                    runCount++;
                }

                if (j < pieces.Length - 1)
                {
                    paragraph.Inlines.Add(new LineBreak());
                }
            }

            segStart = segEnd;
        }

        document.Blocks.Add(paragraph);
        HexDumpBox.Document = document;

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.RebuildHexDocument: " + applied + " span(s) applied, "
            + skipped + " skipped, " + runCount + " run(s) emitted", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // RunHexFind
    //
    // Re-runs the find over the retained payload from the current contents of the find text box
    // and records the results as the window's match list.  The selected highlight color's
    // generation is bumped first, so the previous query's spans in that color go stale while spans
    // in every other color survive; the cursor is invalidated because match positions from the
    // prior query no longer mean anything.  An empty or whitespace-only query leaves the match
    // list empty, which is how a cleared find is expressed.
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

        _hexGenerationMap.Bump(_activeHighlightColor);
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
    // Stamps a highlight span pair over every recorded match and rebuilds the hex document so the
    // matches appear.  Each match contributes one hex-column span and one ASCII-gutter span per
    // dump line it covers, all in the selected highlight color at that color's current generation,
    // so they are born live while the previous query's spans in the same color are still behind and
    // get pruned during the rebuild.  Hand-painted spans in other colors are untouched.  An empty
    // match list still rebuilds, which is what clears a previous query's highlights from view.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void PaintHexFindMatches()
    {
        uint generation = _hexGenerationMap.CurrentGeneration(_activeHighlightColor);
        int added = 0;

        for (int i = 0; i < _findMatches.Count; i++)
        {
            List<HighlightSpan> spans =
                HexDumpSearch.BuildSpans(_findMatches[i], _activeHighlightColor, generation);

            if (spans.Count == 0)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "PacketDetailWindow.PaintHexFindMatches: match at byte "
                    + _findMatches[i].Start + " produced no spans, skipped", LogLevel.Warn);
                continue;
            }

            for (int s = 0; s < spans.Count; s++)
            {
                _hexSpans.Add(spans[s]);
            }

            added += spans.Count;
        }

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.PaintHexFindMatches: " + _findMatches.Count + " match(es) painted as "
            + added + " span(s) in color 0x" + _activeHighlightColor.ToString()
            + " at generation " + generation, LogLevel.Trace);

        RebuildHexDocument();
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

        InitiateHexFind(true);

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
    // offset is past the end of the document or the document is not laid out as expected.  The
    // paragraph's inlines are walked in order, each Run contributing its text length and each
    // LineBreak contributing the single newline character it stands for, so the offsets counted
    // here match the offsets the spans index into the dump text.
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

        Paragraph? paragraph = HexDumpBox.Document.Blocks.FirstBlock as Paragraph;
        if (paragraph == null)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.GetHexTextPointer: hex document has no paragraph",
                LogLevel.Error);
            return null;
        }

        int remaining = charOffset;

        foreach (Inline inline in paragraph.Inlines)
        {
            Run? run = inline as Run;
            if (run != null)
            {
                int runLength = run.Text.Length;
                if (remaining < runLength)
                {
                    TextPointer? pointer =
                        run.ContentStart.GetPositionAtOffset(remaining, LogicalDirection.Forward);
                    if (pointer == null)
                    {
                        DebugLog.Write(LogChannel.Opcodes,
                            "PacketDetailWindow.GetHexTextPointer: GetPositionAtOffset returned "
                            + "null for offset " + remaining + " within run of length " + runLength,
                            LogLevel.Error);
                    }
                    return pointer;
                }

                remaining = remaining - runLength;
                continue;
            }

            LineBreak? lineBreak = inline as LineBreak;
            if (lineBreak != null)
            {
                if (remaining == 0)
                {
                    return lineBreak.ContentStart;
                }

                remaining = remaining - 1;
                continue;
            }

            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.GetHexTextPointer: unexpected inline type "
                + inline.GetType().Name + ", cannot traverse", LogLevel.Error);
            return null;
        }

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.GetHexTextPointer: offset " + charOffset
            + " is past the end of the dump text", LogLevel.Warn);
        return null;
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
            + _findCursorByte + " scrolled into view", LogLevel.Info);
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
    // Repaints the cursor highlight so only the match the cursor sits on carries the cursor color.
    // The cursor color's generation is bumped first, which stales the previous cursor highlight
    // without touching spans in any other color, and the current match's spans are then stamped in
    // that color at the new generation.  With no cursor placed, the bump alone leaves no cursor
    // highlight at all.
    //
    // The spans are appended after any match spans already recorded, so where the cursor's region
    // overlaps a match painted in the selected color, last-writer-wins resolves the shared
    // characters to the cursor color.  This therefore has to run after the matches are painted.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void PaintHexCursor()
    {
        _hexGenerationMap.Bump(HexCursorColor);

        int matchIndex = CursorMatchIndex();
        if (matchIndex < 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "PacketDetailWindow.PaintHexCursor: no cursor match, cursor highlight cleared",
                LogLevel.Trace);

            RebuildHexDocument();
            return;
        }

        uint generation = _hexGenerationMap.CurrentGeneration(HexCursorColor);

        List<HighlightSpan> spans = HexDumpSearch.BuildSpans(
            _findMatches[matchIndex], HexCursorColor, generation);

        for (int i = 0; i < spans.Count; i++)
        {
            _hexSpans.Add(spans[i]);
        }

        DebugLog.Write(LogChannel.Opcodes,
            "PacketDetailWindow.PaintHexCursor: match " + matchIndex + " at byte "
            + _findCursorByte + " painted as " + spans.Count + " cursor span(s) at generation "
            + generation, LogLevel.Trace);

        RebuildHexDocument();
    }
}
