using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Automation;
using UberDashboard.App.Features.Shortcuts;
using UberDashboard.App.Theming;
using UberDashboard.Core.Localization;

internal static class ShortcutsChecks
{
    internal static async Task RunAsync(ShortcutsPanel panel, ThemeService theme, string output)
    {
        CheckEditor(panel, output);
        panel.Model.Items.Clear();
        panel.Model.Save(new() { Name = "Dokumenty", Target = @"C:\Users\Public", Icon = "📁" });
        panel.Model.Save(new() { Name = "Przeglądarka / Browser", Target = "https://example.org", Icon = "🌐" });
        panel.Model.Save(new() { Name = "Mój projekt — dłuższa nazwa skrótu", Target = @"C:\Apps\Project.exe", Icon = "★", Background = "#315746", Foreground = "#FFFFFF" });
        panel.Model.Save(new() { Name = "Notatnik", Target = Path.Combine(Environment.SystemDirectory, "notepad.exe") });
        panel.Selected = panel.Model.Items[0];
        var snapshot = await panel.CaptureStateAsync(CancellationToken.None);
        panel.Mode = ShortcutViewMode.Tiles;
        await panel.RestoreStateAsync(snapshot, CancellationToken.None);
        if (panel.Model.Items.Count != 4 || panel.Mode != ShortcutViewMode.Icons)
            throw new InvalidOperationException("Shortcuts state roundtrip failed.");

        foreach (var kind in new[] { ThemeKind.Light, ThemeKind.Dark })
        {
            await theme.ApplyAsync(kind, CancellationToken.None);
            foreach (var mode in Enum.GetValues<ShortcutViewMode>())
            foreach (var size in new[] { new Size(580, 400), new Size(220, 420), new Size(900, 180) })
            {
                panel.Mode = mode;
                var view = new ShortcutsView { DataContext = panel, Margin = new Thickness(10) };
                var root = new Border { Child = view };
                root.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var list = (ListBox)view.FindName("ShortcutList");
                if (list.Items.Count != 4 || list.ItemContainerGenerator.ContainerFromIndex(0) is not ListBoxItem)
                    throw new InvalidOperationException($"Shortcuts failed to render {mode} at {size}.");
                if (Descendants(view).OfType<TextBlock>().Any(text => text.Text.Contains("ShortcutModeOption", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Shortcut view selector displays a record instead of its label.");
                var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(output, $"{kind}-shortcuts-{mode}-{size.Width}x{size.Height}.png"));
                encoder.Save(stream);
            }
        }
        Console.WriteLine("SHORTCUTS: persistence and 24 renders PASS");
    }

    private static void CheckEditor(ShortcutsPanel panel, string output)
    {
        Exception? failure = null;
        System.Windows.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var window = System.Windows.Application.Current.Windows.OfType<Window>().Single(w => w.Title == UiText.Get("ShortcutAdd"));
            try
            {
                var fields = Descendants(window).OfType<TextBox>().ToArray();
                var name = fields.Single(field => AutomationProperties.GetName(field) == UiText.Get("ShortcutName"));
                var target = fields.Single(field => AutomationProperties.GetName(field) == UiText.Get("ShortcutTarget"));
                name.Text = "Editor test"; target.Text = "invalid";
                var save = Descendants(window).OfType<Button>().Single(button => Equals(button.Content, UiText.Get("Save")));
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!window.IsVisible) throw new InvalidOperationException("Invalid shortcut closed the editor.");
                target.Text = "https://example.org";
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(output, "shortcuts-editor.png"))) encoder.Save(stream);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception ex) { failure = ex; window.Close(); }
        }));
        var count = panel.Model.Items.Count;
        panel.AddCommand.Execute(null);
        if (failure is not null) throw new InvalidOperationException("Shortcut editor check failed.", failure);
        if (panel.Model.Items.Count != count + 1) throw new InvalidOperationException("Valid editor input was not added.");
        var original = panel.Selected;
        System.Windows.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var window = System.Windows.Application.Current.Windows.OfType<Window>().Single(w => w.Title == UiText.Get("ShortcutEdit"));
            try
            {
                Descendants(window).OfType<TextBox>().Single(field => AutomationProperties.GetName(field) == UiText.Get("ShortcutName")).Text = "Canceled";
            }
            catch (Exception ex) { failure = ex; }
            finally { window.Close(); }
        }));
        panel.EditCommand.Execute(null);
        if (failure is not null || panel.Selected != original || panel.Selected?.Name != "Editor test")
            throw new InvalidOperationException("Canceling shortcut edit changed saved data.", failure);
        Console.WriteLine("SHORTCUT EDITOR: validation / save / cancel PASS");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
