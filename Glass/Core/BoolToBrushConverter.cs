using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Glass.Core;

public class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = Brushes.Blue;
    public Brush FalseBrush { get; set; } = Brushes.Transparent;

    // Converts a bool to a Brush — TrueBrush if true, FalseBrush if false.
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value ? TrueBrush : FalseBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}