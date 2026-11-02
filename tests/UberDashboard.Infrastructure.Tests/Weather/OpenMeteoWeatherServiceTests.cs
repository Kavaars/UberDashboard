using System.Net;
using UberDashboard.Application.Weather;
using UberDashboard.Infrastructure.Tests.TestSupport;
using UberDashboard.Infrastructure.Weather;

namespace UberDashboard.Infrastructure.Tests.Weather;

public sealed class OpenMeteoWeatherServiceTests
{
    [Fact]
    public async Task Returns_snapshot_from_forecast_response()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-11-02T07:05:00+00:00"));
        using var client = new HttpClient(new TestHttpMessageHandler(static (request, _) =>
        {
            Assert.Contains("/v1/forecast", request.RequestUri!.PathAndQuery, StringComparison.Ordinal);
            Assert.Contains("daily=temperature_2m_max,temperature_2m_min,sunrise,sunset", request.RequestUri.PathAndQuery, StringComparison.Ordinal);
            Assert.Contains("hourly=temperature_2m,weather_code,is_day", request.RequestUri.PathAndQuery, StringComparison.Ordinal);
            Assert.Contains("latitude=50.0614", request.RequestUri.PathAndQuery, StringComparison.Ordinal);
            Assert.Contains("longitude=19.9366", request.RequestUri.PathAndQuery, StringComparison.Ordinal);

            return TestHttpMessageHandler.Json(
                HttpStatusCode.OK,
                """
                {
                  "current": {
                    "temperature_2m": 17.4,
                    "weather_code": 3,
                    "wind_speed_10m": 12.6,
                    "is_day": 1,
                    "time": "2026-11-02T08:00"
                  },
                  "daily": {
                    "temperature_2m_max": [18.9],
                    "temperature_2m_min": [7.2],
                    "sunrise": ["2026-11-02T06:41"],
                    "sunset": ["2026-11-02T16:18"]
                  },
                  "hourly": {
                    "time": [
                      "2026-11-02T08:00",
                      "2026-11-02T09:00",
                      "2026-11-02T10:00"
                    ],
                    "temperature_2m": [17.4, 18.1, 18.8],
                    "weather_code": [3, 2, 1],
                    "is_day": [1, 1, 1]
                  }
                }
                """);
        }))
        {
            BaseAddress = new Uri("https://api.open-meteo.com"),
        };

        var service = new OpenMeteoWeatherService(client, clock);

        var snapshot = await service.GetCurrentAsync(50.0614d, 19.9366d, CancellationToken.None);

        Assert.Equal(17.4d, snapshot.TemperatureCelsius);
        Assert.Equal(3, snapshot.WeatherCode);
        Assert.Equal(12.6d, snapshot.WindSpeedKph);
        Assert.True(snapshot.IsDay);
        Assert.False(snapshot.IsFromCache);
        Assert.Equal(clock.UtcNow, snapshot.FetchedAtUtc);
        Assert.Equal(18.9d, snapshot.DailyHighCelsius);
        Assert.Equal(7.2d, snapshot.DailyLowCelsius);
        Assert.Equal("06:41", snapshot.Sunrise?.ToString("HH:mm"));
        Assert.Equal("16:18", snapshot.Sunset?.ToString("HH:mm"));
        Assert.Collection(
            snapshot.HourlyForecast ?? Array.Empty<WeatherHourlyForecastPoint>(),
            point =>
            {
                Assert.Equal("08:00", point.Time.ToString("HH:mm"));
                Assert.Equal(17.4d, point.TemperatureCelsius);
                Assert.Equal(3, point.WeatherCode);
                Assert.True(point.IsDay);
            },
            point =>
            {
                Assert.Equal("09:00", point.Time.ToString("HH:mm"));
                Assert.Equal(18.1d, point.TemperatureCelsius);
                Assert.Equal(2, point.WeatherCode);
                Assert.True(point.IsDay);
            },
            point =>
            {
                Assert.Equal("10:00", point.Time.ToString("HH:mm"));
                Assert.Equal(18.8d, point.TemperatureCelsius);
                Assert.Equal(1, point.WeatherCode);
                Assert.True(point.IsDay);
            });
    }
}
