using UberDashboard.Core.Localization;
using UberDashboard.Application.Platform;



namespace UberDashboard.App.Shell;



/// <summary>Koordynuje przejścia głównego okna między trybem normalnym, paskiem i widgetem.</summary>

public sealed class MainWindowModeCoordinator : IWindowModeCoordinator

{

    private readonly IDockedBarWindowHost _dockedWindow;

    private readonly AppBarController _appBar;

    private readonly IMonitorProvider _monitorProvider;

    private readonly IWindowModeSettingsService _settingsService;

    private readonly IDesktopWidgetWindowHost _desktopWidgetWindowHost;

    private IntPtr _windowHandle;

    private WindowMode _currentMode;

    private readonly SemaphoreSlim _transitionGate = new(1, 1);



    /// <summary>Tworzy koordynator trybów okna.</summary>

    public MainWindowModeCoordinator(

        AppBarController appBar,

        IMonitorProvider monitorProvider,

        IWindowModeSettingsService settingsService,

        IDesktopWidgetWindowHost desktopWidgetWindowHost,

        IDockedBarWindowHost dockedWindow)

    {

        ArgumentNullException.ThrowIfNull(appBar);

        ArgumentNullException.ThrowIfNull(dockedWindow);

        ArgumentNullException.ThrowIfNull(monitorProvider);

        ArgumentNullException.ThrowIfNull(settingsService);

        ArgumentNullException.ThrowIfNull(desktopWidgetWindowHost);



        _dockedWindow = dockedWindow;

        _appBar = appBar;

        _monitorProvider = monitorProvider;

        _settingsService = settingsService;

        _desktopWidgetWindowHost = desktopWidgetWindowHost;

    }



    /// <inheritdoc />

    public WindowMode CurrentMode

    {

        get => _currentMode;

        private set => _currentMode = value;

    }



    /// <inheritdoc />

    public bool IsAttached => _windowHandle != IntPtr.Zero;



    /// <inheritdoc />

    public void AttachWindow(IntPtr windowHandle)

    {

        if (windowHandle == IntPtr.Zero)

        {

            throw new ArgumentException(UiText.Get("WindowHandleCannotBeZero"), nameof(windowHandle));

        }



        _windowHandle = windowHandle;

    }



    /// <inheritdoc />

    public Task<WindowModeTransitionResult> InitializeAsync(CancellationToken cancellationToken = default) =>

        ApplyLaunchModeAsync(cancellationToken);



    /// <inheritdoc />



    public async Task<WindowModeTransitionResult> SwitchAsync(WindowMode mode, CancellationToken cancellationToken = default)

    {

        await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var settings = (await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false)).Normalize();
            // Resolve before undocking restores the old normal-window monitor.
            var monitor = mode == WindowMode.DockedBar ? ResolveMonitor(settings.MonitorId) : null;
            var result = await SwitchCoreAsync(mode, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                try
                {
                    await _settingsService.SetAsync(settings with
                    {
                        LaunchMode = result.ActiveMode,
                        MonitorId = monitor?.Id ?? settings.MonitorId,
                    }, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Serilog.Log.Error(ex, "Nie zapisano trybu okna. Module={Module}", nameof(MainWindowModeCoordinator));
                    return WindowModeTransitionResult.Fail(result.ActiveMode, UiText.Get("CouldNotSaveData"));
                }
            }
            return result;
        }

