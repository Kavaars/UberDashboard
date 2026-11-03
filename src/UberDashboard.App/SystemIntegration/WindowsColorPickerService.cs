using System.Windows;
using System.Windows.Controls;
using UberDashboard.App.Controls;
using Button = System.Windows.Controls.Button;

namespace UberDashboard.App.SystemIntegration;

/// <summary>Dialog używa tego samego koła kolorów co ustawienia.</summary>
public sealed class WindowsColorPickerService : IColorPickerService
{
    public bool TryPickColor(string? initialColor, out string? selectedColor)
    {
        var picker = new ColorWheel { SelectedColor = initialColor };
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(picker);
        var window = new Window { Title = "Wybierz kolor", Content = stack, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        if (System.Windows.Application.Current?.MainWindow is { IsVisible: true } owner) window.Owner = owner;
        window.SetResourceReference(Window.BackgroundProperty, "Brush.Surface");
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var accept = new Button { Content = "Wybierz", IsDefault = true };
        var cancel = new Button { Content = "Anuluj", IsCancel = true };
        accept.SetResourceReference(FrameworkElement.StyleProperty, "PanelActionButton");
        cancel.SetResourceReference(FrameworkElement.StyleProperty, "PanelActionButton");
        accept.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(accept); buttons.Children.Add(cancel); stack.Children.Add(buttons);
        var accepted = window.ShowDialog() == true;
        selectedColor = accepted ? picker.SelectedColor : null;
        return accepted;
    }
}
