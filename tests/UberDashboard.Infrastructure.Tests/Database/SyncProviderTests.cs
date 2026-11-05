using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;
using UberDashboard.Infrastructure.Integrations;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class SyncProviderTests
{
    [Fact]
    public async Task Acknowledged_create_stays_successful_when_the_followup_read_fails()
    {
        using var transport = new Transport((request, _) => request.Method == HttpMethod.Post
            ? Json("""{"id":"created","title":"new","status":"notStarted"}""")
            : throw new HttpRequestException("follow-up interrupted"));
        using var http = new HttpClient(transport);
        var result = await new MicrosoftTodoProvider(http, new Accounts()).WriteAsync(Binding(SyncProviderKind.MicrosoftTodo),
            new(Guid.NewGuid(), Guid.NewGuid(), 1, SyncOperationKind.Create, new JsonObject { ["title"] = "new" }, null, null), default);
        Assert.Equal(SyncWriteStatus.Success, result.Status);
        Assert.Equal("created", result.Entity!.Id);
    }

    [Fact]
    public async Task Graph_follows_delta_and_keeps_non_boolean_task_status()
    {
        using var transport = new Transport((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/task", StringComparison.Ordinal))
                return Json("""{"id":"task","title":"a","status":"waitingOnOthers","lastModifiedDateTime":"2026-11-05T00:00:00Z"}""");
            Assert.Equal("https://graph.microsoft.com/v1.0/me/todo/lists/a/tasks/delta", request.RequestUri!.AbsoluteUri);
            return Json("""{"value":[{"id":"task","title":"a","status":"waitingOnOthers","lastModifiedDateTime":"2026-11-05T00:00:00Z"}],"@odata.deltaLink":"https://graph.microsoft.com/v1.0/me/todo/lists/a/tasks/delta?$deltatoken=x"}""");
        });
        using var http = new HttpClient(transport);
        var page = await new MicrosoftTodoProvider(http, new Accounts()).ReadPageAsync(Binding(SyncProviderKind.MicrosoftTodo), null, null, default);
        Assert.Equal("waitingOnOthers", Assert.Single(page.Items).Fields!["status"]!.GetValue<string>());
        Assert.False(page.Items[0].Fields!.ContainsKey("lastModifiedDateTime"));
        Assert.NotNull(page.Cursor);
    }

    [Fact]
    public async Task Continuation_cannot_forward_credentials_to_another_origin()
    {
        using var transport = new Transport((_, _) => throw new InvalidOperationException("HTTP must not be called."));
        using var http = new HttpClient(transport);
        await Assert.ThrowsAsync<InvalidDataException>(() => new MicrosoftTodoProvider(http, new Accounts())
            .ReadPageAsync(Binding(SyncProviderKind.MicrosoftTodo), null, "https://example.invalid/steal", default));
    }

    [Fact]
    public async Task Google_incremental_query_does_not_use_display_range_or_sort_order()
    {
        using var transport = new Transport((request, _) =>
        {
            var uri = request.RequestUri!.AbsoluteUri;
            Assert.Contains("syncToken=old", uri);
            Assert.Contains("pageToken=page", uri);
            Assert.Contains("singleEvents=false", uri);
            Assert.DoesNotContain("timeMin", uri);
            Assert.DoesNotContain("orderBy", uri);
            return Json("""{"items":[{"id":"deleted","status":"cancelled"}],"nextSyncToken":"next"}""");
        });
        using var http = new HttpClient(transport);
        var page = await new GoogleCalendarProvider(http, new Accounts()).ReadPageAsync(Binding(SyncProviderKind.GoogleCalendar), "old", "page", default);
        Assert.Null(Assert.Single(page.Items).Fields);
        Assert.Equal("next", page.Cursor);
        Assert.False(page.IsFullSnapshot);
    }

    [Fact]
    public async Task Google_updates_only_dirty_fields_and_uses_current_etag()
    {
        using var transport = new Transport((request, body) =>
        {
            if (request.Method == HttpMethod.Get)
                return Json("""{"id":"event","etag":"\"current\"","summary":"old","description":"remote","start":{"date":"2026-11-05"},"end":{"date":"2026-11-06"}}""");
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("\"current\"", request.Headers.GetValues("If-Match").Single());
            Assert.Equal("""{"summary":"local"}""", body);
            return new(HttpStatusCode.PreconditionFailed) { Content = new StringContent("{}") };
        });
        using var http = new HttpClient(transport);
        var baseline = JsonNode.Parse("""{"summary":"old","description":"old","start":{"date":"2026-11-05"},"end":{"date":"2026-11-06"}}""")!.AsObject();
        var desired = (JsonObject)baseline.DeepClone(); desired["summary"] = "local";
        var result = await new GoogleCalendarProvider(http, new Accounts()).WriteAsync(Binding(SyncProviderKind.GoogleCalendar),
            new(Guid.NewGuid(), Guid.NewGuid(), 1, SyncOperationKind.Update, desired, "event", "old-etag", Baseline: baseline), default);
        Assert.Equal(SyncWriteStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task Invitation_is_not_sent_without_review_approval()
    {
        using var transport = new Transport((_, _) => throw new InvalidOperationException("Must not send."));
        using var http = new HttpClient(transport);
        var fields = JsonNode.Parse("""{"summary":"a","attendees":[{"email":"test@example.invalid"}]}""")!.AsObject();
        var result = await new GoogleCalendarProvider(http, new Accounts()).WriteAsync(Binding(SyncProviderKind.GoogleCalendar),
            new(Guid.NewGuid(), Guid.NewGuid(), 1, SyncOperationKind.Create, fields, "abcde", null), default);
        Assert.Equal("approval-required", result.ErrorCode);
    }

    [Theory]
    [InlineData(429, SyncWriteStatus.Retry)]
    [InlineData(403, SyncWriteStatus.Forbidden)]
    [InlineData(401, SyncWriteStatus.AuthenticationRequired)]
    [InlineData(500, SyncWriteStatus.UnknownOutcome)]
    public async Task Write_errors_keep_their_meaning(int code, SyncWriteStatus expected)
    {
        using var transport = new Transport((_, _) => new((HttpStatusCode)code) { Content = new StringContent("{}") });
        using var http = new HttpClient(transport);
        var result = await new MicrosoftTodoProvider(http, new Accounts()).WriteAsync(Binding(SyncProviderKind.MicrosoftTodo),
            new(Guid.NewGuid(), Guid.NewGuid(), 1, SyncOperationKind.Create, new JsonObject { ["title"] = "new" }, null, null), default);
        Assert.Equal(expected, result.Status);
    }

    private static SyncBinding Binding(SyncProviderKind kind) => new(Guid.NewGuid(), kind, "client", "account",
        kind == SyncProviderKind.MicrosoftTodo ? "lists/a/tasks" : "calendars/primary/events", "Test", true);
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Accounts : IIntegrationAccountService
    {
        public Task<string?> GetAccessTokenAsync(SyncBinding binding, bool write, CancellationToken token) => Task.FromResult<string?>("test-token");
    }
    private sealed class Transport(Func<HttpRequestMessage, string?, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("Bearer test-token", request.Headers.Authorization!.ToString());
            return handler(request, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
        }
    }
}
