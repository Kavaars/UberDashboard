using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;

namespace UberDashboard.Core.Synchronization;

public enum MicrosoftTaskValidation { None, TitleRequired, InvalidDate, InvalidTimeZone, ReminderDateRequired, InvalidChoice, InvalidRecurrence }

/// <summary>Wall-clock input in its service zone, rather than the computer's current zone.</summary>
public sealed record TaskDateInput(bool HasValue, string Date, string Time, string TimeZone)
{
    public static TaskDateInput Read(JsonNode? value)
    {
        var text = value?["dateTime"]?.GetValue<string>() ?? "";
        var separator = text.IndexOf('T');
        return new(value is not null, separator < 0 ? text : text[..separator],
            separator < 0 ? "09:00" : text[(separator + 1)..], value?["timeZone"]?.GetValue<string>() ?? "UTC");
    }

    public bool TrySerialize(out JsonObject? value, out MicrosoftTaskValidation error)
    {
        value = null; error = MicrosoftTaskValidation.None;
        if (!HasValue) return true;
        if (!DateOnly.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
            !TimeOnly.TryParseExact(Time, ["HH:mm", "HH:mm:ss", "HH:mm:ss.FFFFFFF"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        { error = MicrosoftTaskValidation.InvalidDate; return false; }
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZone.Trim()); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { error = MicrosoftTaskValidation.InvalidTimeZone; return false; }
        var wall = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall) || zone.IsAmbiguousTime(wall))
        { error = MicrosoftTaskValidation.InvalidDate; return false; }
        value = new() { ["dateTime"] = wall.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.'), ["timeZone"] = TimeZone.Trim() };
        return true;
    }
}

/// <summary>Editable Graph v1.0 task fields. Untouched values remain byte-for-byte JSON values.</summary>
public sealed record MicrosoftTaskDraft(string Title, string Description, bool DescriptionIsHtml, string Status, string Importance,
    string Categories, TaskDateInput Start, TaskDateInput Due, TaskDateInput Completed, bool IsReminderOn, TaskDateInput Reminder)
{
    public static IReadOnlyList<string> Statuses { get; } = Array.AsReadOnly(new[] { "notStarted", "inProgress", "completed", "waitingOnOthers", "deferred" });
    public static IReadOnlyList<string> ImportanceValues { get; } = Array.AsReadOnly(new[] { "low", "normal", "high" });

    public static MicrosoftTaskDraft Read(JsonObject? fields) => new(
        fields?["title"]?.GetValue<string>() ?? "", fields?["body"]?["content"]?.GetValue<string>() ?? "",
        string.Equals(fields?["body"]?["contentType"]?.GetValue<string>(), "html", StringComparison.OrdinalIgnoreCase),
        fields?["status"]?.GetValue<string>() ?? "notStarted", fields?["importance"]?.GetValue<string>() ?? "normal",
        string.Join("\n", (fields?["categories"] as JsonArray ?? []).Select(c => c!.GetValue<string>())),
        TaskDateInput.Read(fields?["startDateTime"]), TaskDateInput.Read(fields?["dueDateTime"]), TaskDateInput.Read(fields?["completedDateTime"]),
        fields?["isReminderOn"]?.GetValue<bool>() ?? false, TaskDateInput.Read(fields?["reminderDateTime"]));

    public bool TryApply(JsonObject? original, out JsonObject result, out MicrosoftTaskValidation error)
    {
        result = (JsonObject?)original?.DeepClone() ?? new(); error = MicrosoftTaskValidation.None;
        var previous = Read(original);
        if (string.IsNullOrWhiteSpace(Title)) { error = MicrosoftTaskValidation.TitleRequired; return false; }
        if ((Status != previous.Status || original is null) && !Statuses.Contains(Status) ||
            (Importance != previous.Importance || original is null) && !ImportanceValues.Contains(Importance))
        { error = MicrosoftTaskValidation.InvalidChoice; return false; }
        if (Title != previous.Title || original is null) result["title"] = Title.Trim();
        if (Description != previous.Description || DescriptionIsHtml != previous.DescriptionIsHtml || original is null)
            result["body"] = new JsonObject { ["contentType"] = "html", ["content"] = DescriptionIsHtml ? Description :
                WebUtility.HtmlEncode(Description.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')).Replace("\n", "<br>", StringComparison.Ordinal) };
        if (Status != previous.Status || original is null) result["status"] = Status;
        if (Importance != previous.Importance || original is null) result["importance"] = Importance;
        if (Categories != previous.Categories)
            result["categories"] = new JsonArray(Categories.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0)
                .Distinct(StringComparer.Ordinal).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());

        foreach (var (key, before, after) in new[] { ("startDateTime", previous.Start, Start), ("dueDateTime", previous.Due, Due),
            ("completedDateTime", previous.Completed, Completed), ("reminderDateTime", previous.Reminder, Reminder) })
        {
            if (before == after) continue;
            if (!after.TrySerialize(out var date, out error)) return false;
            result[key] = date;
        }
        if ((IsReminderOn != previous.IsReminderOn || Reminder != previous.Reminder || original is null) && IsReminderOn && !Reminder.HasValue)
        { error = MicrosoftTaskValidation.ReminderDateRequired; return false; }
        if (IsReminderOn != previous.IsReminderOn || original is null) result["isReminderOn"] = IsReminderOn;
        return true;
    }
}
