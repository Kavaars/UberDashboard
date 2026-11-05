using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using UberDashboard.App.Features.Calendar;
using UberDashboard.Contracts.Host;

namespace UberDashboard.App.Tests.Features;

public sealed class GoogleCalendarClientTests
{
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

    private sealed class SessionStorage(byte[] encrypted) : IPanelStorage
    {
        public ValueTask<byte[]?> ReadAsync(string fileName, CancellationToken cancellationToken) =>
            ValueTask.FromResult<byte[]?>(encrypted);

        public ValueTask WriteAsync(string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The test session should not require a token refresh.");

        public ValueTask DeleteAsync(string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public IReadOnlyList<string> ListFiles() => [];
    }
}
