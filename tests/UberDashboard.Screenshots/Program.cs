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
using UberDashboard.Application.Weather;
using UberDashboard.Core.Localization;

// Renders README screenshots from an isolated temp profile and invented data only.
// Usage: dotnet run --project tests/UberDashboard.Screenshots -- docs/screenshots [--english]
internal static class Program
{
    private static int _exitCode = 1;

    private sealed class MockWeather : IWeatherService
    {
        public Task<WeatherSnapshot> GetCurrentAsync(double latitude, double longitude, CancellationToken cancellationToken)
        {
            var time = DateTimeOffset.Parse("2026-11-02T12:00:00+01:00");
            return Task.FromResult(new WeatherSnapshot(8.5, 2, 12.4, true, time, time, false, 12, 4, time.AddHours(-5), time.AddHours(4),
                Enumerable.Range(0, 6).Select(i => new WeatherHourlyForecastPoint(time.AddHours(i), 8.5 + i, i < 3 ? 2 : 61, true)).ToArray()));
        }
    }

    private static void Save(FrameworkElement root, Size size, System.Windows.Application app, string path)
    {
        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var d = bg.RenderOpen()) d.DrawRectangle((Brush)app.FindResource("Brush.Window.Background"), null, new Rect(size));
        bitmap.Render(bg); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
        Console.WriteLine("WROTE " + path);
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) return found;
            if (Find<T>(child) is { } nested) return nested;
        }
        return null;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "docs/screenshots");
        Directory.CreateDirectory(output);
        UiText.Configure(args.Contains("--english") ? UiText.English : UiText.Polish);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var resource in new[] { "Theme.Light", "VisualTokens", "Controls", "WindowFrame", "PanelTemplates", "PanelOptionTemplates" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/UberDashboard;component/Theming/{resource}.xaml") });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                var profile = Path.Combine(Path.GetTempPath(), "UberDashboard-screenshots", Guid.NewGuid().ToString("N"));
                var services = new ServiceCollection();
                services.AddLogging();
                var paths = new UberDashboard.Infrastructure.Paths.AppPaths(profile);
                paths.EnsureCreated();
                UberDashboard.Infrastructure.DependencyInjection.InfrastructureServiceCollectionExtensions.AddUberDashboardInfrastructure(services, paths);
                UberDashboard.Platform.Windows.DependencyInjection.WindowsServiceCollectionExtensions.AddUberDashboardWindows(services);
                services.AddUberDashboardApp();
                services.AddSingleton<IWeatherService, MockWeather>();
                await using var provider = services.BuildServiceProvider();
                var window = provider.GetRequiredService<UberDashboard.App.MainWindow>();
                var shell = (ShellViewModel)window.DataContext;
                var theme = provider.GetRequiredService<ThemeService>();
                await shell.InitializeAsync();
                foreach (var id in new[] { "todo", "weather", "affirmations" })
                    await shell.AddPanelCommand.ExecuteAsync(id);
                foreach (var leaf in shell.Panels)
                {
                    switch (leaf.ViewModel)
                    {
                        case UberDashboard.App.Features.Affirmations.AffirmationPanelViewModel a:
                            a.Texts = "Robię jedną ważną rzecz naraz.\nSpokój jest moją przewagą.";
                            a.Accept("Robię jedną ");
                            break;
                        case UberDashboard.App.Features.Todo.TodoPanelViewModel t:
                            foreach (var item in new[] { "Przejrzeć plan tygodnia", "Zamówić prezent", "Wysłać raport do zespołu", "Kupić bilety na pociąg" })
                            { t.NewItemText = item; t.AddItemCommand.Execute(null); }
                            t.Items[2].IsDone = true;
                            break;
                        case UberDashboard.App.Features.Notes.NotesPanelViewModel n:
                            n.Title = "Pomysły";
                            n.PlainText = "Dashboard na drugi monitor:\n- kalendarz i zadania obok siebie\n- pogoda na dziś\n- szybkie notatki";
                            break;
                        case UberDashboard.App.Features.Weather.WeatherPanelViewModel w:
                            w.IconSet = "color";
                            w.IsNetworkEnabled = true;
                            w.SelectedLocation = new("Warszawa", "Mazowieckie", "Polska", 52.23, 21.01, "Europe/Warsaw");
                            await w.RefreshAsync();
                            break;
                    }
                }
                provider.GetRequiredService<UberDashboard.App.Toasts.IToastService>().Clear();
                var settingsWindow = provider.GetRequiredService<UberDashboard.App.Settings.SettingsWindow>();
                await ((UberDashboard.App.Settings.SettingsViewModel)settingsWindow.DataContext).LoadAsync();

                foreach (var kind in new[] { ThemeKind.Dark, ThemeKind.Light })
                {
                    await theme.ApplyAsync(kind, CancellationToken.None);
                    var name = kind.ToString().ToLowerInvariant();
                    foreach (var edit in new[] { false })
                    {
                        shell.IsEditMode = edit;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Save((FrameworkElement)window.Content, new Size(1280, 760), app, Path.Combine(output, $"main-{name}{(edit ? "-edit" : "")}.png"));
                    }
                    shell.IsEditMode = false;
                    ((UberDashboard.App.Settings.SettingsViewModel)settingsWindow.DataContext).SelectedTheme = kind;
                    var root = (FrameworkElement)settingsWindow.Content;
                    root.Measure(new Size(900, 660)); root.Arrange(new Rect(0, 0, 900, 660)); root.UpdateLayout();
                    for (var tab = 0; tab < 7; tab++)
                    {
                        var tabs = Find<TabControl>(root) ?? throw new InvalidOperationException("No tab control");
                        tabs.SelectedIndex = tab; root.UpdateLayout();
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Save(root, new Size(900, 660), app, Path.Combine(output, $"settings-{tab + 1}-{name}.png"));
                    }
                }
                _exitCode = 0;
            }
            catch (Exception ex) { Console.WriteLine(ex); }
            finally { app.Shutdown(); }
        }));
        app.Run();
        return _exitCode;
    }
}
