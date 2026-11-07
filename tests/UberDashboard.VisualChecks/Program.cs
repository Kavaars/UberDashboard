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
using UberDashboard.Core.Localization;

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
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "Release/VisualChecks");
        Directory.CreateDirectory(output);
        var english = args.Contains("--english");
        UiText.Configure(english ? UiText.English : UiText.Polish);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var resource in new[] { "Theme.Light", "VisualTokens", "Controls", "WindowFrame", "PanelTemplates" })
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
                DockedBarChecks.Run(provider);
                await WindowPlacementChecks.RunAsync(provider);
                window.ApplyTemplate();
                if (window.Template.FindName("Caption", window) is not Border caption ||
                    System.Windows.Shell.WindowChrome.GetWindowChrome(window) is null)
                    throw new InvalidOperationException("Missing themed window caption");

                var scroll = new ScrollViewer
                {
                    Content = new Border { Width = 600, Height = 600 },
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                };
                scroll.Measure(new Size(140, 140));
                scroll.Arrange(new Rect(0, 0, 140, 140));
                foreach (var name in new[] { "PART_VerticalScrollBar", "PART_HorizontalScrollBar" })
                {
                    var bar = (System.Windows.Controls.Primitives.ScrollBar)scroll.Template.FindName(name, scroll);
                    bar.ApplyTemplate();
                    if (bar.Template.FindName("PART_Track", bar) is System.Windows.Controls.Primitives.Track appliedTrack) appliedTrack.Thumb.ApplyTemplate();
                    if (bar.Template.FindName("PART_Track", bar) is not System.Windows.Controls.Primitives.Track track ||
                        track.Thumb.Template.FindName("Grip", track.Thumb) is not Border ||
                        track.Orientation != bar.Orientation)
                        throw new InvalidOperationException($"Missing themed scroll grip: {name}");
                }

                var shell = (ShellViewModel)window.DataContext;
                var theme = provider.GetRequiredService<ThemeService>();
                if (args.Contains("--files-series-only"))
                {
                    var probeOwner = UberDashboard.App.Controls.WindowFrame.Create(null, "Files and series checks", 800, 700);
                    probeOwner.Show();
                    try { await FileSeriesChecks.RunAsync(probeOwner, theme, output); }
                    finally { probeOwner.Close(); }
                    _exitCode = 0; return;
                }
                await shell.InitializeAsync();
                if (shell.IsLayoutEmpty)
                {
                    await shell.AddPanelCommand.ExecuteAsync("notes");
                    await shell.AddPanelCommand.ExecuteAsync("todo");
                    await shell.AddPanelCommand.ExecuteAsync("weather");
                    await shell.AddPanelCommand.ExecuteAsync("affirmations");
                }
                foreach (var leaf in Leaves(shell.Root))
                {
                    if (leaf.ViewModel is UberDashboard.App.Features.Affirmations.AffirmationPanelViewModel affirmation)
                    {
                        affirmation.Texts = "Jestem spokojny.\nKażdy dzień daje nowe możliwości.";
                        affirmation.Accept("Jestem ");
                    }
                    if (leaf.ViewModel is UberDashboard.App.Features.Todo.TodoPanelViewModel todo)
                    {
                        todo.NewItemText = "Przykładowe zadanie z dłuższym opisem, który powinien się zawijać";
                        todo.AddItemCommand.Execute(null);
                        todo.NewItemText = "Gotowe zadanie";
                        todo.AddItemCommand.Execute(null);
                        todo.Items[1].IsDone = true;
                        await leaf.TabSession!.ImportTabsAsync([new("Microsoft To Do", "{}", 3,
                            new UberDashboard.App.Features.Todo.MicrosoftTodoLink(Guid.Parse("44c153ca-a8f3-446e-8ec5-1bc56f5c65ba"), "visual-account", "visual-list"))], CancellationToken.None);

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
                var affirmationInputChecked = false;
                foreach (var kind in new[] { ThemeKind.Light, ThemeKind.Dark, ThemeKind.System })
                {
                    Console.WriteLine($"Applying theme {kind}");
                    await theme.ApplyAsync(kind, CancellationToken.None);
                    Console.WriteLine("Theme applied");
                    foreach (var state in new[] { WindowState.Normal, WindowState.Maximized })
                    {
                        window.WindowState = state;
                        caption.Measure(new Size(640, 39));
                        caption.Arrange(new Rect(0, 0, 640, 39));
                        caption.UpdateLayout();
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        ValidateGlyphs(caption);
                        var icons = Descendants(caption).OfType<Button>()
                            .Where(button => button.Visibility == Visibility.Visible)
                            .Select(button => button.Content)
                            .OfType<System.Windows.Shapes.Path>()
                            .ToArray();
                        if (icons.Length != 3 || icons.Any(icon => icon.Stroke is null || icon.Data.IsEmpty()) ||
                            icons.Select(icon => icon.Data.ToString()).Distinct().Count() != 3)
                            throw new InvalidOperationException($"Invalid caption icons in {kind} / {state}");

                        var bitmap = new RenderTargetBitmap(640, 39, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(caption);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(output, $"{kind}-caption-{state}.png"));
                        encoder.Save(stream);
                    }
                    window.WindowState = WindowState.Normal;
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
                            if (!affirmationInputChecked)
                            {
                                var input = Descendants(root).OfType<UberDashboard.App.Controls.AffirmationInput>().Single();
                                var prompt = Descendants(root).OfType<StackPanel>().Single(p => UberDashboard.App.Controls.AffirmationInput.GetFocusTarget(p) == input);
                                var click = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                                { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
                                prompt.RaiseEvent(click);
                                if (!click.Handled) throw new InvalidOperationException("Affirmation prompt did not handle click for focus");
                                var model = (UberDashboard.App.Features.Affirmations.AffirmationPanelViewModel)input.DataContext;
                                void Type(string text)
                                {
                                    var composition = new System.Windows.Input.TextComposition(System.Windows.Input.InputManager.Current, input, text);
                                    var args = new System.Windows.Input.TextCompositionEventArgs(System.Windows.Input.InputManager.Current.PrimaryKeyboardDevice, composition)
                                    { RoutedEvent = System.Windows.Input.TextCompositionManager.PreviewTextInputEvent };
                                    input.RaiseEvent(args);
                                }
                                Type("x");
                                if (model.Typed != "Jestem ") throw new InvalidOperationException("Invalid character changed affirmation progress");
                                Type("s");
                                if (model.Typed != "Jestem s") throw new InvalidOperationException("Keyboard input did not update affirmation progress");
                                affirmationInputChecked = true;
                                Console.WriteLine("AFFIRMATION PROMPT AND TEXT INPUT PASS");
                            }
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
                if (settingsWindow.Title != (english ? "Settings" : "Ustawienia") ||
                    !string.Equals(window.Language.IetfLanguageTag, english ? "en-US" : "pl-PL", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Window localization did not apply: {settingsWindow.Title}, {window.Language.IetfLanguageTag}.");
                var settingsRoot = (FrameworkElement)settingsWindow.Content;
                foreach (var kind in new[] { ThemeKind.Light, ThemeKind.Dark, ThemeKind.System })
                {
                    await theme.ApplyAsync(kind, CancellationToken.None);
                    foreach (var settingsSize in new[] { new Size(860, 650), new Size(740, 520) })
                    for (var tab = 0; tab < 7; tab++)
                    {
                        Descendants(settingsRoot).OfType<TabControl>().FirstOrDefault()?.SetCurrentValue(TabControl.SelectedIndexProperty, tab);
                        settingsRoot.Measure(settingsSize);
                        settingsRoot.Arrange(new Rect(settingsSize));
                        settingsRoot.UpdateLayout();
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        // The first measure creates the template and its tab control.
                        var tabs = Descendants(settingsRoot).OfType<TabControl>().Single();
                        tabs.SelectedIndex = tab;
                        settingsRoot.UpdateLayout();
                        var pickers = Descendants(settingsRoot).OfType<UberDashboard.App.Controls.CompactColorPicker>().ToArray();
                        if (pickers.Any(p => p.IsEditorCreated)) throw new InvalidOperationException("Color editor was eagerly created");
                        var bitmap = new RenderTargetBitmap((int)settingsSize.Width, (int)settingsSize.Height, 96, 96, PixelFormats.Pbgra32);
                        DrawBackground(bitmap, app, settingsSize);
                        bitmap.Render(settingsRoot);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        ValidateGlyphs(settingsRoot);
                        using var stream = File.Create(Path.Combine(output, $"{kind}-settings-{tab}-{settingsSize.Width}x{settingsSize.Height}.png"));
                        encoder.Save(stream);
                        Console.WriteLine($"RENDER PASS {kind}-settings-{tab}-{settingsSize.Width}x{settingsSize.Height}.png");
                    }
                }
                var liveSettings = (UberDashboard.App.Settings.SettingsViewModel)settingsWindow.DataContext;
                foreach (var kind in new[] { ThemeKind.Light, ThemeKind.Dark })
                {
                    liveSettings.SelectedTheme = kind;
                    liveSettings.FontSize = 24;
                    liveSettings.DensityScale = 1.35;
                    liveSettings.PanelSpacing = 32;
                    liveSettings.DividerThickness = 0;
                    await liveSettings.FlushAsync();
                    if (theme.ReadDouble("Token.Splitter.Line", -1) != 0
                        || app.FindResource("Token.Margin.Panel") is not Thickness { Left: 13 })
                        throw new InvalidOperationException("Live spacing resources did not follow settings");
                    foreach (var tab in new[] { 0, 1, 2, 3, 4, 5, 6 })
                    {
                        Descendants(settingsRoot).OfType<TabControl>().Single().SelectedIndex = tab;
                        var size = new Size(740, 520);
                        settingsRoot.Measure(size); settingsRoot.Arrange(new Rect(size)); settingsRoot.UpdateLayout();
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        var bitmap = new RenderTargetBitmap(740, 520, 96, 96, PixelFormats.Pbgra32);
                        DrawBackground(bitmap, app, size); bitmap.Render(settingsRoot); ValidateGlyphs(settingsRoot);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        var filename = $"{kind}-settings-large-font-{tab}.png";
                        using var stream = File.Create(Path.Combine(output, filename)); encoder.Save(stream);
                        Console.WriteLine($"RENDER PASS {filename}");
                    }
                }
                liveSettings.DensityScale = 1;
                liveSettings.PanelSpacing = 10;
                liveSettings.DividerThickness = 1;
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
                foreach (var panelId in new[] { "calendar", "system-resources", "shortcuts" })
                {
                    if (!Leaves(shell.Root).Any(leaf => leaf.PanelTypeId == panelId))
                        await shell.AddPanelCommand.ExecuteAsync(panelId);
                    if (!Leaves(shell.Root).Any(leaf => leaf.PanelTypeId == panelId))
                        throw new InvalidOperationException($"Cannot add {panelId} through the shell.");
                }

                var shortcuts = (UberDashboard.App.Features.Shortcuts.ShortcutsPanel)
                    Leaves(shell.Root).Single(leaf => leaf.PanelTypeId == "shortcuts").ViewModel;
                await ShortcutsChecks.RunAsync(shortcuts, theme, output);
                await SyncEditorChecks.RunAsync(window, theme, output);

                var calendar = (UberDashboard.App.Features.Calendar.CalendarPanel)
                    Leaves(shell.Root).Single(leaf => leaf.PanelTypeId == "calendar").ViewModel;
                await calendar.RefreshCommand.ExecuteAsync(null);
                if (calendar.Modes[0].Label != (english ? "Month" : "Miesiąc"))
                    throw new InvalidOperationException("Calendar localization did not apply.");
                if (calendar.Columns != 7 || calendar.VisibleDays.Count is < 28 or > 42)
                    throw new InvalidOperationException("Calendar month range is invalid.");

                var resources = (UberDashboard.App.Features.Resources.ResourcePanel)
                    Leaves(shell.Root).Single(leaf => leaf.PanelTypeId == "system-resources").ViewModel;
                if (resources.SampleCommand.ExecutionTask is { } sampling)
                    await sampling;
                Console.WriteLine($"RESOURCE CHECK: {resources.Status} ({resources.Readings.Count} readings)");
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
                        if (leaf.PanelTypeId == "calendar" &&
                            !Descendants(card).OfType<System.Windows.Controls.Primitives.UniformGrid>().Any(grid => grid.Columns == 7))
                            throw new InvalidOperationException("Calendar grid was not rendered.");
                        if (leaf.PanelTypeId == "todo")
                        {
                            var linkedButtons = Descendants(card).OfType<Button>().Where(b => b.Tag is UberDashboard.Core.Panels.PanelTab tab && leaf.TabSession!.IsMicrosoftTodoTab(tab.InstanceId)).ToArray();
                            if (linkedButtons.Length != 1 || linkedButtons[0].BorderThickness != new Thickness(1) ||
                                linkedButtons[0].BorderBrush is not SolidColorBrush brush || brush.Color != Color.FromRgb(0x46, 0x8f, 0xd6))
                                throw new InvalidOperationException("Microsoft To Do tab border was not rendered.");
                        }
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
