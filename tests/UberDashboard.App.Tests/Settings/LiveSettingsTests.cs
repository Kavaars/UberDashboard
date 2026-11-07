using Microsoft.Extensions.Logging.Abstractions;
using UberDashboard.Application.Platform;
using UberDashboard.App.Settings;
using UberDashboard.App.SystemIntegration;
using UberDashboard.App.Tests.TestSupport;
using UberDashboard.App.Theming;
using UberDashboard.App.Toasts;

namespace UberDashboard.App.Tests.Settings;

public sealed class LiveSettingsTests
{
    [Fact]
    public async Task Open_settings_do_not_restore_stale_launch_mode_when_editing_bar_thickness()
    {
        var store = new FakeAppSettingsStore(); var clock = new FakeClock();
        var settings = new ApplicationSettingsService(store, clock);
        using var theme = new ThemeService(store, settings, clock, NullLogger<ThemeService>.Instance);
        var windowSettings = new FakeWindowModeSettingsService
        {
            Settings = new WindowModeSettings { LaunchMode = WindowMode.Normal },
        };
        var integration = new Integration();
        var vm = new SettingsViewModel(settings, windowSettings, new Coordinator { ActiveMode = WindowMode.DockedBar },
            new FakeMonitorProvider { Monitors = [] }, theme, new ToastService(NullLogger<ToastService>.Instance),
            integration, integration, new Picker());
        await vm.LoadAsync();
        windowSettings.Settings = windowSettings.Settings with { LaunchMode = WindowMode.DockedBar };
        vm.DockThickness = 340;
        await vm.FlushAsync();
        Assert.Equal(WindowMode.DockedBar, windowSettings.Settings.LaunchMode);
    }

    [Fact]
    public async Task Changes_save_without_apply_and_font_does_not_switch_window()
    {
        var store = new FakeAppSettingsStore(); var clock = new FakeClock();
        var settings = new ApplicationSettingsService(store, clock);
        using var theme = new ThemeService(store, settings, clock, NullLogger<ThemeService>.Instance);
        var coordinator = new Coordinator(); var integration = new Integration();
        var vm = new SettingsViewModel(settings, new FakeWindowModeSettingsService(), coordinator,
            new FakeMonitorProvider { Monitors = [] }, theme, new ToastService(NullLogger<ToastService>.Instance), integration, integration, new Picker());
        await vm.LoadAsync();
        Assert.Equal("pl", vm.SelectedLanguage);
        vm.SelectedLanguage = "en";
        vm.FontSize = 18; vm.FontSize = 21; vm.AccentColor = "#123456";
        await vm.FlushAsync();
        Assert.Equal(21, (await settings.LoadAsync()).FontSize);
        Assert.Equal("en", (await settings.LoadAsync()).Language);
        Assert.Equal("#123456", (await settings.LoadAsync()).AccentColor);
        Assert.Equal(0, coordinator.Calls);
        Assert.Equal(0, integration.Calls);
        vm.TransparentDesktopBackground = true;
        await vm.FlushAsync();
        Assert.True((await settings.LoadAsync()).TransparentDesktopBackground);
        Assert.Equal(0, coordinator.Calls);
        vm.SelectedTheme = ThemeKind.Dark;
        Assert.Equal(ThemeKind.Dark, theme.Current);
        await vm.FlushAsync();
        Assert.Null((await settings.LoadAsync()).SurfaceColor);
        vm.SelectedLaunchMode = WindowMode.Normal;
        await vm.FlushAsync();
        Assert.Equal(1, coordinator.Calls);
    }

    [Fact]
    public async Task Theme_and_spacing_edits_preserve_all_existing_preferences_after_reload()
    {
        var store = new FakeAppSettingsStore(); var clock = new FakeClock();
        var settings = new ApplicationSettingsService(store, clock);
        var original = ApplicationSettings.Default with
        {
            AccentColor = "#123456", SurfaceColor = "#203040", SurfaceAltColor = "#304050",
            WindowBackgroundColor = "#102030", FontFamilyName = "Verdana", FontSize = 19,
            CornerRadius = 22, DensityScale = 1.2, WallpaperPath = "wallpaper.png", WallpaperBlur = 12,
            TransparentDesktopBackground = true, BackgroundSurfaceOpacity = .4, WindowOpacity = .8,
            DesktopBlur = true, EnableTrayIcon = true, CloseBehavior = CloseBehavior.MinimizeToTray,
            WeatherRefreshMinutes = 55, Language = "en",
        };
        await settings.SaveAsync(original);
        using var theme = new ThemeService(store, settings, clock, NullLogger<ThemeService>.Instance);
        var integration = new Integration(); var coordinator = new Coordinator();
        var vm = new SettingsViewModel(settings, new FakeWindowModeSettingsService(), coordinator,
            new FakeMonitorProvider { Monitors = [] }, theme, new ToastService(NullLogger<ToastService>.Instance), integration, integration, new Picker());
        await vm.LoadAsync();
        vm.PanelSpacing = 24; vm.DividerThickness = 0; vm.SelectedTheme = ThemeKind.Dark;
        await vm.FlushAsync();
        Assert.Equal(original with { PanelSpacing = 24, DividerThickness = 0 }, await settings.LoadAsync());
        await vm.LoadAsync();
        Assert.Equal(24, vm.PanelSpacing); Assert.Equal(0, vm.DividerThickness);
        Assert.Equal("en", vm.SelectedLanguage);
        Assert.Equal(0, coordinator.Calls); Assert.Equal(0, integration.Calls);
    }

