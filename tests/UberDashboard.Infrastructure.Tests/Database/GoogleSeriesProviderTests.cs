using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;
using UberDashboard.Infrastructure.Integrations;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class GoogleSeriesProviderTests
{
    [Fact]
    public async Task Instance_pages_keep_canceled_exception_identity_and_do_not_use_calendar_sync_tokens()
    {
        using var transport = new Handler((request, _) =>
        {
            Assert.EndsWith("/events/master/instances", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("showDeleted=true", request.RequestUri.Query, StringComparison.Ordinal); Assert.Contains("pageToken=page", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.DoesNotContain("syncToken", request.RequestUri.Query, StringComparison.Ordinal);
            return Json("""{"items":[{"id":"exception","etag":"e","status":"cancelled","recurringEventId":"master","originalStartTime":{"date":"2030-01-03"}}],"nextPageToken":"next"}""");
        }); using var http = new HttpClient(transport);
        var page = await new GoogleCalendarProvider(http, new Accounts()).ReadInstancesAsync(Binding, "master", From, From.AddMonths(1), "page", default);
        var item = Assert.Single(page.Items); Assert.Equal("cancelled", item.Fields?["status"]?.GetValue<string>()); Assert.Equal("master", item.Metadata?["recurringEventId"]?.GetValue<string>());
        Assert.Equal("2030-01-03", item.Metadata?["originalStartTime"]?["date"]?.GetValue<string>()); Assert.Equal("next", page.NextPage); Assert.Null(page.Cursor);
    }

    [Fact]
    public async Task A_canceled_exception_from_the_root_feed_is_stored_while_a_deleted_single_event_is_a_tombstone()
    {
        using var transport = new Handler((_, _) => Json("""{"items":[{"id":"instance","status":"cancelled","recurringEventId":"master","originalStartTime":{"date":"2030-01-03"}},{"id":"single","status":"cancelled"}],"nextSyncToken":"cursor"}"""));
        using var http = new HttpClient(transport); var page = await new GoogleCalendarProvider(http, new Accounts()).ReadPageAsync(Binding, null, null, default);
        Assert.NotNull(page.Items[0].Fields); Assert.Null(page.Items[1].Fields); Assert.Equal("cursor", page.Cursor);
    }

    [Fact]
    public async Task Restoring_a_canceled_instance_checks_organizer_of_the_master_and_uses_IfMatch_without_sending_identity_metadata()
    {
        var writes = 0; var saved = false;
        var master = JsonNode.Parse("""{"id":"master","etag":"m","summary":"Series","organizer":{"self":true},"start":{"date":"2030-01-01"},"end":{"date":"2030-01-02"},"recurrence":["RRULE:FREQ=DAILY;COUNT=5"]}""")!.AsObject();
        var canceled = JsonNode.Parse("""{"id":"instance","etag":"c","status":"cancelled","recurringEventId":"master","originalStartTime":{"date":"2030-01-03"}}""")!.AsObject();
        var desired = JsonNode.Parse("""{"summary":"Restored","status":"confirmed","start":{"date":"2030-01-03"},"end":{"date":"2030-01-04"}}""")!.AsObject();
        using var transport = new Handler((request, body) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/master", StringComparison.Ordinal)) return Json(master.ToJsonString());
            if (request.Method == HttpMethod.Patch)
            {
                writes++; Assert.Equal("c", request.Headers.GetValues("If-Match").Single()); Assert.Contains("sendUpdates=all", request.RequestUri.Query, StringComparison.Ordinal);
                var patch = JsonNode.Parse(body!)!.AsObject(); Assert.False(patch.ContainsKey("recurringEventId")); Assert.False(patch.ContainsKey("originalStartTime")); Assert.Equal("confirmed", patch["status"]?.GetValue<string>()); saved = true;
            }
            if (!saved) return Json(canceled.ToJsonString());
            var response = (JsonObject)desired.DeepClone(); response["id"] = "instance"; response["etag"] = "saved"; response["recurringEventId"] = "master"; response["originalStartTime"] = new JsonObject { ["date"] = "2030-01-03" }; response["organizer"] = new JsonObject { ["self"] = true }; return Json(response.ToJsonString());
        }); using var http = new HttpClient(transport);
        var result = await new GoogleCalendarProvider(http, new Accounts()).WriteAsync(Binding, new(Guid.NewGuid(), Guid.NewGuid(), 1, SyncOperationKind.Update, desired, "instance", "c",
            ApprovalHash: SyncEdits.Hash(desired), Baseline: new JsonObject { ["status"] = "cancelled" }), default);
        Assert.Equal(SyncWriteStatus.Success, result.Status); Assert.Equal(1, writes); Assert.Equal("instance", result.Entity?.Id);
    }

    [Fact]
    public async Task Unapproved_occurrence_edit_and_foreign_instances_never_write()
    {
        var writes = 0; using var transport = new Handler((request, _) =>
        { if (request.Method != HttpMethod.Get) writes++; return Json("""{"id":"instance","etag":"e","summary":"Before","recurringEventId":"other","originalStartTime":{"date":"2030-01-03"},"organizer":{"self":true}}"""); }); using var http = new HttpClient(transport);
        var provider = new GoogleCalendarProvider(http, new Accounts()); var fields = new JsonObject { ["summary"] = "After" };
        var result = await provider.WriteAsync(Binding, new(Guid.NewGuid(), Guid.NewGuid(), 1, SyncOperationKind.Update, fields, "instance", "e", Baseline: new JsonObject { ["summary"] = "Before" }), default);
        Assert.Equal(SyncWriteStatus.Failed, result.Status); Assert.Equal("occurrence-approval-required", result.ErrorCode); Assert.Equal(0, writes);
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.ReadInstancesAsync(Binding, "master", From, From.AddMonths(1), null, default)); Assert.Equal(0, writes);
    }

    [Fact]
    public async Task Whole_series_patch_is_minimal_versioned_and_attachment_output_only_fields_are_removed()
    {
        var fields = JsonNode.Parse("""{"summary":"Name","start":{"date":"2030-01-01"},"end":{"date":"2030-01-02"},"recurrence":["RRULE:FREQ=DAILY;COUNT=4"],"attachments":[{"fileId":"old","fileUrl":"https://drive.google.com/file/d/old/view","title":"Old","iconLink":"https://example.invalid"}]}""")!.AsObject();
        var desired = (JsonObject)fields.DeepClone(); desired["attachments"]!.AsArray().Add(new JsonObject { ["fileId"] = "new", ["fileUrl"] = "https://drive.google.com/file/d/new/view", ["title"] = "New", ["mimeType"] = "text/plain" });
        var saved = false; using var transport = new Handler((request, body) =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                Assert.Contains("supportsAttachments=true", request.RequestUri!.Query, StringComparison.Ordinal); Assert.Contains("sendUpdates=all", request.RequestUri.Query, StringComparison.Ordinal);
                Assert.Equal("e", request.Headers.GetValues("If-Match").Single()); var patch = JsonNode.Parse(body!)!.AsObject(); Assert.Single(patch); Assert.True(patch.ContainsKey("attachments"));
                Assert.All(patch["attachments"]!.AsArray().OfType<JsonObject>(), attachment => { Assert.False(attachment.ContainsKey("fileId")); Assert.False(attachment.ContainsKey("iconLink")); }); saved = true;
            }
            var value = (JsonObject)(saved ? desired : fields).DeepClone(); value["id"] = "master"; value["etag"] = saved ? "saved" : "e"; value["organizer"] = new JsonObject { ["self"] = true }; return Json(value.ToJsonString());
        }); using var http = new HttpClient(transport);
        var result = await new GoogleCalendarProvider(http, new Accounts()).WriteAsync(Binding, new(Guid.NewGuid(), Guid.NewGuid(), 1, SyncOperationKind.Update, desired, "master", "e", ApprovalHash: SyncEdits.Hash(desired), Baseline: fields), default);
        Assert.Equal(SyncWriteStatus.Success, result.Status); Assert.True(saved);
    }

    private static readonly DateTimeOffset From = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly SyncBinding Binding = new(Guid.NewGuid(), SyncProviderKind.GoogleCalendar, "client", "account", "calendars/primary/events", "Calendar", true);
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private sealed class Accounts : IIntegrationAccountService { public Task<string?> GetAccessTokenAsync(SyncBinding binding, bool write, CancellationToken token) => Task.FromResult<string?>("test-token"); }
    private sealed class Handler(Func<HttpRequestMessage, string?, HttpResponseMessage> action) : HttpMessageHandler
    { protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => action(request, request.Content is null ? null : await request.Content.ReadAsStringAsync(token)); }
}
