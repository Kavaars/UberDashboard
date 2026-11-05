using UberDashboard.Core.Localization;

namespace UberDashboard.App.Settings;

/// <summary>Trwałe ustawienia personalizacji i ogólnych preferencji aplikacji.</summary>
public sealed record ApplicationSettings
{
    /// <summary>Domyślna konfiguracja bez nadpisanych tokenów.</summary>
    public static ApplicationSettings Default { get; } = new()
    {
        PaletteVersion = 1,
        EnableTrayIcon = true,
        CloseBehavior = CloseBehavior.ExitApplication,
    };

    /// <summary>Wersja zapisu palety; zero oznacza dawny formularz utrwalający kolory motywu.</summary>
    public int PaletteVersion { get; init; }

    /// <summary>Kolor akcentu w formacie hex; pusty oznacza wartość z motywu.</summary>
    public string? AccentColor { get; init; }

    /// <summary>Kolor tła okna w formacie hex; pusty oznacza wartość z motywu.</summary>
    public string? WindowBackgroundColor { get; init; }

    /// <summary>Kolor głównej powierzchni kart w formacie hex; pusty oznacza wartość z motywu.</summary>
    public string? SurfaceColor { get; init; }

    /// <summary>Kolor alternatywnej powierzchni w formacie hex; pusty oznacza wartość z motywu.</summary>
    public string? SurfaceAltColor { get; init; }

    /// <summary>Nazwa fontu interfejsu; pusta oznacza wartość domyślną.</summary>
    public string? FontFamilyName { get; init; }

    /// <summary>Rozmiar bazowy fontu w DIP.</summary>
    public double? FontSize { get; init; }

    /// <summary>Promień narożników głównych kontrolek.</summary>
    public double? CornerRadius { get; init; }

    /// <summary>Skala gęstości interfejsu. 1 oznacza domyślny spacing.</summary>
    public double? DensityScale { get; init; }

    /// <summary>Łączna przerwa między panelami z uchwytem 6 DIP.</summary>
    public double? PanelSpacing { get; init; }

    /// <summary>Widoczna grubość linii, niezależna od obszaru chwytania.</summary>
    public double? DividerThickness { get; init; }

    /// <summary>Domyślny interwał odświeżania danych pogodowych w minutach.</summary>
    public int? WeatherRefreshMinutes { get; init; }

    public string Language { get; init; } = UiText.Polish;

    public string? WallpaperPath { get; init; }
    public double WallpaperBlur { get; init; }
    public double BackgroundSurfaceOpacity { get; init; } = 0.7;
    public double WindowOpacity { get; init; } = 1;
    public bool DesktopBlur { get; init; }
    public bool TransparentDesktopBackground { get; init; }

    /// <summary>Uruchamia aplikację razem z profilem bieżącego użytkownika Windows.</summary>
    public bool StartWithWindows { get; init; }

    /// <summary>Pokazuje ikonę aplikacji w zasobniku systemowym.</summary>
    public bool EnableTrayIcon { get; init; }

    /// <summary>Określa zachowanie po kliknięciu krzyżyka zamknięcia.</summary>
    public CloseBehavior CloseBehavior { get; init; } = CloseBehavior.ExitApplication;

    /// <summary>Zwraca kopię z wartościami przyciętymi do bezpiecznych zakresów.</summary>
    public ApplicationSettings Normalize() =>
        this with
        {
            Language = UiText.NormalizeLanguage(Language),
            AccentColor = NormalizeHex(AccentColor),
            WindowBackgroundColor = NormalizeHex(WindowBackgroundColor),
            SurfaceColor = NormalizeHex(SurfaceColor),
            SurfaceAltColor = NormalizeHex(SurfaceAltColor),
            FontFamilyName = NormalizeText(FontFamilyName),
            FontSize = FontSize is null ? null : (double.IsFinite(FontSize.Value) ? Math.Clamp(FontSize.Value, 11d, 24d) : 13d),
            CornerRadius = CornerRadius is null ? null : (double.IsFinite(CornerRadius.Value) ? Math.Clamp(CornerRadius.Value, 4d, 28d) : 12d),
            DensityScale = DensityScale is null ? null : (double.IsFinite(DensityScale.Value) ? Math.Clamp(DensityScale.Value, 0.85d, 1.35d) : 1d),
            PanelSpacing = PanelSpacing is null ? null : (double.IsFinite(PanelSpacing.Value) ? Math.Clamp(PanelSpacing.Value, 6d, 32d) : 10d),
            DividerThickness = DividerThickness is null ? null : (double.IsFinite(DividerThickness.Value) ? Math.Clamp(DividerThickness.Value, 0d, 3d) : 1d),
            WeatherRefreshMinutes = WeatherRefreshMinutes is null ? null : Math.Clamp(WeatherRefreshMinutes.Value, 5, 180),
            WallpaperPath = NormalizeText(WallpaperPath),
            WallpaperBlur = double.IsFinite(WallpaperBlur) ? Math.Clamp(WallpaperBlur, 0, 30) : 0,
            BackgroundSurfaceOpacity = double.IsFinite(BackgroundSurfaceOpacity) ? Math.Clamp(BackgroundSurfaceOpacity, 0, 1) : 0.7,
            WindowOpacity = double.IsFinite(WindowOpacity) ? Math.Clamp(WindowOpacity, 0.2, 1) : 1,
            EnableTrayIcon = EnableTrayIcon,
            StartWithWindows = StartWithWindows,
            CloseBehavior = NormalizeCloseBehavior(EnableTrayIcon, CloseBehavior),
        };

    private static string? NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var hex = trimmed.StartsWith('#') ? trimmed[1..] : trimmed;
        return hex.Length is 6 or 8 && hex.All(Uri.IsHexDigit) ? $"#{hex.ToUpperInvariant()}" : null;
    }

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CloseBehavior NormalizeCloseBehavior(bool enableTrayIcon, CloseBehavior closeBehavior)
    {
        if (!Enum.IsDefined(closeBehavior))
        {
            return CloseBehavior.ExitApplication;
        }

        if (!enableTrayIcon && closeBehavior == CloseBehavior.MinimizeToTray)
        {
            return CloseBehavior.ExitApplication;
        }

        return closeBehavior;
    }
}
