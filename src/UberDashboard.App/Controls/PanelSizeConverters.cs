using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace UberDashboard.App.Controls;

public sealed class PanelHeaderWidthConverter : IValueConverter
{
    private const double NarrowWidth = 440;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width) return DependencyProperty.UnsetValue;
        return parameter switch
        {
            "SeparateTabRow" => width < NarrowWidth,
            "Title" => width < NarrowWidth ? Math.Max(0, width * .38 - 42) : width * .25,
            _ => width < NarrowWidth ? Math.Min(200, width * .60) : Math.Min(500, width * .45),
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PanelTabRowsHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double height ? Math.Max(44, height / 2) : DependencyProperty.UnsetValue;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CompactPanelConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is double width && values[1] is double height && width >= 480 && height < 260;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
