using System.Windows;
using System.Windows.Input;

namespace UberDashboard.App.Controls;

public static class WindowFrame
{
    public static readonly DependencyProperty EnableCommandsProperty = DependencyProperty.RegisterAttached(
        "EnableCommands", typeof(bool), typeof(WindowFrame), new PropertyMetadata(false, OnEnableCommands));

    public static void SetEnableCommands(DependencyObject window, bool value) => window.SetValue(EnableCommandsProperty, value);
    public static bool GetEnableCommands(DependencyObject window) => (bool)window.GetValue(EnableCommandsProperty);

    private static void OnEnableCommands(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Window window || e.NewValue is not true) return;
        window.CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => window.Close()));
        window.CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(window)));
        window.CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (_, _) => SystemCommands.MaximizeWindow(window)));
        window.CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (_, _) => SystemCommands.RestoreWindow(window)));
    }

    internal static Window Create(Window? owner, string title, double width, double height)
    {
        var window = new Window
        {
            Owner = owner, Title = title, Width = width, Height = height,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = owner is null,
        };
        window.SetResourceReference(FrameworkElement.StyleProperty, "ThemedWindow");
        window.SetResourceReference(Window.BackgroundProperty, "Brush.Surface");
        window.SetResourceReference(Window.ForegroundProperty, "Brush.Text");
        return window;
    }
}
