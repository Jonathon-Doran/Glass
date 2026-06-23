using Glass.Core.Logging;
using Glass.Network.Protocol.Fields;
using Glass.UI;
using Inference.Core;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Inference.UI;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldHighlightBehavior
//
// Attached behavior bound to the field TreeView's per-node TextBlock.  The TextBlock's
// DataContext is the FieldDisplayNode that the HierarchicalDataTemplate is rendering, so the
// behavior reaches the node, its Text, and its highlight Spans directly through DataContext;
// no reference to the owning row or presenter is threaded through the data model.
//
// A single attached property, Source, is bound to the node.  Its changed callback rebuilds the
// TextBlock's Inlines as an alternating sequence of plain Runs and highlighted Runs over the
// node's Text, one highlighted Run per span, painted with the span's OverrideColor or the
// class default when the span carries none.
//
// Inlines are built here rather than using TextBlock.Text because setting Text clears Inlines;
// the two cannot be combined on one TextBlock.
///////////////////////////////////////////////////////////////////////////////////////////////
public static class FieldHighlightBehavior
{
    private static readonly Brush DefaultMatchBrush;
    private static HighlightGenerationMap? _generationMap;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // FieldHighlightBehavior (static constructor)
    //
    // Builds the frozen default match brush once so it can be shared across every node's
    // TextBlock with no per-node allocation.
    ///////////////////////////////////////////////////////////////////////////////////////////
    static FieldHighlightBehavior()
    {
        SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0x00));
        brush.Freeze();
        DefaultMatchBrush = brush;

        DebugLog.Write(LogChannel.Fields,
            "FieldHighlightBehavior: default match brush built", LogLevel.Trace);
    }

    public static readonly DependencyProperty FDNProperty =
        DependencyProperty.RegisterAttached(
            "FDN",
            typeof(FieldDisplayNode),
            typeof(FieldHighlightBehavior),
            new PropertyMetadata(null, OnFDNChanged));

    private static readonly DependencyProperty SubscriptionProperty =
        DependencyProperty.RegisterAttached(
            "Subscription",
            typeof(System.Action),
            typeof(FieldHighlightBehavior),
            new PropertyMetadata(null));

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetGenerationMap
    //
    // Installs the generation map the behavior consults at realization to drop spans left behind
    // by a superseded search.  Set once during startup before any field row realizes.
    //
    // map:  The generation map.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static void SetGenerationMap(HighlightGenerationMap map)
    {
        _generationMap = map;

        DebugLog.Write(LogChannel.Fields,
            "FieldHighlightBehavior.SetGenerationMap: generation map installed", LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetFDN / SetFDN
    //
    // Accessors for the attached Source property, the FieldDisplayNode whose Text and Spans
    // drive the TextBlock's inlines.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static FieldDisplayNode? GetFDN(DependencyObject target)
    {
        return (FieldDisplayNode?)target.GetValue(FDNProperty);
    }

    public static void SetFDN(DependencyObject target, FieldDisplayNode? value)
    {
        target.SetValue(FDNProperty, value);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OnFDNChanged
    //
    // Attached property changed callback for a FDN.  Unsubscribes the stored SpansChanged handler
    // from the previously bound node, subscribes a fresh handler to the newly bound node so a
    // later in-place span change rebuilds this TextBlock, stores that handler for the next
    // unbind, then rebuilds the inlines from the new node.  Targets that are not TextBlocks are
    // logged and ignored.
    //
    // target:  The element the property is attached to, expected to be a TextBlock.
    // e:       Carries the previously bound node in OldValue and the newly bound node in NewValue.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void OnFDNChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        TextBlock? block = target as TextBlock;
        if (block == null)
        {
            DebugLog.Write(LogChannel.Fields,
                "FieldHighlightBehavior.OnFDNChanged: target is not a TextBlock, ignoring",
                LogLevel.Warn);
            return;
        }

        FieldDisplayNode? oldNode = e.OldValue as FieldDisplayNode;
        System.Action? existingHandler = (System.Action?)block.GetValue(SubscriptionProperty);
        if (oldNode != null && existingHandler != null)
        {
            oldNode.SpansChanged -= existingHandler;
            block.SetValue(SubscriptionProperty, null);

            DebugLog.Write(LogChannel.Fields,
                "FieldHighlightBehavior.OnFDNChanged: unsubscribed from old node '"
                + oldNode.Text + "'", LogLevel.Trace);
        }

        FieldDisplayNode? newNode = e.NewValue as FieldDisplayNode;
        if (newNode != null)
        {
            System.Action handler = delegate { Rebuild(block, newNode); };
            newNode.SpansChanged += handler;
            block.SetValue(SubscriptionProperty, handler);

            DebugLog.Write(LogChannel.Fields,
                "FieldHighlightBehavior.OnFDNChanged: subscribed to new node '"
                + newNode.Text + "'", LogLevel.Trace);
        }

        Rebuild(block, newNode);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Rebuild
    //
    // Clears and rebuilds the TextBlock's Inlines from the node's Text and Spans.  A null node or
    // empty Text clears the inlines.  Stale spans are pruned per color before painting.  Each
    // character's color is the color of the last span covering it, so overlapping spans resolve by
    // last-writer-wins; runs are emitted one per maximal run of identical color, with an
    // uncolored run for characters no span covers.
    //
    // Runs on the UI thread; attached property callbacks are UI-thread by contract.
    //
    // block:  The TextBlock whose Inlines are rebuilt.
    // node:   The node supplying Text and Spans, or null to clear.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void Rebuild(TextBlock block, FieldDisplayNode? node)
    {
        block.Inlines.Clear();

        if (node == null)
        {
            DebugLog.Write(LogChannel.Fields,
                "FieldHighlightBehavior.Rebuild: null node, inlines cleared", LogLevel.Trace);
            return;
        }

        string text = node.Text;
        if (string.IsNullOrEmpty(text))
        {
            DebugLog.Write(LogChannel.Fields,
                "FieldHighlightBehavior.Rebuild: empty text, inlines cleared", LogLevel.Trace);
            return;
        }

        if (_generationMap != null)
        {
            node.RemoveStaleSpans(_generationMap.CurrentGeneration);
        }

        IReadOnlyList<HighlightSpan> spans = node.Spans;
        if (spans.Count == 0)
        {
            block.Inlines.Add(new Run(text));

            // This is a pretty useless message
            DebugLog.Write(LogChannel.Fields,
                "FieldHighlightBehavior.Rebuild: no spans, single plain run for '" + text + "'",
                LogLevel.Trace);
            return;
        }

        int textLength = text.Length;

        // Per-character winning color: null means no highlight on that character.  Walk spans in
        // order, writing each span's color across the characters it covers; a later span overwrites
        // an earlier one on shared characters, so the last span to cover a character wins.
        ArgbColor?[] charColor = new ArgbColor?[textLength];
        int applied = 0;
        int skipped = 0;

        for (int i = 0; i < spans.Count; i++)
        {
            HighlightSpan span = spans[i];
            int spanStart = span.Start;
            int spanLength = span.Length;

            if (spanLength <= 0)
            {
                skipped++;
                DebugLog.Write(LogChannel.Fields,
                    "FieldHighlightBehavior.Rebuild: skipping span with non-positive length "
                    + spanLength + " at start=" + spanStart, LogLevel.Warn);
                continue;
            }

            if (spanStart >= textLength)
            {
                skipped++;
                DebugLog.Write(LogChannel.Fields,
                    "FieldHighlightBehavior.Rebuild: skipping span past end of text, start="
                    + spanStart + " textLength=" + textLength, LogLevel.Warn);
                continue;
            }

            int spanEnd = spanStart + spanLength;
            if (spanEnd > textLength)
            {
                spanEnd = textLength;
            }

            string applyCovered;
            if (spanStart >= 0 && spanEnd <= textLength && spanEnd > spanStart)
            {
                applyCovered = text.Substring(spanStart, spanEnd - spanStart);
            }
            else
            {
                applyCovered = "<out of range>";
            }

            DebugLog.Write(LogChannel.Fields,
                "FieldHighlightBehavior.Rebuild: applying span start=" + spanStart
                + " end=" + spanEnd
                + " color=0x" + span.OverrideColor
                + " generation=" + span.Generation
                + " covered='" + applyCovered + "'", LogLevel.Trace);


            for (int c = spanStart; c < spanEnd; c++)
            {
                charColor[c] = span.OverrideColor;
            }
            applied++;
        }

        // Emit one Run per maximal run of identical color, where a null color is an unhighlighted
        // Run.  A color change starts a new Run.
        int segStart = 0;
        while (segStart < textLength)
        {
            ArgbColor? segColor = charColor[segStart];
            int segEnd = segStart + 1;
            while (segEnd < textLength && NullableColorEquals(charColor[segEnd], segColor))
            {
                segEnd++;
            }

            string segText = text.Substring(segStart, segEnd - segStart);
            Run run = new Run(segText);


            if (segColor.HasValue)
            {
                DebugLog.Write(LogChannel.Fields,
                    "FieldHighlightBehavior.Rebuild: segment start=" + segStart
                    + " length=" + (segEnd - segStart)
                    + " color=0x" + segColor.Value.Value.ToString("x8")
                    + " text='" + segText + "'", LogLevel.Trace);

                uint argb = segColor.Value.Value;
                SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(
                    (byte)((argb >> 24) & 0xFF),
                    (byte)((argb >> 16) & 0xFF),
                    (byte)((argb >> 8) & 0xFF),
                    (byte)(argb & 0xFF)));
                brush.Freeze();
                run.Background = brush;
            }

            block.Inlines.Add(run);
            segStart = segEnd;
        }

        DebugLog.Write(LogChannel.Fields,
            "FieldHighlightBehavior.Rebuild: applied " + applied
            + " span(s), skipped " + skipped, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // NullableColorEquals
    //
    // Returns whether two optional colors are the same: both absent, or both present with equal
    // color.  Used to detect a color change between adjacent characters when emitting runs.
    //
    // a:  First optional color.
    // b:  Second optional color.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static bool NullableColorEquals(ArgbColor? a, ArgbColor? b)
    {
        if (a.HasValue != b.HasValue)
        {
            return false;
        }

        if (!a.HasValue)
        {
            return true;
        }

        return a.Value.Equals(b!.Value);
    }
}
