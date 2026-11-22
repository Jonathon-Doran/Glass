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
// AnchorFoundConverter
//
// Decides whether a field's relative-to anchor names a field in the collection editor's grid.
// The anchor is trimmed and compared exactly against the field_name of every row in the bound
// DataView.  A blank anchor is never found.  The third bound value is a version number that
// changes whenever field names change; it is not read, and exists so the binding re-evaluates
// when another row's name changes.
///////////////////////////////////////////////////////////////////////////////////////////////
public class AnchorFoundConverter : IMultiValueConverter
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Convert
    //
    // Maps an anchor name and a fields view to whether the anchor names a field in the view.
    //
    // values:      [0] the row's relative_to value (string or DBNull), [1] the fields DataView,
    //              [2] the field names version number; not read.
    // targetType:  The binding target type; not inspected.
    // parameter:   The converter parameter; not inspected.
    // culture:     The culture; not inspected.
    //
    // Returns:
    //   True when the trimmed anchor exactly matches a field_name in the view; false when the
    //   anchor is blank, no view is bound, or no field matches.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        string? anchorRaw = values[0] as string;
        if (string.IsNullOrWhiteSpace(anchorRaw) == true)
        {
            DebugLog.Write(LogChannel.Fields, "AnchorFoundConverter.Convert: blank anchor, not found", LogLevel.Trace);
            return false;
        }

        DataView? view = values[1] as DataView;
        if (view == null)
        {
            DebugLog.Write(LogChannel.Fields, "AnchorFoundConverter.Convert: no fields view bound, not found", LogLevel.Trace);
            return false;
        }

        string anchorName = anchorRaw.Trim();

        foreach (DataRowView rowView in view)
        {
            string fieldName = rowView["field_name"] as string ?? string.Empty;
            if (fieldName == anchorName)
            {
                DebugLog.Write(LogChannel.Fields, "AnchorFoundConverter.Convert: anchor '"
                    + anchorName + "' found", LogLevel.Trace);
                return true;
            }
        }

        DebugLog.Write(LogChannel.Fields, "AnchorFoundConverter.Convert: anchor '"
            + anchorName + "' not found", LogLevel.Trace);
        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ConvertBack
    //
    // Not supported.  The converter is one-way; a result is never written back to the sources.
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
        DebugLog.Write(LogChannel.Fields, "AnchorFoundConverter.ConvertBack: not supported, throwing", LogLevel.Error);
        throw new NotSupportedException("AnchorFoundConverter is one-way.");
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
