using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace UberDashboard.App.Features.Weather;

public sealed class WeatherTemperatureBrushConverter : IValueConverter
{
    private static readonly Brush Cold = Frozen("#2874B8");
    private static readonly Brush Mild = Frozen("#348253");
    private static readonly Brush Warm = Frozen("#B85D1A");
    private static readonly Brush Hot = Frozen("#B63232");
    private static Brush Frozen(string color) { var brush = (Brush)new BrushConverter().ConvertFromString(color)!; brush.Freeze(); return brush; }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double temperature
        ? temperature < 5 ? Cold : temperature < 18 ? Mild : temperature < 28 ? Warm : Hot : Cold;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

