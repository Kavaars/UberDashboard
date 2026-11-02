using System.Runtime.InteropServices;

namespace UberDashboard.Platform.Windows.Interop;

/// <summary>Punkt we współrzędnych ekranu (<c>POINT</c>).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;

    public int Y;
}

/// <summary>Prostokąt Win32 (<c>RECT</c>) w pikselach.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;

    public int Top;

    public int Right;

    public int Bottom;
}

/// <summary>
/// Informacje o monitorze z nazwą urządzenia (<c>MONITORINFOEXW</c>).
/// Nazwa urządzenia (np. <c>\\.\DISPLAY1</c>) jest używana jako stabilny identyfikator monitora.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MonitorInfoEx
{
    /// <summary>Rozmiar struktury w bajtach; musi być ustawiony przed wywołaniem.</summary>
    public int Size;

    /// <summary>Pełny obszar monitora (<c>rcMonitor</c>).</summary>
    public NativeRect Monitor;

    /// <summary>Obszar roboczy monitora (<c>rcWork</c>).</summary>
    public NativeRect Work;

    /// <summary>Flagi monitora; bit <c>MONITORINFOF_PRIMARY</c> oznacza monitor główny.</summary>
    public uint Flags;

    /// <summary>Nazwa urządzenia monitora (<c>CCHDEVICENAME</c> = 32 znaki).</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string DeviceName;
}

/// <summary>Dane przekazywane do <c>SHAppBarMessage</c> (<c>APPBARDATA</c>).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AppBarData
{
    /// <summary>Rozmiar struktury w bajtach; musi być ustawiony przed wywołaniem.</summary>
    public int Size;

    /// <summary>Uchwyt okna paska.</summary>
    public IntPtr WindowHandle;

    /// <summary>Komunikat zwrotny powłoki wysyłany do okna paska.</summary>
    public uint CallbackMessage;

    /// <summary>Krawędź paska (<c>ABE_*</c>).</summary>
    public uint Edge;

    /// <summary>Prostokąt paska w pikselach; powłoka może go zmienić.</summary>
    public NativeRect Rect;

    /// <summary>Parametr dodatkowy zależny od komunikatu.</summary>
    public IntPtr Param;
}
