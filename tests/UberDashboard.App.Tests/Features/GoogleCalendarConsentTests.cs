using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Json;
using UberDashboard.App.Features.Calendar;
using UberDashboard.Application.Synchronization;
using UberDashboard.Contracts.Host;

namespace UberDashboard.App.Tests.Features;

public sealed class GoogleCalendarConsentTests
{
    private const string Events = "https://www.googleapis.com/auth/calendar.events";
    private const string EventRead = "https://www.googleapis.com/auth/calendar.events.readonly";
    private const string ListRead = "https://www.googleapis.com/auth/calendar.calendarlist.readonly";

    [Fact]
    public async Task Calendar_management_needs_its_own_scope_and_keeps_existing_event_capabilities()
    {
        var storage = new Storage(Session(["openid", EventRead, Events])); var calls = 0;
        var client = new GoogleCalendarClient(storage, authorization: (scopes, _) =>
        {
            calls++; Assert.Contains(GoogleCalendarClient.CalendarManagementScope, scopes); Assert.Contains(Events, scopes); Assert.Contains(ListRead, scopes);
            // A broader event grant can replace its readonly alias in the actual provider response.
            return Task.FromResult(new GoogleCalendarClient.GoogleAuthorization("account", Token(["openid", Events, ListRead, GoogleCalendarClient.CalendarManagementScope])));
        });
        Assert.Null(await client.GetAccessTokenAsync("client", "account", true, default, GoogleCalendarClient.CalendarManagementScopes));
        await client.RequestCalendarManagementAsync("client", "account", default);
        Assert.Equal("upgraded-test-token", await client.GetAccessTokenAsync("client", "account", true, default, GoogleCalendarClient.CalendarManagementScopes));
        Assert.Equal("upgraded-test-token", await client.GetAccessTokenAsync("client", "account", true, default));
        await client.RequestCalendarManagementAsync("client", "account", default); Assert.Equal(1, calls);
        await client.RequestCalendarManagementAsync("client", "account", default, forceConsent: true); Assert.Equal(2, calls);
        var metadata = JsonSerializer.Serialize(await client.GetConnectionAsync(default));
        Assert.DoesNotContain("upgraded-test-token", metadata, StringComparison.Ordinal); Assert.DoesNotContain("ClientSecret", metadata, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wrong-account")]
    [InlineData("partial-consent")]
    [InlineData("denied")]
    [InlineData("canceled")]
    public async Task Failed_additional_consent_does_not_replace_existing_credentials(string failure)
    {
        var previous = Session(["openid", Events]); var storage = new Storage(previous);
        var client = new GoogleCalendarClient(storage, authorization: (_, _) => failure switch
        {
            "denied" => throw new InvalidOperationException("Declined"),
            "canceled" => throw new OperationCanceledException(),
            _ => Task.FromResult(new GoogleCalendarClient.GoogleAuthorization(failure == "wrong-account" ? "other" : "account",
                Token(failure == "partial-consent" ? [Events] : [Events, ListRead, GoogleCalendarClient.CalendarManagementScope])))
        });
        await Assert.ThrowsAnyAsync<Exception>(() => client.RequestCalendarManagementAsync("client", "account", default));
        Assert.Equal(previous, storage.Data); Assert.Equal(0, storage.Writes);
        Assert.Equal("old-test-token", await client.GetAccessTokenAsync("client", "account", true, default));
    }

    [Fact]
    public async Task Disconnect_during_consent_cannot_resurrect_the_session()
    {
        var storage = new Storage(Session([Events])); GoogleCalendarClient? client = null;
        client = new GoogleCalendarClient(storage, authorization: async (_, _) =>
        {
            await client!.DisconnectAsync(default);
            return new("account", Token([Events, ListRead, GoogleCalendarClient.CalendarManagementScope]));
        });
        await Assert.ThrowsAsync<SyncAuthenticationException>(() => client.RequestCalendarManagementAsync("client", "account", default));
        Assert.Null(storage.Data); Assert.Equal(0, storage.Writes);
    }

    [Fact]
    public async Task A_late_consent_cannot_overwrite_another_account_or_a_newer_scope_upgrade()
    {
        foreach (var otherAccount in new[] { false, true })
        {
            var storage = new Storage(Session([Events])); byte[]? newer = null;
            var client = new GoogleCalendarClient(storage, authorization: (_, _) =>
            {
                newer = Session([Events, ListRead, GoogleCalendarClient.CalendarManagementScope, "https://www.googleapis.com/auth/calendar.acls"], otherAccount ? "other" : "account");
                storage.Data = newer;
                return Task.FromResult(new GoogleCalendarClient.GoogleAuthorization("account", Token([Events, ListRead, GoogleCalendarClient.CalendarManagementScope])));
            });
            await Assert.ThrowsAsync<SyncAuthenticationException>(() => client.RequestCalendarManagementAsync("client", "account", default));
            Assert.Equal(newer, storage.Data); Assert.Equal(0, storage.Writes);
        }
    }

    [Fact]
    public async Task Identity_and_actual_token_scopes_are_checked_before_use()
    {
        var storage = new Storage(Session([Events, ListRead, GoogleCalendarClient.CalendarManagementScope], tokenScopes: [Events]));
        var client = new GoogleCalendarClient(storage, authorization: (_, _) => throw new InvalidOperationException("Unexpected browser login"));
        await Assert.ThrowsAsync<SyncAuthenticationException>(() => client.RequestCalendarManagementAsync("wrong", "account", default));
        Assert.Null(await client.GetAccessTokenAsync("client", "account", true, default, GoogleCalendarClient.CalendarManagementScopes));
    }

    private static TokenResponse Token(string[] scopes) => NewtonsoftJsonSerializer.Instance.Deserialize<TokenResponse>(JsonSerializer.Serialize(new
        { access_token = "upgraded-test-token", scope = string.Join(' ', scopes), expires_in = 3600, IssuedUtc = DateTime.UtcNow }));
    private static byte[] Session(string[] scopes, string account = "account", string[]? tokenScopes = null) => ProtectedData.Protect(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { ClientId = "client", ClientSecret = "test-only", AccountId = account, GrantedScopes = scopes,
            Token = new { access_token = "old-test-token", scope = string.Join(' ', tokenScopes ?? scopes), expires_in = 3600, IssuedUtc = DateTime.UtcNow } })), null, DataProtectionScope.CurrentUser);
    private sealed class Storage(byte[] data) : IPanelStorage
    {
        public byte[]? Data { get; set; } = data;
        public int Writes { get; private set; }
        public ValueTask<byte[]?> ReadAsync(string fileName, CancellationToken cancellationToken) => ValueTask.FromResult(Data);
        public ValueTask WriteAsync(string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        { Writes++; Data = content.ToArray(); return ValueTask.CompletedTask; }
        public ValueTask DeleteAsync(string fileName, CancellationToken cancellationToken) { Data = null; return ValueTask.CompletedTask; }
        public IReadOnlyList<string> ListFiles() => [];
    }
}
