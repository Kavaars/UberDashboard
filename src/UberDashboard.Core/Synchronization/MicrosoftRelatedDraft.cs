using System.Globalization;
using System.Text.Json.Nodes;

namespace UberDashboard.Core.Synchronization;

public enum RelatedDraftValidation { None, NameRequired, InvalidTimestamp, InvalidUrl, InvalidKind }

public sealed record MicrosoftRelatedDraft(string DisplayName, bool IsChecked, string CheckedAt, string CreatedAt,
    string ApplicationName, string WebUrl, string ExternalId)
{
    public static MicrosoftRelatedDraft Read(JsonObject? original) => new(original?["displayName"]?.GetValue<string>() ?? "",
        original?["isChecked"]?.GetValue<bool>() ?? false, original?["checkedDateTime"]?.GetValue<string>() ?? "",
        original?["createdDateTime"]?.GetValue<string>() ?? "", original?["applicationName"]?.GetValue<string>() ?? "",
        original?["webUrl"]?.GetValue<string>() ?? "", original?["externalId"]?.GetValue<string>() ?? "");

    public bool TryApply(SyncEntityKind kind, JsonObject? original, out JsonObject result, out RelatedDraftValidation error)
    {
        result = (JsonObject?)original?.DeepClone() ?? new(); error = RelatedDraftValidation.None;
        if (kind is not (SyncEntityKind.ChecklistItem or SyncEntityKind.LinkedResource)) { error = RelatedDraftValidation.InvalidKind; return false; }
        if (string.IsNullOrWhiteSpace(DisplayName)) { error = RelatedDraftValidation.NameRequired; return false; }
        var before = Read(original);
        if (DisplayName != before.DisplayName || original is null) result["displayName"] = DisplayName.Trim();
        if (kind == SyncEntityKind.ChecklistItem)
        {
            if (IsChecked != before.IsChecked || original is null) result["isChecked"] = IsChecked;
            foreach (var (key, old, value) in new[] { ("checkedDateTime", before.CheckedAt, CheckedAt), ("createdDateTime", before.CreatedAt, CreatedAt) })
            {
                if (old == value) continue;
                if (string.IsNullOrWhiteSpace(value)) { result[key] = null; continue; }
                if (value.Length < 20 || value[10] != 'T' || value.Any(char.IsControl) ||
                    !(value.EndsWith('Z') || value.Length >= 6 && value[^6] is '+' or '-' && value[^3] == ':') ||
                    !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                { error = RelatedDraftValidation.InvalidTimestamp; return false; }
                result[key] = date.ToString("O", CultureInfo.InvariantCulture);
            }
        }
        else
        {
            if (WebUrl != before.WebUrl)
            {
                if (WebUrl.Any(char.IsControl) || WebUrl.Trim().Length > 0 && !Uri.TryCreate(WebUrl.Trim(), UriKind.Absolute, out _))
                { error = RelatedDraftValidation.InvalidUrl; return false; }
                result["webUrl"] = string.IsNullOrWhiteSpace(WebUrl) ? null : WebUrl.Trim();
            }
            if (ApplicationName != before.ApplicationName) result["applicationName"] = ApplicationName;
            if (ExternalId != before.ExternalId) result["externalId"] = ExternalId;
        }
        return true;
    }

    public static bool IsBrowserUrl(string? value) => value is not null && !value.Any(char.IsControl) &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);
}
