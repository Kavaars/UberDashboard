using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Serilog;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Platform;
using UberDashboard.Platform.Windows.Interop;
using UberDashboard.App.Toasts;
using UberDashboard.Core.Localization;

namespace UberDashboard.App.Shell;

/// <summary>Autozapis geometrii poza UI; nie zapisuje tymczasowej geometrii zadokowanego paska.</summary>
public sealed class WindowPlacementTracker(IAppSettingsStore store, IClock clock, IMonitorProvider monitors,
    IWindowModeCoordinator modes, IToastService toasts)
{
    private const string Key = "app.windowPlacement";
    private Window? _window;
    private DispatcherTimer? _timer;
    private WindowPlacement? _placement;
    private Task _write = Task.CompletedTask;

    public async Task RestoreAsync(Window window)
    {
        try
        {
            var raw = await store.GetAsync(Key).ConfigureAwait(true);
            if (raw is null) return;
            var saved = JsonSerializer.Deserialize<WindowPlacement>(raw);
            if (saved is null || !saved.Bounds.IsValid) return;
            var available = monitors.Enumerate().Where(m => m.IsUsable).ToArray();
            var monitor = available.FirstOrDefault(m => string.Equals(m.Id, saved.MonitorId, StringComparison.OrdinalIgnoreCase))
                ?? available.FirstOrDefault(m => m.IsPrimary) ?? available.FirstOrDefault();
            if (monitor is null) return;
            window.WindowState = WindowState.Normal;
            if (!ShellWindowInterop.ApplyBounds(new WindowInteropHelper(window).Handle, saved.FitTo(monitor)))
                throw new InvalidOperationException("Nie można przywrócić położenia okna.");
            _placement = saved with { Bounds = saved.FitTo(monitor), MonitorId = monitor.Id };
            if (saved.Maximized) window.WindowState = WindowState.Maximized;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Nie przywrócono położenia okna. Module={Module}", nameof(WindowPlacementTracker));
        }
    }

    public void Start(Window window)
    {
        _window = window;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background,
            OnTimer, window.Dispatcher);
        _timer.Stop();
        window.LocationChanged += OnChanged;
        window.SizeChanged += OnChanged;
        window.StateChanged += OnChanged;
        window.Closed += OnClosed;
        Capture();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer?.Stop();
        if (_window is not { } window) return;
        window.LocationChanged -= OnChanged;
        window.SizeChanged -= OnChanged;
        window.StateChanged -= OnChanged;
        window.Closed -= OnClosed;
        _window = null;
    }

    private void OnChanged(object? sender, EventArgs e) => Capture();

    public void Capture()
    {
        if (_window is not { IsVisible: true } window || modes.CurrentMode != WindowMode.Normal
            || window.WindowState == WindowState.Minimized) return;
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = monitors.GetByWindowHandle(handle);
        var bounds = window.WindowState == WindowState.Normal ? ShellWindowInterop.ReadBounds(handle) : _placement?.Bounds;
        if (monitor is null || bounds is not { IsValid: true } rect) return;
        var placement = new WindowPlacement(monitor.Id, rect, window.WindowState == WindowState.Maximized);
        if (_placement == placement) return;
        _placement = placement;
        _timer?.Stop();
        _timer?.Start();
    }

    private async void OnTimer(object? sender, EventArgs e)
    {
        try { await FlushAsync().ConfigureAwait(true); }
        catch { toasts.Show(UiText.Get("CouldNotSaveData"), ToastSeverity.Error); }
    }

    public Task FlushAsync()
    {
        _timer?.Stop();
        if (_placement is { } placement) _write = SaveAfterAsync(_write, placement);
        return _write;
    }

    private async Task SaveAfterAsync(Task previous, WindowPlacement placement)
    {
        try { await previous.ConfigureAwait(false); }
        catch { /* A new snapshot retries a failed write. */ }
        try
        {
            await store.SetAsync(Key, JsonSerializer.Serialize(placement), clock.UtcNow).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Nie zapisano położenia okna. Module={Module}", nameof(WindowPlacementTracker));
            throw;
        }
    }
}
