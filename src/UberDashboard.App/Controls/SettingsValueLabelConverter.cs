using UberDashboard.Core.Localization;
using System.Globalization;
using System.Windows.Data;
using UberDashboard.Application.Platform;
using UberDashboard.App.Settings;
using UberDashboard.App.Theming;

namespace UberDashboard.App.Controls;

public sealed class SettingsValueLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Label(value);

    internal static string Label(object? value) => value switch
    {
        ThemeKind.Light => UiText.Get("Light"),
        ThemeKind.Dark => UiText.Get("Dark"),
        ThemeKind.System => UiText.Get("System"),
        WindowMode.Normal => UiText.Get("NormalWindow"),
        WindowMode.DockedBar => UiText.Get("DockedBar2"),
        WindowMode.DesktopWidget => UiText.Get("WidgetExperimental"),
        AppBarEdge.Left => UiText.Get("Left"),
        AppBarEdge.Top => UiText.Get("Top"),
        AppBarEdge.Right => UiText.Get("Right"),
        AppBarEdge.Bottom => UiText.Get("Bottom"),
        CloseBehavior.ExitApplication => UiText.Get("QuitTheApp"),
        CloseBehavior.MinimizeToTray => UiText.Get("HideInSystemTray"),
        _ => value?.ToString() ?? string.Empty,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}
