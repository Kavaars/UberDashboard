namespace UberDashboard.Application.Weather;

/// <summary>
/// Zapis cache z ostatnim poprawnym odczytem pogody.
/// </summary>
public sealed record WeatherCacheEntry(
    string CacheKey,
    string LocationKey,
    WeatherSnapshot Snapshot,
    DateTimeOffset FetchedAtUtc,
    DateTimeOffset? ValidUntilUtc);
