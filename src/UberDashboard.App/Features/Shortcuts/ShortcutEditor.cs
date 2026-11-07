using System.Windows;
using System.Windows.Automation;
using UberDashboard.App.Controls;
using UberDashboard.Core.Localization;
using Wpf = System.Windows.Controls;

namespace UberDashboard.App.Features.Shortcuts;

internal static class ShortcutEditor
{
    internal static string ImageFilter => UiText.Get("ShortcutImageFilter");

    internal static ShortcutEntry? Show(ShortcutEntry? original)
    {
        var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive);
        var window = WindowFrame.Create(owner, UiText.Get(original is null ? "ShortcutAdd" : "ShortcutEdit"), 500, 620);
        window.MinWidth = 340; window.MinHeight = 320;
        var draft = original ?? new ShortcutEntry();
        var root = new Wpf.DockPanel { Margin = new Thickness(16) };
        var form = new Wpf.StackPanel();
        var name = Field("ShortcutName", draft.Name);
        var target = Field("ShortcutTarget", draft.Target);
        var icon = Field("ShortcutSymbol", draft.Icon);
        var image = Field("ShortcutImage", draft.ImagePath ?? "");
        var builtIn = draft.BuiltInIcon;
        var selectedIcon = new Wpf.TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        selectedIcon.SetResourceReference(Wpf.TextBlock.ForegroundProperty, "Brush.Text.Muted");
        void UpdateSelectedIcon() => selectedIcon.Text = builtIn is null ? "" : UiText.Format("ShortcutIconSelected", builtIn.Replace('-', ' '));
        icon.TextChanged += (_, _) => { builtIn = null; UpdateSelectedIcon(); };
        image.TextChanged += (_, _) => { builtIn = null; UpdateSelectedIcon(); };
        UpdateSelectedIcon();
        var background = new CompactColorPicker { SelectedColor = draft.Background };
        var foreground = new CompactColorPicker { SelectedColor = draft.Foreground, DefaultBrushKey = "Brush.Text" };
        var status = new Wpf.TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        status.SetResourceReference(Wpf.TextBlock.ForegroundProperty, "Brush.Text");

        AddField("ShortcutName", name);
        AddField("ShortcutTarget", target);
        var browse = new Wpf.WrapPanel();
        AddButton(browse, "ShortcutBrowseFile", () =>
        {
            var dialog = new ThemedFileDialog { Title = UiText.Get("ShortcutBrowseFile"), Filter = UiText.Get("ShortcutFileFilter") };
            if (dialog.ShowDialog(window) == true)
            {
                target.Text = dialog.FileName;
                if (string.IsNullOrWhiteSpace(name.Text)) name.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        });
        AddButton(browse, "ShortcutBrowseFolder", () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = UiText.Get("ShortcutBrowseFolder") };
            if (dialog.ShowDialog(window) == true)
            {
                target.Text = dialog.FolderName;
                if (string.IsNullOrWhiteSpace(name.Text)) name.Text = System.IO.Path.GetFileName(dialog.FolderName);
            }
        });
        form.Children.Add(browse);
        var hint = new Wpf.TextBlock { Text = UiText.Get("ShortcutTargetHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
        hint.SetResourceReference(Wpf.TextBlock.ForegroundProperty, "Brush.Text.Muted");
        form.Children.Add(hint);
        AddField("ShortcutSymbol", icon);
        AddField("ShortcutImage", image);
        var images = new Wpf.WrapPanel();
        AddButton(images, "ShortcutIconGallery", () =>
        {
            var chosen = ShortcutIconPicker.Show(window);
            if (chosen is null) return;
            image.Text = "";
            builtIn = chosen;
            UpdateSelectedIcon();
        });
        AddButton(images, "ShortcutChooseImage", () =>
        {
            var dialog = new ThemedFileDialog { Title = UiText.Get("ShortcutChooseImage"), Filter = ImageFilter };
            if (dialog.ShowDialog(window) == true) image.Text = dialog.FileName;
        });
        AddButton(images, "ShortcutAutomaticIcon", () => { image.Text = ""; icon.Text = "↗"; builtIn = null; UpdateSelectedIcon(); });
        form.Children.Add(images);
        form.Children.Add(selectedIcon);
        AddField("ShortcutBackground", background);
        AddField("ShortcutForeground", foreground);
        var colors = new Wpf.WrapPanel();
        AddButton(colors, "ShortcutThemeColors", () => { background.SelectedColor = null; foreground.SelectedColor = null; });
        form.Children.Add(colors);
        form.Children.Add(status);
        var buttons = new Wpf.WrapPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        Wpf.DockPanel.SetDock(buttons, Wpf.Dock.Bottom);
        root.Children.Add(buttons);
        ShortcutEntry? result = null;
        var save = AddButton(buttons, "Save", () =>
        {
            try
            {
                var candidate = draft with
                {
                    Name = name.Text, Target = target.Text, Icon = icon.Text,
                    ImagePath = string.IsNullOrWhiteSpace(image.Text) ? null : image.Text.Trim(), BuiltInIcon = builtIn,
                    Background = background.SelectedColor, Foreground = foreground.SelectedColor,
                };
                // Apply the same validation as persistence before closing the editor.
                result = new ShortcutsModel().Save(candidate) with { Id = draft.Id };
                window.DialogResult = true;
            }
            catch (ArgumentException) { status.Text = UiText.Get("ShortcutValidation"); }
        });
        save.IsDefault = true;
        var cancel = AddButton(buttons, "Cancel", () => window.Close());
        cancel.IsCancel = true;
        root.Children.Add(new Wpf.ScrollViewer { Content = form, VerticalScrollBarVisibility = Wpf.ScrollBarVisibility.Auto });
        window.Content = root;
        window.Loaded += (_, _) => name.Focus();
        return window.ShowDialog() == true ? result : null;

        void AddField(string key, FrameworkElement control)
        {
            var label = new Wpf.Label { Content = UiText.Get(key), Target = control, Padding = new Thickness(0, 8, 0, 3) };
            label.SetResourceReference(Wpf.Control.ForegroundProperty, "Brush.Text");
            AutomationProperties.SetName(control, UiText.Get(key));
            form.Children.Add(label); form.Children.Add(control);
        }
    }

    private static Wpf.TextBox Field(string key, string text)
    {
        var field = new Wpf.TextBox { Text = text, ToolTip = UiText.Get(key), MaxLength = 4096 };
        field.SetResourceReference(FrameworkElement.StyleProperty, "ThemedTextBox");
        return field;
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
