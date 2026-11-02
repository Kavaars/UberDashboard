namespace UberDashboard.Application.Weather;

/// <summary>
/// Wyszukuje lokalizacje pogodowe po nazwie wpisanej przez użytkownika.
/// </summary>
public interface IWeatherLocationSearchService
{
    /// <summary>Zwraca dopasowania dla tekstu wyszukiwania.</summary>
    Task<IReadOnlyList<WeatherLocationOption>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken);
}
