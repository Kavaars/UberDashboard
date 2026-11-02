using System.Net.Http.Json;
using System.Text.Json.Serialization;
using UberDashboard.Application.Weather;

namespace UberDashboard.Infrastructure.Weather;

/// <summary>
/// Klient Open-Meteo geocoding do wyszukiwania lokalizacji.
/// </summary>
public sealed class OpenMeteoLocationSearchService : IWeatherLocationSearchService
{
    private readonly HttpClient _client;

    /// <summary>Tworzy klienta geokodowania Open-Meteo.</summary>
    public OpenMeteoLocationSearchService(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WeatherLocationOption>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var safeQuery = Uri.EscapeDataString(query.Trim());
        var count = Math.Clamp(maxResults, 1, 20);

        using var response = await _client
            .GetAsync($"/v1/search?name={safeQuery}&count={count}&language=pl&format=json", cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<OpenMeteoSearchResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (payload?.Results is null || payload.Results.Count == 0)
        {
            return [];
        }

        return payload.Results
            .Where(static result => !string.IsNullOrWhiteSpace(result.Name) && !string.IsNullOrWhiteSpace(result.Country))
            .Select(static result => new WeatherLocationOption(
                result.Name!,
                result.Admin1,
                result.Country!,
                result.Latitude,
                result.Longitude,
                result.TimeZone))
            .ToArray();
    }

    private sealed class OpenMeteoSearchResponse
    {
        [JsonPropertyName("results")]
        public List<OpenMeteoLocationResult>? Results { get; init; }
    }

    private sealed class OpenMeteoLocationResult
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("admin1")]
        public string? Admin1 { get; init; }

        [JsonPropertyName("country")]
        public string? Country { get; init; }

        [JsonPropertyName("latitude")]
        public double Latitude { get; init; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; init; }

        [JsonPropertyName("timezone")]
        public string? TimeZone { get; init; }
    }
}
