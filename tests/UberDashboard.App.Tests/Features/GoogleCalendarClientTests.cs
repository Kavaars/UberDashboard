using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using UberDashboard.App.Features.Calendar;
using UberDashboard.Contracts.Host;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.App.Tests.Features;

public sealed class GoogleCalendarClientTests
{
    [Fact]
    public async Task Completed_expanded_range_is_available_after_restart_and_offline_is_explicit()
    {
        var cache = new ViewCache();
        using var handler = new ViewHandler(); using var http = new HttpClient(handler);
        var client = new GoogleCalendarClient(WithIdentity(), http, cache);
        var period = new CalendarPeriod(new(2030, 5, 1), new(2030, 6, 1), 7);
        var online = await client.ReadViewAsync("primary", period, default);
        Assert.Equal(CalendarViewFailure.None, online.Failure); Assert.False(online.IsCached);
        Assert.Equal("event", Assert.Single(cache.Snapshot!.Entries).RemoteId);
        handler.Offline = true;
        var reopened = new GoogleCalendarClient(WithIdentity(), http, cache);
        var offline = await reopened.ReadViewAsync("primary", period, default);
        Assert.True(offline.IsCached); Assert.Equal(CalendarViewFailure.Offline, offline.Failure);
        Assert.Equal(online.Entries![0].Title, Assert.Single(offline.Entries!).Title);
        var missing = await reopened.ReadViewAsync("primary", period with { End = period.End.AddDays(1) }, default);
        Assert.False(missing.IsCached); Assert.Null(missing.Entries);
    }

    [Fact]
    public async Task Failed_later_page_never_replaces_a_complete_cached_range()
    {
        var cache = new ViewCache();
        using var handler = new ViewHandler(); using var http = new HttpClient(handler);
        var client = new GoogleCalendarClient(WithIdentity(), http, cache);
        var period = new CalendarPeriod(new(2030, 5, 1), new(2030, 6, 1), 7);
        await client.ReadViewAsync("primary", period, default);
        var saved = cache.Snapshot;
        handler.FailSecondPage = true;
        var failure = await client.ReadViewAsync("primary", period, default);
        Assert.True(failure.IsCached); Assert.Same(saved, cache.Snapshot); Assert.Equal(1, cache.Saves);
    }

    [Fact]
    public async Task Disconnected_or_different_account_never_receives_another_accounts_cached_view()
    {
        var cache = new ViewCache(); using var handler = new ViewHandler(); using var http = new HttpClient(handler);
        var storage = WithIdentity(); var client = new GoogleCalendarClient(storage, http, cache);
        var period = new CalendarPeriod(new(2030, 5, 1), new(2030, 6, 1), 7);
        await client.ReadViewAsync("primary", period, default); handler.Offline = true;
        var other = new GoogleCalendarClient(WithIdentity("other"), http, cache);
        Assert.Null((await other.ReadViewAsync("primary", period, default)).Entries);
        await client.DisconnectAsync(default);
        var disconnected = await client.ReadViewAsync("primary", period, default);
        Assert.Null(disconnected.Entries); Assert.Equal(CalendarViewFailure.NotConnected, disconnected.Failure);
    }

    [Fact]
    public async Task Account_replaced_during_request_does_not_return_or_cache_the_previous_accounts_response()
    {
        var cache = new ViewCache(); var storage = WithIdentity();
        using var handler = new ViewHandler { OnRead = () => storage.Data = WithIdentity("other").Data };
        using var http = new HttpClient(handler);
        var result = await new GoogleCalendarClient(storage, http, cache).ReadViewAsync("primary", new(new(2030, 5, 1), new(2030, 6, 1), 7), default);
        Assert.Null(result.Entries); Assert.Null(cache.Snapshot);
    }

