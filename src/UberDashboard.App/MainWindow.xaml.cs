using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using UberDashboard.Application.Platform;
using UberDashboard.App.Shell;
using UberDashboard.App.Settings;
using UberDashboard.App.SystemIntegration;
using UberDashboard.App.Theming;

namespace UberDashboard.App;

/// <summary>
/// Główne okno pulpitu: pasek górny, obszar roboczy z drzewem paneli, pasek stanu
/// oraz nakładka komunikatów globalnych. Okno nie zawiera logiki domenowej —
/// deleguje operacje do modelu widoku powłoki. Wczytanie motywu i pulpitu następuje
/// po załadowaniu okna, więc start interfejsu nie jest blokowany.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly ThemeService _theme;
    private readonly IWindowModeCoordinator _windowModeCoordinator;
    private readonly IApplicationSettingsService _applicationSettingsService;
    private readonly ITrayIconService _trayIconService;
    private bool _initialized;
    private bool _forceExit;

    /// <summary>Tworzy okno główne z modelem widoku powłoki i usługą motywu.</summary>
    public MainWindow(
        ShellViewModel viewModel,
        ThemeService theme,
        IWindowModeCoordinator windowModeCoordinator,
        IApplicationSettingsService applicationSettingsService,
        ITrayIconService trayIconService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(windowModeCoordinator);
        ArgumentNullException.ThrowIfNull(applicationSettingsService);
        ArgumentNullException.ThrowIfNull(trayIconService);

        _viewModel = viewModel;
        _theme = theme;
        _windowModeCoordinator = windowModeCoordinator;
        _applicationSettingsService = applicationSettingsService;
        _trayIconService = trayIconService;

        InitializeComponent();
        DataContext = _viewModel;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            _windowModeCoordinator.AttachWindow(handle);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        Loaded -= OnLoaded;

        await _theme.LoadAndApplyAsync(CancellationToken.None).ConfigureAwait(true);
        await _viewModel.InitializeAsync().ConfigureAwait(true);
        var result = await _windowModeCoordinator.InitializeAsync().ConfigureAwait(true);
        _viewModel.SyncWindowMode(result.ActiveMode);

        var settings = (await _applicationSettingsService.LoadAsync(CancellationToken.None).ConfigureAwait(true)).Normalize();
        _trayIconService.Initialize(RestoreFromTray, ExitFromTray);
        _trayIconService.ApplySettings(settings);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_forceExit)
        {
            return;
        }

        var settings = _trayIconService.CurrentSettings.Normalize();
        if (settings.EnableTrayIcon && settings.CloseBehavior == CloseBehavior.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
        }
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        _forceExit = true;
        System.Windows.Application.Current.Shutdown();
    }
}
