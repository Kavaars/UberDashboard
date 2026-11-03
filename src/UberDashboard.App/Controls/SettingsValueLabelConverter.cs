using System.Globalization;
using System.Windows.Data;
using UberDashboard.Application.Platform;
using UberDashboard.App.Settings;
using UberDashboard.App.Theming;

namespace UberDashboard.App.Controls;

public sealed class SettingsValueLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ThemeKind.Light => "Jasny",
        ThemeKind.Dark => "Ciemny",
        ThemeKind.System => "Systemowy",
        WindowMode.Normal => "Zwykłe okno",
        WindowMode.DockedBar => "Pasek pulpitu",
        WindowMode.DesktopWidget => "Widget (eksperymentalny)",
        AppBarEdge.Left => "Lewa",
        AppBarEdge.Top => "Górna",
        AppBarEdge.Right => "Prawa",
        AppBarEdge.Bottom => "Dolna",
        CloseBehavior.ExitApplication => "Zakończ aplikację",
        CloseBehavior.MinimizeToTray => "Ukryj w zasobniku",
        _ => value?.ToString() ?? string.Empty,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}
