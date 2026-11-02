namespace UberDashboard.Application.Weather;

/// <summary>
/// Wynik wyszukiwania lokalizacji pogodowej zwracany przez usługę geokodowania.
/// </summary>
public sealed record WeatherLocationOption(
    string Name,
    string? Region,
    string Country,
    double Latitude,
    double Longitude,
    string? TimeZone = null)
{
    /// <summary>Tekst prezentowany użytkownikowi przy wyborze lokalizacji.</summary>
    public string DisplayName =>
        string.Join(
            ", ",
            new[]
            {
                Name,
                string.IsNullOrWhiteSpace(Region) ? null : Region,
                Country,
            }.Where(static value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>Stabilny klucz cache dla współrzędnych lokalizacji.</summary>
    public string CacheKey => FormattableString.Invariant($"{Latitude:0.####},{Longitude:0.####}");
}
