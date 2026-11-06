using System.Net.Mail;
using System.Text.Json.Nodes;

namespace UberDashboard.Core.Synchronization;

public static class GoogleCalendarAccess
{
    public const string Subscriptions = "users/me/calendarList";
    public static bool IsSubscriptions(SyncBinding binding) => binding.Provider == SyncProviderKind.GoogleCalendar && binding.ResourcePath == Subscriptions;
    public static bool IsSharing(SyncBinding binding) => binding.Provider == SyncProviderKind.GoogleCalendar && SyncContainers.ParentId(binding) is not null && binding.ResourcePath.EndsWith("/acl", StringComparison.Ordinal);
    public static bool IsAccessDocument(SyncBinding binding) => IsSubscriptions(binding) || IsSharing(binding);
    public static bool CanUnsubscribe(JsonObject? metadata) => metadata?["primary"]?.GetValue<bool>() == false && metadata["isDataOwner"]?.GetValue<bool>() == false;
    public static bool SameScope(JsonNode? first, JsonNode? second) => first?["type"]?.GetValue<string>() == second?["type"]?.GetValue<string>() &&
        string.Equals(first?["value"]?.GetValue<string>() ?? "", second?["value"]?.GetValue<string>() ?? "", StringComparison.OrdinalIgnoreCase);
    public static bool CanChangeRule(JsonObject? rule, JsonObject? metadata)
    {
        if (metadata?["accessRole"]?.GetValue<string>() != "owner" || string.IsNullOrEmpty(metadata["actingCalendarId"]?.GetValue<string>())) return false;
        if (rule?["scope"]?["type"]?.GetValue<string>() != "user") return true;
        var target = rule["scope"]?["value"]?.GetValue<string>();
        return !new[] { metadata["actingCalendarId"]?.GetValue<string>(), metadata["dataOwner"]?.GetValue<string>(), metadata["calendarId"]?.GetValue<string>() }
            .Any(id => id is not null && string.Equals(id, target, StringComparison.OrdinalIgnoreCase));
    }
    public static bool ValidCalendarId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 1024 && value is not ("." or ".." or "primary") && !value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c));
}

public sealed record GoogleSharingDraft(string ScopeType, string ScopeValue, string Role)
{
    public static IReadOnlyList<string> Roles { get; } = ["freeBusyReader", "reader", "writerWithoutPrivateAccess", "writer", "owner"];
    public static GoogleSharingDraft Read(JsonObject? fields) => new(fields?["scope"]?["type"]?.GetValue<string>() ?? "user", fields?["scope"]?["value"]?.GetValue<string>() ?? "", fields?["role"]?.GetValue<string>() ?? "reader");
    public JsonObject Fields() => new() { ["scope"] = ScopeType == "default" ? new JsonObject { ["type"] = ScopeType } : new JsonObject { ["type"] = ScopeType, ["value"] = ScopeValue.Trim() }, ["role"] = Role };
    public bool TryApply(JsonObject? original, out JsonObject fields)
    {
        fields = (JsonObject?)original?.DeepClone() ?? new(); var initial = Read(original);
        if (original is not null && (ScopeType != initial.ScopeType || ScopeValue != initial.ScopeValue)) return false;
        if (original is null && (ScopeType switch
        {
            "default" => !string.IsNullOrWhiteSpace(ScopeValue),
            "user" or "group" => !MailAddress.TryCreate(ScopeValue.Trim(), out var mail) || mail.Address != ScopeValue.Trim() || !mail.Address.Contains('@') || ScopeValue.Any(char.IsControl),
            "domain" => Uri.CheckHostName(ScopeValue.Trim()) != UriHostNameType.Dns || !ScopeValue.Contains('.') || ScopeValue.Any(char.IsWhiteSpace),
            _ => true
        })) return false;
        if (Role != initial.Role && !Roles.Contains(Role, StringComparer.Ordinal) || original is null && !Roles.Contains(Role, StringComparer.Ordinal)) return false;
        if (original is null) fields["scope"] = Fields()["scope"]!.DeepClone();
        if (original is null || Role != initial.Role) fields["role"] = Role;
        return true;
    }
}

public sealed record GoogleSubscriptionDraft(string CalendarId, string Alias, bool Hidden, bool Selected, string BackgroundColor, string ForegroundColor)
{
    public static GoogleSubscriptionDraft Read(JsonObject? fields) => new(fields?["id"]?.GetValue<string>() ?? "", fields?["summaryOverride"]?.GetValue<string>() ?? "",
        fields?["hidden"]?.GetValue<bool>() ?? false, fields?["selected"]?.GetValue<bool>() ?? false,
        fields?["backgroundColor"]?.GetValue<string>() ?? "", fields?["foregroundColor"]?.GetValue<string>() ?? "");
    public bool TryApply(JsonObject? original, out JsonObject fields)
    {
        fields = (JsonObject?)original?.DeepClone() ?? new(); var initial = Read(original);
        if (!GoogleCalendarAccess.ValidCalendarId(CalendarId) || original is not null && CalendarId != initial.CalendarId) return false;
        if (original is null) fields["id"] = CalendarId;
        foreach (var (key, value, previous) in new[] { ("summaryOverride", Alias, initial.Alias), ("backgroundColor", BackgroundColor, initial.BackgroundColor), ("foregroundColor", ForegroundColor, initial.ForegroundColor) })
        {
            if (value == previous) continue;
            if (key != "summaryOverride" && (value.Length != 7 || value[0] != '#' || !value.Skip(1).All(Uri.IsHexDigit))) return false;
            fields[key] = value;
        }
        if (Hidden != initial.Hidden) fields["hidden"] = Hidden;
        if (Selected != initial.Selected) fields["selected"] = Selected;
        return true;
    }
}
