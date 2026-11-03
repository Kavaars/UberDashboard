using UberDashboard.Application.Weather;

internal sealed class SampleWeatherService : IWeatherService
{
    public Task<WeatherSnapshot> GetCurrentAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        var time = DateTimeOffset.Parse("2026-11-02T12:00:00+01:00");
        return Task.FromResult(new WeatherSnapshot(8.5, 2, 12.4, true, time, time, false,
            12, 4, time.AddHours(-5), time.AddHours(4),
            Enumerable.Range(0, 6).Select(i => new WeatherHourlyForecastPoint(time.AddHours(i), 8.5 + i, 2, true)).ToArray()));
    }
}