        finally { _transitionGate.Release(); }

    }



    /// <inheritdoc />
    public async Task<WindowModeTransitionResult> SwitchTemporarilyAsync(WindowMode mode, CancellationToken cancellationToken = default)
    {
        await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await SwitchCoreAsync(mode, cancellationToken).ConfigureAwait(false); }
        finally { _transitionGate.Release(); }
    }

    private async Task<WindowModeTransitionResult> SwitchCoreAsync(WindowMode mode, CancellationToken cancellationToken)

    {

        var settings = (await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false)).Normalize();

        if (mode == WindowMode.DockedBar && CurrentMode == WindowMode.DockedBar)

        {

            var monitor = ResolveMonitor(settings.MonitorId);

            if (monitor is null)
            {
                if (settings.MonitorId is not null) _dockedWindow.Configure(_windowHandle, settings.MonitorId, settings.DockEdge, settings.DockThickness);
                return _dockedWindow.WaitForMonitor() ? WindowModeTransitionResult.Success(WindowMode.DockedBar) : WindowModeTransitionResult.Fail(CurrentMode, UiText.Get("NoDisplayIsAvailable"));
            }
            _dockedWindow.Configure(_windowHandle, monitor.Id, settings.DockEdge, settings.DockThickness);
            if (!_appBar.IsRegistered) return EnterDockedBar(settings);

            var updated = _appBar.UpdateConfiguration(settings.DockEdge, settings.DockThickness, monitor.Bounds);

            return updated.IsSuccess && _dockedWindow.Apply(_windowHandle, updated.Rect)

                ? WindowModeTransitionResult.Success(WindowMode.DockedBar)

                : WindowModeTransitionResult.Fail(CurrentMode, updated.Message ?? UiText.Get("CouldNotRepositionTheDockedBar"));

        }

        if (mode == WindowMode.DockedBar && settings.MonitorId is null)
            settings = settings with { MonitorId = ResolveMonitor(null)?.Id };

        await ExitSpecialModesAsync(cancellationToken).ConfigureAwait(false);



        return mode switch

        {

            WindowMode.Normal => SetCurrent(WindowMode.Normal),

            WindowMode.DockedBar => EnterDockedBar(settings),

            WindowMode.DesktopWidget => await EnterDesktopWidgetAsync(settings, cancellationToken).ConfigureAwait(false),

            _ => WindowModeTransitionResult.Fail(WindowMode.Normal, UiText.Get("UnsupportedWindowMode")),

        };

    }



    internal AppBarDockResult EnterDockedBar(

        IntPtr windowHandle,

        string? monitorId,

        AppBarEdge edge,

        int thickness,

        PixelRect workArea)

    {

        var result = _appBar.Register(windowHandle, edge, thickness, workArea);

        CurrentMode = result.IsSuccess ? WindowMode.DockedBar : WindowMode.Normal;

        return result;

    }



    private async Task<WindowModeTransitionResult> ApplyLaunchModeAsync(CancellationToken cancellationToken)

    {

        var settings = await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false);
        var monitor = settings.LaunchMode == WindowMode.DockedBar && settings.MonitorId is null ? ResolveMonitor(null) : null;
        var result = await SwitchTemporarilyAsync(settings.LaunchMode, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess && monitor is not null)
            await _settingsService.SetAsync(settings with { MonitorId = monitor.Id }, cancellationToken).ConfigureAwait(false);
        return result;

    }



    private WindowModeTransitionResult EnterDockedBar(WindowModeSettings settings)

    {

        if (!IsAttached)

        {

            CurrentMode = WindowMode.Normal;

            return WindowModeTransitionResult.Fail(WindowMode.Normal, UiText.Get("MainWindowHandleNotAttached"));

        }



        var monitor = ResolveMonitor(settings.MonitorId);
        if (settings.MonitorId is { } preferred) _dockedWindow.Configure(_windowHandle, preferred, settings.DockEdge, settings.DockThickness);

        if (monitor is null && settings.MonitorId is not null && _dockedWindow.WaitForMonitor())
        {
            CurrentMode = WindowMode.DockedBar;
            return WindowModeTransitionResult.Success(WindowMode.DockedBar);
        }

        if (monitor is null)

        {

            CurrentMode = WindowMode.Normal;

            return WindowModeTransitionResult.Fail(WindowMode.Normal, UiText.Get("NoMonitorForDock"));

        }



        _dockedWindow.Configure(_windowHandle, monitor.Id, settings.DockEdge, settings.DockThickness);
        var result = EnterDockedBar(_windowHandle, settings.MonitorId, settings.DockEdge, settings.DockThickness, monitor.Bounds);

        if (result.IsSuccess && !_dockedWindow.Apply(_windowHandle, result.Rect))

        {

            _appBar.Remove();

            _dockedWindow.Restore();

            CurrentMode = WindowMode.Normal;

            return WindowModeTransitionResult.Fail(WindowMode.Normal, UiText.Get("CouldNotPlaceTheWindowInThe"));

        }

        if (!result.IsSuccess) _dockedWindow.Restore();

        return result.IsSuccess

            ? WindowModeTransitionResult.Success(WindowMode.DockedBar)

            : WindowModeTransitionResult.Fail(WindowMode.Normal, result.Message ?? UiText.Get("DockedBarActivationFailed"));

    }



    private async Task<WindowModeTransitionResult> EnterDesktopWidgetAsync(

        WindowModeSettings settings,

        CancellationToken cancellationToken)

    {

        if (!IsAttached)

        {

            CurrentMode = WindowMode.Normal;

            return WindowModeTransitionResult.Fail(WindowMode.Normal, UiText.Get("MainWindowHandleNotAttached"));

        }



        var attached = await _desktopWidgetWindowHost

            .TryAttachAsync(_windowHandle, settings.WidgetOpacity, cancellationToken)

            .ConfigureAwait(false);



        if (attached)

        {

            CurrentMode = WindowMode.DesktopWidget;

            return WindowModeTransitionResult.Success(WindowMode.DesktopWidget);

        }



        CurrentMode = WindowMode.Normal;

        return settings.DesktopWidgetFallbackToNormal

            ? WindowModeTransitionResult.Fail(WindowMode.Normal, UiText.Get("TheDesktopWidgetIsExperimentalTheApp"))

            : WindowModeTransitionResult.Fail(WindowMode.Normal, UiText.Get("TheDesktopWidgetIsUnavailableTheApp"));

    }



    private async Task ExitSpecialModesAsync(CancellationToken cancellationToken)

    {

        if (_appBar.IsRegistered)

        {

            _appBar.Remove();

        }



        if (IsAttached)

        {

            await _desktopWidgetWindowHost.DetachAsync(_windowHandle, cancellationToken).ConfigureAwait(false);

        }



        _dockedWindow.Restore();

        CurrentMode = WindowMode.Normal;

    }



    private DisplayMonitor? ResolveMonitor(string? monitorId)

    {

        var monitors = _monitorProvider.Enumerate();

        if (monitors.Count == 0)

        {

            return null;

        }



        if (!string.IsNullOrWhiteSpace(monitorId))

        {

            foreach (var monitor in monitors)

            {

                if (string.Equals(monitor.Id, monitorId, StringComparison.OrdinalIgnoreCase) && monitor.IsUsable)

                {

                    return monitor;

                }

            }
            return null;

        }



        var windowMonitor = IsAttached ? _monitorProvider.GetByWindowHandle(_windowHandle) : null;

        if (windowMonitor?.IsUsable == true)

        {

            return windowMonitor;

        }



        foreach (var monitor in monitors)

        {

            if (monitor.IsPrimary && monitor.IsUsable)

            {

                return monitor;

            }

        }



        foreach (var monitor in monitors)

        {

            if (monitor.IsUsable)

            {

                return monitor;

            }

        }



        return null;

    }



    private WindowModeTransitionResult SetCurrent(WindowMode mode)

    {

        CurrentMode = mode;

        return WindowModeTransitionResult.Success(mode);

    }

}
