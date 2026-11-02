using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace UberDashboard.App.Shell;

/// <summary>Best-effort host trybu widgetu pulpitu.</summary>
public interface IDesktopWidgetWindowHost
{
    /// <summary>Próbuje podłączyć okno do warstwy pulpitu.</summary>
    Task<bool> TryAttachAsync(IntPtr windowHandle, double opacity, CancellationToken cancellationToken);

    /// <summary>Odłącza okno od warstwy widgetu, jeśli było wcześniej podłączone.</summary>
    Task DetachAsync(IntPtr windowHandle, CancellationToken cancellationToken);
}

/// <summary>
/// Szkielet hosta widgetu pulpitu. v1 zachowuje się defensywnie: bez stabilnej,
/// jawnie zaimplementowanej integracji zwraca niepowodzenie i pozwala koordynatorowi
/// wykonać fallback do trybu normalnego.
/// </summary>
public sealed class DesktopWidgetWindowHost : IDesktopWidgetWindowHost
{
    private readonly ConcurrentDictionary<IntPtr, WindowSnapshot> _snapshots = new();

    /// <inheritdoc />
    public Task<bool> TryAttachAsync(IntPtr windowHandle, double opacity, CancellationToken cancellationToken)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return Task.FromResult(false);
        }

        var window = ResolveWindow(windowHandle);
        if (window is null)
        {
            return Task.FromResult(false);
        }

        var screen = Screen.FromHandle(windowHandle);
        _snapshots[windowHandle] = new WindowSnapshot(
            window.WindowStyle,
            window.ResizeMode,
            window.ShowInTaskbar,
            window.Topmost,
            window.Left,
            window.Top,
            window.Width,
            window.Height,
            window.Opacity);

        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        window.Topmost = false;
        window.Left = screen.Bounds.Left;
        window.Top = screen.Bounds.Top;
        window.Width = screen.Bounds.Width;
        window.Height = screen.Bounds.Height;
        window.Opacity = opacity;
        window.WindowState = WindowState.Normal;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task DetachAsync(IntPtr windowHandle, CancellationToken cancellationToken)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return Task.CompletedTask;
        }

        var window = ResolveWindow(windowHandle);
        if (window is null)
        {
            return Task.CompletedTask;
        }

        if (_snapshots.TryRemove(windowHandle, out var snapshot))
        {
            window.WindowStyle = snapshot.WindowStyle;
            window.ResizeMode = snapshot.ResizeMode;
            window.ShowInTaskbar = snapshot.ShowInTaskbar;
            window.Topmost = snapshot.Topmost;
            window.Left = snapshot.Left;
            window.Top = snapshot.Top;
            window.Width = snapshot.Width;
            window.Height = snapshot.Height;
            window.Opacity = snapshot.Opacity;
        }

        return Task.CompletedTask;
    }

    private sealed record WindowSnapshot(
        WindowStyle WindowStyle,
        ResizeMode ResizeMode,
        bool ShowInTaskbar,
        bool Topmost,
        double Left,
        double Top,
        double Width,
        double Height,
        double Opacity);

    private static Window? ResolveWindow(IntPtr windowHandle)
    {
        foreach (Window window in System.Windows.Application.Current.Windows)
        {
            if (new WindowInteropHelper(window).Handle == windowHandle)
            {
                return window;
            }
        }

        return null;
    }
}
