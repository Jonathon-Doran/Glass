using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Inference.UI;

///////////////////////////////////////////////////////////////////////////////////////////////
// ColorArgbToBrushConverter
//
// IValueConverter that turns a uint ARGB color value into a SolidColorBrush
// for binding row backgrounds in the Opcode Trace list.  Zero produces a
// Transparent brush so rows with no override render with the default
// background of the parent ListView.
//
// One-way: ConvertBack is not implemented and throws.  Bindings using this
// converter must be OneWay.
///////////////////////////////////////////////////////////////////////////////////////////////
public class ColorArgbToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is uint argb)
        {
            if (argb == 0)
            {
                return Brushes.Transparent;
            }
            byte a = (byte)((argb >> 24) & 0xFF);
            byte r = (byte)((argb >> 16) & 0xFF);
            byte g = (byte)((argb >> 8) & 0xFF);
            byte b = (byte)(argb & 0xFF);
            return new SolidColorBrush(Color.FromArgb(a, r, g, b));
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("ColorArgbToBrushConverter.ConvertBack: one-way only");
    }
}
