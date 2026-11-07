using System.Windows;
using System.Windows.Automation;
using UberDashboard.App.Controls;
using UberDashboard.Core.Localization;
using Wpf = System.Windows.Controls;

namespace UberDashboard.App.Features.Shortcuts;

internal static class ShortcutIconPicker
{
    internal static string? Show(Window owner)
    {
        var window = WindowFrame.Create(owner, UiText.Get("ShortcutIconGallery"), 650, 580);
        window.MinWidth = 340; window.MinHeight = 300;
        var root = new Wpf.DockPanel { Margin = new Thickness(12) };
        var search = new Wpf.TextBox { Margin = new Thickness(0, 0, 0, 8), ToolTip = UiText.Get("ShortcutIconSearchHelp") };
        search.SetResourceReference(FrameworkElement.StyleProperty, "ThemedTextBox");
        AutomationProperties.SetName(search, UiText.Get("ShortcutIconSearch"));
        Wpf.DockPanel.SetDock(search, Wpf.Dock.Top); root.Children.Add(search);
        var cancel = new Wpf.Button { Content = UiText.Get("Cancel"), IsCancel = true, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        cancel.SetResourceReference(FrameworkElement.StyleProperty, "ToolbarButton");
        Wpf.DockPanel.SetDock(cancel, Wpf.Dock.Bottom); root.Children.Add(cancel);
        var items = new Wpf.WrapPanel();
        root.Children.Add(new Wpf.ScrollViewer { Content = items, VerticalScrollBarVisibility = Wpf.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = Wpf.ScrollBarVisibility.Disabled });
        string? selected = null;
        foreach (var name in ShortcutIconCatalog.Names)
        {
            var preview = new ShortcutImage { BuiltInIcon = name, Width = 32, Height = 32, Margin = new Thickness(0, 4, 0, 4) };
            preview.SetResourceReference(ShortcutImage.IconBrushProperty, "Brush.Text");
            var content = new Wpf.StackPanel(); content.Children.Add(preview);
            content.Children.Add(new Wpf.TextBlock { Text = name.Replace('-', ' '), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 88 });
            var button = new Wpf.Button { Content = content, Tag = name, Width = 104, MinHeight = 75, Margin = new Thickness(3), ToolTip = name.Replace('-', ' ') };
            button.SetResourceReference(FrameworkElement.StyleProperty, "ToolbarButton");
            AutomationProperties.SetName(button, name.Replace('-', ' '));
            button.Click += (_, _) => { selected = name; window.DialogResult = true; };
            items.Children.Add(button);
        }
        search.TextChanged += (_, _) =>
        {
            foreach (Wpf.Button button in items.Children)
                button.Visibility = ((string)button.Tag).Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        };
        window.Content = root;
        window.Loaded += (_, _) => search.Focus();
        return window.ShowDialog() == true ? selected : null;
    }
}
