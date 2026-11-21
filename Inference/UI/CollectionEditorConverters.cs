///////////////////////////////////////////////////////////////////////////////////////////////
// AnchorMatchConverter
//
// Decides whether a row's field name is the relative-to anchor of the selected row.  The first
// bound value is the row's field_name; the second is the selected row's relative_to.  The anchor
// is trimmed and compared exactly against the field name.  A blank or missing anchor never
// matches, so no row is marked when the selected row has no anchor or nothing is selected.
///////////////////////////////////////////////////////////////////////////////////////////////
using Glass.Core.Logging;
using System;
using System.Data;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Inference.UI;

///////////////////////////////////////////////////////////////////////////////////////////////
// RelativeToBrushConverter
//
// Converts a field's relative-to anchor into a foreground brush for the collection editor.  The
// anchor is trimmed and compared exactly against the field_name of every row in the bound
// DataView.  A match yields green; no match, or a blank anchor, yields red.  The third bound value
// is a version number that changes whenever field names change; it is not read, and exists so the
// binding re-evaluates when another row's name changes.
///////////////////////////////////////////////////////////////////////////////////////////////
public class RelativeToBrushConverter : IMultiValueConverter
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // FoundBrush
    //
    // Foreground used when the anchor names a field in the view.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private static readonly SolidColorBrush FoundBrush = CreateFrozenBrush(Color.FromRgb(0x4C, 0xD9, 0x64));

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // MissingBrush
    //
    // Foreground used when the anchor is blank or names no field in the view.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private static readonly SolidColorBrush MissingBrush = CreateFrozenBrush(Color.FromRgb(0xFF, 0x5C, 0x5C));

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CreateFrozenBrush
    //
    // Creates a solid brush of the given color and freezes it so it can be shared across bindings.
    //
    // color:  The brush color.
    //
    // Returns:
    //   The frozen brush.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        SolidColorBrush brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Convert
    //
    // Maps an anchor name and a fields view to a foreground brush.
    //
    // values:      [0] the row's relative_to value (string or DBNull), [1] the fields DataView,
    //              [2] the field names version number; not read.
    // targetType:  The binding target type; not inspected.
    // parameter:   The converter parameter; not inspected.
    // culture:     The culture; not inspected.
    //
    // Returns:
    //   The found brush when the trimmed anchor exactly matches a field_name in the view, the
    //   missing brush otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        string? anchorRaw = values[0] as string;
        if (string.IsNullOrWhiteSpace(anchorRaw) == true)
        {
            DebugLog.Write(LogChannel.Fields, "RelativeToBrushConverter.Convert: blank anchor, missing", LogLevel.Trace);
            return MissingBrush;
        }

        DataView? view = values[1] as DataView;
        if (view == null)
        {
            DebugLog.Write(LogChannel.Fields, "RelativeToBrushConverter.Convert: no fields view bound, missing", LogLevel.Trace);
            return MissingBrush;
        }

        string anchorName = anchorRaw.Trim();

        foreach (DataRowView rowView in view)
        {
            string fieldName = rowView["field_name"] as string ?? string.Empty;
            if (fieldName == anchorName)
            {
                DebugLog.Write(LogChannel.Fields, "RelativeToBrushConverter.Convert: anchor '"
                    + anchorName + "' found", LogLevel.Trace);
                return FoundBrush;
            }
        }

        DebugLog.Write(LogChannel.Fields, "RelativeToBrushConverter.Convert: anchor '"
            + anchorName + "' not found", LogLevel.Trace);
        return MissingBrush;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ConvertBack
    //
    // Not supported.  The converter is one-way; a brush is never written back to the sources.
    //
    // value:       The bound value; not inspected.
    // targetTypes: The binding target types; not inspected.
    // parameter:   The converter parameter; not inspected.
    // culture:     The culture; not inspected.
    //
    // Throws:
    //   NotSupportedException always.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        DebugLog.Write(LogChannel.Fields, "RelativeToBrushConverter.ConvertBack: not supported, throwing", LogLevel.Error);
        throw new NotSupportedException("RelativeToBrushConverter is one-way.");
    }
}

public class AnchorMatchConverter : IMultiValueConverter
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Convert
    //
    // Maps a field name and an anchor name to whether they match.
    //
    // values:      [0] the row's field_name (string or DBNull), [1] the selected row's relative_to
    //              (string, DBNull, or unset when nothing is selected).
    // targetType:  The binding target type; not inspected.
    // parameter:   The converter parameter; not inspected.
    // culture:     The culture; not inspected.
    //
    // Returns:
    //   True when the anchor is non-blank and its trimmed value equals the field name; false
    //   otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        string? anchorRaw = values[1] as string;
        if (string.IsNullOrWhiteSpace(anchorRaw) == true)
        {
            return false;
        }

        string fieldName = values[0] as string ?? string.Empty;
        string anchorName = anchorRaw.Trim();

        if (fieldName == anchorName)
        {
            DebugLog.Write(LogChannel.Fields, "AnchorMatchConverter.Convert: field '"
                + fieldName + "' is the selected row's anchor", LogLevel.Trace);
            return true;
        }

        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ConvertBack
    //
    // Not supported.  The converter is one-way; a match result is never written back to the sources.
    //
    // value:       The bound value; not inspected.
    // targetTypes: The binding target types; not inspected.
    // parameter:   The converter parameter; not inspected.
    // culture:     The culture; not inspected.
    //
    // Throws:
    //   NotSupportedException always.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        DebugLog.Write(LogChannel.Fields, "AnchorMatchConverter.ConvertBack: not supported, throwing", LogLevel.Error);
        throw new NotSupportedException("AnchorMatchConverter is one-way.");
    }
}
