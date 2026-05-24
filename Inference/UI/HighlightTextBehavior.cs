using Glass.Core.Logging;
using Inference.Models;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Inference.UI;

///////////////////////////////////////////////////////////////////////////////////////////////
// HighlightTextBehavior
//
// Attached behavior that turns a plain TextBlock into a highlight-aware
// renderer driven by a character-offset range list.  Three attached
// properties cooperate:
//
//   Text         The string to display.  Replaces TextBlock.Text -- the
//                two cannot be used together because setting Text clears
//                Inlines and any binding on Text would overwrite the
//                Inlines this behavior builds.
//
//   Ranges       The IReadOnlyList<HighlightRange> of character ranges
//                within Text that should be highlighted.  Null or empty
//                renders as a single plain Run.
//
//   MatchBrush   The Background brush applied to highlighted runs.
//                Defaults to a semi-transparent yellow.
//
// On any change to Text or Ranges, the target TextBlock's Inlines are
// cleared and rebuilt as an alternating sequence of plain Runs and
// highlighted Runs.  All work runs on the UI thread; WPF attached
// property callbacks are UI-thread by contract.
//
// The behavior holds no subscriptions and needs no cleanup.  When a
// virtualized row is recycled, WPF rebinds the attached properties and
// the changed callbacks rebuild Inlines automatically.
///////////////////////////////////////////////////////////////////////////////////////////////
public static class HighlightTextBehavior
{
    private static readonly Brush DefaultMatchBrush;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HighlightTextBehavior (static constructor)
    //
    // Builds the frozen default match brush once.  Freezing lets the brush
    // be shared across threads and across every TextBlock that doesn't
    // override MatchBrush, with no per-row allocation cost.
    ///////////////////////////////////////////////////////////////////////////////////////////
    static HighlightTextBehavior()
    {
        SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0x00));
        brush.Freeze();
        DefaultMatchBrush = brush;
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(HighlightTextBehavior),
            new PropertyMetadata(null, OnTextChanged));

    public static readonly DependencyProperty RangesProperty =
        DependencyProperty.RegisterAttached(
            "Ranges",
            typeof(IReadOnlyList<HighlightRange>),
            typeof(HighlightTextBehavior),
            new PropertyMetadata(null, OnRangesChanged));

    public static readonly DependencyProperty MatchBrushProperty =
        DependencyProperty.RegisterAttached(
            "MatchBrush",
            typeof(Brush),
            typeof(HighlightTextBehavior),
            new PropertyMetadata(null, OnMatchBrushChanged));

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetText / SetText
    //
    // Accessors for the attached Text property.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static string? GetText(DependencyObject target)
    {
        return (string?)target.GetValue(TextProperty);
    }

    public static void SetText(DependencyObject target, string? value)
    {
        target.SetValue(TextProperty, value);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetRanges / SetRanges
    //
    // Accessors for the attached Ranges property.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static IReadOnlyList<HighlightRange>? GetRanges(DependencyObject target)
    {
        return (IReadOnlyList<HighlightRange>?)target.GetValue(RangesProperty);
    }

    public static void SetRanges(DependencyObject target, IReadOnlyList<HighlightRange>? value)
    {
        target.SetValue(RangesProperty, value);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetMatchBrush / SetMatchBrush
    //
    // Accessors for the attached MatchBrush property.  Null means "use the
    // class default" rather than "no brush"; the rebuild reads through
    // null to DefaultMatchBrush.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static Brush? GetMatchBrush(DependencyObject target)
    {
        return (Brush?)target.GetValue(MatchBrushProperty);
    }

    public static void SetMatchBrush(DependencyObject target, Brush? value)
    {
        target.SetValue(MatchBrushProperty, value);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OnTextChanged
    //
    // Attached property changed callback for Text.  Triggers a rebuild of
    // the target TextBlock's Inlines.  Targets that are not TextBlocks are
    // ignored with a log message; this is a usage error, not a runtime
    // condition the behavior tries to recover from.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void OnTextChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        TextBlock? block = target as TextBlock;
        if (block == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "HighlightTextBehavior.OnTextChanged: target is not a TextBlock, ignoring");
            return;
        }

        Rebuild(block);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OnRangesChanged
    //
    // Attached property changed callback for Ranges.  Triggers a rebuild
    // of the target TextBlock's Inlines.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void OnRangesChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        TextBlock? block = target as TextBlock;
        if (block == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "HighlightTextBehavior.OnRangesChanged: target is not a TextBlock, ignoring");
            return;
        }

        Rebuild(block);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OnMatchBrushChanged
    //
    // Attached property changed callback for MatchBrush.  Triggers a
    // rebuild so existing highlighted runs pick up the new brush.  A
    // brush change is rare (typically once at construction or via theme
    // switch), so a full rebuild is acceptable.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void OnMatchBrushChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        TextBlock? block = target as TextBlock;
        if (block == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "HighlightTextBehavior.OnMatchBrushChanged: target is not a TextBlock, ignoring");
            return;
        }

        Rebuild(block);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Rebuild
    //
    // Clears and rebuilds the target TextBlock's Inlines as an alternating
    // sequence of plain Runs and highlighted Runs based on the current
    // Text, Ranges, and MatchBrush attached property values.
    //
    // Empty or null Text clears Inlines and returns.  Null or empty Ranges
    // emits a single plain Run.  Otherwise ranges are copied to a local
    // array, sorted by Start, and walked left-to-right; any range whose
    // Start lies before the previous emitted range's end (overlap) or
    // whose bounds fall outside the text is clamped or skipped with a
    // log message.
    //
    // Runs on the UI thread; WPF attached property callbacks are
    // UI-thread by contract.
    //
    // block:  The TextBlock whose Inlines should be rebuilt.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void Rebuild(TextBlock block)
    {
        string? text = GetText(block);
        IReadOnlyList<HighlightRange>? ranges = GetRanges(block);
        Brush brush = GetMatchBrush(block) ?? DefaultMatchBrush;

        block.Inlines.Clear();

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (ranges == null || ranges.Count == 0)
        {
            block.Inlines.Add(new Run(text));
            return;
        }

        int rangeCount = ranges.Count;
        HighlightRange[] sorted = new HighlightRange[rangeCount];
        for (int i = 0; i < rangeCount; i++)
        {
            sorted[i] = ranges[i];
        }
        System.Array.Sort(sorted, (a, b) => a.Start.CompareTo(b.Start));

        int textLength = text.Length;
        int cursor = 0;
        int emittedRuns = 0;
        int skippedRanges = 0;

        for (int i = 0; i < sorted.Length; i++)
        {
            HighlightRange range = sorted[i];
            int rangeStart = range.Start;
            int rangeLength = range.Length;

            if (rangeLength <= 0)
            {
                skippedRanges++;
                DebugLog.Write(LogChannel.InferenceDebug,
                    "HighlightTextBehavior.Rebuild: skipping range with non-positive length "
                    + rangeLength + " at start=" + rangeStart);
                continue;
            }

            if (rangeStart >= textLength)
            {
                skippedRanges++;
                DebugLog.Write(LogChannel.InferenceDebug,
                    "HighlightTextBehavior.Rebuild: skipping range past end of text, start="
                    + rangeStart + " textLength=" + textLength);
                continue;
            }

            int rangeEnd = rangeStart + rangeLength;
            if (rangeEnd > textLength)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "HighlightTextBehavior.Rebuild: clamping range end from "
                    + rangeEnd + " to textLength=" + textLength);
                rangeEnd = textLength;
            }

            if (rangeStart < cursor)
            {
                if (rangeEnd <= cursor)
                {
                    skippedRanges++;
                    DebugLog.Write(LogChannel.InferenceDebug,
                        "HighlightTextBehavior.Rebuild: skipping range fully overlapped by prior, start="
                        + rangeStart + " end=" + rangeEnd + " cursor=" + cursor);
                    continue;
                }

                DebugLog.Write(LogChannel.InferenceDebug,
                    "HighlightTextBehavior.Rebuild: clamping overlapping range start from "
                    + rangeStart + " to cursor=" + cursor);
                rangeStart = cursor;
            }

            if (rangeStart > cursor)
            {
                string plain = text.Substring(cursor, rangeStart - cursor);
                block.Inlines.Add(new Run(plain));
                emittedRuns++;
            }

            string match = text.Substring(rangeStart, rangeEnd - rangeStart);
            Run highlighted = new Run(match);
            highlighted.Background = brush;
            block.Inlines.Add(highlighted);
            emittedRuns++;

            cursor = rangeEnd;
        }

        if (cursor < textLength)
        {
            string tail = text.Substring(cursor, textLength - cursor);
            block.Inlines.Add(new Run(tail));
            emittedRuns++;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "HighlightTextBehavior.Rebuild: textLength=" + textLength
            + " inputRanges=" + rangeCount
            + " skipped=" + skippedRanges
            + " emittedRuns=" + emittedRuns);
    }

}