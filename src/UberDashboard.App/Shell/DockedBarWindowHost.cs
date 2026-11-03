using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using UberDashboard.Application.Platform;
using UberDashboard.Platform.Windows.Interop;

namespace UberDashboard.App.Shell;

public interface IDockedBarWindowHost
{
    bool Apply(IntPtr handle, PixelRect bounds);
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

    public bool Apply(IntPtr handle, PixelRect bounds)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return false;
        if (!dispatcher.CheckAccess()) return dispatcher.Invoke(() => Apply(handle, bounds));
        _source = HwndSource.FromHwnd(handle);
        if (_source?.RootVisual is not Window window) return false;
        _window = window;
        if (_snapshot is null)
        {
            _snapshot = new Snapshot(window.WindowStyle, window.ResizeMode, window.MinWidth, window.MinHeight,
                window.Left, window.Top, window.Width, window.Height, window.WindowState);
            _source.AddHook(WindowProc);
            window.MinWidth = 0;
            window.MinHeight = 0;
            window.WindowState = WindowState.Normal;
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
        }
        _applying = true;
        try { return ShellWindowInterop.ApplyBounds(handle, bounds); }
        finally { _applying = false; }
    }

    public void Restore()
    {
        if (_window is not { } window || _snapshot is not { } snapshot) return;
        if (!window.Dispatcher.CheckAccess()) { window.Dispatcher.Invoke(Restore); return; }
        _source?.RemoveHook(WindowProc);
        window.WindowStyle = snapshot.Style;
        window.ResizeMode = snapshot.Resize;
        window.MinWidth = snapshot.MinWidth;
        window.MinHeight = snapshot.MinHeight;
        window.Left = snapshot.Left;
        window.Top = snapshot.Top;
        window.Width = snapshot.Width;
        window.Height = snapshot.Height;
        window.WindowState = snapshot.State;
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
            if (_window is null || !appBar.IsRegistered) return;
            _applying = true;
            try
            {
                var monitor = monitors.GetByWindowHandle(appBar.WindowHandle);
                var result = restart
                    ? appBar.Restart(monitor?.Bounds)
                    : updateMonitor && monitor is { IsUsable: true }
                        ? appBar.UpdateWorkArea(monitor.Bounds)
                        : appBar.Reapply();
                if (!result.IsSuccess || !ShellWindowInterop.ApplyBounds(appBar.WindowHandle, result.Rect))
                {
                    appBar.Remove();
                    var shell = _window.DataContext as ShellViewModel;
                    Restore();
                    shell?.SyncWindowMode(WindowMode.Normal);
                }
            }
            finally { _applying = false; }
        }));
    }

    private sealed record Snapshot(WindowStyle Style, ResizeMode Resize, double MinWidth, double MinHeight,
        double Left, double Top, double Width, double Height, WindowState State);
}
