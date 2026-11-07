using UberDashboard.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Orientation = System.Windows.Controls.Orientation;

namespace UberDashboard.App.Controls;

/// <summary>WspĂłlny komunikat korzystajÄ…cy z zasobĂłw aktywnego motywu.</summary>
public static class ThemedMessageBox
{
    public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult = MessageBoxResult.None) => Show(null, text, caption, buttons, image, defaultResult);
    public static MessageBoxResult Show(Window? owner, string text, string caption, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        _ = image;
        owner ??= System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        var window = new Window { Title = caption, SizeToContent = SizeToContent.WidthAndHeight, MinWidth = 340, MaxWidth = 620, MaxHeight = SystemParameters.WorkArea.Height * .8, WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        if (owner is { IsVisible: true }) window.Owner = owner;
        window.SetResourceReference(Window.BackgroundProperty, "Brush.Surface");
        window.SetResourceReference(Window.ForegroundProperty, "Brush.Text");
        var root = new StackPanel { Margin = new Thickness(22) };
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 14), LastChildFill = true };
        var close = new Button { Content = "×", MinWidth = 30, Padding = new Thickness(4), ToolTip = UiText.Get("ClosesTheMessageWithoutConfirmingTheOperation") };
        System.Windows.Automation.AutomationProperties.SetName(close, UiText.Get("CloseMessage"));
        if (System.Windows.Application.Current?.TryFindResource("ToolbarButton") is Style closeStyle) close.Style = closeStyle;
        close.Click += (_, _) => window.Close();
        DockPanel.SetDock(close, Dock.Right);
        heading.Children.Add(close);
        var title = new TextBlock { Text = caption, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        heading.Children.Add(title);
        heading.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && (ReferenceEquals(e.OriginalSource, heading) || ReferenceEquals(e.OriginalSource, title))) window.DragMove(); };
        root.Children.Add(heading);
        var message = new System.Windows.Controls.TextBox { Text = text, IsReadOnly = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, TextWrapping = TextWrapping.Wrap, MaxWidth = 550, MinWidth = 280, Margin = new Thickness(0, 0, 0, 20), AcceptsReturn = true };
        message.SetResourceReference(System.Windows.Controls.TextBox.ForegroundProperty, "Brush.Text");
        System.Windows.Automation.AutomationProperties.SetName(message, caption);
        root.Children.Add(message);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var result = defaultResult == MessageBoxResult.None ? MessageBoxResult.Cancel : defaultResult;
        void Add(string label, MessageBoxResult value, bool cancel)
        {
            var button = new Button { Content = label, MinWidth = 85, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 5, 10, 5), IsCancel = cancel, IsDefault = value == defaultResult || (buttons == MessageBoxButton.OK && value == MessageBoxResult.OK) };
            if (System.Windows.Application.Current?.TryFindResource("ToolbarButton") is Style style) button.Style = style;
            button.Click += (_, _) => { result = value; window.Close(); };
            bar.Children.Add(button);
            if (button.IsDefault) window.Loaded += (_, _) => button.Focus();
        }
        if (buttons == MessageBoxButton.YesNo) { Add(UiText.Get("Yes"), MessageBoxResult.Yes, false); Add(UiText.Get("No"), MessageBoxResult.No, true); }
        else if (buttons == MessageBoxButton.OKCancel) { Add("OK", MessageBoxResult.OK, false); Add(UiText.Get("Cancel"), MessageBoxResult.Cancel, true); }
        else Add("OK", MessageBoxResult.OK, true);
        root.Children.Add(bar);
        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        frame.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        window.Content = frame;
        window.ShowDialog();
        return result;
    }
}
