using System.Windows;
using System.Windows.Automation;
using UberDashboard.App.Controls;
using UberDashboard.Application.HomeAssistant;
using UberDashboard.Core.Localization;
using Wpf = System.Windows.Controls;

namespace UberDashboard.App.Features.HomeAssistant;

internal static class HomeAssistantEditor
{
    /// <summary>Zwraca znormalizowany adres i nowy token; <see langword="null"/> token oznacza zachowanie obecnego.</summary>
    internal static (string Url, string? Token)? ShowConnection(string? currentUrl)
    {
        var url = Field(currentUrl ?? "");
        var token = new Wpf.PasswordBox { Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 4) };
        token.SetResourceReference(Wpf.Control.ForegroundProperty, "Brush.Text");
        token.SetResourceReference(Wpf.Control.BackgroundProperty, "Brush.Surface.Alt");
        (string Url, string? Token)? result = null;
        return Show("HomeAssistantConnection", 460, 340, form =>
        {
            AddField(form, "HomeAssistantAddress", url);
            AddField(form, "HomeAssistantToken", token);
            AddHint(form, "HomeAssistantTokenHint");
        }, (status, window) =>
        {
            try
            {
                var address = HomeAssistantValidation.NormalizeBaseUrl(url.Text);
                var password = token.Password.Trim();
                result = (address.ToString(), password.Length == 0 ? null : password);
                window.DialogResult = true;
            }
            catch (ArgumentException) { status.Text = UiText.Get("HomeAssistantValidation"); }
        }) ? result : null;
    }

    /// <summary>Zwraca nowy moduł (z nowym identyfikatorem) albo <see langword="null"/> po anulowaniu.</summary>
    internal static HomeAssistantModuleState? ShowModule()
    {
        var entity = Field("");
        var label = Field("");
        var action = new Wpf.CheckBox { Content = UiText.Get("HomeAssistantIsAction"), Margin = new Thickness(0, 8, 0, 0) };
        action.SetResourceReference(FrameworkElement.StyleProperty, "ThemedCheckBox");
        HomeAssistantModuleState? result = null;
        return Show("HomeAssistantAddModule", 460, 340, form =>
        {
            AddField(form, "HomeAssistantEntity", entity);
            AddField(form, "HomeAssistantLabel", label);
            form.Children.Add(action);
        }, (status, window) =>
        {
            try
            {
                var entityId = HomeAssistantValidation.NormalizeEntityId(entity.Text);
                var text = label.Text.Trim();
                result = new HomeAssistantModuleState(
                    Guid.NewGuid().ToString("D"),
                    entityId,
                    text.Length == 0 ? entityId : text,
                    action.IsChecked == true ? HomeAssistantModuleKind.Action : HomeAssistantModuleKind.Sensor);
                window.DialogResult = true;
            }
            catch (ArgumentException) { status.Text = UiText.Get("HomeAssistantValidation"); }
        }) ? result : null;
    }

    private static bool Show(string titleKey, double width, double height, Action<Wpf.StackPanel> fill, Action<Wpf.TextBlock, Window> save)
    {
        var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive);
        var window = WindowFrame.Create(owner, UiText.Get(titleKey), width, height);
        window.MinWidth = 320; window.MinHeight = 240;
        var root = new Wpf.DockPanel { Margin = new Thickness(16) };
        var form = new Wpf.StackPanel();
        var status = new Wpf.TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        status.SetResourceReference(Wpf.TextBlock.ForegroundProperty, "Brush.Text");
        fill(form);
        form.Children.Add(status);

        var buttons = new Wpf.WrapPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        Wpf.DockPanel.SetDock(buttons, Wpf.Dock.Bottom);
        root.Children.Add(buttons);
        AddButton(buttons, "Save", () => save(status, window)).IsDefault = true;
        AddButton(buttons, "Cancel", window.Close).IsCancel = true;
        root.Children.Add(new Wpf.ScrollViewer { Content = form, VerticalScrollBarVisibility = Wpf.ScrollBarVisibility.Auto });
        window.Content = root;
        return window.ShowDialog() == true;
    }

    private static Wpf.TextBox Field(string text)
    {
        var field = new Wpf.TextBox { Text = text, MaxLength = 256 };
        field.SetResourceReference(FrameworkElement.StyleProperty, "ThemedTextBox");
        return field;
    }

    private static void AddField(Wpf.StackPanel form, string key, FrameworkElement control)
    {
        var label = new Wpf.Label { Content = UiText.Get(key), Target = control, Padding = new Thickness(0, 8, 0, 3) };
        label.SetResourceReference(Wpf.Control.ForegroundProperty, "Brush.Text");
        AutomationProperties.SetName(control, UiText.Get(key));
        form.Children.Add(label);
        form.Children.Add(control);
    }

    private static void AddHint(Wpf.StackPanel form, string key)
    {
        var hint = new Wpf.TextBlock { Text = UiText.Get(key), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
        hint.SetResourceReference(Wpf.TextBlock.ForegroundProperty, "Brush.Text.Muted");
        form.Children.Add(hint);
    }

    private static Wpf.Button AddButton(Wpf.Panel panel, string key, Action action)
    {
        var button = new Wpf.Button { Content = UiText.Get(key), ToolTip = UiText.Get(key), Margin = new Thickness(0, 4, 6, 4) };
        button.SetResourceReference(FrameworkElement.StyleProperty, "ToolbarButton");
        button.Click += (_, _) => action();
        panel.Children.Add(button);
        return button;
    }
}
