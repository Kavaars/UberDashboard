namespace UberDashboard.Application.Platform;

/// <summary>Trwałe ustawienia uruchamiania i przełączania trybów okna.</summary>
public sealed record WindowModeSettings
{
    /// <summary>Bezpieczne wartości domyślne używane przy pierwszym starcie i fallbacku.</summary>
    public static WindowModeSettings Default { get; } = new();

    /// <summary>Tryb uruchomienia aplikacji.</summary>
    public WindowMode LaunchMode { get; init; } = WindowMode.Normal;

    /// <summary>Preferowany identyfikator monitora dla trybu paska.</summary>
    public string? MonitorId { get; init; }

    /// <summary>Krawędź monitora dla trybu dokowanego paska.</summary>
    public AppBarEdge DockEdge { get; init; } = AppBarEdge.Bottom;

    /// <summary>Grubość paska w pikselach.</summary>
    public int DockThickness { get; init; } = 280;

    /// <summary>Przezroczystość okna w trybie widgetu pulpitu.</summary>
    public double WidgetOpacity { get; init; } = 0.92d;

    /// <summary>Powrót do trybu normalnego po niepowodzeniu aktywacji widgetu.</summary>
    public bool DesktopWidgetFallbackToNormal { get; init; } = true;

    /// <summary>Zwraca kopię ustawień z bezpiecznymi zakresami wartości.</summary>
    public WindowModeSettings Normalize()
    {
        var launchMode = Enum.IsDefined(LaunchMode) ? LaunchMode : WindowMode.Normal;
        var dockEdge = Enum.IsDefined(DockEdge) ? DockEdge : AppBarEdge.Bottom;

        return this with
        {
            LaunchMode = launchMode,
            MonitorId = string.IsNullOrWhiteSpace(MonitorId) ? null : MonitorId.Trim(),
            DockEdge = dockEdge,
            DockThickness = Math.Clamp(DockThickness, 200, 1200),
            WidgetOpacity = Math.Clamp(WidgetOpacity, 0.2d, 1.0d),
        };
    }
}