    [Fact]
    public async Task Account_replaced_during_local_cache_save_does_not_display_the_previous_accounts_view()
    {
        var storage = WithIdentity(); var cache = new ViewCache { OnSave = () => storage.Data = WithIdentity("other").Data };
        using var handler = new ViewHandler(); using var http = new HttpClient(handler);
        var result = await new GoogleCalendarClient(storage, http, cache).ReadViewAsync("primary", new(new(2030, 5, 1), new(2030, 6, 1), 7), default);
        Assert.Null(result.Entries); Assert.Equal(CalendarViewFailure.AuthenticationRequired, result.Failure);
        Assert.Equal("account", cache.Snapshot!.Identity.AccountId);
    }

    [Fact]
    public async Task Cache_write_failure_does_not_discard_successfully_fetched_events()
    {
        var cache = new ViewCache { FailSave = true }; using var handler = new ViewHandler(); using var http = new HttpClient(handler);
        var result = await new GoogleCalendarClient(WithIdentity(), http, cache).ReadViewAsync("primary", new(new(2030, 5, 1), new(2030, 6, 1), 7), default);
        Assert.Single(result.Entries!); Assert.Equal(CalendarViewFailure.CacheUnavailable, result.Failure); Assert.False(result.IsCached);
    }

    [Theory]
    [InlineData("wrong-client", "account", true)]
    [InlineData("client", "wrong-account", true)]
    [InlineData("client", "account", true)]
    public async Task Does_not_supply_a_write_token_for_wrong_identity_or_read_only_consent(string clientId, string accountId, bool write)
    {
        var session = $$$"""
            {"ClientId":"client","ClientSecret":"test","AccountId":"account","GrantedScopes":["https://www.googleapis.com/auth/calendar.events.readonly"],"Token":{"access_token":"test-token","expires_in":3600,"IssuedUtc":"{{{DateTime.UtcNow:O}}}"}}
            """;
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(session), null, DataProtectionScope.CurrentUser);
        var client = new GoogleCalendarClient(new SessionStorage(encrypted));
        Assert.Null(await client.GetAccessTokenAsync(clientId, accountId, write, default));
    }

    [Fact]
    public async Task Supplies_only_a_matching_consented_identity_without_exposing_secrets_in_metadata()
    {
        var session = $$$"""
            {"ClientId":"client","ClientSecret":"test","AccountId":"account","GrantedScopes":["https://www.googleapis.com/auth/calendar.events"],"Token":{"access_token":"test-token","expires_in":3600,"IssuedUtc":"{{{DateTime.UtcNow:O}}}"}}
            """;
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(session), null, DataProtectionScope.CurrentUser);
        var client = new GoogleCalendarClient(new SessionStorage(encrypted));
        Assert.Equal("test-token", await client.GetAccessTokenAsync("client", "account", true, default));
        var info = await client.GetConnectionAsync(default);
        Assert.Equal("account", info!.AccountId);
        Assert.DoesNotContain("test-token", System.Text.Json.JsonSerializer.Serialize(info));
        Assert.DoesNotContain("ClientSecret", System.Text.Json.JsonSerializer.Serialize(info));
    }

    [Fact]
    public async Task Fetch_expands_recurring_events_follows_page_tokens_and_parses_both_date_formats()
    {
        using var handler = new CalendarHandler();
        using var http = new HttpClient(handler);
        var session = $$$"""
            {"ClientId":"test","ClientSecret":"test","Token":{"access_token":"test-token","expires_in":3600,"IssuedUtc":"{{{DateTime.UtcNow:O}}}"}}
            """;
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(session), null, DataProtectionScope.CurrentUser);
        var client = new GoogleCalendarClient(new SessionStorage(encrypted), http);
        var start = new DateOnly(2026, 11, 4);

        var entries = await client.ReadAsync("sample@group.calendar.google.com", new(start, start.AddDays(7), 7), CancellationToken.None);

        Assert.Collection(Assert.IsAssignableFrom<IReadOnlyList<CalendarEntry>>(entries),
            entry =>
            {
                Assert.Equal("Urlop", entry.Title);
                Assert.True(entry.OccursOn(start.AddDays(1)));
                Assert.False(entry.OccursOn(start.AddDays(2)));
                Assert.Null(entry.Time);
            },
            entry =>
            {
                Assert.Equal("Spotkanie", entry.Title);
                Assert.Equal(TimeOnly.FromDateTime(new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.Zero).LocalDateTime), entry.Time);
            });
        Assert.Equal(2, handler.Requests);
    }

    private sealed class CalendarHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = Assert.IsType<Uri>(request.RequestUri);
            Assert.Equal("www.googleapis.com", uri.Host);
            Assert.Contains("sample%40group.calendar.google.com/events", uri.AbsolutePath);
            Assert.Contains("singleEvents=true", uri.Query);
            Assert.Contains("timeMin=", uri.Query);
            Assert.Contains("timeMax=", uri.Query);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            string json;
            if (Requests++ == 0)
            {
                Assert.DoesNotContain("pageToken=", uri.Query);
                json = """{"nextPageToken":"next+page","items":[{"summary":"Urlop","start":{"date":"2026-11-04"},"end":{"date":"2026-11-06"}}]}""";
            }
            else
            {
                Assert.Contains("pageToken=next%2Bpage", uri.Query);
                json = """{"items":[{"summary":"Spotkanie","start":{"dateTime":"2026-11-06T10:00:00Z"},"end":{"dateTime":"2026-11-06T11:00:00Z"}}]}""";
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static SessionStorage WithIdentity(string account = "account") => new(ProtectedData.Protect(Encoding.UTF8.GetBytes($$$"""
        {"ClientId":"client","ClientSecret":"test","AccountId":"{{{account}}}","GrantedScopes":["https://www.googleapis.com/auth/calendar.events"],"Token":{"access_token":"test-token","expires_in":3600,"IssuedUtc":"{{{DateTime.UtcNow:O}}}"}}
        """), null, DataProtectionScope.CurrentUser));

    private sealed class ViewHandler : HttpMessageHandler
    {
        public bool Offline { get; set; }
        public bool FailSecondPage { get; set; }
        public Action? OnRead { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline || FailSecondPage && request.RequestUri!.Query.Contains("pageToken=", StringComparison.Ordinal)) throw new HttpRequestException();
            OnRead?.Invoke();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(FailSecondPage
                ? """{"nextPageToken":"next","items":[]}"""
                : """{"items":[{"id":"event","summary":"Meeting","start":{"date":"2030-05-02"},"end":{"date":"2030-05-03"},"recurringEventId":"series","originalStartTime":{"date":"2030-05-02"}}]}""", Encoding.UTF8, "application/json") });
        }
    }

    private sealed class ViewCache : ICalendarViewCache
    {
        public CalendarViewSnapshot? Snapshot { get; private set; }
        public int Saves { get; private set; }
        public bool FailSave { get; init; }
        public Action? OnSave { get; init; }
        public Task<CalendarViewSnapshot?> ReadAsync(CalendarViewIdentity identity, DateOnly start, DateOnly end, CancellationToken token = default) =>
            Task.FromResult(Snapshot is { } value && value.Identity == identity && value.Start <= start && value.End >= end ? value : null);
        public Task<bool> SaveAsync(CalendarViewSnapshot value, CancellationToken token = default)
        { if (FailSave) throw new System.IO.IOException(); Snapshot = value; Saves++; OnSave?.Invoke(); return Task.FromResult(true); }
    }

    private sealed class SessionStorage(byte[] encrypted) : IPanelStorage
    {
        public byte[]? Data { get; set; } = encrypted;
        public ValueTask<byte[]?> ReadAsync(string fileName, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Data);

        public ValueTask WriteAsync(string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The test session should not require a token refresh.");

        public ValueTask DeleteAsync(string fileName, CancellationToken cancellationToken) { Data = null; return ValueTask.CompletedTask; }

        public IReadOnlyList<string> ListFiles() => [];
    }
}
