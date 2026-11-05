using UberDashboard.Core.Synchronization;
using UberDashboard.Infrastructure.Database.Stores;
using UberDashboard.Infrastructure.Tests.TestSupport;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class CalendarViewCacheTests
{
    private static readonly DateOnly First = new(2030, 5, 1);
    private static readonly CalendarViewIdentity Identity = new("client", "account", "calendar", "Europe/Warsaw");

    [Fact]
    public async Task Complete_range_survives_reopening_and_covers_smaller_views_without_any_sync_binding()
    {
        await using var fixture = new DatabaseFixture();
        var store = new CalendarViewCache(fixture.Queue, fixture.Database);
        var snapshot = Sample();
        Assert.True(await store.SaveAsync(snapshot));
        var reopened = new CalendarViewCache(fixture.Queue, fixture.Database);
        var loaded = await reopened.ReadAsync(Identity, First.AddDays(2), First.AddDays(5));
        Assert.NotNull(loaded); Assert.Equal(snapshot.FetchedAt, loaded.FetchedAt);
        Assert.Equal("occurrence", Assert.Single(loaded.Entries).RemoteId);
        Assert.Equal("series", loaded.Entries[0].RecurringEventId);
        Assert.Null(await reopened.ReadAsync(Identity, First.AddDays(-1), First.AddDays(5)));
    }

    [Fact]
    public async Task Identity_isolates_accounts_clients_calendars_and_display_zones()
    {
        await using var fixture = new DatabaseFixture();
        var store = new CalendarViewCache(fixture.Queue, fixture.Database);
        await store.SaveAsync(Sample());
        foreach (var identity in new[] { Identity with { AccountId = "another" }, Identity with { ClientId = "another" },
            Identity with { CalendarId = "another" }, Identity with { DisplayTimeZone = "UTC" } })
            Assert.Null(await store.ReadAsync(identity, First, First.AddDays(1)));
    }

    [Fact]
    public async Task Empty_fetched_range_is_distinct_from_an_unfetched_range()
    {
        await using var fixture = new DatabaseFixture();
        var store = new CalendarViewCache(fixture.Queue, fixture.Database);
        Assert.Null(await store.ReadAsync(Identity, First, First.AddDays(1)));
        await store.SaveAsync(Sample() with { Entries = [] });
        Assert.Empty((await store.ReadAsync(Identity, First, First.AddDays(1)))!.Entries);
    }

    [Fact]
    public async Task Older_response_cannot_replace_a_newer_request_even_if_it_completes_later()
    {
        await using var fixture = new DatabaseFixture();
        var store = new CalendarViewCache(fixture.Queue, fixture.Database);
        var original = Sample();
        var newer = original with { RequestedAt = original.RequestedAt.AddMinutes(1), FetchedAt = original.FetchedAt.AddMinutes(1), Entries = [] };
        Assert.True(await store.SaveAsync(newer));
        Assert.False(await store.SaveAsync(original with { FetchedAt = original.FetchedAt.AddMinutes(5) }));
        Assert.Empty((await store.ReadAsync(Identity, First, First.AddDays(1)))!.Entries);
    }

    [Fact]
    public async Task Cancelled_or_invalid_replacement_preserves_the_previous_snapshot()
    {
        await using var fixture = new DatabaseFixture();
        var store = new CalendarViewCache(fixture.Queue, fixture.Database);
        await store.SaveAsync(Sample());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(Sample() with { Entries = [] }, cancellation.Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(Sample() with { FormatVersion = 2 }));
        Assert.Single((await store.ReadAsync(Identity, First, First.AddDays(1)))!.Entries);
    }

    private static CalendarViewSnapshot Sample()
    {
        var now = new DateTimeOffset(2030, 5, 1, 12, 0, 0, TimeSpan.Zero);
        return new(Identity, First, First.AddDays(31), now, now.AddSeconds(1),
            [new("Meeting", First.AddDays(2), First.AddDays(3), new TimeOnly(9, 0), "occurrence", "series", "2030-05-03T09:00:00+02:00")]);
    }
}
