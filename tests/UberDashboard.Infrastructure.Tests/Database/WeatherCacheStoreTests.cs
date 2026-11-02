using UberDashboard.Application.Weather;
using UberDashboard.Infrastructure.Tests.TestSupport;
using UberDashboard.Infrastructure.Weather;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class WeatherCacheStoreTests
{
    [Fact]
    public async Task Stores_and_restores_latest_snapshot()
    {
        await using var fixture = new DatabaseFixture();
        var store = new WeatherCacheStore(fixture.Queue, fixture.Database);
        var snapshot = new WeatherSnapshot(
            17.4d,
            3,
            12.6d,
            true,
            DateTimeOffset.Parse("2026-11-02T08:00:00+01:00"),
            fixture.Clock.UtcNow,
            false);

        await store.UpsertAsync("50.0614,19.9366", snapshot, fixture.Clock.UtcNow.AddMinutes(30));
        var cached = await store.GetAsync("50.0614,19.9366");

        Assert.NotNull(cached);
        Assert.Equal("50.0614,19.9366", cached!.LocationKey);
        Assert.Equal(17.4d, cached.Snapshot.TemperatureCelsius);
        Assert.True(cached.Snapshot.IsFromCache);
    }
}
