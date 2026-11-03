using System.Runtime.InteropServices;

namespace UberDashboard.App.SystemIntegration;

/// <summary>Systemowy materiał Acrylic dla okna Windows 11 22H2 i nowszych.</summary>
internal static class DesktopBackdrop
{
    private const int SystemBackdropType = 38;

    public static bool Apply(IntPtr window, bool transparent, bool blur)
    {
        if (window == IntPtr.Zero) return false;
        if (blur && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return false;
        if ((transparent || blur) && (DwmIsCompositionEnabled(out var compositionEnabled) < 0 || !compositionEnabled)) return false;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            var backdrop = blur ? 3 : 1;
            if (DwmSetWindowAttribute(window, SystemBackdropType, ref backdrop, sizeof(int)) < 0) return false;
        }
        var extend = transparent || blur;
        var margins = new Margins { Left = extend ? -1 : 0, Right = extend ? -1 : 0, Top = extend ? -1 : 0, Bottom = extend ? -1 : 0 };
        return DwmExtendFrameIntoClientArea(window, ref margins) >= 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left; public int Right; public int Top; public int Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
}
