using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;
using UberDashboard.Infrastructure.Integrations;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class SyncProviderTests
{
    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Read_failures_preserve_retry_after_instead_of_becoming_an_uncertain_write(int code)
    {
        using var transport = new Transport((_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent("{}") };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90)); return response;
        }); using var http = new HttpClient(transport);
        var failure = await Assert.ThrowsAsync<SyncReadException>(() => new MicrosoftTodoProvider(http, new Accounts())
            .ReadPageAsync(Binding(SyncProviderKind.MicrosoftTodo), null, null, default));
        Assert.Equal(TimeSpan.FromSeconds(90), failure.RetryAfter);
    }

    [Fact]
    public async Task Related_collections_follow_pages_and_keep_their_own_ids_and_timestamp_fields()
    {
        var parent = new SyncEntity(Guid.NewGuid(), "task", null, null);
        var binding = Binding(SyncProviderKind.MicrosoftTodo);
        using var transport = new Transport((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/attachments", StringComparison.Ordinal))
            {
                Assert.Contains("$select=", request.RequestUri.Query, StringComparison.Ordinal);
                return Json("""{"value":[{"id":"file","name":"File","size":9,"contentBytes":"must-not-enter-state"}]}""");
            }
            if (path.EndsWith("/linkedResources", StringComparison.Ordinal))
                return Json("""{"value":[{"id":"same","applicationName":"Source","displayName":"Link","webUrl":"custom:source"}]}""");
            Assert.EndsWith("/tasks/task/checklistItems", path, StringComparison.Ordinal);
            return request.RequestUri.Query.Length == 0
                ? Json("""{"value":[{"id":"first","displayName":"Step","isChecked":true,"checkedDateTime":"2030-01-01T12:00:00Z"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/me/todo/lists/a/tasks/task/checklistItems?$skiptoken=x"}""")
                : Json("""{"value":[{"id":"second","displayName":"Next","createdDateTime":"2030-01-01T11:00:00Z"}]}""");
        });
        using var http = new HttpClient(transport);
        var collections = await new MicrosoftTodoProvider(http, new Accounts()).ReadRelationsAsync(binding, [parent], default);
        Assert.Equal(3, collections.Count); Assert.Equal(2, collections[0].Items.Count);
        Assert.Equal(SyncEntityKind.FileAttachment, collections[2].Kind); Assert.False(collections[2].Items[0].Metadata!.ContainsKey("contentBytes"));
        Assert.Equal("2030-01-01T12:00:00Z", collections[0].Items[0].Fields!["checkedDateTime"]!.GetValue<string>());
        Assert.Equal("custom:source", Assert.Single(collections[1].Items).Fields!["webUrl"]!.GetValue<string>());
        Assert.All(collections, c => Assert.Equal(parent.LocalId, c.ParentId));
    }

    [Fact]
    public async Task Related_continuation_cannot_switch_parent_collection_or_forward_credentials()
    {
        using var transport = new Transport((_, _) => throw new InvalidOperationException("No HTTP expected.")); using var http = new HttpClient(transport);
        var scope = Binding(SyncProviderKind.MicrosoftTodo) with { ResourcePath = "lists/a/tasks/task/checklistItems" };
        await Assert.ThrowsAsync<InvalidDataException>(() => new MicrosoftTodoProvider(http, new Accounts()).ReadPageAsync(scope, null,
            "https://graph.microsoft.com/v1.0/me/todo/lists/a/tasks/other/checklistItems?$skiptoken=x", default));
    }

    [Theory]
    [InlineData("checklistItems", """{"displayName":"Step","isChecked":true,"createdDateTime":"2030-01-01T12:00:00Z"}""")]
    [InlineData("linkedResources", """{"displayName":"Link","webUrl":"https://example.invalid","applicationName":"Source","externalId":"ticket-1"}""")]
    public async Task Related_creates_use_the_collection_endpoint_and_preserve_server_id(string collection, string payload)
    {
        using var transport = new Transport((request, body) =>
        {
            if (request.Method == HttpMethod.Post) { Assert.EndsWith('/' + collection, request.RequestUri!.AbsolutePath, StringComparison.Ordinal); Assert.True(JsonNode.DeepEquals(JsonNode.Parse(payload), JsonNode.Parse(body!))); }
            var fields = JsonNode.Parse(payload)!.AsObject(); fields["id"] = "server-id"; return Json(fields.ToJsonString());
        }); using var http = new HttpClient(transport);
        var scope = Binding(SyncProviderKind.MicrosoftTodo) with { ResourcePath = "lists/a/tasks/task/" + collection };
        var result = await new MicrosoftTodoProvider(http, new Accounts()).WriteAsync(scope,
            new(Guid.NewGuid(), Guid.NewGuid(), 1, SyncOperationKind.Create, JsonNode.Parse(payload)!.AsObject(), null, null), default);
        Assert.Equal(SyncWriteStatus.Success, result.Status); Assert.Equal("server-id", result.Entity!.Id);
    }

    [Fact]
    public async Task Graph_extended_task_edit_sends_only_dirty_fields_and_preserves_server_status()
    {
        var baseline = JsonNode.Parse("""{"title":"Task","status":"waitingOnOthers","importance":"normal","categories":["Old"],"dueDateTime":{"dateTime":"2030-05-12T00:00:00","timeZone":"UTC"},"body":{"contentType":"html","content":"<b>Keep</b>"}}""")!.AsObject();
        var draft = MicrosoftTaskDraft.Read(baseline) with { Importance = "high", Categories = "Work", Due = new(true, "2030-05-13", "09:00", "Europe/Warsaw") };
        Assert.True(draft.TryApply(baseline, out var desired, out _));
        var server = (JsonObject)baseline.DeepClone(); server["status"] = "deferred";
        JsonObject? patch = null;
        using var transport = new Transport((request, body) =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                patch = JsonNode.Parse(body!)!.AsObject();
                Assert.Equal(new[] { "categories", "dueDateTime", "importance" }, patch.Select(p => p.Key).Order(StringComparer.Ordinal));
                foreach (var field in patch) server[field.Key] = field.Value?.DeepClone();
            }
            var response = (JsonObject)server.DeepClone(); response["id"] = "task"; return Json(response.ToJsonString());
        });
        using var http = new HttpClient(transport);
        var result = await new MicrosoftTodoProvider(http, new Accounts()).WriteAsync(Binding(SyncProviderKind.MicrosoftTodo),
            new(Guid.NewGuid(), Guid.NewGuid(), 1, SyncOperationKind.Update, desired, "task", null, Baseline: baseline), default);
        Assert.NotNull(patch); Assert.Equal(SyncWriteStatus.Success, result.Status);
        Assert.Equal("deferred", result.Entity!.Fields!["status"]!.GetValue<string>());
        Assert.Equal("Europe/Warsaw", result.Entity.Fields["dueDateTime"]!["timeZone"]!.GetValue<string>());
    }

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
                return Json("""{"id":"event","etag":"\"current\"","organizer":{"self":true},"summary":"old","description":"remote","start":{"date":"2026-11-05"},"end":{"date":"2026-11-06"}}""");
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
