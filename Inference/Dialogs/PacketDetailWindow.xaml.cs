using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Inference.Models;
using Inference.UI;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
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

        ReadOnlySpan<byte> payload = packet.Payload.AsReadOnlySpan();
        int payloadLength = payload.Length;

        // note on version:  This usage is ok because we do not need to know the exact version
        // when obtaining the opcode name.  V1's output is identical to all other versions.

        PatchOpcode patchOpcode = new PatchOpcode(GlassContext.CurrentPatchLevel, packet.Opcode);
        string opcodeName = GlassContext.PatchRegistry.GetOpcodeName(patchOpcode);
        string opcodeHex = "0x" + packet.Opcode;
        string characterName = ResolveCharacterName(packet.Metadata.SessionId);

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
        HexDumpTextBox.Text = HexDumpFormatter.Format(payload, int.MaxValue);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ResolveCharacterName
    //
    // Returns the character name associated with the given session id by
    // asking the SessionRegistry directly.  Returns empty string when no
    // session is associated with the packet (sessionId negative) or when
    // the registry has no name for that session.
    //
    // sessionId:  Session id from the packet's metadata.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static string ResolveCharacterName(int sessionId)
    {
        if (sessionId < 0)
        {
            return string.Empty;
        }

        string? name = GlassContext.SessionRegistry.CharacterNameFromSession(sessionId);
        if (name == null)
        {
            return string.Empty;
        }

        return name;
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
        DebugLog.Write(LogChannel.Opcodes, "PacketDetailWindow.FieldTree_PreviewKeyDown: copied " + builder.Length + " chars", LogLevel.Info);
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
}
