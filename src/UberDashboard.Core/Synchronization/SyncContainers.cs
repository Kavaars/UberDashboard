using System.Text.Json.Nodes;

namespace UberDashboard.Core.Synchronization;

public static class SyncContainers
{
    public static string CatalogPath(SyncProviderKind provider) => provider == SyncProviderKind.MicrosoftTodo ? "lists" : "calendars";
    public static bool IsCatalog(SyncBinding binding) => binding.ResourcePath == CatalogPath(binding.Provider);
    public static bool CanManuallyLink(SyncBinding binding) => binding.Provider == SyncProviderKind.MicrosoftTodo || IsCatalog(binding);
    public static bool CanIdentifyCreated(SyncBinding binding, JsonObject? metadata) => !IsCatalog(binding) || CanDelete(binding.Provider, metadata);
    public static string? ParentId(SyncBinding binding)
    {
        var parts = binding.ResourcePath.Split('/');
        if (binding.Provider == SyncProviderKind.MicrosoftTodo && MicrosoftTodoRelations.TaskCollectionPath(binding.ResourcePath) is not null ||
            binding.Provider == SyncProviderKind.GoogleCalendar && parts.Length == 3 && parts[0] == "calendars" && parts[2] is "events" or "acl")
            return Uri.UnescapeDataString(parts[1]);
        return null;
    }
    public static bool CanEdit(SyncProviderKind provider, JsonObject? metadata) => provider == SyncProviderKind.MicrosoftTodo
        ? metadata?["isOwner"]?.GetValue<bool>() == true && metadata["wellknownListName"]?.GetValue<string>() == "none"
        : metadata?["accessRole"]?.GetValue<string>() == "owner" && metadata["propertiesUnavailable"]?.GetValue<bool>() != true;
    public static bool CanDelete(SyncProviderKind provider, JsonObject? metadata) => CanEdit(provider, metadata) &&
        (provider == SyncProviderKind.MicrosoftTodo || metadata?["primary"]?.GetValue<bool>() == false && metadata["isDataOwner"]?.GetValue<bool>() == true);
    public static bool CanWriteContents(SyncProviderKind provider, JsonObject? metadata) => provider == SyncProviderKind.MicrosoftTodo ||
        metadata?["accessRole"]?.GetValue<string>() is "owner" or "writer" or "writerWithoutPrivateAccess";
    public static bool SupportedFormat(SyncState state) => state.FormatVersion == (IsCatalog(state.Binding) ? 3 : 2);
}

public enum SyncContainerValidation { None, NameRequired, InvalidTimeZone }
public sealed record SyncContainerDraft(string Name, string Description, string Location, string TimeZone)
{
    public static SyncContainerDraft Read(SyncProviderKind provider, JsonObject? value) => new(
        value?[provider == SyncProviderKind.MicrosoftTodo ? "displayName" : "summary"]?.GetValue<string>() ?? "",
        value?["description"]?.GetValue<string>() ?? "", value?["location"]?.GetValue<string>() ?? "", value?["timeZone"]?.GetValue<string>() ?? "UTC");
    public bool TryApply(SyncProviderKind provider, JsonObject? original, out JsonObject result, out SyncContainerValidation error)
    {
        result = (JsonObject?)original?.DeepClone() ?? new(); error = SyncContainerValidation.None;
        if (string.IsNullOrWhiteSpace(Name)) { error = SyncContainerValidation.NameRequired; return false; }
        var initial = Read(provider, original);
        if (original is null || Name != initial.Name) result[provider == SyncProviderKind.MicrosoftTodo ? "displayName" : "summary"] = Name.Trim();
        if (provider != SyncProviderKind.GoogleCalendar) return true;
        if (original is null || Description != initial.Description) result["description"] = Description;
        if (original is null || Location != initial.Location) result["location"] = Location;
        if (original is null || TimeZone != initial.TimeZone)
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone.Trim(), out var zone) || !zone.HasIanaId && TimeZone != "UTC")
            { error = SyncContainerValidation.InvalidTimeZone; return false; }
            result["timeZone"] = TimeZone.Trim();
        }
        return true;
    }
}
