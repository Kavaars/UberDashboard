using UberDashboard.Application.Platform;

namespace UberDashboard.App.Shell;

/// <summary>Koordynuje przejścia głównego okna między trybem normalnym, paskiem i widgetem.</summary>
public sealed class MainWindowModeCoordinator : IWindowModeCoordinator
{
    private readonly AppBarController _appBar;
    private readonly IMonitorProvider _monitorProvider;
    private readonly IWindowModeSettingsService _settingsService;
    private readonly IDesktopWidgetWindowHost _desktopWidgetWindowHost;
    private IntPtr _windowHandle;

    /// <summary>Tworzy koordynator trybów okna.</summary>
    public MainWindowModeCoordinator(
        AppBarController appBar,
        IMonitorProvider monitorProvider,
        IWindowModeSettingsService settingsService,
        IDesktopWidgetWindowHost desktopWidgetWindowHost)
    {
        ArgumentNullException.ThrowIfNull(appBar);
        ArgumentNullException.ThrowIfNull(monitorProvider);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(desktopWidgetWindowHost);

        _appBar = appBar;
        _monitorProvider = monitorProvider;
        _settingsService = settingsService;
        _desktopWidgetWindowHost = desktopWidgetWindowHost;
    }

    /// <inheritdoc />
    public WindowMode CurrentMode { get; private set; } = WindowMode.Normal;

    /// <inheritdoc />
    public bool IsAttached => _windowHandle != IntPtr.Zero;

    /// <inheritdoc />
    public void AttachWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle cannot be zero.", nameof(windowHandle));
        }

        _windowHandle = windowHandle;
    }

    /// <inheritdoc />
    public Task<WindowModeTransitionResult> InitializeAsync(CancellationToken cancellationToken = default) =>
        ApplyLaunchModeAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<WindowModeTransitionResult> SwitchAsync(WindowMode mode, CancellationToken cancellationToken = default)
    {
        var settings = (await _settingsService.GetAsync(cancellationToken).ConfigureAwait(false)).Normalize();
        await ExitSpecialModesAsync(cancellationToken).ConfigureAwait(false);

        return mode switch
        {
            WindowMode.Normal => SetCurrent(WindowMode.Normal),
            WindowMode.DockedBar => EnterDockedBar(settings),
            WindowMode.DesktopWidget => await EnterDesktopWidgetAsync(settings, cancellationToken).ConfigureAwait(false),
            _ => WindowModeTransitionResult.Fail(WindowMode.Normal, "Unsupported window mode."),
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
        return await SwitchAsync(settings.LaunchMode, cancellationToken).ConfigureAwait(false);
    }

    private WindowModeTransitionResult EnterDockedBar(WindowModeSettings settings)
    {
        if (!IsAttached)
        {
            CurrentMode = WindowMode.Normal;
            return WindowModeTransitionResult.Fail(WindowMode.Normal, "Main window handle is not attached.");
        }

        var monitor = ResolveMonitor(settings.MonitorId);
        if (monitor is null)
        {
            CurrentMode = WindowMode.Normal;
            return WindowModeTransitionResult.Fail(WindowMode.Normal, "No usable monitor is available for docked bar mode.");
        }

        var result = EnterDockedBar(_windowHandle, settings.MonitorId, settings.DockEdge, settings.DockThickness, monitor.WorkArea);
        return result.IsSuccess
            ? WindowModeTransitionResult.Success(WindowMode.DockedBar)
            : WindowModeTransitionResult.Fail(WindowMode.Normal, result.Message ?? "Docked bar activation failed.");
    }

    private async Task<WindowModeTransitionResult> EnterDesktopWidgetAsync(
        WindowModeSettings settings,
        CancellationToken cancellationToken)
    {
        if (!IsAttached)
        {
            CurrentMode = WindowMode.Normal;
            return WindowModeTransitionResult.Fail(WindowMode.Normal, "Main window handle is not attached.");
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
            ? WindowModeTransitionResult.Fail(WindowMode.Normal, "Desktop widget activation failed.")
            : WindowModeTransitionResult.Fail(WindowMode.Normal, "Desktop widget activation failed without fallback target.");
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
