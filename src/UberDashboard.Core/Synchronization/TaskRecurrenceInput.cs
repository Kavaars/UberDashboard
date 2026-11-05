using System.Globalization;
using System.Text.Json.Nodes;

namespace UberDashboard.Core.Synchronization;

public enum TaskRecurrenceValidation { None, InvalidPattern, InvalidRange, InvalidTimeZone }

/// <summary>Microsoft Graph patternedRecurrence. Pattern and range are edited as one conflict group.</summary>
public sealed record TaskRecurrenceInput(bool Enabled, string Pattern, string Interval, string Days, string FirstDay,
    string Index, string DayOfMonth, string Month, string Range, string StartDate, string EndDate, string Occurrences, string TimeZone)
{
    public static IReadOnlyList<string> Patterns { get; } = Array.AsReadOnly(new[] { "daily", "weekly", "absoluteMonthly", "relativeMonthly", "absoluteYearly", "relativeYearly" });
    public static IReadOnlyList<string> Ranges { get; } = Array.AsReadOnly(new[] { "noEnd", "endDate", "numbered" });
    public static IReadOnlyList<string> Weekdays { get; } = Array.AsReadOnly(new[] { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" });
    public static IReadOnlyList<string> Indices { get; } = Array.AsReadOnly(new[] { "first", "second", "third", "fourth", "last" });

    public static TaskRecurrenceInput Read(JsonObject? original)
    {
        var p = original?["pattern"]; var r = original?["range"];
        return new(original is not null, p?["type"]?.GetValue<string>() ?? "daily", p?["interval"]?.ToString() ?? "1",
            string.Join(",", (p?["daysOfWeek"] as JsonArray ?? []).Select(d => d!.GetValue<string>())),
            p?["firstDayOfWeek"]?.GetValue<string>() ?? "sunday", p?["index"]?.GetValue<string>() ?? "first",
            p?["dayOfMonth"]?.ToString() ?? "1", p?["month"]?.ToString() ?? "1", r?["type"]?.GetValue<string>() ?? "noEnd",
            r?["startDate"]?.GetValue<string>() ?? "", r?["endDate"]?.GetValue<string>() ?? "", r?["numberOfOccurrences"]?.ToString() ?? "1",
            r?["recurrenceTimeZone"]?.GetValue<string>() ?? "");
    }

    public bool TryApply(JsonObject? original, out JsonObject? result, out TaskRecurrenceValidation error)
    {
        error = TaskRecurrenceValidation.None;
        result = (JsonObject?)original?.DeepClone();
        if (this == Read(original)) return true;
        if (!Enabled) { result = null; return true; }
        if (!Patterns.Contains(Pattern) || !Positive(Interval, out var interval))
        { error = TaskRecurrenceValidation.InvalidPattern; return false; }
        var pattern = new JsonObject { ["type"] = Pattern, ["interval"] = interval };
        if (Pattern is "weekly" or "relativeMonthly" or "relativeYearly")
        {
            var days = Days.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray();
            if (days.Length == 0 || days.Any(d => !Weekdays.Contains(d)))
            { error = TaskRecurrenceValidation.InvalidPattern; return false; }
            pattern["daysOfWeek"] = new JsonArray(days.Select(d => (JsonNode?)JsonValue.Create(d)).ToArray());
        }
        if (Pattern == "weekly")
        {
            if (!Weekdays.Contains(FirstDay)) { error = TaskRecurrenceValidation.InvalidPattern; return false; }
            pattern["firstDayOfWeek"] = FirstDay;
        }
        if (Pattern is "relativeMonthly" or "relativeYearly")
        {
            if (!Indices.Contains(Index)) { error = TaskRecurrenceValidation.InvalidPattern; return false; }
            pattern["index"] = Index;
        }
        if (Pattern is "absoluteMonthly" or "absoluteYearly")
        {
            if (!Positive(DayOfMonth, out var day) || day > 31) { error = TaskRecurrenceValidation.InvalidPattern; return false; }
            pattern["dayOfMonth"] = day;
        }
        if (Pattern is "absoluteYearly" or "relativeYearly")
        {
            if (!Positive(Month, out var month) || month > 12) { error = TaskRecurrenceValidation.InvalidPattern; return false; }
            pattern["month"] = month;
        }
        if (!Ranges.Contains(Range) || !Date(StartDate, out var start)) { error = TaskRecurrenceValidation.InvalidRange; return false; }
        var range = new JsonObject { ["type"] = Range, ["startDate"] = StartDate };
        if (Range == "endDate")
        {
            if (!Date(EndDate, out var end) || end < start) { error = TaskRecurrenceValidation.InvalidRange; return false; }
            range["endDate"] = EndDate;
        }
        if (Range == "numbered")
        {
            if (!Positive(Occurrences, out var count)) { error = TaskRecurrenceValidation.InvalidRange; return false; }
            range["numberOfOccurrences"] = count;
        }
        if (!string.IsNullOrWhiteSpace(TimeZone))
        {
            try { _ = TimeZoneInfo.FindSystemTimeZoneById(TimeZone.Trim()); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
            { error = TaskRecurrenceValidation.InvalidTimeZone; return false; }
            range["recurrenceTimeZone"] = TimeZone.Trim();
        }
        result = new() { ["pattern"] = pattern, ["range"] = range }; return true;
    }

    private static bool Positive(string value, out int number) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
    private static bool Date(string value, out DateOnly date) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
