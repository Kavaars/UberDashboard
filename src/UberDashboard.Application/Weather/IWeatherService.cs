namespace UberDashboard.Application.Weather;

/// <summary>
/// Pobiera bieżący odczyt pogody dla wskazanych współrzędnych.
/// </summary>
public interface IWeatherService
{
    /// <summary>Pobiera bieżącą pogodę dla wskazanej lokalizacji.</summary>
    Task<WeatherSnapshot> GetCurrentAsync(double latitude, double longitude, CancellationToken cancellationToken);
}
