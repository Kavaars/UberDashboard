using System.Text.Json.Nodes;

namespace UberDashboard.Core.Synchronization;

public sealed record GoogleAttendeeReview(string[] Added, string[] Removed, string[] Remaining)
{
    public static GoogleAttendeeReview Compare(JsonObject? baseline, JsonObject? desired)
    {
        static string[] Emails(JsonObject? value) => (value?["attendees"] as JsonArray)?.OfType<JsonObject>()
            .Select(p => p["email"]?.GetValue<string>()).OfType<string>().Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        var before = Emails(baseline); var after = Emails(desired);
        return new(after.Except(before, StringComparer.OrdinalIgnoreCase).ToArray(), before.Except(after, StringComparer.OrdinalIgnoreCase).ToArray(),
            after.Intersect(before, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
