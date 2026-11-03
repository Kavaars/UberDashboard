using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using UberDashboard.App.Hosting;
using UberDashboard.App.Shell;
using UberDashboard.App.Theming;

internal static class Program
{
    private static int _exitCode = 1;

    private static IEnumerable<PanelLeafViewModel> Leaves(LayoutNodeViewModel? node) => node switch
    {
        PanelLeafViewModel leaf => [leaf],
        PanelSplitViewModel split => Leaves(split.First).Concat(Leaves(split.Second)),
        _ => [],
    };

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static void ValidateGlyphs(DependencyObject root)
    {
        foreach (var text in Descendants(root).OfType<TextBlock>())
        {
            var codes = text.Text.Where(c => c >= '\uE000' && c <= '\uF8FF').ToArray();
            if (codes.Length == 0) continue;
            if (!new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch).TryGetGlyphTypeface(out var font)
                || codes.Any(c => !font.CharacterToGlyphMap.ContainsKey(c)))
                throw new InvalidOperationException($"Missing icon glyph in {text.FontFamily.Source}");
        }
    }

    private static void DrawBackground(RenderTargetBitmap bitmap, System.Windows.Application app, Size size)
    {
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
            drawing.DrawRectangle((System.Windows.Media.Brush)app.FindResource("Brush.Window.Background"), null, new Rect(size));
        bitmap.Render(background);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "Release/VisualChecks");
        Directory.CreateDirectory(output);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var resource in new[] { "Theme.Light", "VisualTokens", "Controls", "PanelTemplates" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/UberDashboard;component/Theming/{resource}.xaml") });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                var services = new ServiceCollection();
                services.AddLogging();
                var paths = new UberDashboard.Infrastructure.Paths.AppPaths(Path.Combine(output, "test-profile"));
                paths.EnsureCreated();
                UberDashboard.Infrastructure.DependencyInjection.InfrastructureServiceCollectionExtensions.AddUberDashboardInfrastructure(services, paths);
                UberDashboard.Platform.Windows.DependencyInjection.WindowsServiceCollectionExtensions.AddUberDashboardWindows(services);
                services.AddUberDashboardApp();
                services.AddSingleton<UberDashboard.Application.Weather.IWeatherService, SampleWeatherService>();
                await using var provider = services.BuildServiceProvider();
                var window = provider.GetRequiredService<UberDashboard.App.MainWindow>();
                var shell = (ShellViewModel)window.DataContext;
                var theme = provider.GetRequiredService<ThemeService>();
                await shell.InitializeAsync();
                if (shell.IsLayoutEmpty)
                {
                    await shell.AddPanelCommand.ExecuteAsync("notes");
                    await shell.AddPanelCommand.ExecuteAsync("todo");
                    await shell.AddPanelCommand.ExecuteAsync("weather");
                }
                foreach (var leaf in Leaves(shell.Root))
                {
                    if (leaf.ViewModel is UberDashboard.App.Features.Todo.TodoPanelViewModel todo)
                    {
                        todo.NewItemText = "Przykładowe zadanie z dłuższym opisem, który powinien się zawijać";
                        todo.AddItemCommand.Execute(null);
                        todo.NewItemText = "Gotowe zadanie";
                        todo.AddItemCommand.Execute(null);
                        todo.Items[1].IsDone = true;
                    }
                    if (leaf.ViewModel is UberDashboard.App.Features.Notes.NotesPanelViewModel notes)
                    {
                        notes.Title = "Notatka testowa";
                        var pixels = Enumerable.Repeat(new byte[] { 190, 120, 40, 255 }, 80 * 40).SelectMany(x => x).ToArray();
                        var image = new System.Windows.Controls.Image
                        {
                            Source = BitmapSource.Create(80, 40, 96, 96, PixelFormats.Bgra32, null, pixels, 80 * 4),
                            Width = 80,
                            Height = 40,
                        };
                        var document = new System.Windows.Documents.FlowDocument(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("Tekst notatki z obrazem. Zażółć gęślą jaźń.")));
                        document.Blocks.Add(new System.Windows.Documents.BlockUIContainer(image));
                        notes.RichContent = UberDashboard.App.Features.Notes.NotesRichContentSerializer.Serialize(document).Payload;
                        notes.PlainText = "Tekst notatki z obrazem. Zażółć gęślą jaźń.";
                    }
                    if (leaf.ViewModel is UberDashboard.App.Features.Weather.WeatherPanelViewModel weather)
                    {
                        weather.IsNetworkEnabled = true;
                        weather.SelectedLocation = new("Testowe miasto", "Region", "Polska", 50, 20, "Europe/Warsaw");
                        await weather.RefreshAsync();
                    }
                }
                provider.GetRequiredService<UberDashboard.App.Toasts.IToastService>().Clear();
                if (args.Contains("--interactive"))
                {
                    shell.IsEditMode = true;
                    window.Title = "UberDashboard — test UI";
                    window.Width = 1200; window.Height = 850;
                    app.MainWindow = window;
                    var closed = new TaskCompletionSource();
                    window.Closed += (_, _) => closed.TrySetResult();
                    window.Show();
                    await closed.Task;
                    _exitCode = 0;
                    return;
                }
                foreach (var kind in new[] { ThemeKind.Light, ThemeKind.Dark, ThemeKind.System })
                {
                    Console.WriteLine($"Applying theme {kind}");
                    await theme.ApplyAsync(kind, CancellationToken.None);
                    Console.WriteLine("Theme applied");
                    foreach (var key in new[] { "Token.FontFamily", "Token.Padding.Panel", "Token.FontSize.Body", "Token.CornerRadius.L" })
                        if (app.TryFindResource(key) is null) throw new InvalidOperationException($"Missing resource after theme switch: {key}");
                    foreach (var editMode in new[] { false, true })
                        foreach (var size in new[] { new Size(1280, 820), new Size(860, 560), new Size(1920, 200), new Size(200, 900) })
                        {
                            shell.IsEditMode = editMode;
                            shell.SyncWindowMode(size.Width == 200 || size.Height == 200 ? UberDashboard.Application.Platform.WindowMode.DockedBar : UberDashboard.Application.Platform.WindowMode.Normal);
                            var root = (FrameworkElement)window.Content;
                            root.Measure(size);
                            root.Arrange(new Rect(size));
                            root.UpdateLayout();
                            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            foreach (var noteEditor in Descendants(root).OfType<UberDashboard.App.Controls.BindableRichTextBox>())
                            {
                                var run = noteEditor.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().SelectMany(p => p.Inlines.OfType<System.Windows.Documents.Run>()).FirstOrDefault();
                                if (run?.Foreground is SolidColorBrush runBrush && noteEditor.Foreground is SolidColorBrush editorBrush && runBrush.Color != editorBrush.Color)
                                    throw new InvalidOperationException("Serialized note foreground did not follow the theme");
                            }
                            var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                            DrawBackground(bitmap, app, size);
                            bitmap.Render(root);
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            ValidateGlyphs(root);
                            if (!Descendants(root).OfType<System.Windows.Controls.Image>().Any(i => i.Source is not null)) throw new InvalidOperationException("Image round-trip was not rendered");
                            var filename = $"{kind}-{size.Width}x{size.Height}{(editMode ? "-edit" : "")}.png";
                            using var stream = File.Create(Path.Combine(output, filename));
                            encoder.Save(stream);
                            Console.WriteLine($"RENDER PASS {filename}");
                        }
                }
                var settingsWindow = provider.GetRequiredService<UberDashboard.App.Settings.SettingsWindow>();
                await ((UberDashboard.App.Settings.SettingsViewModel)settingsWindow.DataContext).LoadAsync();
                var settingsRoot = (FrameworkElement)settingsWindow.Content;
                foreach (var kind in new[] { ThemeKind.Light, ThemeKind.Dark, ThemeKind.System })
                {
                    await theme.ApplyAsync(kind, CancellationToken.None);
                    for (var tab = 0; tab < 3; tab++)
                    {
                        Descendants(settingsRoot).OfType<TabControl>().FirstOrDefault()?.SetCurrentValue(TabControl.SelectedIndexProperty, tab);
                        settingsRoot.Measure(new Size(980, 720));
                        settingsRoot.Arrange(new Rect(0, 0, 980, 720));
                        settingsRoot.UpdateLayout();
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        // The first measure creates the template and its tab control.
                        var tabs = Descendants(settingsRoot).OfType<TabControl>().Single();
                        tabs.SelectedIndex = tab;
                        settingsRoot.UpdateLayout();
                        var bitmap = new RenderTargetBitmap(980, 720, 96, 96, PixelFormats.Pbgra32);
                        DrawBackground(bitmap, app, new Size(980, 720));
                        bitmap.Render(settingsRoot);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        ValidateGlyphs(settingsRoot);
                        using var stream = File.Create(Path.Combine(output, $"{kind}-settings-{tab}.png"));
                        encoder.Save(stream);
                        Console.WriteLine($"RENDER PASS {kind}-settings-{tab}.png");
                    }
                }
                var liveSettings = (UberDashboard.App.Settings.SettingsViewModel)settingsWindow.DataContext;
                liveSettings.SelectedFontFamily = "Consolas";
                liveSettings.FontSize = 18;
                if (theme.ReadFontFamilyName() != "Consolas" || theme.ReadDouble("Token.FontSize.Body", 0) != 18)
                    throw new InvalidOperationException("Live font preview did not update immediately");
                await liveSettings.FlushAsync();
                var customLeaf = Leaves(shell.Root).First();
                customLeaf.Appearance!.CustomizeText = true;
                customLeaf.Appearance.FontFamily = "Verdana";
                customLeaf.Appearance.FontSize = 17;
                customLeaf.Appearance.BackgroundColor = "#FF204535";
                var customRoot = (FrameworkElement)window.Content;
                customRoot.Measure(new Size(1280, 820)); customRoot.Arrange(new Rect(0, 0, 1280, 820)); customRoot.UpdateLayout();
                var editor = Descendants(customRoot).OfType<UberDashboard.App.Controls.BindableRichTextBox>().First();
                if (editor.FontFamily.Source != "Verdana" || editor.FontSize != 17 || editor.Document.FontFamily.Source != "Verdana")
                    throw new InvalidOperationException("Panel font override did not reach the editor");
                await shell.FlushAppearancesAsync();
                Console.WriteLine("LIVE PREVIEW AND PANEL FONT PASS");
                foreach (var kind in new[] { ThemeKind.Light, ThemeKind.Dark, ThemeKind.System })
                {
                    await theme.ApplyAsync(kind, CancellationToken.None);
                    foreach (var leaf in Leaves(shell.Root))
                    foreach (var size in new[] { new Size(900, 180), new Size(320, 700) })
                    {
                        var card = new ContentControl { Content = leaf, ContentTemplate = (DataTemplate)app.FindResource("PanelLeafTemplate") };
                        card.Measure(size); card.Arrange(new Rect(size)); card.UpdateLayout();
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                        DrawBackground(bitmap, app, size); bitmap.Render(card);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        var filename = $"{kind}-{leaf.PanelTypeId}-{size.Width}x{size.Height}.png";
                        using var stream = File.Create(Path.Combine(output, filename)); encoder.Save(stream);
                        Console.WriteLine($"RENDER PASS {filename}");
                    }
                }
                settingsWindow.Close();
                window.Close();

                _exitCode = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }));
        Dispatcher.Run();
        return _exitCode;
    }
}