    [Fact]
    public async Task Changing_bar_thickness_preserves_the_current_mode_instead_of_switching_to_launch_mode()
    {
        var store = new FakeAppSettingsStore(); var clock = new FakeClock();
        var settings = new ApplicationSettingsService(store, clock);
        using var theme = new ThemeService(store, settings, clock, NullLogger<ThemeService>.Instance);
        var coordinator = new Coordinator { ActiveMode = WindowMode.DockedBar }; var integration = new Integration();
        var vm = new SettingsViewModel(settings, new FakeWindowModeSettingsService(), coordinator,
            new FakeMonitorProvider { Monitors = [] }, theme, new ToastService(NullLogger<ToastService>.Instance), integration, integration, new Picker());
        await vm.LoadAsync();
        vm.DockThickness = 340;
        await vm.FlushAsync();
        Assert.Equal(WindowMode.DockedBar, coordinator.LastMode);
    }

    [Fact]
    public async Task Appearance_survives_reload_and_is_isolated_by_instance()
    {
        var store = new FakeAppSettingsStore(); var clock = new FakeClock(); var id = Guid.NewGuid();
        var first = new PanelAppearanceViewModel(id, store, clock, new ToastService(NullLogger<ToastService>.Instance));
        await first.LoadAsync();
        first.BackgroundColor = "#123456"; first.CustomizeText = true; first.FontSize = 19;
        await first.SaveAsync();
        var restored = new PanelAppearanceViewModel(id, store, clock, new ToastService(NullLogger<ToastService>.Instance)); await restored.LoadAsync();
        Assert.Equal(first.Snapshot, restored.Snapshot);
        var other = new PanelAppearanceViewModel(Guid.NewGuid(), store, clock, new ToastService(NullLogger<ToastService>.Instance)); await other.LoadAsync();
        Assert.Null(other.BackgroundColor); Assert.False(other.CustomizeText);
        restored.ResetCommand.Execute(null); await restored.SaveAsync();
        Assert.Null(restored.BackgroundColor); Assert.False(restored.CustomizeText);
    }

    [Fact]
    public async Task Appearance_batches_slider_changes_and_retries_a_failed_write()
    {
        var store = new FailingStore();
        var vm = new PanelAppearanceViewModel(Guid.NewGuid(), store, new FakeClock(), new ToastService(NullLogger<ToastService>.Instance));
        await vm.LoadAsync();
        vm.FontSize = 14; vm.FontSize = 16; vm.FontSize = 23;
        await vm.FlushAsync();
        Assert.Equal(1, store.Calls); Assert.True(vm.HasSaveError);
        store.Fail = false;
        await vm.RetrySaveCommand.ExecuteAsync(null);
        Assert.False(vm.HasSaveError); Assert.Equal(2, store.Calls);
        Assert.Contains("23", store.Saved);
    }
    private sealed class FailingStore : UberDashboard.Application.Abstractions.IAppSettingsStore
    {
        public bool Fail { get; set; } = true;
        public int Calls { get; private set; }
        public string Saved { get; private set; } = "";
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SetAsync(string key, string valueJson, DateTimeOffset timestamp, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Fail) return Task.FromException(new IOException("Testowy błąd zapisu"));
            Saved = valueJson; return Task.CompletedTask;
        }
    }

    private sealed class Coordinator : IWindowModeCoordinator
    {
        public int Calls { get; private set; }
        public WindowMode ActiveMode { get; set; } = WindowMode.Normal;
        public WindowMode LastMode { get; private set; }
        public WindowMode CurrentMode => ActiveMode;
        public bool IsAttached => false;
        public void AttachWindow(IntPtr windowHandle) { }
        public Task<WindowModeTransitionResult> InitializeAsync(CancellationToken cancellationToken = default) => SwitchAsync(WindowMode.Normal, cancellationToken);
        public Task<WindowModeTransitionResult> SwitchAsync(WindowMode mode, CancellationToken cancellationToken = default)
        { Calls++; LastMode = mode; return Task.FromResult(WindowModeTransitionResult.Success(mode)); }
    }
    private sealed class Integration : IStartupRegistrationService, ITrayIconService
    {
        public int Calls { get; private set; }
        public bool IsEnabled(string path) => false;
        public void Apply(bool enabled, string path) => Calls++;
        public ApplicationSettings CurrentSettings => ApplicationSettings.Default;
        public void Initialize(Action restore, Action reset, Action exit) { }
        public void ApplySettings(ApplicationSettings settings) => Calls++;
        public void SetRequired(bool required) { }
        public void Dispose() { }
    }
    private sealed class Picker : IColorPickerService
    {
        public bool TryPickColor(string? initial, out string? selected) { selected = null; return false; }
    }
}
