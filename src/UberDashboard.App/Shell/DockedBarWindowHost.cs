using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using UberDashboard.Application.Platform;
using UberDashboard.Platform.Windows.Interop;

namespace UberDashboard.App.Shell;

public interface IDockedBarWindowHost
{
    bool Apply(IntPtr handle, PixelRect bounds);
    void Configure(IntPtr handle, string monitorId, AppBarEdge edge, int thickness);
    bool WaitForMonitor();
    void Restore();
}

/// <summary>Stosuje systemowy RECT i obsługuje cykl komunikatów zarejestrowanego paska.</summary>
public sealed class DockedBarWindowHost(AppBarController appBar, IMonitorProvider monitors) : IDockedBarWindowHost
{
    private Window? _window;
    private HwndSource? _source;
    private Snapshot? _snapshot;
    private bool _pending;
    private bool _applying;
    private string? _monitorId;
    private AppBarEdge _edge;
    private int _thickness;
    private bool _waiting;

    public bool Apply(IntPtr handle, PixelRect bounds)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return false;
        if (!dispatcher.CheckAccess()) return dispatcher.Invoke(() => Apply(handle, bounds));
        if (!Prepare(handle)) return false;
        _waiting = false;
        if (_window is { IsVisible: false } window) window.Show();
        _applying = true;
        try { return ShellWindowInterop.ApplyBounds(handle, bounds); }
        finally { _applying = false; }
    }

    public void Configure(IntPtr handle, string monitorId, AppBarEdge edge, int thickness)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (!dispatcher.CheckAccess()) { dispatcher.Invoke(() => Configure(handle, monitorId, edge, thickness)); return; }
        _monitorId = monitorId;
        _edge = edge;
        _thickness = thickness;
        Prepare(handle);
    }

    private bool Prepare(IntPtr handle)
    {
        if (_snapshot is not null) return true;
        _source = HwndSource.FromHwnd(handle);
        if (_source?.RootVisual is not Window window) return false;
        _window = window;
        _snapshot = new Snapshot(window.WindowStyle, window.ResizeMode, window.MinWidth, window.MinHeight,
            window.Left, window.Top, window.Width, window.Height, window.WindowState, window.ShowInTaskbar);
        _source.AddHook(WindowProc);
        window.MinWidth = 0;
        window.MinHeight = 0;
        window.WindowState = WindowState.Normal;
        window.SetValue(System.Windows.Shell.WindowChrome.WindowChromeProperty, null);
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        return true;
    }

    public bool WaitForMonitor()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return false;
        if (!dispatcher.CheckAccess()) return dispatcher.Invoke(WaitForMonitor);
        if (_window is null) return false;
        appBar.Remove();
        _waiting = true;
        _window.Hide();
        return true;
    }

    public void Restore()
    {
        if (_window is not { } window || _snapshot is not { } snapshot) return;
        if (!window.Dispatcher.CheckAccess()) { window.Dispatcher.Invoke(Restore); return; }
        _source?.RemoveHook(WindowProc);
        window.WindowStyle = snapshot.Style;
        window.ClearValue(System.Windows.Shell.WindowChrome.WindowChromeProperty);
        window.ResizeMode = snapshot.Resize;
        window.MinWidth = snapshot.MinWidth;
        window.MinHeight = snapshot.MinHeight;
        window.Left = snapshot.Left;
        window.Top = snapshot.Top;
        window.Width = snapshot.Width;
        window.Height = snapshot.Height;
        window.WindowState = snapshot.State;
        window.ShowInTaskbar = snapshot.ShowInTaskbar;
        _waiting = false;
        _monitorId = null;
        _snapshot = null;
        _source = null;
        _window = null;
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_applying) return IntPtr.Zero;
        if ((uint)message == ShellWindowInterop.AppBarCallbackMessage)
        {
            if (wParam.ToInt32() == 1) QueuePosition(false, false);
            if (wParam.ToInt32() == 2) ShellWindowInterop.SetFullScreenOrder(hwnd, lParam != IntPtr.Zero);
        }
        else if ((uint)message == ShellWindowInterop.TaskbarCreatedMessage) QueuePosition(true, true);
        else if (message is 0x007E or 0x02E0) QueuePosition(false, true);
        else if (message == 0x0006) appBar.NotifyActivated((wParam.ToInt64() & 0xffff) != 0);
        else if (message == 0x0047) appBar.NotifyWindowPositionChanged();
        else if (message == 0x0002) appBar.Remove();
        return IntPtr.Zero;
    }

    private void QueuePosition(bool restart, bool updateMonitor)
    {
        if (_pending || _window is null) return;
        _pending = true;
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _pending = false;
            if (_window is null || _monitorId is null) return;
            _applying = true;
            try
            {
                var monitor = monitors.Enumerate().FirstOrDefault(m => m.IsUsable &&
                    string.Equals(m.Id, _monitorId, StringComparison.OrdinalIgnoreCase));
                if (monitor is null)
                {
                    WaitForMonitor();
                    return;
                }
                var result = !appBar.IsRegistered
                    ? appBar.Register(new WindowInteropHelper(_window).Handle, _edge, _thickness, monitor.Bounds)
                    : restart ? appBar.Restart(monitor.Bounds)
                    : updateMonitor ? appBar.UpdateWorkArea(monitor.Bounds) : appBar.Reapply();
                if (!result.IsSuccess || !ShellWindowInterop.ApplyBounds(appBar.WindowHandle, result.Rect))
                {
                    WaitForMonitor();
                    return;
                }
                if (_waiting)
                {
                    _waiting = false;
                    _window.Show();
                }
            }
            finally { _applying = false; }
        }));
    }

    private sealed record Snapshot(WindowStyle Style, ResizeMode Resize, double MinWidth, double MinHeight,
        double Left, double Top, double Width, double Height, WindowState State, bool ShowInTaskbar);
}
