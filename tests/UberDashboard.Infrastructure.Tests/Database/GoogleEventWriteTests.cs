using System.Net;
using System.Text.Json.Nodes;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;
using UberDashboard.Infrastructure.Integrations;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class GoogleEventWriteTests
{
    [Theory]
    [InlineData(false, false, "attendees", "event-shared-fields-forbidden")]
    [InlineData(true, true, "attendees", "attendees-incomplete")]
    [InlineData(false, false, "summary", "event-shared-fields-forbidden")]
    public async Task Fresh_permissions_and_incomplete_collections_block_writes(bool organizer, bool omitted, string field, string expected)
    {
        var source = Source(); var desired = (JsonObject)source.DeepClone();
        if (field == "summary") desired[field] = "Changed";
        else { desired["attendees"]!.AsArray().Add(new JsonObject { ["email"] = "new@example.invalid" }); if (!organizer) desired["summary"] = "Changed"; }
        var metadata = new JsonObject { ["organizer"] = new JsonObject { ["self"] = organizer }, ["attendeesOmitted"] = omitted };
        var (result, writes, _) = await Write(source, desired, metadata);
        Assert.Equal(SyncWriteStatus.Forbidden, result.Status); Assert.Equal(expected, result.ErrorCode); Assert.Equal(0, writes);
    }

    [Fact]
    public async Task Guest_can_send_only_their_own_rsvp_and_transport_removes_readonly_fields()
    {
        var source = Source(); var metadata = new JsonObject { ["organizer"] = new JsonObject { ["self"] = false } };
        Assert.True((GoogleEventOptions.Read(source) with { SelfResponse = "tentative" }).TryApply(source, metadata, out var desired, out _));
        var (result, writes, patch) = await Write(source, desired, metadata);
        Assert.Equal(SyncWriteStatus.Success, result.Status); Assert.Equal(1, writes);
        Assert.NotNull(patch); Assert.Equal(2, patch.Count); Assert.True(patch["attendeesOmitted"]!.GetValue<bool>());
        Assert.Single(patch["attendees"]!.AsArray()); Assert.Equal("tentative", patch["attendees"]![0]!["responseStatus"]!.GetValue<string>());
        Assert.Null(patch["attendees"]![0]!["self"]);
    }

    [Fact]
    public async Task Guest_cannot_change_someone_elses_response_or_replace_their_attributes()
    {
        var source = Source(); var desired = (JsonObject)source.DeepClone(); desired["attendees"]![1]!["responseStatus"] = "declined";
        var (result, writes, _) = await Write(source, desired, new JsonObject { ["organizer"] = new JsonObject { ["self"] = false } });
        Assert.Equal("attendees-edit-forbidden", result.ErrorCode); Assert.Equal(0, writes);
    }

    [Fact]
    public async Task Locked_guest_copy_allows_private_reminders_with_a_minimal_conditional_patch()
    {
        var source = Source(); var desired = (JsonObject)source.DeepClone(); desired["reminders"] = JsonNode.Parse("""{"useDefault":false,"overrides":[]}""");
        var (result, writes, patch) = await Write(source, desired, new JsonObject { ["locked"] = true });
        Assert.Equal(SyncWriteStatus.Success, result.Status); Assert.Equal(1, writes); Assert.Equal("reminders", Assert.Single(patch!).Key);
    }

    [Fact]
    public async Task Organizer_can_add_people_preserving_writable_details_and_explicit_notification_parameter()
    {
        var source = Source(); var desired = (JsonObject)source.DeepClone(); desired["attendees"]!.AsArray().Add(new JsonObject { ["email"] = "new@example.invalid", ["responseStatus"] = "needsAction" });
        var (result, writes, patch) = await Write(source, desired, new JsonObject { ["organizer"] = new JsonObject { ["self"] = true } });
        Assert.Equal(SyncWriteStatus.Success, result.Status); Assert.Equal(1, writes);
        Assert.Equal(3, patch!["attendees"]!.AsArray().Count); Assert.Equal(2, patch["attendees"]![1]!["additionalGuests"]!.GetValue<int>());
        Assert.Null(patch["attendees"]![1]!["organizer"]); Assert.Null(patch["attendees"]![1]!["id"]);
        Assert.Equal("Keep", patch["attendees"]![1]!["comment"]!.GetValue<string>());
    }

    [Fact]
    public async Task Special_event_keeps_its_data_and_is_not_sent_through_the_default_editor()
    {
        var source = Source(); source["eventType"] = "birthday"; var desired = (JsonObject)source.DeepClone(); desired["summary"] = "Changed";
        var (result, writes, _) = await Write(source, desired, new JsonObject { ["organizer"] = new JsonObject { ["self"] = true } });
        Assert.Equal("special-event-editor-unavailable", result.ErrorCode); Assert.Equal(0, writes);
    }

    private static JsonObject Source() => JsonNode.Parse("""{"summary":"Meeting","attendees":[{"email":"me@example.invalid","self":true,"responseStatus":"accepted"},{"id":"profile","email":"host@example.invalid","organizer":true,"responseStatus":"accepted","optional":true,"additionalGuests":2,"comment":"Keep"}],"reminders":{"useDefault":true}}""")!.AsObject();
    private static async Task<(SyncWriteResult Result, int Writes, JsonObject? Patch)> Write(JsonObject baseline, JsonObject desired, JsonObject metadata)
    {
        var writes = 0; JsonObject? patch = null;
        using var transport = new Transport(async request =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                writes++; patch = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
                Assert.Contains("sendUpdates=all", request.RequestUri!.Query, StringComparison.Ordinal);
                Assert.Equal("\"current\"", request.Headers.GetValues("If-Match").Single());
            }
            var value = (JsonObject)(writes == 0 ? baseline : desired).DeepClone(); value["id"] = "event"; value["etag"] = "\"current\"";
            foreach (var pair in metadata) value[pair.Key] = pair.Value?.DeepClone();
            return new(HttpStatusCode.OK) { Content = new StringContent(value.ToJsonString()) };
        });
        using var http = new HttpClient(transport);
        var operation = new SyncOperation(Guid.NewGuid(), Guid.NewGuid(), 1, SyncOperationKind.Update, desired, "event", null,
            ApprovalHash: SyncEdits.Hash(desired), Baseline: baseline);
        var result = await new GoogleCalendarProvider(http, new Accounts()).WriteAsync(new(Guid.NewGuid(), SyncProviderKind.GoogleCalendar, "client", "account", "calendars/primary/events", "Calendar"), operation, default);
        return (result, writes, patch);
    }
    private sealed class Accounts : IIntegrationAccountService
    {
        public Task<string?> GetAccessTokenAsync(SyncBinding binding, bool write, CancellationToken token) => Task.FromResult<string?>("test-token");
    }
    private sealed class Transport(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
