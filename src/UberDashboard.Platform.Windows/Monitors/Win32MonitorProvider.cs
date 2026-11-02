using System.Runtime.InteropServices;
using UberDashboard.Application.Platform;
using UberDashboard.Platform.Windows.Interop;

namespace UberDashboard.Platform.Windows.Monitors;

/// <summary>
/// Implementacja <see cref="IMonitorProvider"/> oparta o <c>EnumDisplayMonitors</c>,
/// <c>GetMonitorInfo</c> i <c>GetDpiForMonitor</c>. Zwraca geometrię w pikselach
/// właściwych dla kontekstu DPI monitora, wraz z poprawną obsługą ujemnych
/// współrzędnych ekranu wirtualnego.
/// </summary>
internal sealed class Win32MonitorProvider : IMonitorProvider
{
    private const uint FallbackDpi = 96;

    /// <inheritdoc />
    public IReadOnlyList<DisplayMonitor> Enumerate()
    {
        var monitors = new List<DisplayMonitor>();

        NativeMethods.MonitorEnumProc callback = (IntPtr handle, IntPtr deviceContext, ref NativeRect rect, IntPtr data) =>
        {
            var monitor = Describe(handle);
            if (monitor is not null)
            {
                monitors.Add(monitor);
            }

            return true;
        };

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

        return monitors
            .OrderBy(static monitor => monitor.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public DisplayMonitor? GetByWindowHandle(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return null;
        }

        var handle = NativeMethods.MonitorFromWindow(windowHandle, NativeMethods.MonitorDefaultToNearest);
        return handle == IntPtr.Zero ? null : Describe(handle);
    }

    /// <inheritdoc />
    public DisplayMonitor? GetByPoint(int x, int y)
    {
        var point = new NativePoint { X = x, Y = y };
        var handle = NativeMethods.MonitorFromPoint(point, NativeMethods.MonitorDefaultToNearest);
        return handle == IntPtr.Zero ? null : Describe(handle);
    }

    private static DisplayMonitor? Describe(IntPtr monitorHandle)
    {
        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        if (!NativeMethods.GetMonitorInfo(monitorHandle, ref info))
        {
            return null;
        }

        var id = string.IsNullOrWhiteSpace(info.DeviceName)
            ? $"display-{monitorHandle.ToInt64():X}"
            : info.DeviceName;

        var isPrimary = (info.Flags & NativeMethods.MonitorInfoFlagPrimary) != 0;

        return new DisplayMonitor(
            id,
            ToManaged(info.Monitor),
            ToManaged(info.Work),
            ReadDpi(monitorHandle),
            isPrimary);
    }

    private static uint ReadDpi(IntPtr monitorHandle)
    {
        var result = NativeMethods.GetDpiForMonitor(monitorHandle, NativeMethods.MdtEffectiveDpi, out var dpiX, out _);
        return result == 0 && dpiX > 0 ? dpiX : FallbackDpi;
    }

    private static PixelRect ToManaged(NativeRect rect) =>
        new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
