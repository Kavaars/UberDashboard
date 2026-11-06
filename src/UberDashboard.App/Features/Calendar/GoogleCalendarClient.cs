using UberDashboard.Core.Localization;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using Google.Apis.Json;
using UberDashboard.Contracts.Host;

namespace UberDashboard.App.Features.Calendar;

internal sealed partial class GoogleCalendarClient(IPanelStorage storage, HttpClient? http = null,
    UberDashboard.Application.Synchronization.ICalendarViewCache? viewCache = null,
    Func<IReadOnlyList<string>, CancellationToken, Task<GoogleCalendarClient.GoogleAuthorization>>? authorization = null)
{
    internal const string DefaultClientId = "566017659051-5tnkvv0jjiih7kj7p51378nik127pqd9.apps.googleusercontent.com";
    private const string SessionFile = "google-calendar.session";
    private const string User = "calendar";
    private const string Scope = "https://www.googleapis.com/auth/calendar.events.readonly";
    private const string WriteScope = "https://www.googleapis.com/auth/calendar.events";
    private const string CalendarListScope = "https://www.googleapis.com/auth/calendar.calendarlist.readonly";
    internal const string CalendarManagementScope = "https://www.googleapis.com/auth/calendar.calendars";
    internal static IReadOnlyList<string> CalendarManagementScopes { get; } = [CalendarManagementScope, CalendarListScope];
    private static readonly SemaphoreSlim CredentialGate = new(1, 1);
    private static readonly HttpClient DefaultHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ConnectAsync(string path, CancellationToken token, bool allowWrite = false, string? expectedAccountId = null)
    {
        await using var file = File.OpenRead(path);
        if (file.Length > 64 * 1024)
            throw new InvalidDataException();

        using var json = await JsonDocument.ParseAsync(file, cancellationToken: token).ConfigureAwait(false);
        if (!json.RootElement.TryGetProperty("installed", out var installed))
            throw new InvalidDataException();

        var clientId = installed.GetProperty("client_id").GetString();
        var clientSecret = installed.GetProperty("client_secret").GetString();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidDataException();

        var requested = allowWrite ? new[] { "openid", WriteScope, CalendarListScope } : ["openid", Scope];
        var session = new Session(clientId, clientSecret, null, RequestedScopes: requested);
        await AuthorizeAndStoreAsync(session, requested, allowWrite ? [WriteScope] : [], expectedAccountId, true, token).ConfigureAwait(false);
    }

    internal async Task RequestCalendarManagementAsync(string clientId, string accountId, CancellationToken token, bool forceConsent = false)
    {
        var session = await LoadSessionAsync(token).ConfigureAwait(false);
        if (session?.Token is null || session.ClientId != clientId || session.AccountId != accountId)
            throw new UberDashboard.Application.Synchronization.SyncAuthenticationException();
        if (!forceConsent && HasScopes(session.GrantedScopes ?? [], CalendarManagementScopes)) return;
        var requested = (session.GrantedScopes ?? []).Concat(CalendarManagementScopes).Append("openid").Distinct(StringComparer.Ordinal).ToArray();
        await AuthorizeAndStoreAsync(session with { Token = null, RequestedScopes = requested }, requested,
            CalendarManagementScopes.Concat(session.GrantedScopes ?? []).ToArray(), accountId, false, token).ConfigureAwait(false);
    }

    private async Task AuthorizeAndStoreAsync(Session session, IReadOnlyList<string> requested, IReadOnlyList<string> required,
        string? expectedAccountId, bool allowReplacement, CancellationToken token)
    {
        var result = authorization is null ? await AuthorizeGoogleAsync(session, requested, token).ConfigureAwait(false)
            : await authorization(requested, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(result.AccountId) || expectedAccountId is not null && result.AccountId != expectedAccountId)
            throw new InvalidOperationException(UiText.Get("SyncReconnectRequired"));
        var granted = ParseScopes(result.Token.Scope);
        if (!HasScopes(granted, required)) throw new InvalidOperationException(UiText.Get("WriteConsentRequired"));
        var verified = session with { AccountId = result.AccountId, GrantedScopes = granted, Token = result.Token };
        await new TokenStore(verified, storage, allowAccountReplacement: allowReplacement,
            requiredPreviousScopes: allowReplacement ? null : session.GrantedScopes ?? []).StoreAsync(User, result.Token).ConfigureAwait(false);
    }

    private async Task<GoogleAuthorization> AuthorizeGoogleAsync(Session session, IReadOnlyList<string> requested, CancellationToken token)
    {
        // Do not replace the existing account/session if additional consent fails.
        var store = new TokenStore(session, storage, persist: false);
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(Initializer(session, store, interactive: true),
            requested, User, true, token, store).ConfigureAwait(false);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                await credential.GetAccessTokenForRequestAsync(cancellationToken: token).ConfigureAwait(false));
            using var response = await (http ?? DefaultHttp).SendAsync(request, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var identity = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), cancellationToken: token).ConfigureAwait(false);
            var subject = identity.RootElement.GetProperty("sub").GetString();
            if (string.IsNullOrWhiteSpace(subject)) throw new InvalidDataException();
            return new(subject, credential.Token);
        }
        finally { credential.Flow.Dispose(); }
    }

    internal async Task<GoogleConnection?> GetConnectionAsync(CancellationToken token)
    {
        var session = await LoadSessionAsync(token).ConfigureAwait(false);
        return session?.AccountId is { Length: > 0 } subject ? new(session.ClientId, subject, session.GrantedScopes ?? []) : null;
    }

    internal async Task<string?> GetAccessTokenAsync(string clientId, string accountId, bool write, CancellationToken token, IReadOnlyList<string>? requiredScopes = null)
    {
        var required = requiredScopes ?? (write ? [WriteScope] : Array.Empty<string>());
        var session = await LoadSessionAsync(token).ConfigureAwait(false);
        if (session?.Token is null || session.ClientId != clientId || session.AccountId != accountId ||
            !HasScopes(session.GrantedScopes ?? [], required)) return null;
        using var flow = new GoogleAuthorizationCodeFlow(Initializer(session, new TokenStore(session, storage)));
        var credential = new UserCredential(flow, User, session.Token);
        var access = await credential.GetAccessTokenForRequestAsync(cancellationToken: token).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(credential.Token.Scope) && !HasScopes(ParseScopes(credential.Token.Scope), required)) return null;
        return access;
    }

    private async Task<Session?> LoadSessionAsync(CancellationToken token)
    {
        var data = await ReadSessionDataAsync(storage, token).ConfigureAwait(false);
        return data is null ? null : NewtonsoftJsonSerializer.Instance.Deserialize<Session>(System.Text.Encoding.UTF8.GetString(
            ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser)));
    }

    private static string[] ParseScopes(string? value) => value?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
    private static bool HasScopes(IEnumerable<string> granted, IEnumerable<string> required)
    {
        var scopes = granted.ToHashSet(StringComparer.Ordinal);
        return required.All(s => scopes.Contains(s) || s.EndsWith(".readonly", StringComparison.Ordinal) && scopes.Contains(s[..^9]) ||
            s.StartsWith("https://www.googleapis.com/auth/calendar.", StringComparison.Ordinal) && scopes.Contains("https://www.googleapis.com/auth/calendar"));
    }

    private static async Task<byte[]?> ReadSessionDataAsync(IPanelStorage storage, CancellationToken token)
    {
        await CredentialGate.WaitAsync(token).ConfigureAwait(false);
        try { return await storage.ReadAsync(SessionFile, token).ConfigureAwait(false); }
        finally { CredentialGate.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken token)
    {
        await CredentialGate.WaitAsync(token).ConfigureAwait(false);
        try { await storage.DeleteAsync(SessionFile, token).ConfigureAwait(false); }
        finally { CredentialGate.Release(); }
    }

    public async Task<IReadOnlyList<CalendarEntry>?> ReadAsync(string calendarId, CalendarPeriod period, CancellationToken token)
    {
        var session = await LoadSessionAsync(token).ConfigureAwait(false);
        if (session is null) return null;
        if (session?.Token is null)
            throw new InvalidDataException();
        return await ReadEventsAsync(session, calendarId, period, token).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CalendarEntry>> ReadEventsAsync(Session session, string calendarId, CalendarPeriod period, CancellationToken token)
    {
        using var flow = new GoogleAuthorizationCodeFlow(Initializer(session, new TokenStore(session, storage)));
        var credential = new UserCredential(flow, User, session.Token);
        var items = new List<CalendarEntry>();
        string? page = null;
        var pages = new HashSet<string>(StringComparer.Ordinal);

        // Padded for calendars in other time zones. The view filters the returned local dates.
        var start = FormatBoundary(period.Start.AddDays(-2));
        var end = FormatBoundary(period.End.AddDays(2));
        var uri = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendarId)}/events" +
            $"?singleEvents=true&orderBy=startTime&maxResults=250&timeMin={start}&timeMax={end}" +
            "&fields=nextPageToken,items(id,summary,start,end,recurringEventId,originalStartTime)";
        do
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, page is null ? uri : $"{uri}&pageToken={Uri.EscapeDataString(page)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                await credential.GetAccessTokenForRequestAsync(cancellationToken: token).ConfigureAwait(false));
            using var response = await (http ?? DefaultHttp).SendAsync(request, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<EventPage>(JsonOptions, token).ConfigureAwait(false)
                ?? throw new InvalidDataException();
            foreach (var item in result.Items ?? [])
            {
                if (ToEntry(item.Summary, item.Start?.Date, item.End?.Date, item.Start?.DateTime, item.End?.DateTime) is { } entry)
                    items.Add(entry with { RemoteId = item.Id, RecurringEventId = item.RecurringEventId,
                        OriginalStart = item.OriginalStartTime?.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            ?? item.OriginalStartTime?.DateTime?.ToString("O", CultureInfo.InvariantCulture) });
            }
            page = result.NextPageToken;
            if (!string.IsNullOrEmpty(page) && (!pages.Add(page) || pages.Count > 10000)) throw new InvalidDataException("Invalid calendar pagination.");
        } while (!string.IsNullOrEmpty(page));

        return items;
    }

    internal static CalendarEntry? ToEntry(string? summary, DateOnly? startDate, DateOnly? endDate,
        DateTimeOffset? startTime, DateTimeOffset? endTime)
    {
        var title = string.IsNullOrWhiteSpace(summary) ? UiText.Get("UntitledEvent") : summary;
        if (startDate is { } allDay)
            return new(title, allDay, endDate > allDay ? endDate.Value : allDay.AddDays(1), null);
        if (startTime is not { } start)
            return null;

        var first = DateOnly.FromDateTime(start.LocalDateTime);
        var last = endTime > start ? DateOnly.FromDateTime(endTime.Value.AddTicks(-1).LocalDateTime) : first;
        return new(title, first, last.AddDays(1), TimeOnly.FromDateTime(start.LocalDateTime));
    }

    private static string FormatBoundary(DateOnly day) =>
        Uri.EscapeDataString(new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local))
            .ToString("O", CultureInfo.InvariantCulture));

    private static GoogleAuthorizationCodeFlow.Initializer Initializer(Session session, IDataStore store, bool interactive = false) => new()
    {
        ClientSecrets = new ClientSecrets { ClientId = session.ClientId, ClientSecret = session.ClientSecret },
        Scopes = session.RequestedScopes ?? [Scope],
        DataStore = store,
        IncludeGrantedScopes = true,
        Prompt = interactive ? "consent select_account" : null,
    };

    private sealed record EventPage(string? NextPageToken, GoogleEvent[]? Items);
    private sealed record GoogleEvent(string? Summary, EventTime? Start, EventTime? End, string? Id = null,
        string? RecurringEventId = null, EventTime? OriginalStartTime = null);
    private sealed record EventTime(DateOnly? Date, DateTimeOffset? DateTime);
    private sealed record Session(string ClientId, string ClientSecret, TokenResponse? Token, string? AccountId = null,
        string[]? GrantedScopes = null, string[]? RequestedScopes = null);

    internal sealed record GoogleConnection(string ClientId, string AccountId, string[] GrantedScopes);
    internal sealed record GoogleAuthorization(string AccountId, TokenResponse Token);

    private sealed class TokenStore(Session session, IPanelStorage storage, bool persist = true, bool allowAccountReplacement = false,
        IReadOnlyList<string>? requiredPreviousScopes = null) : IDataStore
    {
        private TokenResponse? _currentToken = session.Token;
        public async Task StoreAsync<T>(string key, T value)
        {
            if (value is not TokenResponse token)
                throw new InvalidOperationException();
            _currentToken = token;
            if (!persist) return;
            var granted = string.IsNullOrWhiteSpace(token.Scope) ? session.GrantedScopes : ParseScopes(token.Scope);
            var data = System.Text.Encoding.UTF8.GetBytes(NewtonsoftJsonSerializer.Instance.Serialize(session with { Token = token, GrantedScopes = granted }));
            await CredentialGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!allowAccountReplacement)
                {
                    var currentData = await storage.ReadAsync(SessionFile, CancellationToken.None).ConfigureAwait(false);
                    var current = currentData is null ? null : NewtonsoftJsonSerializer.Instance.Deserialize<Session>(System.Text.Encoding.UTF8.GetString(
                        ProtectedData.Unprotect(currentData, null, DataProtectionScope.CurrentUser)));
                    if (current is null || current.ClientId != session.ClientId || current.AccountId != session.AccountId)
                        throw new UberDashboard.Application.Synchronization.SyncAuthenticationException();
                    if (requiredPreviousScopes is not null && !(current.GrantedScopes ?? []).ToHashSet(StringComparer.Ordinal).SetEquals(requiredPreviousScopes))
                        throw new UberDashboard.Application.Synchronization.SyncAuthenticationException();
                }
                await storage.WriteAsync(SessionFile, ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally { CredentialGate.Release(); }
        }

        public Task<T> GetAsync<T>(string key) => Task.FromResult(_currentToken is T token ? token : default!);
        public Task DeleteAsync<T>(string key) => ClearAsync();
        public async Task ClearAsync()
        {
            if (!persist) return;
            await CredentialGate.WaitAsync().ConfigureAwait(false);
            try { await storage.DeleteAsync(SessionFile, CancellationToken.None).ConfigureAwait(false); }
            finally { CredentialGate.Release(); }
        }
    }
}
