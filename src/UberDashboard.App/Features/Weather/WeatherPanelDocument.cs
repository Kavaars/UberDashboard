using UberDashboard.Application.Weather;

namespace UberDashboard.App.Features.Weather;

/// <summary>
/// Stan utrwalany dla instancji panelu Weather.
/// </summary>
public sealed class WeatherPanelDocument
{
    /// <summary>Wersja schematu stanu panelu.</summary>
    public const int SchemaVersion = 3;

    public DateTimeOffset? NetworkConsentGrantedAtUtc { get; init; }

    /// <summary>Wybrana lokalizacja albo <see langword="null"/>.</summary>
    public WeatherLocationOption? SelectedLocation { get; init; }

    /// <summary>Interwał automatycznego odświeżania danych w minutach.</summary>
    public int AutoRefreshMinutes { get; init; } = 30;

    /// <summary>Pokazuje prędkość wiatru na karcie danych.</summary>
    public bool ShowWind { get; init; } = true;

    /// <summary>Pokazuje czas ostatniej aktualizacji na karcie danych.</summary>
    public bool ShowUpdatedTime { get; init; } = true;
}
