using UberDashboard.Application.Weather;
using UberDashboard.Contracts.Host;

namespace UberDashboard.App.Tests.TestSupport;

internal sealed class FakeWeatherService : IWeatherService
{
    public int CallCount { get; private set; }

    public Func<double, double, CancellationToken, Task<WeatherSnapshot>>? OnGetCurrentAsync { get; init; }

    public Task<WeatherSnapshot> GetCurrentAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        CallCount++;
        return OnGetCurrentAsync is null
            ? Task.FromResult(new WeatherSnapshot(21.5d, 1, 12d, true, DateTimeOffset.Parse("2026-11-02T09:00:00+01:00"), DateTimeOffset.UtcNow, false))
            : OnGetCurrentAsync(latitude, longitude, cancellationToken);
    }
}

internal sealed class FakeWeatherLocationSearchService : IWeatherLocationSearchService
{
    public Task<IReadOnlyList<WeatherLocationOption>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WeatherLocationOption>>(
        [
            new WeatherLocationOption("Kraków", "Małopolskie", "Polska", 50.0614d, 19.9366d, "Europe/Warsaw"),
        ]);
}

internal sealed class FakeWeatherCacheStore : IWeatherCacheStore
{
    private readonly Dictionary<string, WeatherCacheEntry> _entries = new(StringComparer.Ordinal);

    public int GetCount { get; private set; }

    public int UpsertCount { get; private set; }

    public Task<WeatherCacheEntry?> GetAsync(string locationKey, CancellationToken cancellationToken = default)
    {
        GetCount++;
        _entries.TryGetValue(locationKey, out var entry);
        return Task.FromResult(entry);
    }

    public Task UpsertAsync(
        string locationKey,
        WeatherSnapshot snapshot,
        DateTimeOffset? validUntilUtc,
        CancellationToken cancellationToken = default)
    {
        UpsertCount++;
        _entries[locationKey] = new WeatherCacheEntry(locationKey, locationKey, snapshot, snapshot.FetchedAtUtc, validUntilUtc);
        return Task.CompletedTask;
    }

    public void Seed(string locationKey, WeatherSnapshot snapshot, DateTimeOffset? validUntilUtc = null) =>
        _entries[locationKey] = new WeatherCacheEntry(locationKey, locationKey, snapshot, snapshot.FetchedAtUtc, validUntilUtc);
}

internal sealed class FakePanelNotifier : IPanelNotifier
{
    public List<(string Message, PanelNotificationSeverity Severity)> Messages { get; } = [];

    public void Publish(string message, PanelNotificationSeverity severity = PanelNotificationSeverity.Information) =>
        Messages.Add((message, severity));
}
