using System.Net;
using System.Text.Json.Nodes;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;
using UberDashboard.Infrastructure.Integrations;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class GoogleAccessProviderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Empty_delta_can_omit_items_and_still_advance_its_token(bool sharing)
    {
        using var handler = new Handler((_, _) => Json("""{"nextSyncToken":"next"}""")); using var http = new HttpClient(handler);
        var page = await new GoogleCalendarProvider(http, new Accounts()).ReadPageAsync(Binding(sharing), "old", null, default);
        Assert.Empty(page.Items); Assert.Equal("next", page.Cursor); Assert.False(page.IsFullSnapshot);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Access_feeds_keep_cursor_parameters_and_parse_deleted_entries(bool sharing)
    {
        using var handler = new Handler((request, _) =>
        {
            Assert.Contains("syncToken=old", request.RequestUri!.Query, StringComparison.Ordinal);
            Assert.Contains("pageToken=page", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Contains("showDeleted=true", request.RequestUri.Query, StringComparison.Ordinal);
            if (!sharing) Assert.Contains("showHidden=true", request.RequestUri.Query, StringComparison.Ordinal);
            return Json(sharing ? """{"items":[{"id":"deleted","role":"none"}],"nextSyncToken":"next"}""" : """{"items":[{"id":"deleted","deleted":true}],"nextSyncToken":"next"}""");
        }); using var http = new HttpClient(handler);
        var page = await new GoogleCalendarProvider(http, new Accounts()).ReadPageAsync(Binding(sharing), "old", "page", default);
        Assert.Null(Assert.Single(page.Items).Fields); Assert.Equal("next", page.Cursor); Assert.False(page.IsFullSnapshot);
    }
    [Fact]
    public async Task Readonly_metadata_uses_its_own_full_pages_without_changing_the_incremental_cursor()
    {
        using var handler = new Handler((request, _) =>
        {
            Assert.DoesNotContain("syncToken", request.RequestUri!.Query, StringComparison.Ordinal);
            return Json(request.RequestUri.Query.Contains("pageToken=next", StringComparison.Ordinal)
                ? """{"items":[{"id":"two@example.invalid","summary":"Renamed","accessRole":"reader","dataOwner":"other@example.invalid"}]}"""
                : """{"items":[{"id":"one@example.invalid","summary":"First","accessRole":"owner","dataOwner":"me@example.invalid"}],"nextPageToken":"next"}""");
        }); using var http = new HttpClient(handler);
        var metadata = (await new GoogleCalendarProvider(http, new Accounts()).ReadMetadataAsync(Binding(false), default))!;
        Assert.Equal(2, metadata.Items!.Count); Assert.True(metadata.Items["one@example.invalid"]["isDataOwner"]!.GetValue<bool>());
        Assert.Equal("Renamed", metadata.Items["two@example.invalid"]["summary"]!.GetValue<string>());
    }
    [Theory]
    [InlineData("me@example.invalid")]
    [InlineData("owner@example.invalid")]
    public async Task Protected_sharing_rules_do_not_send_even_after_approval(string target)
    {
        var writes = 0;
        using var handler = new Handler((request, _) =>
        {
            if (request.Method != HttpMethod.Get) writes++;
            return Json("""{"id":"calendar@example.invalid","accessRole":"owner","dataOwner":"owner@example.invalid"}""");
        }); using var http = new HttpClient(handler);
        var fields = new GoogleSharingDraft("user", target, "reader").Fields();
        var result = await new GoogleCalendarProvider(http, new Accounts()).WriteAsync(Binding(true), Create(fields), default);
        Assert.Equal(SyncWriteStatus.Forbidden, result.Status); Assert.Equal(0, writes);
    }
    [Fact]
    public async Task Sharing_update_sends_only_the_role_and_explicit_notifications_with_if_match()
    {
        var baseline = new GoogleSharingDraft("user", "guest@example.invalid", "reader").Fields();
        var desired = new GoogleSharingDraft("user", "guest@example.invalid", "writer").Fields(); var writes = 0;
        using var handler = new Handler((request, body) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("calendarList", StringComparison.Ordinal)) return Context();
            if (request.Method == HttpMethod.Patch)
            {
                writes++; Assert.Equal("""{"role":"writer"}""", body); Assert.Equal("?sendNotifications=true", request.RequestUri.Query);
                Assert.Equal("\"current\"", request.Headers.GetValues("If-Match").Single());
            }
            var value = (JsonObject)(writes > 0 ? desired : baseline).DeepClone(); value["id"] = "rule"; value["etag"] = "\"current\""; return Json(value.ToJsonString());
        }); using var http = new HttpClient(handler);
        var operation = new SyncOperation(Guid.NewGuid(), Guid.NewGuid(), 1, SyncOperationKind.Update, desired, "rule", null, ApprovalHash: SyncEdits.Hash(desired), Baseline: baseline);
        Assert.Equal(SyncWriteStatus.Success, (await new GoogleCalendarProvider(http, new Accounts()).WriteAsync(Binding(true), operation, default)).Status); Assert.Equal(1, writes);
    }
    [Fact]
    public async Task Public_create_requires_review_and_does_not_send_a_scope_value_or_fabricated_id()
    {
        var fields = new GoogleSharingDraft("default", "", "reader").Fields(); var writes = 0;
        using var handler = new Handler((request, body) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("calendarList", StringComparison.Ordinal)) return Context();
            if (request.Method == HttpMethod.Post)
            { writes++; Assert.Null(JsonNode.Parse(body!)!["scope"]!["value"]); Assert.Null(JsonNode.Parse(body!)!["id"]); Assert.Contains("sendNotifications=true", request.RequestUri.Query, StringComparison.Ordinal); }
            var value = (JsonObject)fields.DeepClone(); value["id"] = "server-rule"; return Json(value.ToJsonString());
        }); using var http = new HttpClient(handler); var provider = new GoogleCalendarProvider(http, new Accounts());
        Assert.Equal("approval-required", (await provider.WriteAsync(Binding(true), Create(fields) with { ApprovalHash = null }, default)).ErrorCode);
        Assert.Equal(0, writes); Assert.Equal(SyncWriteStatus.Success, (await provider.WriteAsync(Binding(true), Create(fields), default)).Status); Assert.Equal(1, writes);
    }
    [Fact]
    public async Task Subscription_create_and_personal_patch_use_calendar_list_not_global_calendars()
    {
        var baseline = JsonNode.Parse("""{"id":"calendar@example.invalid","summaryOverride":"Old","defaultReminders":[{"method":"popup","minutes":20}]}""")!.AsObject();
        var desired = (JsonObject)baseline.DeepClone(); desired["summaryOverride"] = "Personal"; desired["backgroundColor"] = "#123456"; var writes = 0;
        using var handler = new Handler((request, body) =>
        {
            Assert.Contains("/users/me/calendarList", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            if (request.Method == HttpMethod.Patch)
            {
                writes++; var patch = JsonNode.Parse(body!)!.AsObject(); Assert.Equal(2, patch.Count); Assert.False(patch.ContainsKey("id")); Assert.False(patch.ContainsKey("defaultReminders")); Assert.Equal("?colorRgbFormat=true", request.RequestUri.Query);
            }
            var value = (JsonObject)(writes > 0 ? desired : baseline).DeepClone(); value["etag"] = "\"current\""; value["accessRole"] = "reader"; value["dataOwner"] = "owner@example.invalid"; return Json(value.ToJsonString());
        }); using var http = new HttpClient(handler);
        var operation = new SyncOperation(Guid.NewGuid(), Guid.NewGuid(), 1, SyncOperationKind.Update, desired, "calendar@example.invalid", null, Baseline: baseline);
        Assert.Equal(SyncWriteStatus.Success, (await new GoogleCalendarProvider(http, new Accounts()).WriteAsync(Binding(false), operation, default)).Status); Assert.Equal(1, writes);
    }
    [Theory]
    [InlineData(false, false, SyncWriteStatus.Success)]
    [InlineData(true, false, SyncWriteStatus.Forbidden)]
    [InlineData(false, true, SyncWriteStatus.Forbidden)]
    public async Task Unsubscribe_is_separate_and_protects_primary_and_owned_memberships(bool primary, bool owner, SyncWriteStatus status)
    {
        var baseline = new JsonObject { ["id"] = "calendar@example.invalid" }; var writes = 0;
        using var handler = new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Delete) { writes++; Assert.Contains("/users/me/calendarList/", request.RequestUri!.AbsolutePath, StringComparison.Ordinal); Assert.Empty(request.RequestUri.Query); return new(HttpStatusCode.NoContent); }
            return Json(new JsonObject { ["id"] = "calendar@example.invalid", ["primary"] = primary, ["dataOwner"] = owner ? "me@example.invalid" : "other@example.invalid" }.ToJsonString());
        }); using var http = new HttpClient(handler);
        var operation = new SyncOperation(Guid.NewGuid(), Guid.NewGuid(), 1, SyncOperationKind.Delete, null, "calendar@example.invalid", null, ApprovalHash: SyncEdits.Hash(null), Baseline: baseline);
        Assert.Equal(status, (await new GoogleCalendarProvider(http, new Accounts()).WriteAsync(Binding(false), operation, default)).Status); Assert.Equal(status == SyncWriteStatus.Success ? 1 : 0, writes);
    }
    [Theory]
    [InlineData(429, SyncWriteStatus.Retry)]
    [InlineData(400, SyncWriteStatus.Failed)]
    public async Task Permission_check_errors_before_post_are_not_unknown_writes(int status, SyncWriteStatus expected)
    {
        var writes = 0; using var handler = new Handler((request, _) =>
        {
            if (request.Method != HttpMethod.Get) writes++;
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("{}") };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(2)); return response;
        }); using var http = new HttpClient(handler);
        var result = await new GoogleCalendarProvider(http, new Accounts()).WriteAsync(Binding(true), Create(new GoogleSharingDraft("group", "team@example.invalid", "reader").Fields()), default);
        Assert.Equal(expected, result.Status); Assert.Equal(0, writes);
        if (status == 429) Assert.Equal(TimeSpan.FromMinutes(2), result.RetryAfter);
    }
    [Fact]
    public async Task Expired_access_cursor_requests_rebuild_without_discarding_local_state()
    {
        using var handler = new Handler((_, _) => new(HttpStatusCode.Gone) { Content = new StringContent("{}") }); using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<SyncCursorExpiredException>(() => new GoogleCalendarProvider(http, new Accounts()).ReadPageAsync(Binding(true), "old", null, default));
    }
    private static SyncBinding Binding(bool sharing) => new(Guid.NewGuid(), SyncProviderKind.GoogleCalendar, "client", "account", sharing ? "calendars/calendar%40example.invalid/acl" : GoogleCalendarAccess.Subscriptions, "Access", true);
    private static SyncOperation Create(JsonObject fields) => new(Guid.NewGuid(), Guid.NewGuid(), 1, SyncOperationKind.Create, fields, null, null, ApprovalHash: SyncEdits.Hash(fields));
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private static HttpResponseMessage Context() => Json("""{"id":"calendar@example.invalid","accessRole":"owner","dataOwner":"owner@example.invalid"}""");
    private sealed class Accounts : IIntegrationAccountService { public Task<string?> GetAccessTokenAsync(SyncBinding binding, bool write, CancellationToken token) => Task.FromResult<string?>("test-token"); }
    private sealed class Handler(Func<HttpRequestMessage, string?, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.RequestUri!.AbsolutePath.EndsWith("/calendarList/primary", StringComparison.Ordinal) ? Json("""{"id":"me@example.invalid","primary":true}""")
                : handler(request, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
    }
}
