using System.Runtime.InteropServices;

namespace UberDashboard.Platform.Windows.Interop;

/// <summary>
/// Wywołania natywne wymagane przez adaptery powłoki: AppBar, monitory i DPI.
/// Klasa jest wewnętrzna, aby P/Invoke nie wyciekał poza warstwę platformy, oraz
/// nazwana <c>NativeMethods</c>, zgodnie z konwencją grupowania wywołań natywnych.
/// </summary>
internal static class NativeMethods
{
    /// <summary><c>ABM_NEW</c> — rejestracja okna jako paska.</summary>
    internal const uint AbmNew = 0x00000000;

    /// <summary><c>ABM_REMOVE</c> — wyrejestrowanie paska.</summary>
    internal const uint AbmRemove = 0x00000001;

    /// <summary><c>ABM_QUERYPOS</c> — zapytanie o dozwolony prostokąt.</summary>
    internal const uint AbmQueryPos = 0x00000002;

    /// <summary><c>ABM_SETPOS</c> — ustawienie pozycji paska.</summary>
    internal const uint AbmSetPos = 0x00000003;

    /// <summary><c>ABM_GETSTATE</c> — odczyt stanu paska.</summary>
    internal const uint AbmGetState = 0x00000004;

    /// <summary><c>ABM_GETTASKBARPOS</c> — prostokąt paska zadań.</summary>
    internal const uint AbmGetTaskbarPos = 0x00000005;

    /// <summary><c>ABM_ACTIVATE</c> — powiadomienie o aktywacji okna paska.</summary>
    internal const uint AbmActivate = 0x00000006;

    /// <summary><c>ABM_WINDOWPOSCHANGED</c> — powiadomienie o zmianie pozycji okna paska.</summary>
    internal const uint AbmWindowPosChanged = 0x00000009;

    /// <summary><c>MONITOR_DEFAULTTONEAREST</c> — najbliższy monitor, gdy punkt poza ekranem.</summary>
    internal const uint MonitorDefaultToNearest = 0x00000002;

    /// <summary><c>MONITORINFOF_PRIMARY</c> — monitor jest głównym monitorem powłoki.</summary>
    internal const uint MonitorInfoFlagPrimary = 0x00000001;

    /// <summary><c>MDT_EFFECTIVE_DPI</c> — typ DPI zwracany przez <c>GetDpiForMonitor</c>.</summary>
    internal const int MdtEffectiveDpi = 0;

    /// <summary>Delegat wywoływany przez <c>EnumDisplayMonitors</c> dla każdego monitora.</summary>
    internal delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, ref NativeRect rect, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("shell32.dll", EntryPoint = "SHAppBarMessage", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr SHAppBarMessage(uint message, ref AppBarData data);
}
