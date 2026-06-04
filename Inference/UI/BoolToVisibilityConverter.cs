using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Glass.Core.Logging;

namespace Inference.UI;

///////////////////////////////////////////////////////////////////////////////////////////////
// BoolToVisibilityConverter
//
// Maps a bound bool to a Visibility: true yields Visible, false yields Collapsed.  Used to drive
// a DataGridRow's details visibility from the row's any_detail column so a row with no second-line
// editor collapses its details area and reserves no space.
///////////////////////////////////////////////////////////////////////////////////////////////
public class BoolToVisibilityConverter : IValueConverter
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Convert
    //
    // Maps a bound bool to a Visibility.  A true value yields Visible; a false or non-bool value
    // yields Collapsed.
    //
    // value:      The bound value.
    // targetType: The binding target type; not inspected.
    // parameter:  The converter parameter; not inspected.
    // culture:    The culture; not inspected.
    //
    // Returns:
    //   Visibility.Visible when the value is the bool true, Visibility.Collapsed otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool flag)
        {
            if (flag == true)
            {
                DebugLog.Write(LogChannel.Fields, "BoolToVisibilityConverter.Convert: true, Visible");
                return Visibility.Visible;
            }

            DebugLog.Write(LogChannel.Fields, "BoolToVisibilityConverter.Convert: false, Collapsed");
            return Visibility.Collapsed;
        }

        DebugLog.Write(LogChannel.Fields, "BoolToVisibilityConverter.Convert: non-bool, Collapsed");
        return Visibility.Collapsed;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ConvertBack
    //
    // Not supported.  The converter is one-way; visibility is never written back to the source.
    //
    // value:      The bound value; not inspected.
    // targetType: The binding target type; not inspected.
    // parameter:  The converter parameter; not inspected.
    // culture:    The culture; not inspected.
    //
    // Throws:
    //   NotSupportedException always.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        DebugLog.Write(LogChannel.Fields, "BoolToVisibilityConverter.ConvertBack: not supported, throwing");
        throw new NotSupportedException("BoolToVisibilityConverter is one-way.");
    }
}
