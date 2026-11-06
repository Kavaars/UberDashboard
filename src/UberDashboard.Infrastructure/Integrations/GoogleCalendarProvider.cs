using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Infrastructure.Integrations;

public sealed partial class GoogleCalendarProvider(HttpClient http, IIntegrationAccountService accounts)
    : SyncHttpProvider(http, accounts), IGoogleCalendarProvider, ISyncResourceMetadataProvider, IGoogleSeriesProvider
{
    public override SyncProviderKind Kind => SyncProviderKind.GoogleCalendar;
    public override IReadOnlyList<string[]> AtomicGroups { get; } = [["start", "end", "endTimeUnspecified"], ["recurrence", "recurringEventId", "originalStartTime"]];
    protected override Uri Root { get; } = new("https://www.googleapis.com/calendar/v3/");
    protected override bool ValidPath(string path) => ResourcePath().IsMatch(path);
    protected override IReadOnlySet<string> WritableFields(string path) => path.EndsWith("/acl", StringComparison.Ordinal)
        ? new HashSet<string>(["scope", "role"], StringComparer.Ordinal)
        : path == "calendars" ? new HashSet<string>(["summary", "description", "location", "timeZone"], StringComparer.Ordinal)
        : path == "users/me/calendarList" ? new HashSet<string>(["id", "summaryOverride", "colorId", "backgroundColor", "foregroundColor", "hidden", "selected", "defaultReminders", "notificationSettings"], StringComparer.Ordinal)
        : new HashSet<string>(["summary", "description", "location", "start", "end", "endTimeUnspecified", "recurrence", "attendees", "reminders", "visibility", "transparency", "colorId", "status", "conferenceData", "attachments", "guestsCanInviteOthers", "guestsCanModify", "guestsCanSeeOtherGuests", "extendedProperties", "eventType", "focusTimeProperties", "outOfOfficeProperties", "workingLocationProperties", "birthdayProperties"], StringComparer.Ordinal);

    public override async Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token)
    {
        if (binding.ResourcePath == "calendars") return await ReadCalendarsAsync(binding, page, token).ConfigureAwait(false);
        if (GoogleCalendarAccess.IsAccessDocument(binding)) return await ReadAccessPageAsync(binding, cursor, page, token).ConfigureAwait(false);
        var incremental = binding.ResourcePath.EndsWith("/events", StringComparison.Ordinal);
        var uri = CollectionUri(binding).AbsoluteUri + "?maxResults=250";
        if (binding.ResourcePath.EndsWith("/events", StringComparison.Ordinal)) uri += "&singleEvents=false&showDeleted=true";
        if (incremental && cursor is not null) uri += "&syncToken=" + Uri.EscapeDataString(cursor);
        if (page is not null) uri += "&pageToken=" + Uri.EscapeDataString(page);
        using var response = await SendAsync(binding, HttpMethod.Get, new Uri(uri), null, null, false, token).ConfigureAwait(false);
        await EnsureReadAsync(response).ConfigureAwait(false);
        var body = await BodyAsync(response, token).ConfigureAwait(false);
        var items = body["items"] as System.Text.Json.Nodes.JsonArray ?? [];
        return new(items.Select(n => Parse(binding, n!.AsObject())).ToArray(), body["nextPageToken"]?.GetValue<string>(),
            body["nextSyncToken"]?.GetValue<string>(), !incremental || cursor is null);
    }

    protected override SyncWriteResult? ValidateWrite(SyncBinding binding, SyncOperation operation, SyncRemoteEntity? current, JsonObject? patch)
    {
        if (binding.ResourcePath == "calendars" && current is not null)
            return (operation.Kind == SyncOperationKind.Delete ? SyncContainers.CanDelete(Kind, current.Metadata) : SyncContainers.CanEdit(Kind, current.Metadata))
                ? null : new(SyncWriteStatus.Forbidden, ErrorCode: "calendar-primary-or-not-owned");
        if (!binding.ResourcePath.EndsWith("/events", StringComparison.Ordinal) || patch is null) return null;
        if (!GoogleEventOptions.IsStandard(current?.Fields ?? patch)) return new(SyncWriteStatus.Failed, ErrorCode: "special-event-editor-unavailable");
        if (current is null) return null;
        if (patch.ContainsKey("eventType")) return new(SyncWriteStatus.Failed, ErrorCode: "event-type-immutable");
        var main = new[] { "summary", "description", "location", "start", "end", "endTimeUnspecified", "recurrence", "visibility", "transparency",
            "guestsCanInviteOthers", "guestsCanModify", "guestsCanSeeOtherGuests", "conferenceData", "attachments", "status" };
        if (patch.Any(p => main.Contains(p.Key, StringComparer.Ordinal)) && !GoogleEventOptions.CanEditMain(current.Fields, current.Metadata))
            return new(SyncWriteStatus.Forbidden, ErrorCode: "event-shared-fields-forbidden");
        if (!patch.ContainsKey("attendees")) return null;
        if (current.Metadata?["attendeesOmitted"]?.GetValue<bool>() == true ||
            (current.Fields?["attendees"] as JsonArray)?.OfType<JsonObject>().Any(p => p["asyncOperation"] is not null) == true)
            return new(SyncWriteStatus.Forbidden, ErrorCode: "attendees-incomplete");
        if (GoogleEventOptions.CanManageAttendees(current.Fields, current.Metadata)) return null;
        // A guest can change their own RSVP without replacing other guests or their attributes.
        if (GoogleEventOptions.CanRespond(current.Fields, current.Metadata) && patch["attendees"] is JsonArray desired)
        {
            var own = desired.OfType<JsonObject>().Where(p => p["self"]?.GetValue<bool>() == true).ToArray();
            if (own.Length == 1)
            {
                var response = own[0]["responseStatus"]?.GetValue<string>() ?? "";
                var draft = GoogleEventOptions.Read(current.Fields) with { SelfResponse = response };
                if (draft.TryApply(current.Fields, current.Metadata, out var allowed, out _) && JsonNode.DeepEquals(allowed["attendees"], desired)) return null;
            }
        }
        return new(SyncWriteStatus.Forbidden, ErrorCode: "attendees-edit-forbidden");
    }

    public override async Task<SyncRemoteEntity?> GetAsync(SyncBinding binding, string remoteId, CancellationToken token)
    {
        if (GoogleCalendarAccess.IsAccessDocument(binding)) return await GetAccessItemAsync(binding, remoteId, token).ConfigureAwait(false);
        if (binding.ResourcePath != "calendars") return await base.GetAsync(binding, remoteId, token).ConfigureAwait(false);
        _ = CollectionUri(binding);
        using var response = await SendAsync(binding, HttpMethod.Get, CalendarListUri(remoteId), null, null, false, token).ConfigureAwait(false);
        JsonObject entry;
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) entry = new() { ["id"] = remoteId, ["accessRole"] = "none" };
        else { await EnsureReadAsync(response).ConfigureAwait(false); entry = await BodyAsync(response, token).ConfigureAwait(false); }
        var primaryId = entry["primary"]?.GetValue<bool>() == true ? entry["id"]?.GetValue<string>() : await ReadPrimaryIdAsync(binding, token).ConfigureAwait(false);
        return await ReadCalendarAsync(binding, remoteId, entry, primaryId, token).ConfigureAwait(false);
    }

    private Uri CalendarListUri(string id) => Trusted(new Uri(Root, "users/me/calendarList/" + EncodeItemId(id)));
    private async Task<SyncPage> ReadCalendarsAsync(SyncBinding binding, string? page, CancellationToken token)
    {
        _ = CollectionUri(binding);
        var uri = new Uri(Root, "users/me/calendarList?maxResults=250&showHidden=true" + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page)));
        using var response = await SendAsync(binding, HttpMethod.Get, uri, null, null, false, token).ConfigureAwait(false);
        await EnsureReadAsync(response).ConfigureAwait(false); var body = await BodyAsync(response, token).ConfigureAwait(false);
        if (body["items"] is not JsonArray items) throw new InvalidDataException("Missing calendar collection.");
        if (items.Any(i => i is not JsonObject)) throw new InvalidDataException("Invalid calendar entry.");
        var primaryId = items.OfType<JsonObject>().FirstOrDefault(e => e["primary"]?.GetValue<bool>() == true)?["id"]?.GetValue<string>()
            ?? await ReadPrimaryIdAsync(binding, token).ConfigureAwait(false);
        var entries = new List<SyncRemoteEntity>();
        foreach (var item in items.OfType<JsonObject>())
        {
            var id = item["id"]?.GetValue<string>() ?? throw new InvalidDataException("Missing calendar ID.");
            entries.Add(await ReadCalendarAsync(binding, id, item, primaryId, token).ConfigureAwait(false) ?? new(id, null));
        }
        return new(entries, body["nextPageToken"]?.GetValue<string>(), null, true);
    }
    private async Task<string?> ReadPrimaryIdAsync(SyncBinding binding, CancellationToken token)
    {
        using var response = await SendAsync(binding, HttpMethod.Get, CalendarListUri("primary"), null, null, false, token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureReadAsync(response).ConfigureAwait(false); var value = await BodyAsync(response, token).ConfigureAwait(false);
        return value["primary"]?.GetValue<bool>() == true ? value["id"]?.GetValue<string>() : null;
    }
    private async Task<SyncRemoteEntity?> ReadCalendarAsync(SyncBinding binding, string id, JsonObject entry, string? primaryId, CancellationToken token)
    {
        SyncRemoteEntity? calendar;
        try { calendar = await base.GetAsync(binding, id, token).ConfigureAwait(false); }
        catch (HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.Forbidden && entry["summary"] is not null)
        { calendar = Parse(binding, entry); entry = (JsonObject)entry.DeepClone(); entry["propertiesUnavailable"] = true; }
        if (calendar is null) return null;
        var metadata = (JsonObject?)calendar.Metadata?.DeepClone() ?? new();
        foreach (var key in new[] { "accessRole", "dataOwner", "propertiesUnavailable" }) metadata[key] = entry[key]?.DeepClone();
        var owner = entry["dataOwner"]?.GetValue<string>() ?? calendar.Metadata?["dataOwner"]?.GetValue<string>();
        metadata["dataOwner"] = owner;
        metadata["isDataOwner"] = owner is not null && primaryId is not null ? JsonValue.Create(string.Equals(owner, primaryId, StringComparison.OrdinalIgnoreCase)) : null;
        metadata["primary"] = entry["primary"]?.DeepClone() ?? (entry["accessRole"]?.GetValue<string>() == "none" ? null : JsonValue.Create(false));
        return calendar with { Metadata = metadata };
    }

    protected override JsonObject? PreparePayload(SyncBinding binding, JsonObject? payload, SyncRemoteEntity? current)
    {
        if (GoogleCalendarAccess.IsSubscriptions(binding) && current is not null && payload is not null)
        { var personal = (JsonObject)payload.DeepClone(); personal.Remove("id"); return personal; }
        if (GoogleCalendarAccess.IsSharing(binding) && current is not null && payload is not null)
        { var permission = (JsonObject)payload.DeepClone(); permission.Remove("scope"); return permission; }
        if (!binding.ResourcePath.EndsWith("/events", StringComparison.Ordinal) || payload is null) return payload;
        var copy = (JsonObject)payload.DeepClone();
        if (copy["attachments"] is JsonArray attachments)
            foreach (var attachment in attachments.OfType<JsonObject>()) { attachment.Remove("fileId"); attachment.Remove("iconLink"); }
        if (payload["attendees"] is not JsonArray attendees) return copy;
        var writable = new HashSet<string>(["email", "displayName", "optional", "responseStatus", "comment", "additionalGuests", "resource"], StringComparer.Ordinal);
        IEnumerable<JsonObject> sentPeople = attendees.OfType<JsonObject>();
        if (current is not null && !GoogleEventOptions.CanManageAttendees(current.Fields, current.Metadata))
        {
            // Documented RSVP update: only the calendar's own attendee, without replacing the collection.
            sentPeople = sentPeople.Where(p => p["self"]?.GetValue<bool>() == true);
            copy["attendeesOmitted"] = true;
        }
        copy["attendees"] = new JsonArray(sentPeople.Select(p => (JsonNode)new JsonObject(
            p.Where(v => writable.Contains(v.Key)).Select(v => new KeyValuePair<string, JsonNode?>(v.Key, v.Value?.DeepClone())))).ToArray());
        return copy;
    }

    [GeneratedRegex("^(?:users/me/calendarList|calendars(?:/(?:[A-Za-z0-9_@.=-]|%[0-9a-fA-F]{2})+/(?:events|acl))?)$", RegexOptions.CultureInvariant)]
    private static partial Regex ResourcePath();
}
