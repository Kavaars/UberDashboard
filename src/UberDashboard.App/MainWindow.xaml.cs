using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using UberDashboard.Application.Platform;
using UberDashboard.App.Shell;
using UberDashboard.App.Settings;
using UberDashboard.App.SystemIntegration;
using UberDashboard.App.Theming;
using UberDashboard.App.Toasts;

namespace UberDashboard.App;

/// <summary>
/// Główne okno pulpitu: pasek górny, obszar roboczy z drzewem paneli, pasek stanu
/// oraz nakładka komunikatów globalnych. Okno nie zawiera logiki domenowej —
/// deleguje operacje do modelu widoku powłoki. Wczytanie motywu i pulpitu następuje
/// po załadowaniu okna, więc start interfejsu nie jest blokowany.
/// </summary>
public partial class MainWindow : Window
{
    private async void DeleteDashboard_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is System.Windows.Controls.Button { Tag: UberDashboard.Application.Abstractions.DashboardSummary dashboard })
            await _viewModel.DeleteDashboardAsync(dashboard);
    }

    private readonly UberDashboard.App.Plugins.PluginManager _plugins;
    private readonly AppBarController _appBar;
    private readonly ShellViewModel _viewModel;
    private readonly ThemeService _theme;
    private readonly IWindowModeCoordinator _windowModeCoordinator;
    private readonly IApplicationSettingsService _applicationSettingsService;
    private readonly ITrayIconService _trayIconService;
    private readonly IToastService _toasts;
    private bool _initialized;
    private bool _forceExit;
    private bool _shutdownPending;
    private bool _exitRequested;
    private int _wallpaperRevision;
    private string? _wallpaperPath;
    private (bool Transparent, bool Blur)? _backdropRequest;
    private bool _transparentBackgroundApplied;

    /// <summary>Tworzy okno główne z modelem widoku powłoki i usługą motywu.</summary>
    public MainWindow(
        ShellViewModel viewModel,
        ThemeService theme,
        IWindowModeCoordinator windowModeCoordinator,
        IApplicationSettingsService applicationSettingsService,
        ITrayIconService trayIconService,
        AppBarController appBar,
        UberDashboard.App.Plugins.PluginManager plugins,
        UberDashboard.App.Plugins.PluginSettingsViewModel pluginSettings,
        IToastService toasts)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(windowModeCoordinator);
        ArgumentNullException.ThrowIfNull(applicationSettingsService);
        ArgumentNullException.ThrowIfNull(trayIconService);

        _plugins = plugins;
        _toasts = toasts;
        _appBar = appBar;
        _viewModel = viewModel;
        _theme = theme;
        _windowModeCoordinator = windowModeCoordinator;
        _applicationSettingsService = applicationSettingsService;
        _trayIconService = trayIconService;

        InitializeComponent();
        _theme.ThemeChanged += OnThemeChanged;
        DataContext = _viewModel;
        UberDashboard.App.Plugins.PluginFileDrop.Attach(this, pluginSettings);
        _plugins.RefreshPanelsAsync = _viewModel.RefreshPluginPanelsAsync;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) => { _theme.ThemeChanged -= OnThemeChanged; _wallpaperRevision++; _appBar.Remove(); };
    }

    private async void OnThemeChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess()) { await Dispatcher.InvokeAsync(() => OnThemeChanged(sender, e)); return; }
        var settings = _theme.CurrentSettings.Normalize();
        Opacity = 1d;
        ApplicationContent.Opacity = settings.WindowOpacity;
        var backdropRequest = (settings.TransparentDesktopBackground, settings.DesktopBlur);
        if (_backdropRequest != backdropRequest && PresentationSource.FromVisual(this) is HwndSource source)
        {
            var applied = DesktopBackdrop.Apply(source.Handle, backdropRequest.TransparentDesktopBackground, backdropRequest.DesktopBlur);
            var blurApplied = applied && backdropRequest.DesktopBlur;
            if (!applied && backdropRequest.DesktopBlur && backdropRequest.TransparentDesktopBackground)
                applied = DesktopBackdrop.Apply(source.Handle, true, false);
            _backdropRequest = backdropRequest;
            _transparentBackgroundApplied = applied && backdropRequest.TransparentDesktopBackground;
            if (!applied)
            {
                DesktopBackdrop.Apply(source.Handle, false, false);
                _toasts.Show("Nie można włączyć przezroczystości lub rozmycia. Przywrócono zwykłe tło okna.", ToastSeverity.Warning);
            }
            else if (backdropRequest.DesktopBlur && !blurApplied)
                _toasts.Show("Rozmycie wymaga Windows 11 22H2; tło pozostaje przezroczyste bez rozmycia.", ToastSeverity.Warning);
            if (applied && (backdropRequest.TransparentDesktopBackground || backdropRequest.DesktopBlur))
            {
                Background = System.Windows.Media.Brushes.Transparent;
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            }
            else
            {
                SetResourceReference(BackgroundProperty, "Brush.Window.Background");
                source.CompositionTarget.BackgroundColor = Colors.White;
            }
        }
        ApplicationWallpaper.Visibility = _transparentBackgroundApplied ? Visibility.Collapsed : Visibility.Visible;
        WallpaperBlurEffect.Radius = settings.WallpaperBlur;
        if (!string.Equals(_wallpaperPath, settings.WallpaperPath, StringComparison.Ordinal))
        {
            _wallpaperPath = settings.WallpaperPath;
            var revision = ++_wallpaperRevision;
            ImageSource? image = null;
            if (settings.WallpaperPath is { } path)
            {
                try
                {
                    image = await Task.Run(() =>
                    {
                        using var stream = File.OpenRead(path);
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.DecodePixelWidth = 2560; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                        return bitmap;
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException)
                {
                    System.Diagnostics.Trace.TraceWarning("Nie można odczytać tapety aplikacji ({0}).", ex.GetType().Name);
                    if (revision == _wallpaperRevision) _toasts.Show("Nie można odczytać tapety aplikacji. Wybierz dostępny obraz.", ToastSeverity.Warning);
                }
            }
            if (revision != _wallpaperRevision) return;
            ApplicationWallpaper.Source = image;
            settings = _theme.CurrentSettings.Normalize();
        }
        ApplicationContent.Resources.Remove("Brush.Surface");
        ApplicationContent.Resources.Remove("Brush.Surface.Alt");
        var hasVisibleBackdrop = _transparentBackgroundApplied || ApplicationWallpaper.Source is not null;
        ApplicationContent.Resources["Token.BackgroundSurfaceOpacity"] = hasVisibleBackdrop ? settings.BackgroundSurfaceOpacity : 1d;
        if (!hasVisibleBackdrop) return;
        foreach (var key in new[] { "Brush.Surface", "Brush.Surface.Alt" })
        {
            if (System.Windows.Application.Current.TryFindResource(key) is SolidColorBrush original)
            {
                var brush = original.Clone();
                brush.Opacity *= settings.BackgroundSurfaceOpacity;
                ApplicationContent.Resources[key] = brush;
            }
        }
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
        await _plugins.InitializeAsync();
        _viewModel.RefreshPanelTypes();
        await _viewModel.InitializeAsync().ConfigureAwait(true);
        var result = await _windowModeCoordinator.InitializeAsync().ConfigureAwait(true);
        _viewModel.SyncWindowMode(result.ActiveMode);

        var settings = (await _applicationSettingsService.LoadAsync(CancellationToken.None).ConfigureAwait(true)).Normalize();
        _trayIconService.Initialize(RestoreFromTray, ExitFromTray);
        _trayIconService.ApplySettings(settings);
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_forceExit)
        {
            return;
        }

        var settings = _trayIconService.CurrentSettings.Normalize();
        if (!_exitRequested && settings.EnableTrayIcon && settings.CloseBehavior == CloseBehavior.MinimizeToTray)
        {
            e.Cancel = true;
            var result = await _windowModeCoordinator.SwitchAsync(WindowMode.Normal).ConfigureAwait(true);
            _viewModel.SyncWindowMode(result.ActiveMode);
            Hide();
            ShowInTaskbar = false;
            return;
        }

        if (System.Windows.Application.Current is App app)
        {
            e.Cancel = true;
            if (_shutdownPending) return;
            _shutdownPending = true;
            IsEnabled = false;
            if (await app.PrepareShutdownAsync().ConfigureAwait(true))
            {
                _forceExit = true;
                Close();
            }
            else
            {
                _shutdownPending = false;
                IsEnabled = true;
                UberDashboard.App.Controls.ThemedMessageBox.Show(this, "Nie potwierdzono zapisu danych. Okno pozostaje otwarte. Spróbuj zamknąć je ponownie po usunięciu błędu zapisu.", "Nie udało się zapisać danych", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
        _exitRequested = true;
        Show();
        Close();
    }
}
