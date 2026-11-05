using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UberDashboard.App.Features.Integrations;
using UberDashboard.App.Theming;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;

internal static class SyncEditorChecks
{
    internal static async Task RunAsync(Window owner, ThemeService theme, string output)
    {
        _ = new System.Windows.Interop.WindowInteropHelper(owner).EnsureHandle();
        foreach (var kind in new[] { ThemeKind.Light, ThemeKind.Dark })
        {
            await theme.ApplyAsync(kind, CancellationToken.None);
            foreach (var provider in Enum.GetValues<SyncProviderKind>())
            {
                var original = provider == SyncProviderKind.MicrosoftTodo
                    ? JsonNode.Parse("""{"title":"Próba","status":"waitingOnOthers","body":{"contentType":"html","content":"<p>Zachowaj opis</p>"},"importance":"high"}""")!.AsObject()
                    : JsonNode.Parse("""{"summary":"Spotkanie","description":"Opis","start":{"dateTime":"2030-05-12T10:00:00+09:00","timeZone":"Asia/Tokyo"},"end":{"dateTime":"2030-05-12T11:00:00+09:00","timeZone":"Asia/Tokyo"},"visibility":"private"}""")!.AsObject();
                Exception? failure = null;
                var result = SyncEditorWindow.Edit(owner, provider, original, window =>
                {
                    _ = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                    try
                    {
                        var title = Descendants(window).OfType<TextBox>().Single(f => AutomationProperties.GetName(f) == UiText.Get("SyncTitle"));
                        var save = Descendants(window).OfType<Button>().Single(b => Equals(b.Content, UiText.Get("SyncSaveLocal")));
                        title.Text = ""; save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (!window.IsVisible) throw new InvalidOperationException("An empty title closed the synchronization editor.");
                        title.Text = "Zmieniony tytuł / Changed title";
                        foreach (var size in new[] { new Size(620, 640), new Size(420, 360) })
                        {
                            window.Width = size.Width; window.Height = size.Height; window.UpdateLayout();
                            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                            bitmap.Render(window);
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var stream = File.Create(Path.Combine(output, $"{kind}-sync-{provider}-{size.Width}x{size.Height}.png")); encoder.Save(stream);
                        }
                        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch (Exception error) { failure = error; window.Close(); }
                    }));
                });
                if (failure is not null || result is null) throw new InvalidOperationException("Synchronization editor failed.", failure);
                var preserved = provider == SyncProviderKind.MicrosoftTodo ? "body" : "start";
                if (!JsonNode.DeepEquals(original[preserved], result[preserved]))
                    throw new InvalidOperationException("Editing the title changed an unrelated field.");
                if (provider == SyncProviderKind.MicrosoftTodo && result["status"]!.GetValue<string>() != "waitingOnOthers")
                    throw new InvalidOperationException("Editing the title flattened the task's status.");
            }
        }
        Console.WriteLine("SYNC EDITORS: validation / localized controls / preservation / 8 renders PASS");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
