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

internal sealed class GoogleCalendarClient(IPanelStorage storage, HttpClient? http = null)
{
    private const string SessionFile = "google-calendar.session";
    private const string User = "calendar";
    private const string Scope = "https://www.googleapis.com/auth/calendar.events.readonly";
    private static readonly HttpClient DefaultHttp = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ConnectAsync(string path, CancellationToken token)
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

        var session = new Session(clientId, clientSecret, null);
        var store = new TokenStore(session, storage);
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(Initializer(session, store, interactive: true),
            [Scope], User, true, token, store).ConfigureAwait(false);
        credential.Flow.Dispose();
    }

    public async Task DisconnectAsync(CancellationToken token) =>
        await storage.DeleteAsync(SessionFile, token).ConfigureAwait(false);

    public async Task<IReadOnlyList<CalendarEntry>?> ReadAsync(string calendarId, CalendarPeriod period, CancellationToken token)
    {
        var data = await storage.ReadAsync(SessionFile, token).ConfigureAwait(false);
        if (data is null)
            return null;

        var session = NewtonsoftJsonSerializer.Instance.Deserialize<Session>(System.Text.Encoding.UTF8.GetString(
            ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser)));
        if (session?.Token is null)
            throw new InvalidDataException();

        using var flow = new GoogleAuthorizationCodeFlow(Initializer(session, new TokenStore(session, storage)));
        var credential = new UserCredential(flow, User, session.Token);
        var items = new List<CalendarEntry>();
        string? page = null;

        // Padded for calendars in other time zones. The view filters the returned local dates.
        var start = FormatBoundary(period.Start.AddDays(-2));
        var end = FormatBoundary(period.End.AddDays(2));
        var uri = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendarId)}/events" +
            $"?singleEvents=true&orderBy=startTime&maxResults=250&timeMin={start}&timeMax={end}" +
            "&fields=nextPageToken,items(summary,start,end)";
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
                    items.Add(entry);
            }
            page = result.NextPageToken;
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
        Scopes = [Scope],
        DataStore = store,
        Prompt = interactive ? "consent select_account" : null,
    };

    private sealed record EventPage(string? NextPageToken, GoogleEvent[]? Items);
    private sealed record GoogleEvent(string? Summary, EventTime? Start, EventTime? End);
    private sealed record EventTime(DateOnly? Date, DateTimeOffset? DateTime);
    private sealed record Session(string ClientId, string ClientSecret, TokenResponse? Token);

    private sealed class TokenStore(Session session, IPanelStorage storage) : IDataStore
    {
        public async Task StoreAsync<T>(string key, T value)
        {
            if (value is not TokenResponse token)
                throw new InvalidOperationException();
            var data = System.Text.Encoding.UTF8.GetBytes(NewtonsoftJsonSerializer.Instance.Serialize(session with { Token = token }));
            await storage.WriteAsync(SessionFile, ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser),
                CancellationToken.None).ConfigureAwait(false);
        }

        public Task<T> GetAsync<T>(string key) => Task.FromResult(session.Token is T token ? token : default!);
        public Task DeleteAsync<T>(string key) => ClearAsync();
        public Task ClearAsync() => storage.DeleteAsync(SessionFile, CancellationToken.None).AsTask();
    }
}
