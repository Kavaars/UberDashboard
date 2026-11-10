using UberDashboard.Application.Weather;
using UberDashboard.Core.ModularPanels;

namespace UberDashboard.App.Features.Weather;

/// <summary>
/// Stan utrwalany dla instancji panelu Weather.
/// </summary>
public sealed class WeatherPanelDocument
{
    /// <summary>Wersja schematu stanu panelu.</summary>
    public const int SchemaVersion = 3;

    public ModulePosition[] ModulePositions { get; init; } = [];

    public double ModuleSpacing { get; init; }

    public bool ColorfulTheme { get; init; }
    public bool ShowLocation { get; init; } = true;
    public bool ShowIcon { get; init; } = true;
    public bool ShowTemperature { get; init; } = true;
    public bool ShowCondition { get; init; } = true;
    public bool ShowDailyRange { get; init; } = true;
    public bool ShowSunrise { get; init; } = true;
    public bool ShowSunset { get; init; } = true;
    public bool ShowForecast { get; init; } = true;

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
