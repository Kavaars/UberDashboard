namespace UberDashboard.Application.Weather;

/// <summary>
/// Magazyn ostatnich poprawnych odczytów pogody.
/// </summary>
public interface IWeatherCacheStore
{
    /// <summary>Zwraca ostatni zapis cache dla wskazanego klucza lokalizacji.</summary>
    Task<WeatherCacheEntry?> GetAsync(string locationKey, CancellationToken cancellationToken = default);

    /// <summary>Utrwala ostatni poprawny odczyt dla wskazanego klucza lokalizacji.</summary>
    Task UpsertAsync(
        string locationKey,
        WeatherSnapshot snapshot,
        DateTimeOffset? validUntilUtc,
        CancellationToken cancellationToken = default);
}
