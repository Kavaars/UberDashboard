using UberDashboard.Core.Localization;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UberDashboard.Application.Platform;
using UberDashboard.App.Shell;
using UberDashboard.App.SystemIntegration;
using UberDashboard.App.Theming;
using UberDashboard.App.Toasts;

namespace UberDashboard.App.Settings;

/// <summary>Model widoku okna ustawień aplikacji.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IApplicationSettingsService _applicationSettingsService;
    private readonly IWindowModeSettingsService _windowModeSettingsService;
    private readonly IWindowModeCoordinator _windowModeCoordinator;
    private readonly IMonitorProvider _monitorProvider;
    private readonly ThemeService _themeService;
    private readonly IToastService _toasts;
    private readonly IStartupRegistrationService _startupRegistrationService;
    private readonly ITrayIconService _trayIconService;
    private readonly IColorPickerService _colorPickerService;
    private bool _loaded;
    private bool _saving;
    private int _revision;
    private bool _windowDirty;
    private bool _launchModeDirty;
    private bool _monitorDirty;
    private bool _integrationDirty;
    private Task _pendingSave = Task.CompletedTask;
    [ObservableProperty] private string saveStatus = UiText.Get("ChangesSaveAutomatically");
    public Task FlushAsync() => _pendingSave;
    internal void NotifyProjectLinkFailure() => _toasts.Show(UiText.Get("CouldNotOpenProjectLink"), ToastSeverity.Warning);
    public IAsyncRelayCommand RetrySaveCommand { get; }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loaded || e.PropertyName == nameof(SaveStatus)) return;
        _integrationDirty |= e.PropertyName is nameof(StartWithWindows) or nameof(EnableTrayIcon) or nameof(SelectedCloseBehavior);
        _windowDirty |= e.PropertyName is nameof(SelectedLaunchMode) or nameof(SelectedMonitor) or nameof(SelectedDockEdge) or nameof(DockThickness) or nameof(WidgetOpacity) or nameof(DesktopWidgetFallbackToNormal);
        _launchModeDirty |= e.PropertyName == nameof(SelectedLaunchMode);
        _monitorDirty |= e.PropertyName == nameof(SelectedMonitor);
        _themeService.Preview(SelectedTheme, CaptureSettings());
        _revision++;
        if (!_saving) _pendingSave = SaveChangesAsync();
    }

    private async Task SaveChangesAsync()
    {
        _saving = true;
        try
        {
            int revision;
            do
            {
                revision = _revision;
                SaveStatus = UiText.Get("Saving");
                await Task.Delay(180).ConfigureAwait(true);
                if (revision != _revision) continue;
                var windowDirty = _windowDirty; _windowDirty = false;
                var integrationDirty = _integrationDirty; _integrationDirty = false;
                try { await ApplyAsync(windowDirty, integrationDirty).ConfigureAwait(true); }
                catch { _windowDirty |= windowDirty; _integrationDirty |= integrationDirty; throw; }
            } while (revision != _revision);
            SaveStatus = UiText.Get("SavedAutomatically");
        }
        catch (Exception)
        {
            SaveStatus = UiText.Get("SettingsCouldNotBeSavedSelectRetry");
            _toasts.Show(SaveStatus, ToastSeverity.Error);
        }
        finally { _saving = false; }
    }

    /// <summary>Tworzy model widoku okna ustawień.</summary>
    public SettingsViewModel(
        IApplicationSettingsService applicationSettingsService,
        IWindowModeSettingsService windowModeSettingsService,
        IWindowModeCoordinator windowModeCoordinator,
        IMonitorProvider monitorProvider,
        ThemeService themeService,
        IToastService toasts,
        IStartupRegistrationService startupRegistrationService,
        ITrayIconService trayIconService,
        IColorPickerService colorPickerService)
    {
        ArgumentNullException.ThrowIfNull(applicationSettingsService);
        ArgumentNullException.ThrowIfNull(windowModeSettingsService);
        ArgumentNullException.ThrowIfNull(windowModeCoordinator);
        ArgumentNullException.ThrowIfNull(monitorProvider);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(startupRegistrationService);
        ArgumentNullException.ThrowIfNull(trayIconService);
        ArgumentNullException.ThrowIfNull(colorPickerService);

        _applicationSettingsService = applicationSettingsService;
        _windowModeSettingsService = windowModeSettingsService;
        _windowModeCoordinator = windowModeCoordinator;
        _monitorProvider = monitorProvider;
        _themeService = themeService;
        _toasts = toasts;
        _startupRegistrationService = startupRegistrationService;
        _trayIconService = trayIconService;
        _colorPickerService = colorPickerService;

        ThemeOptions = Enum.GetValues<ThemeKind>();
        WindowModes = Enum.GetValues<WindowMode>();
        DockEdges = Enum.GetValues<AppBarEdge>();
        AvailableFonts = Fonts.SystemFontFamilies
            .Select(static family => family.Source)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        Monitors = [];

        RetrySaveCommand = new AsyncRelayCommand(() => _saving ? _pendingSave : _pendingSave = SaveChangesAsync());
        ResetDefaultsCommand = new AsyncRelayCommand(ResetDefaultsAsync);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
        ChooseAccentColorCommand = new RelayCommand(() => PickColor(static vm => vm.AccentColor, static (vm, color) => vm.AccentColor = color));
        ChooseWindowBackgroundColorCommand = new RelayCommand(() => PickColor(static vm => vm.WindowBackgroundColor, static (vm, color) => vm.WindowBackgroundColor = color));
        ChooseSurfaceColorCommand = new RelayCommand(() => PickColor(static vm => vm.SurfaceColor, static (vm, color) => vm.SurfaceColor = color));
        ChooseSurfaceAltColorCommand = new RelayCommand(() => PickColor(static vm => vm.SurfaceAltColor, static (vm, color) => vm.SurfaceAltColor = color));
    }

    /// <summary>Akcja przypięta przez okno do zamknięcia widoku.</summary>
    public Action? RequestClose { get; set; }

    /// <summary>Dostępne tryby motywu.</summary>
    public IReadOnlyList<ThemeKind> ThemeOptions { get; }

    /// <summary>Dostępne tryby okna.</summary>
    public IReadOnlyList<WindowMode> WindowModes { get; }

    /// <summary>Dostępne krawędzie AppBar.</summary>
    public IReadOnlyList<AppBarEdge> DockEdges { get; }

    /// <summary>Dostępne zachowania przy zamknięciu okna.</summary>
    public IReadOnlyList<CloseBehavior> CloseBehaviorOptions { get; } = Enum.GetValues<CloseBehavior>();

    /// <summary>Dostępne fonty systemowe.</summary>
    public IReadOnlyList<string> AvailableFonts { get; }

    /// <summary>Monitory wykryte przez host systemowy.</summary>
    public ObservableCollection<DisplayMonitor> Monitors { get; }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
        [new(UiText.Polish, "Polski"), new(UiText.English, "English")];

    public sealed record LanguageOption(string Code, string Name)
    {
        public override string ToString() => Name;
    }

    [ObservableProperty]
    private string selectedLanguage = UiText.Polish;

    [ObservableProperty]
    private ThemeKind selectedTheme;

    [ObservableProperty]
    private string accentColor = "#FF1668C1";

    [ObservableProperty]
    private string windowBackgroundColor = "#FFF6F7F9";

    [ObservableProperty]
    private string surfaceColor = "#FFFFFFFF";

    [ObservableProperty]
    private string surfaceAltColor = "#FFEFF1F4";

    [ObservableProperty]
    private string selectedFontFamily = "Segoe UI";

    [ObservableProperty]
    private double fontSize = 13d;

    [ObservableProperty]
    private double cornerRadius = 12d;

    [ObservableProperty]
    private double densityScale = 1d;

    [ObservableProperty] private double panelSpacing = 10d;
    [ObservableProperty] private double dividerThickness = 1d;

    [ObservableProperty]
    private int weatherRefreshMinutes = 20;

    [ObservableProperty] private string? wallpaperPath;
    [ObservableProperty] private double wallpaperBlur;
    [ObservableProperty] private double backgroundSurfaceOpacity = 0.7;
    [ObservableProperty] private double windowOpacity = 1;
    [ObservableProperty] private bool desktopBlur;
    [ObservableProperty] private bool transparentDesktopBackground;

    [RelayCommand]
    private void ChooseWallpaper()
    {
        var dialog = new UberDashboard.App.Controls.ThemedFileDialog { Filter = UiText.Get("ImagesPngJpgJpegBmp"), Title = UiText.Get("ChooseAppWallpaper") };
        if (dialog.ShowDialog() == true) WallpaperPath = dialog.FileName;
    }

    [RelayCommand]
    private void RemoveWallpaper() => WallpaperPath = null;

    [ObservableProperty]
    private WindowMode selectedLaunchMode = WindowMode.Normal;

    [ObservableProperty]
    private DisplayMonitor? selectedMonitor;

    [ObservableProperty]
    private AppBarEdge selectedDockEdge = AppBarEdge.Bottom;

    [ObservableProperty]
    private int dockThickness = 56;

    [ObservableProperty]
    private double widgetOpacity = 0.92d;

    [ObservableProperty]
    private bool desktopWidgetFallbackToNormal = true;

    [ObservableProperty]
    private bool startWithWindows;

    [ObservableProperty]
    private bool enableTrayIcon = true;

    [ObservableProperty]
    private CloseBehavior selectedCloseBehavior = CloseBehavior.ExitApplication;

    /// <summary>Komenda przywracająca wartości domyślne.</summary>
    public IAsyncRelayCommand ResetDefaultsCommand { get; }

    /// <summary>Komenda zamykająca okno ustawień.</summary>
    public IRelayCommand CloseCommand { get; }

    /// <summary>Graficzny picker koloru akcentu.</summary>
    public IRelayCommand ChooseAccentColorCommand { get; }

    /// <summary>Graficzny picker koloru tła okna.</summary>
    public IRelayCommand ChooseWindowBackgroundColorCommand { get; }

    /// <summary>Graficzny picker koloru powierzchni kart.</summary>
    public IRelayCommand ChooseSurfaceColorCommand { get; }

    /// <summary>Graficzny picker koloru powierzchni pomocniczej.</summary>
    public IRelayCommand ChooseSurfaceAltColorCommand { get; }

    /// <summary>Wczytuje aktualne ustawienia do formularza.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _loaded = false;
        Monitors.Clear();
        foreach (var monitor in _monitorProvider.Enumerate())
        {
            Monitors.Add(monitor);
        }

        SelectedTheme = _themeService.Current;

        var applicationSettings = await _applicationSettingsService.LoadAsync(cancellationToken).ConfigureAwait(true);
        SelectedLanguage = applicationSettings.Language;
        AccentColor = applicationSettings.AccentColor ?? "";
        WindowBackgroundColor = applicationSettings.WindowBackgroundColor ?? "";
        SurfaceColor = applicationSettings.SurfaceColor ?? "";
        SurfaceAltColor = applicationSettings.SurfaceAltColor ?? "";
        SelectedFontFamily = applicationSettings.FontFamilyName ?? _themeService.ReadFontFamilyName();
        FontSize = applicationSettings.FontSize ?? _themeService.ReadDouble("Token.FontSize.Body", 13d);
        CornerRadius = applicationSettings.CornerRadius ?? _themeService.ReadDouble("Token.CornerRadius.M", 12d);
        DensityScale = applicationSettings.DensityScale ?? 1d;
        PanelSpacing = applicationSettings.PanelSpacing ?? 10d;
        DividerThickness = applicationSettings.DividerThickness ?? 1d;
        WeatherRefreshMinutes = applicationSettings.WeatherRefreshMinutes ?? 20;
        WallpaperPath = applicationSettings.WallpaperPath;
        WallpaperBlur = applicationSettings.WallpaperBlur;
        BackgroundSurfaceOpacity = applicationSettings.BackgroundSurfaceOpacity;
        WindowOpacity = applicationSettings.WindowOpacity;
        DesktopBlur = applicationSettings.DesktopBlur;
        TransparentDesktopBackground = applicationSettings.TransparentDesktopBackground;
        StartWithWindows = _startupRegistrationService.IsEnabled(Environment.ProcessPath ?? string.Empty) || applicationSettings.StartWithWindows;
        EnableTrayIcon = applicationSettings.EnableTrayIcon;
        SelectedCloseBehavior = applicationSettings.CloseBehavior;

        var windowSettings = (await _windowModeSettingsService.GetAsync(cancellationToken).ConfigureAwait(true)).Normalize();
        SelectedLaunchMode = windowSettings.LaunchMode;
        SelectedDockEdge = windowSettings.DockEdge;
        DockThickness = windowSettings.DockThickness;
        WidgetOpacity = windowSettings.WidgetOpacity;
        DesktopWidgetFallbackToNormal = windowSettings.DesktopWidgetFallbackToNormal;
        SelectedMonitor = Monitors.FirstOrDefault(static monitor => monitor.IsPrimary)
            ?? Monitors.FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(windowSettings.MonitorId))
        {
            SelectedMonitor = Monitors.FirstOrDefault(
                monitor => string.Equals(monitor.Id, windowSettings.MonitorId, StringComparison.OrdinalIgnoreCase));
        }
        _loaded = true;
    }

    private ApplicationSettings CaptureSettings() => new ApplicationSettings
        {
            PaletteVersion = 1,
            Language = SelectedLanguage,
            AccentColor = AccentColor, WindowBackgroundColor = WindowBackgroundColor, SurfaceColor = SurfaceColor,
            SurfaceAltColor = SurfaceAltColor, FontFamilyName = SelectedFontFamily, FontSize = FontSize,
            CornerRadius = CornerRadius, DensityScale = DensityScale, WeatherRefreshMinutes = WeatherRefreshMinutes,
            PanelSpacing = PanelSpacing, DividerThickness = DividerThickness,
            StartWithWindows = StartWithWindows, EnableTrayIcon = EnableTrayIcon, CloseBehavior = SelectedCloseBehavior,
            WallpaperPath = WallpaperPath, WallpaperBlur = WallpaperBlur, BackgroundSurfaceOpacity = BackgroundSurfaceOpacity, WindowOpacity = WindowOpacity, DesktopBlur = DesktopBlur, TransparentDesktopBackground = TransparentDesktopBackground,
        }.Normalize();

    private async Task ApplyAsync(bool changeWindow = true, bool changeIntegration = true)
    {
        var applicationSettings = CaptureSettings();
        await _themeService.PersistSelectionAsync(SelectedTheme, applicationSettings).ConfigureAwait(true);
        if (changeIntegration)
        {
            _startupRegistrationService.Apply(applicationSettings.StartWithWindows, Environment.ProcessPath ?? string.Empty);
            _trayIconService.ApplySettings(applicationSettings);
        }

        if (!changeWindow) return;
        var currentWindowSettings = await _windowModeSettingsService.GetAsync().ConfigureAwait(true);
        var windowSettings = (currentWindowSettings with
        {
            LaunchMode = _launchModeDirty ? SelectedLaunchMode : currentWindowSettings.LaunchMode,
            MonitorId = _monitorDirty ? SelectedMonitor?.Id : currentWindowSettings.MonitorId,
            DockEdge = SelectedDockEdge,
            DockThickness = DockThickness,
            WidgetOpacity = WidgetOpacity,
            DesktopWidgetFallbackToNormal = DesktopWidgetFallbackToNormal,
        }).Normalize();

        await _windowModeSettingsService.SetAsync(windowSettings, CancellationToken.None).ConfigureAwait(true);
        var targetMode = _launchModeDirty ? windowSettings.LaunchMode : _windowModeCoordinator.CurrentMode;
        _launchModeDirty = false;
        _monitorDirty = false;
        var result = await _windowModeCoordinator.SwitchAsync(targetMode, CancellationToken.None).ConfigureAwait(true);
        SyncShellWindowMode(result.ActiveMode);

        if (result.IsSuccess)
        {
            SaveStatus = UiText.Get("SavedAutomatically");
        }
        else
        {
            _toasts.Show(
                string.IsNullOrWhiteSpace(result.Message)
                    ? UiText.Get("SettingsWereSavedTheWindowReturnedTo")
                    : result.Message,
                ToastSeverity.Warning);
        }
    }

    private async Task ResetDefaultsAsync()
    {
        await FlushAsync().ConfigureAwait(true);
        _loaded = false;
        await _themeService.ApplyAsync(ThemeKind.System, CancellationToken.None).ConfigureAwait(true);
        await _themeService.ApplyApplicationSettingsAsync(ApplicationSettings.Default, CancellationToken.None, persist: true).ConfigureAwait(true);
        _startupRegistrationService.Apply(false, Environment.ProcessPath ?? string.Empty);
        _trayIconService.ApplySettings(ApplicationSettings.Default);
        await _windowModeSettingsService.SetAsync(WindowModeSettings.Default, CancellationToken.None).ConfigureAwait(true);
        var result = await _windowModeCoordinator.SwitchAsync(WindowModeSettings.Default.LaunchMode, CancellationToken.None).ConfigureAwait(true);
        SyncShellWindowMode(result.ActiveMode);
        await LoadAsync(CancellationToken.None).ConfigureAwait(true);
        _toasts.Show(UiText.Get("DefaultAppSettingsRestored"), ToastSeverity.Success);
    }

    private static void SyncShellWindowMode(WindowMode mode)
    {
        if (System.Windows.Application.Current?.MainWindow?.DataContext is ShellViewModel shellViewModel)
        {
            shellViewModel.SyncWindowMode(mode);
        }
    }

    private void PickColor(Func<SettingsViewModel, string> getter, Action<SettingsViewModel, string> setter)
    {
        if (_colorPickerService.TryPickColor(getter(this), out var selectedColor) && !string.IsNullOrWhiteSpace(selectedColor))
        {
            setter(this, selectedColor);
        }
    }
}
