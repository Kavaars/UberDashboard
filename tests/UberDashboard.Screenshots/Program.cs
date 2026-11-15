using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using UberDashboard.App.Hosting;
using UberDashboard.App.Settings;
using UberDashboard.App.Shell;
using UberDashboard.App.Theming;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Weather;
using UberDashboard.Core.Layout;
using UberDashboard.Core.Localization;

// Renders README screenshots of the main window: English UI, isolated temp profile, invented data only.
// Usage: dotnet run --project tests/UberDashboard.Screenshots -- docs/screenshots
internal static class Program
{
    private static int _exitCode = 1;

    private sealed class MockWeather : IWeatherService
    {
        public Task<WeatherSnapshot> GetCurrentAsync(double latitude, double longitude, CancellationToken cancellationToken)
        {
            var time = DateTimeOffset.Now.Date.AddHours(12);
            return Task.FromResult(new WeatherSnapshot(8.5, 2, 12.4, true, time, time, false, 12, 4, time.AddHours(-5), time.AddHours(4),
                Enumerable.Range(0, 6).Select(i => new WeatherHourlyForecastPoint(time.AddHours(i), 8.5 + i, i < 3 ? 2 : 61, true)).ToArray()));
        }
    }

    private sealed record Scenario(string Name, ThemeKind Theme, ApplicationSettings Settings, Dictionary<string, int[]> Cells, int Width = 1600, int Height = 900);

    private static readonly string[] AllTypes = ["weather", "homeassistant", "todo", "notes", "shortcuts", "affirmations"];

    // Cell = { column, row, columnSpan, rowSpan } on a 24 x 16 grid.
    private static readonly Dictionary<string, int[]> CurrentLayout = new()
    {
        ["todo"] = [0, 0, 6, 16], ["weather"] = [6, 0, 18, 5], ["notes"] = [6, 5, 10, 11], ["homeassistant"] = [16, 5, 3, 11],
        ["shortcuts"] = [19, 5, 5, 4], ["affirmations"] = [19, 9, 5, 7],
    };

    private static Scenario[] Scenarios() =>
    [
        // 1. Same arrangement and look as the author's installation, with mock content.
        new("main-current-dark", ThemeKind.Dark, new ApplicationSettings
        {
            PaletteVersion = 1, AccentColor = "#FFB5BEC8", WindowBackgroundColor = "#FF202329", SurfaceColor = "#FF30343B", SurfaceAltColor = "#FF3A3F48",
            FontFamilyName = "Segoe UI", FontSize = 13, CornerRadius = 18, DensityScale = 1, PanelSpacing = 10,
            WallpaperPath = "builtin:graphite", WallpaperBlur = 20, BackgroundSurfaceOpacity = 0.5,
        }, CurrentLayout),
        // 2. Light theme, softer corners, same panels.
        new("main-light", ThemeKind.Light, new ApplicationSettings { PaletteVersion = 1, CornerRadius = 12, PanelSpacing = 12, FontSize = 13 }, CurrentLayout),
        // 3. Blue dark theme, three columns.
        new("main-ocean-dark", ThemeKind.Dark, new ApplicationSettings
        {
            PaletteVersion = 1, AccentColor = "#FF7AA2F7", WindowBackgroundColor = "#FF14161C", SurfaceColor = "#FF1E222B", SurfaceAltColor = "#FF272C37",
            CornerRadius = 10, PanelSpacing = 8, FontSize = 13,
        }, new()
        {
            ["todo"] = [0, 0, 8, 16], ["weather"] = [8, 0, 8, 8], ["notes"] = [8, 8, 8, 8],
            ["affirmations"] = [16, 0, 8, 6], ["shortcuts"] = [16, 6, 8, 4], ["homeassistant"] = [16, 10, 8, 6],
        }),
        // 4. Warm light theme, sharp corners, larger text, 2x2 grid.
        new("main-warm-light", ThemeKind.Light, new ApplicationSettings
        {
            PaletteVersion = 1, AccentColor = "#FF0F766E", WindowBackgroundColor = "#FFF7F1EA", SurfaceColor = "#FFFFFBF6", SurfaceAltColor = "#FFEFE4D8",
            CornerRadius = 4, PanelSpacing = 6, FontSize = 14,
        }, new()
        {
            ["todo"] = [0, 0, 8, 16], ["weather"] = [8, 0, 16, 9], ["shortcuts"] = [8, 9, 8, 7], ["affirmations"] = [16, 9, 8, 7],
        }, 1400, 800),
    ];

