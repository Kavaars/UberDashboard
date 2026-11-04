using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace UberDashboard.App.Controls;

public sealed class RemainingTextBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var source = values.Length > 2 && values[2] is Brush brush ? brush : Brushes.Black;
        if (values.Length > 0 && values[0] is string color && !string.IsNullOrWhiteSpace(color))
        {
            try { source = (Brush)new BrushConverter().ConvertFromString(color)!; }
            catch (FormatException) { }
        }
        var result = source.Clone();
        result.Opacity *= values.Length > 1 && values[1] is double opacity ? Math.Clamp(opacity, 0, 1) : 1;
        return result;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
