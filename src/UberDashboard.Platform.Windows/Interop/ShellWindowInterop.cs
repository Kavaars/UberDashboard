using System.Runtime.InteropServices;
using UberDashboard.Application.Platform;

namespace UberDashboard.Platform.Windows.Interop;

/// <summary>Wąska granica Win32 dla geometrii i powiadomień okna powłoki (piksele).</summary>
public static class ShellWindowInterop
{
    public static uint AppBarCallbackMessage { get; } = RegisterWindowMessage("UberDashboard.AppBar.Callback.v1");
    public static uint TaskbarCreatedMessage { get; } = RegisterWindowMessage("TaskbarCreated");

    public static bool ApplyBounds(IntPtr handle, PixelRect rect) =>
        SetWindowPos(handle, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height, 0x0014);

    public static void SetFullScreenOrder(IntPtr handle, bool fullScreen) =>
        SetWindowPos(handle, fullScreen ? new IntPtr(1) : IntPtr.Zero, 0, 0, 0, 0, 0x0013);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
