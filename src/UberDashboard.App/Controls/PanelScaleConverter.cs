using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace UberDashboard.App.Controls;

public sealed class PanelScaleConverter : IMultiValueConverter
{
    public static double Calculate(double width, double height, double minimum, double maximum)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return 1;
        minimum = double.IsFinite(minimum) ? Math.Clamp(minimum, 15, 200) : 15;
        maximum = double.IsFinite(maximum) ? Math.Clamp(maximum, minimum, 200) : 200;
        return Math.Clamp(Math.Sqrt(width / 400 * (height / 300)), minimum / 100, maximum / 100);
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4 || values[0] is not double width || values[1] is not double height ||
            values[2] is not double minimum || values[3] is not double maximum) return Transform.Identity;
        var scale = Calculate(width, height, minimum, maximum);
        return new ScaleTransform(scale, scale);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
