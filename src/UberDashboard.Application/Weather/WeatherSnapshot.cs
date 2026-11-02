namespace UberDashboard.Application.Weather;

/// <summary>
/// Bieżący odczyt pogody dla jednej lokalizacji.
/// </summary>
public sealed record WeatherHourlyForecastPoint(
    DateTimeOffset Time,
    double TemperatureCelsius,
    int WeatherCode,
    bool IsDay);

/// <summary>
/// Bieżący odczyt pogody dla jednej lokalizacji wraz z krótką prognozą godzinową i podsumowaniem dnia.
/// </summary>
public sealed record WeatherSnapshot(
    double TemperatureCelsius,
    int WeatherCode,
    double WindSpeedKph,
    bool IsDay,
    DateTimeOffset SourceTime,
    DateTimeOffset FetchedAtUtc,
    bool IsFromCache,
    double? DailyHighCelsius = null,
    double? DailyLowCelsius = null,
    DateTimeOffset? Sunrise = null,
    DateTimeOffset? Sunset = null,
    IReadOnlyList<WeatherHourlyForecastPoint>? HourlyForecast = null);
