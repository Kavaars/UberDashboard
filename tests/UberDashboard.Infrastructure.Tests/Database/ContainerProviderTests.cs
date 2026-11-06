using System.Net;
using System.Text.Json.Nodes;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;
using UberDashboard.Infrastructure.Integrations;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class ContainerProviderTests
{
    [Fact]
    public async Task Calendar_manager_role_does_not_grant_the_data_owners_exclusive_delete_right()
    {
        var writes = 0; var baseline = new JsonObject { ["summary"] = "Calendar" };
        using var handler = new Handler((request, _) =>
        {
            if (request.Method != HttpMethod.Get) writes++;
            return request.RequestUri!.AbsolutePath.Contains("calendarList", StringComparison.Ordinal)
                ? Json(new JsonObject { ["id"] = "resource", ["accessRole"] = "owner", ["primary"] = false, ["dataOwner"] = "someone-else@example.invalid" })
                : Json(new JsonObject { ["id"] = "resource", ["summary"] = "Calendar" });
        }); using var http = new HttpClient(handler);
        var result = await new GoogleCalendarProvider(http, new Accounts()).WriteAsync(Binding(SyncProviderKind.GoogleCalendar), Op(baseline, null), default);
        Assert.Equal(SyncWriteStatus.Forbidden, result.Status); Assert.Equal(0, writes);
    }
    [Theory]
    [InlineData(true, "defaultList")]
    [InlineData(true, "flaggedEmails")]
    [InlineData(false, "none")]
    [InlineData(true, "unknownFutureValue")]
    public async Task Microsoft_rechecks_system_and_ownership_flags_before_update_and_delete(bool owner, string system)
    {
        foreach (var delete in new[] { false, true })
        {
            var fields = new JsonObject { ["displayName"] = "List" }; var desired = delete ? null : new JsonObject { ["displayName"] = "Changed" }; var writes = 0;
            using var handler = new Handler((request, _) =>
            { if (request.Method != HttpMethod.Get) writes++; return Json(new JsonObject { ["id"] = "list", ["displayName"] = "List", ["isOwner"] = owner, ["wellknownListName"] = system }); });
            using var http = new HttpClient(handler);
            var result = await new MicrosoftTodoProvider(http, new Accounts()).WriteAsync(Binding(SyncProviderKind.MicrosoftTodo), Op(fields, desired), default);
            Assert.Equal(SyncWriteStatus.Forbidden, result.Status); Assert.Equal(0, writes);
        }
    }

    [Theory]
    [InlineData(SyncProviderKind.MicrosoftTodo)]
    [InlineData(SyncProviderKind.GoogleCalendar)]
    public async Task Owned_container_updates_use_a_minimal_patch_and_current_resource_etag(SyncProviderKind kind)
    {
        var key = kind == SyncProviderKind.MicrosoftTodo ? "displayName" : "summary";
        var fields = new JsonObject { [key] = "Name" }; var desired = new JsonObject { [key] = "Changed" }; var writes = 0;
        using var handler = new Handler((request, body) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("calendarList", StringComparison.Ordinal)) return Roles();
            var resource = (JsonObject)(writes == 0 ? fields : desired).DeepClone();
            resource["id"] = "resource"; resource["etag"] = "\"current\""; resource["isOwner"] = true; resource["wellknownListName"] = "none";
            resource["workingHours"] = JsonNode.Parse("""{"workingHoursEnabled":true}""");
            if (request.Method == HttpMethod.Patch)
            {
                Assert.Equal(desired.ToJsonString(), body); Assert.Equal("\"current\"", request.Headers.GetValues("If-Match").Single()); writes++;
                resource[key] = "Changed";
            }
            return Json(resource);
        }); using var http = new HttpClient(handler);
        SyncHttpProvider provider = kind == SyncProviderKind.MicrosoftTodo ? new MicrosoftTodoProvider(http, new Accounts()) : new GoogleCalendarProvider(http, new Accounts());
        var result = await provider.WriteAsync(Binding(kind), Op(fields, desired), default);
        Assert.Equal(SyncWriteStatus.Success, result.Status); Assert.Equal(1, writes);
        Assert.NotNull(result.Entity!.Metadata!["workingHours"]);
    }

    [Theory]
    [InlineData("owner", true)]
    [InlineData("writer", false)]
    [InlineData("reader", false)]
    public async Task Google_does_not_delete_primary_or_non_owned_calendars(string role, bool primary)
    {
        var writes = 0; var baseline = new JsonObject { ["summary"] = "Calendar" };
        using var handler = new Handler((request, _) =>
        {
            if (request.Method != HttpMethod.Get) writes++;
            return request.RequestUri!.AbsolutePath.Contains("calendarList", StringComparison.Ordinal) ? Roles(role, primary) : Json(new JsonObject { ["id"] = "resource", ["summary"] = "Calendar" });
        }); using var http = new HttpClient(handler);
        var result = await new GoogleCalendarProvider(http, new Accounts()).WriteAsync(Binding(SyncProviderKind.GoogleCalendar), Op(baseline, null), default);
        Assert.Equal(SyncWriteStatus.Forbidden, result.Status); Assert.Equal(0, writes);
    }

    [Fact]
    public async Task Secondary_calendar_delete_requires_approval_then_uses_the_calendar_endpoint()
    {
        var writes = 0; var baseline = new JsonObject { ["summary"] = "Calendar" };
        using var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("calendarList", StringComparison.Ordinal)) return Roles();
            Assert.Equal("/calendar/v3/calendars/resource", request.RequestUri.AbsolutePath);
            if (request.Method == HttpMethod.Delete) { writes++; Assert.Empty(request.RequestUri.Query); return new(HttpStatusCode.NoContent); }
            return Json(new JsonObject { ["id"] = "resource", ["summary"] = "Calendar", ["etag"] = "\"current\"" });
        }); using var http = new HttpClient(handler); var provider = new GoogleCalendarProvider(http, new Accounts()); var operation = Op(baseline, null);
        Assert.Equal("approval-required", (await provider.WriteAsync(Binding(provider.Kind), operation with { ApprovalHash = null }, default)).ErrorCode);
        Assert.Equal(SyncWriteStatus.Success, (await provider.WriteAsync(Binding(provider.Kind), operation, default)).Status); Assert.Equal(1, writes);
    }

    [Theory]
    [InlineData(SyncProviderKind.MicrosoftTodo)]
    [InlineData(SyncProviderKind.GoogleCalendar)]
    public async Task Unknown_server_assigned_container_creation_is_not_given_a_fabricated_id(SyncProviderKind kind)
    {
        using var handler = new Handler((request, body) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Null(JsonNode.Parse(body!)!["id"]);
            return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") };
        }); using var http = new HttpClient(handler);
        SyncHttpProvider provider = kind == SyncProviderKind.MicrosoftTodo ? new MicrosoftTodoProvider(http, new Accounts()) : new GoogleCalendarProvider(http, new Accounts());
        var fields = new JsonObject { [kind == SyncProviderKind.MicrosoftTodo ? "displayName" : "summary"] = "New" };
        var operation = new SyncOperation(Guid.NewGuid(), Guid.NewGuid(), 1, SyncOperationKind.Create, fields, null, null);
        Assert.Equal(SyncWriteStatus.UnknownOutcome, (await provider.WriteAsync(Binding(kind), operation, default)).Status);
    }

    [Fact]
    public async Task Calendar_catalog_follows_pages_and_reads_global_properties_without_a_display_range()
    {
        using var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("calendarList", StringComparison.Ordinal))
            {
                Assert.Contains("showHidden=true", request.RequestUri.Query, StringComparison.Ordinal);
                Assert.DoesNotContain("syncToken", request.RequestUri.Query, StringComparison.Ordinal);
                return Json(JsonNode.Parse(request.RequestUri.Query.Contains("pageToken=next", StringComparison.Ordinal)
                    ? """{"items":[{"id":"second","accessRole":"reader"}]}""" : """{"items":[{"id":"first","accessRole":"owner","primary":true}],"nextPageToken":"next"}""")!.AsObject());
            }
            return Json(new JsonObject { ["id"] = request.RequestUri.AbsolutePath.Split('/').Last(), ["summary"] = "Global title", ["description"] = "Global description", ["timeZone"] = "Europe/Warsaw" });
        }); using var http = new HttpClient(handler); var provider = new GoogleCalendarProvider(http, new Accounts()); var binding = Binding(provider.Kind);
        var first = await provider.ReadPageAsync(binding, null, null, default); var second = await provider.ReadPageAsync(binding, null, first.NextPage, default);
        Assert.True(first.IsFullSnapshot); Assert.Null(first.Cursor); Assert.True(Assert.Single(first.Items).Metadata!["primary"]!.GetValue<bool>());
        var item = Assert.Single(second.Items); Assert.Equal("reader", item.Metadata!["accessRole"]!.GetValue<string>()); Assert.False(item.Metadata["primary"]!.GetValue<bool>());
        Assert.Equal("Global description", item.Fields!["description"]!.GetValue<string>());
    }

    [Fact]
    public async Task Unreadable_properties_keep_a_catalog_entry_but_do_not_enable_writes()
    {
        using var handler = new Handler((request, _) => request.RequestUri!.AbsolutePath.Contains("calendarList", StringComparison.Ordinal)
            ? Json(JsonNode.Parse("""{"items":[{"id":"resource","summary":"Visible title","accessRole":"owner"}]}""")!.AsObject())
            : new(HttpStatusCode.Forbidden) { Content = new StringContent("{}") });
        using var http = new HttpClient(handler); var provider = new GoogleCalendarProvider(http, new Accounts());
        var item = Assert.Single((await provider.ReadPageAsync(Binding(provider.Kind), null, null, default)).Items);
        Assert.Equal("Visible title", item.Fields!["summary"]!.GetValue<string>()); Assert.False(SyncContainers.CanEdit(provider.Kind, item.Metadata));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("")]
    public async Task Untrusted_ids_cannot_change_the_endpoint(string id)
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("No HTTP expected")); using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new GoogleCalendarProvider(http, new Accounts()).GetAsync(Binding(SyncProviderKind.GoogleCalendar), id, default));
    }
    private static SyncBinding Binding(SyncProviderKind kind) => new(Guid.NewGuid(), kind, "client", "account", SyncContainers.CatalogPath(kind), "Catalog", true);
    private static SyncOperation Op(JsonObject baseline, JsonObject? desired) => new(Guid.NewGuid(), Guid.NewGuid(), 1, desired is null ? SyncOperationKind.Delete : SyncOperationKind.Update,
        desired, "resource", null, ApprovalHash: desired is null ? SyncEdits.Hash(null) : null, Baseline: baseline);
    private static HttpResponseMessage Roles(string role = "owner", bool primary = false) => Json(new JsonObject { ["id"] = "resource", ["accessRole"] = role, ["primary"] = primary, ["dataOwner"] = "me@example.invalid" });
    private static HttpResponseMessage Json(JsonObject body) => new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString()) };
    private sealed class Accounts : IIntegrationAccountService
    { public Task<string?> GetAccessTokenAsync(SyncBinding binding, bool write, CancellationToken token) => Task.FromResult<string?>("test-token"); }
    private sealed class Handler(Func<HttpRequestMessage, string?, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.RequestUri!.AbsolutePath.EndsWith("/calendarList/primary", StringComparison.Ordinal)
                ? Json(new JsonObject { ["id"] = "me@example.invalid", ["primary"] = true, ["accessRole"] = "owner" })
                : handler(request, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
    }
}
