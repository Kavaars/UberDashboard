using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Weather;

namespace UberDashboard.Infrastructure.Weather;

/// <summary>
/// Klient Open-Meteo pobierający bieżące warunki pogodowe.
/// </summary>
public sealed class OpenMeteoWeatherService : IWeatherService
{
    private readonly HttpClient _client;
    private readonly IClock _clock;

    /// <summary>Tworzy klienta prognozy Open-Meteo.</summary>
    public OpenMeteoWeatherService(HttpClient client, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(clock);
        _client = client;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<WeatherSnapshot> GetCurrentAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        var url =
            "/v1/forecast?current=temperature_2m,weather_code,wind_speed_10m,is_day" +
            "&daily=temperature_2m_max,temperature_2m_min,sunrise,sunset" +
            "&hourly=temperature_2m,weather_code,is_day&forecast_days=1&timezone=auto" +
            $"&latitude={latitude.ToString("0.####", CultureInfo.InvariantCulture)}" +
            $"&longitude={longitude.ToString("0.####", CultureInfo.InvariantCulture)}";

        using var response = await _client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<OpenMeteoForecastResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var current = payload?.Current
            ?? throw new InvalidOperationException("Open-Meteo response does not contain current weather data.");
        var daily = payload?.Daily;
        var hourlyForecast = BuildHourlyForecast(payload?.Hourly, current.Time);

        return new WeatherSnapshot(
            current.TemperatureCelsius,
            current.WeatherCode,
            current.WindSpeedKph,
            current.IsDay,
            current.Time,
            _clock.UtcNow,
            false,
            daily?.TemperatureMax?.FirstOrDefault(),
            daily?.TemperatureMin?.FirstOrDefault(),
            daily?.Sunrise?.FirstOrDefault(),
            daily?.Sunset?.FirstOrDefault(),
            hourlyForecast);
    }

    private static IReadOnlyList<WeatherHourlyForecastPoint> BuildHourlyForecast(
        OpenMeteoHourlyResponse? hourly,
        DateTimeOffset sourceTime)
    {
        if (hourly?.Times is null
            || hourly.Temperatures is null
            || hourly.WeatherCodes is null
            || hourly.IsDayRaw is null)
        {
            return Array.Empty<WeatherHourlyForecastPoint>();
        }

        var length = new[]
        {
            hourly.Times.Count,
            hourly.Temperatures.Count,
            hourly.WeatherCodes.Count,
            hourly.IsDayRaw.Count,
        }.Min();

        if (length == 0)
        {
            return Array.Empty<WeatherHourlyForecastPoint>();
        }

        var points = new List<WeatherHourlyForecastPoint>(Math.Min(length, 6));
        for (var index = 0; index < length; index++)
        {
            var time = hourly.Times[index];
            if (time < sourceTime)
            {
                continue;
            }

            points.Add(new WeatherHourlyForecastPoint(
                time,
                hourly.Temperatures[index],
                hourly.WeatherCodes[index],
                hourly.IsDayRaw[index] == 1));

            if (points.Count == 6)
            {
                break;
            }
        }

        return points;
    }

    private sealed class OpenMeteoForecastResponse
    {
        [JsonPropertyName("current")]
        public OpenMeteoCurrentResponse? Current { get; init; }

        [JsonPropertyName("daily")]
        public OpenMeteoDailyResponse? Daily { get; init; }

        [JsonPropertyName("hourly")]
        public OpenMeteoHourlyResponse? Hourly { get; init; }
    }

    private sealed class OpenMeteoCurrentResponse
    {
        [JsonPropertyName("temperature_2m")]
        public double TemperatureCelsius { get; init; }

        [JsonPropertyName("weather_code")]
        public int WeatherCode { get; init; }

        [JsonPropertyName("wind_speed_10m")]
        public double WindSpeedKph { get; init; }

        [JsonPropertyName("is_day")]
        public int IsDayRaw { get; init; }

        [JsonPropertyName("time")]
        public DateTimeOffset Time { get; init; }

        public bool IsDay => IsDayRaw == 1;
    }

    private sealed class OpenMeteoDailyResponse
    {
        [JsonPropertyName("temperature_2m_max")]
        public List<double>? TemperatureMax { get; init; }

        [JsonPropertyName("temperature_2m_min")]
        public List<double>? TemperatureMin { get; init; }

        [JsonPropertyName("sunrise")]
        public List<DateTimeOffset>? Sunrise { get; init; }

        [JsonPropertyName("sunset")]
        public List<DateTimeOffset>? Sunset { get; init; }
    }

    private sealed class OpenMeteoHourlyResponse
    {
        [JsonPropertyName("time")]
        public List<DateTimeOffset>? Times { get; init; }

        [JsonPropertyName("temperature_2m")]
        public List<double>? Temperatures { get; init; }

        [JsonPropertyName("weather_code")]
        public List<int>? WeatherCodes { get; init; }

        [JsonPropertyName("is_day")]
        public List<int>? IsDayRaw { get; init; }
    }
}
