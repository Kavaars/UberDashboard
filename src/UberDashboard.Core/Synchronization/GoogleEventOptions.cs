using System.Net.Mail;
using System.Text.Json.Nodes;

namespace UberDashboard.Core.Synchronization;

public enum GoogleEventValidation { None, InvalidAttendees, InvalidResponse, InvalidReminders, InvalidChoice, NotAllowed }
public sealed record GoogleReminder(string Method, int Minutes);

/// <summary>Editable event options. Unchanged collections and unknown server fields are preserved.</summary>
public sealed record GoogleEventOptions(string AttendeeEmails, string SelfResponse, string ReminderMode,
    IReadOnlyList<GoogleReminder> Reminders, string Visibility, string Transparency)
{
    public static GoogleEventOptions Read(JsonObject? value)
    {
        var people = People(value);
        var reminders = value?["reminders"];
        var overrides = reminders?["overrides"] as JsonArray;
        return new(string.Join('\n', people.Select(p => p["email"]?.GetValue<string>() ?? "")),
            people.FirstOrDefault(p => p["self"]?.GetValue<bool>() == true)?["responseStatus"]?.GetValue<string>() ?? "needsAction",
            reminders is null || reminders["useDefault"]?.GetValue<bool>() == true ? "default" : overrides?.Count > 0 ? "custom" : "none",
            overrides?.OfType<JsonObject>().Select(r => new GoogleReminder(r["method"]?.GetValue<string>() ?? "", r["minutes"]?.GetValue<int>() ?? -1)).ToArray() ?? [],
            value?["visibility"]?.GetValue<string>() ?? "default", value?["transparency"]?.GetValue<string>() ?? "opaque");
    }

    public static bool IsStandard(JsonObject? value) => value?["eventType"]?.GetValue<string>() is null or "default";
    public static bool CanManageAttendees(JsonObject? value, JsonObject? metadata) => IsStandard(value) &&
        CompletePeople(value, metadata) && metadata?["locked"]?.GetValue<bool>() != true &&
        (metadata is null || metadata["organizer"]?["self"]?.GetValue<bool>() == true);
    public static bool CanEditMain(JsonObject? value, JsonObject? metadata) => IsStandard(value) &&
        metadata?["locked"]?.GetValue<bool>() != true &&
        (metadata is null || metadata["organizer"]?["self"]?.GetValue<bool>() == true || value?["guestsCanModify"]?.GetValue<bool>() == true);
    public static bool CanRespond(JsonObject? value, JsonObject? metadata) => IsStandard(value) &&
        CompletePeople(value, metadata) && People(value).Count(p => p["self"]?.GetValue<bool>() == true) == 1;

    public bool TryApply(JsonObject? original, JsonObject? metadata, out JsonObject result, out GoogleEventValidation error)
    {
        result = (JsonObject?)original?.DeepClone() ?? new(); error = GoogleEventValidation.None;
        var initial = Read(original);
        if (AttendeeEmails != initial.AttendeeEmails)
        {
            if (!CanManageAttendees(original, metadata)) { error = GoogleEventValidation.NotAllowed; return false; }
            var emails = AttendeeEmails.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (emails.Any(e => e.Any(char.IsControl) || !MailAddress.TryCreate(e, out var parsed) || parsed.Address != e || !e.Contains('@')) ||
                emails.Distinct(StringComparer.OrdinalIgnoreCase).Count() != emails.Length)
            { error = GoogleEventValidation.InvalidAttendees; return false; }
            var existing = People(original);
            if (existing.Any(p => p["organizer"]?.GetValue<bool>() == true && !emails.Contains(p["email"]?.GetValue<string>() ?? "", StringComparer.OrdinalIgnoreCase)))
            { error = GoogleEventValidation.NotAllowed; return false; }
            var people = new JsonArray();
            foreach (var email in emails)
            {
                var matches = existing.Where(p => string.Equals(p["email"]?.GetValue<string>(), email, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length > 1) { error = GoogleEventValidation.InvalidAttendees; return false; }
                people.Add(matches.SingleOrDefault()?.DeepClone() ?? new JsonObject { ["email"] = email, ["responseStatus"] = "needsAction" });
            }
            result["attendees"] = people;
        }
        if (SelfResponse != initial.SelfResponse)
        {
            if (!CanRespond(original, metadata)) { error = GoogleEventValidation.NotAllowed; return false; }
            if (SelfResponse is not ("needsAction" or "accepted" or "declined" or "tentative"))
            { error = GoogleEventValidation.InvalidResponse; return false; }
            var self = People(result).SingleOrDefault(p => p["self"]?.GetValue<bool>() == true);
            if (self is null) { error = GoogleEventValidation.InvalidResponse; return false; }
            self["responseStatus"] = SelfResponse;
        }
        if (ReminderMode != initial.ReminderMode || !Reminders.SequenceEqual(initial.Reminders))
        {
            if (!IsStandard(original)) { error = GoogleEventValidation.NotAllowed; return false; }
            if (ReminderMode is not ("default" or "none" or "custom") || ReminderMode == "custom" &&
                (Reminders.Count is < 1 or > 5 || Reminders.Any(r => r.Method is not ("email" or "popup") || r.Minutes is < 0 or > 40320)))
            { error = GoogleEventValidation.InvalidReminders; return false; }
            var reminders = new JsonObject { ["useDefault"] = ReminderMode == "default" };
            if (ReminderMode != "default") reminders["overrides"] = new JsonArray((ReminderMode == "none" ? [] : Reminders)
                .Select(r => (JsonNode)new JsonObject { ["method"] = r.Method, ["minutes"] = r.Minutes }).ToArray());
            result["reminders"] = reminders;
        }
        foreach (var (key, value, previous, choices) in new[]
        {
            ("visibility", Visibility, initial.Visibility, new[] { "default", "public", "private", "confidential" }),
            ("transparency", Transparency, initial.Transparency, new[] { "opaque", "transparent" })
        })
        {
            if (value == previous) continue;
            if (!CanEditMain(original, metadata)) { error = GoogleEventValidation.NotAllowed; return false; }
            if (!choices.Contains(value, StringComparer.Ordinal)) { error = GoogleEventValidation.InvalidChoice; return false; }
            result[key] = value;
        }
        return true;
    }

    private static JsonObject[] People(JsonObject? value) => (value?["attendees"] as JsonArray)?.OfType<JsonObject>().ToArray() ?? [];
    private static bool CompletePeople(JsonObject? value, JsonObject? metadata) => metadata?["attendeesOmitted"]?.GetValue<bool>() != true &&
        People(value).All(p => p["asyncOperation"] is null);
}
