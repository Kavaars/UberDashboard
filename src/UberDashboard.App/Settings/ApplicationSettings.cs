namespace UberDashboard.App.Settings;

/// <summary>Trwałe ustawienia personalizacji i ogólnych preferencji aplikacji.</summary>
public sealed record ApplicationSettings
{
    /// <summary>Domyślna konfiguracja bez nadpisanych tokenów.</summary>
    public static ApplicationSettings Default { get; } = new()
    {
        EnableTrayIcon = true,
        CloseBehavior = CloseBehavior.ExitApplication,
    };

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

    /// <summary>Domyślny interwał odświeżania danych pogodowych w minutach.</summary>
    public int? WeatherRefreshMinutes { get; init; }

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
            AccentColor = NormalizeHex(AccentColor),
            WindowBackgroundColor = NormalizeHex(WindowBackgroundColor),
            SurfaceColor = NormalizeHex(SurfaceColor),
            SurfaceAltColor = NormalizeHex(SurfaceAltColor),
            FontFamilyName = NormalizeText(FontFamilyName),
            FontSize = FontSize is null ? null : Math.Clamp(FontSize.Value, 11d, 24d),
            CornerRadius = CornerRadius is null ? null : Math.Clamp(CornerRadius.Value, 4d, 28d),
            DensityScale = DensityScale is null ? null : Math.Clamp(DensityScale.Value, 0.85d, 1.35d),
            WeatherRefreshMinutes = WeatherRefreshMinutes is null ? null : Math.Clamp(WeatherRefreshMinutes.Value, 5, 180),
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
        return trimmed.StartsWith('#') ? trimmed.ToUpperInvariant() : $"#{trimmed.ToUpperInvariant()}";
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
