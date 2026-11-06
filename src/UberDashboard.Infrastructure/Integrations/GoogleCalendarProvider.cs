using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Infrastructure.Integrations;

public sealed partial class GoogleCalendarProvider(HttpClient http, IIntegrationAccountService accounts)
    : SyncHttpProvider(http, accounts), IGoogleCalendarProvider
{
    public override SyncProviderKind Kind => SyncProviderKind.GoogleCalendar;
    public override IReadOnlyList<string[]> AtomicGroups { get; } = [["start", "end", "endTimeUnspecified"], ["recurrence", "recurringEventId", "originalStartTime"]];
    protected override Uri Root { get; } = new("https://www.googleapis.com/calendar/v3/");
    protected override bool ValidPath(string path) => ResourcePath().IsMatch(path);
    protected override IReadOnlySet<string> WritableFields(string path) => path.EndsWith("/acl", StringComparison.Ordinal)
        ? new HashSet<string>(["scope", "role"], StringComparer.Ordinal)
        : path == "calendars" ? new HashSet<string>(["summary", "description", "location", "timeZone"], StringComparer.Ordinal)
        : path == "users/me/calendarList" ? new HashSet<string>(["summaryOverride", "colorId", "backgroundColor", "foregroundColor", "hidden", "selected", "defaultReminders", "notificationSettings"], StringComparer.Ordinal)
        : new HashSet<string>(["summary", "description", "location", "start", "end", "endTimeUnspecified", "recurrence", "attendees", "reminders", "visibility", "transparency", "colorId", "status", "conferenceData", "attachments", "guestsCanInviteOthers", "guestsCanModify", "guestsCanSeeOtherGuests", "extendedProperties", "eventType", "focusTimeProperties", "outOfOfficeProperties", "workingLocationProperties", "birthdayProperties"], StringComparer.Ordinal);

    public override async Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token)
    {
        if (binding.ResourcePath == "calendars") throw new InvalidOperationException("Discover calendars through calendarList.");
        var incremental = binding.ResourcePath.EndsWith("/events", StringComparison.Ordinal) || binding.ResourcePath == "users/me/calendarList";
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
        if (!binding.ResourcePath.EndsWith("/events", StringComparison.Ordinal) || patch is null) return null;
        if (!GoogleEventOptions.IsStandard(current?.Fields ?? patch)) return new(SyncWriteStatus.Failed, ErrorCode: "special-event-editor-unavailable");
        if (current is null) return null;
        if (patch.ContainsKey("eventType")) return new(SyncWriteStatus.Failed, ErrorCode: "event-type-immutable");
        var main = new[] { "summary", "description", "location", "start", "end", "endTimeUnspecified", "recurrence", "visibility", "transparency",
            "guestsCanInviteOthers", "guestsCanModify", "guestsCanSeeOtherGuests", "conferenceData", "attachments" };
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

    protected override JsonObject? PreparePayload(SyncBinding binding, JsonObject? payload, SyncRemoteEntity? current)
    {
        if (!binding.ResourcePath.EndsWith("/events", StringComparison.Ordinal) || payload?["attendees"] is not JsonArray attendees) return payload;
        var copy = (JsonObject)payload.DeepClone();
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
