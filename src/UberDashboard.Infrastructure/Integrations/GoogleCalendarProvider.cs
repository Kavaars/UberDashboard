using System.Text.RegularExpressions;
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

    [GeneratedRegex("^(?:users/me/calendarList|calendars(?:/(?:[A-Za-z0-9_@.=-]|%[0-9a-fA-F]{2})+/(?:events|acl))?)$", RegexOptions.CultureInvariant)]
    private static partial Regex ResourcePath();
}