    private static ServiceProvider Build(string profile)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var paths = new UberDashboard.Infrastructure.Paths.AppPaths(profile);
        paths.EnsureCreated();
        UberDashboard.Infrastructure.DependencyInjection.InfrastructureServiceCollectionExtensions.AddUberDashboardInfrastructure(services, paths);
        UberDashboard.Platform.Windows.DependencyInjection.WindowsServiceCollectionExtensions.AddUberDashboardWindows(services);
        services.AddUberDashboardApp();
        services.AddSingleton<IWeatherService, MockWeather>();
        return services.BuildServiceProvider();
    }

    private static async Task SetNormalMode(IServiceProvider provider)
    {
        var settings = provider.GetRequiredService<UberDashboard.Application.Platform.IWindowModeSettingsService>();
        await settings.SetAsync((await settings.GetAsync()) with { LaunchMode = UberDashboard.Application.Platform.WindowMode.Normal });
    }

    private static string TypeKey(object vm) => vm switch
    {
        UberDashboard.App.Features.Todo.TodoPanelViewModel => "todo",
        UberDashboard.App.Features.Weather.WeatherPanelViewModel => "weather",
        UberDashboard.App.Features.Notes.NotesPanelViewModel => "notes",
        UberDashboard.App.Features.Affirmations.AffirmationPanelViewModel => "affirmations",
        UberDashboard.App.Features.Resources.ResourcePanel => "system-resources",
        UberDashboard.App.Features.Shortcuts.ShortcutsPanel => "shortcuts",
        UberDashboard.App.Features.HomeAssistant.HomeAssistantPanel => "homeassistant",
        _ => "",
    };

    private static void Save(FrameworkElement root, string path)
    {
        var w = (int)root.ActualWidth; var h = (int)root.ActualHeight;
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
        Console.WriteLine("WROTE " + path);
    }

    private static async Task Render(Scenario scenario, string output)
    {
        var profile = Path.Combine(Path.GetTempPath(), "UberDashboard-screenshots", Guid.NewGuid().ToString("N"));
        // Phase 1: create the panels and store the wanted layout tree.
        await using (var first = Build(profile))
        {
            var shell = first.GetRequiredService<UberDashboard.App.MainWindow>().DataContext as ShellViewModel ?? throw new InvalidOperationException();
            await shell.InitializeAsync();
            foreach (var id in AllTypes.Where(scenario.Cells.ContainsKey)) await shell.AddPanelCommand.ExecuteAsync(id);
            var ids = shell.Panels.ToDictionary(p => TypeKey(p.ViewModel), p => p.InstanceId);
            await Task.Delay(1000);
            var dashboard = (await first.GetRequiredService<IDashboardStore>().ListAsync()).First();
            var cells = scenario.Cells.ToDictionary(pair => ids[pair.Key].Value,
                pair => new { Column = pair.Value[0], Row = pair.Value[1], ColumnSpan = pair.Value[2], RowSpan = pair.Value[3] });
            await first.GetRequiredService<IAppSettingsStore>().SetAsync($"dashboard.grid.{dashboard.DashboardId:N}",
                System.Text.Json.JsonSerializer.Serialize(new { Columns = 24, Rows = 16, Cells = cells }), DateTimeOffset.UtcNow);
            await shell.FlushAppearancesAsync();
            await Task.Delay(1000);
            await SetNormalMode(first);
        }
        // Phase 2: load that profile, fill the panels with mock content, apply the look and render.
        await using var provider = Build(profile);
        var window = provider.GetRequiredService<UberDashboard.App.MainWindow>();
        var model = (ShellViewModel)window.DataContext;
        var theme = provider.GetRequiredService<ThemeService>();
        await SetNormalMode(provider);
        await model.InitializeAsync();
        foreach (var leaf in model.Panels)
        {
            switch (leaf.ViewModel)
            {
                case UberDashboard.App.Features.Affirmations.AffirmationPanelViewModel a:
                    a.Texts = "I do one important thing at a time.\nCalm is my advantage.";
                    a.Accept("I do one ");
                    break;
                case UberDashboard.App.Features.Todo.TodoPanelViewModel t:
                    foreach (var item in new[] { "Plan the week", "Order a birthday gift", "Send the weekly report", "Book train tickets", "Water the plants" })
                    { t.NewItemText = item; t.AddItemCommand.Execute(null); }
                    t.Items[2].IsDone = true;
                    break;
                case UberDashboard.App.Features.Notes.NotesPanelViewModel n:
                    n.Title = "Ideas";
                    var doc = new System.Windows.Documents.FlowDocument();
                    foreach (var line in new[] { "Second-monitor dashboard:", "• calendar and tasks side by side", "• today's weather", "• quick notes" })
                        doc.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(line)));
                    n.RichContent = UberDashboard.App.Features.Notes.NotesRichContentSerializer.Serialize(doc).Payload;
                    n.PlainText = "Second-monitor dashboard";
                    break;
                case UberDashboard.App.Features.Weather.WeatherPanelViewModel w:
                    w.IconSet = "color";
                    w.IsNetworkEnabled = true;
                    w.SelectedLocation = new("Springfield", "Demo County", "Exampleland", 52.23, 21.01, "Europe/Warsaw");
                    await w.RefreshAsync();
                    break;
                case UberDashboard.App.Features.Shortcuts.ShortcutsPanel s:
                    foreach (var (name, glyph) in new[] { ("Mail", "✉"), ("Maps", "▦"), ("Docs", "▤"), ("Music", "♪"), ("Photos", "▣"), ("Shell", "›_") })
                        s.Model.Items.Add(new UberDashboard.App.Features.Shortcuts.ShortcutEntry { Name = name, Target = "https://example.com/" + name.ToLowerInvariant(), Icon = glyph });
                    break;
                case UberDashboard.App.Features.HomeAssistant.HomeAssistantPanel ha:
                    foreach (var (label, value, kind) in new[]
                    {
                        ("Living room", "21.5 °C", UberDashboard.App.Features.HomeAssistant.HomeAssistantModuleKind.Sensor),
                        ("Humidity", "46 %", UberDashboard.App.Features.HomeAssistant.HomeAssistantModuleKind.Sensor),
                        ("Power now", "340 W", UberDashboard.App.Features.HomeAssistant.HomeAssistantModuleKind.Sensor),
                        ("Lights off", "", UberDashboard.App.Features.HomeAssistant.HomeAssistantModuleKind.Action),
                    })
                        ha.Modules.Add(new UberDashboard.App.Features.HomeAssistant.HomeAssistantModuleItem
                        { Id = Guid.NewGuid().ToString("N"), EntityId = "sensor.demo", Label = label, Kind = kind, Value = value });
                    break;
            }
        }
        provider.GetRequiredService<UberDashboard.App.Toasts.IToastService>().Clear();
        await theme.ApplyAsync(scenario.Theme, CancellationToken.None);
        await theme.ApplyApplicationSettingsAsync(scenario.Settings with { Language = UiText.English }, CancellationToken.None, persist: true);

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = 20; window.Top = 20; window.Width = scenario.Width; window.Height = scenario.Height;
        window.ShowInTaskbar = false;
        window.Show();
        for (var i = 0; i < 6; i++) { await Task.Delay(500); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
        window.UpdateLayout();
        Save((FrameworkElement)window.Content, Path.Combine(output, scenario.Name + ".png"));
        window.Close();
    }

    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "docs/screenshots");
        Directory.CreateDirectory(output);
        UiText.Configure(UiText.English);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var resource in new[] { "Theme.Light", "VisualTokens", "Controls", "WindowFrame", "PanelTemplates", "PanelOptionTemplates" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/UberDashboard;component/Theming/{resource}.xaml") });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                foreach (var scenario in Scenarios()) await Render(scenario, output);
                _exitCode = 0;
            }
            catch (Exception ex) { Console.WriteLine(ex); }
            finally { app.Shutdown(); }
        }));
        app.Run();
        return _exitCode;
    }
}
