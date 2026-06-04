using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Glass.Core.Logging;

namespace Inference.UI;

///////////////////////////////////////////////////////////////////////////////////////////////
// StringToVisibilityConverter
//
// Converts bound values to a Visibility for the collection editor's second-line editors.  As an
// IValueConverter it maps a single string to Visible when the string has non-whitespace content
// and Collapsed otherwise.  As an IMultiValueConverter it maps a set of values to Visible when
// any bound string has non-whitespace content or any bound bool is true, and Collapsed otherwise;
// this drives an editor that shows when its value is populated or its toggle is set.
///////////////////////////////////////////////////////////////////////////////////////////////
public class StringToVisibilityConverter : IValueConverter, IMultiValueConverter
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Convert
    //
    // Maps a single bound value to a Visibility.  A non-whitespace string yields Visible; a null,
    // non-string, or whitespace value yields Collapsed.
    //
    // value:      The bound value.
    // targetType: The binding target type; not inspected.
    // parameter:  The converter parameter; not inspected.
    // culture:    The culture; not inspected.
    //
    // Returns:
    //   Visibility.Visible when the value is a non-whitespace string, Visibility.Collapsed otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string? text = value as string;
        if (string.IsNullOrWhiteSpace(text) == false)
        {
            DebugLog.Write(LogChannel.Fields, "StringToVisibilityConverter.Convert: non-empty string, Visible");
            return Visibility.Visible;
        }

        DebugLog.Write(LogChannel.Fields, "StringToVisibilityConverter.Convert: empty or non-string, Collapsed");
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
        DebugLog.Write(LogChannel.Fields, "StringToVisibilityConverter.ConvertBack: not supported, throwing");
        throw new NotSupportedException("StringToVisibilityConverter is one-way.");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Convert
    //
    // Maps a set of bound values to a Visibility.  Yields Visible when any value is a non-whitespace
    // string or a bool that is true; yields Collapsed when no value satisfies either condition.
    //
    // values:     The bound values.
    // targetType: The binding target type; not inspected.
    // parameter:  The converter parameter; not inspected.
    // culture:    The culture; not inspected.
    //
    // Returns:
    //   Visibility.Visible when any value is a non-whitespace string or a true bool,
    //   Visibility.Collapsed otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
        {
            object value = values[valueIndex];

            if (value is string text)
            {
                if (string.IsNullOrWhiteSpace(text) == false)
                {
                    DebugLog.Write(LogChannel.Fields, "StringToVisibilityConverter.Convert(multi): "
                        + "non-empty string at index " + valueIndex + ", Visible");
                    return Visibility.Visible;
                }
            }
            else if (value is bool flag)
            {
                if (flag == true)
                {
                    DebugLog.Write(LogChannel.Fields, "StringToVisibilityConverter.Convert(multi): "
                        + "true flag at index " + valueIndex + ", Visible");
                    return Visibility.Visible;
                }
            }
        }

        DebugLog.Write(LogChannel.Fields, "StringToVisibilityConverter.Convert(multi): "
            + "no populated value or set flag, Collapsed");
        return Visibility.Collapsed;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ConvertBack
    //
    // Not supported.  The converter is one-way; visibility is never written back to the sources.
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
        DebugLog.Write(LogChannel.Fields, "StringToVisibilityConverter.ConvertBack(multi): not supported, throwing");
        throw new NotSupportedException("StringToVisibilityConverter is one-way.");
    }
}