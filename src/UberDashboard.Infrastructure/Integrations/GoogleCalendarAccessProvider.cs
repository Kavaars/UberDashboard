using System.Net;
using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Infrastructure.Integrations;

public sealed partial class GoogleCalendarProvider
{
    protected override bool IsDeleted(SyncBinding binding, JsonObject value) => base.IsDeleted(binding, value) || GoogleCalendarAccess.IsSharing(binding) && value["role"]?.GetValue<string>() == "none";
    protected override string? WriteQuery(SyncBinding binding, SyncOperation operation) => GoogleCalendarAccess.IsSubscriptions(binding) && operation.Kind != SyncOperationKind.Delete
        ? "colorRgbFormat=true" : GoogleCalendarAccess.IsSharing(binding) && operation.Kind != SyncOperationKind.Delete ? "sendNotifications=true" : null;

    private async Task<JsonObject> AccessMetadataAsync(SyncBinding binding, string calendarId, CancellationToken token, JsonObject? entry = null)
    {
        _ = CollectionUri(binding);
        if (entry is null)
        {
            using var response = await SendAsync(binding, HttpMethod.Get, CalendarListUri(calendarId), null, null, false, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) entry = new() { ["id"] = calendarId, ["accessRole"] = "none" };
            else { await EnsureReadAsync(response).ConfigureAwait(false); entry = await BodyAsync(response, token).ConfigureAwait(false); }
        }
        var primaryId = entry["primary"]?.GetValue<bool>() == true ? entry["id"]?.GetValue<string>() : await ReadPrimaryIdAsync(binding, token).ConfigureAwait(false);
        return MetadataFromEntry(entry, primaryId, calendarId);
    }

    private static JsonObject MetadataFromEntry(JsonObject entry, string? primaryId, string calendarId)
    {
        var metadata = new JsonObject();
        foreach (var key in new[] { "summary", "accessRole", "dataOwner" }) metadata[key] = entry[key]?.DeepClone();
        var owner = entry["dataOwner"]?.GetValue<string>();
        metadata["calendarId"] = entry["id"]?.DeepClone() ?? JsonValue.Create(calendarId);
        metadata["actingCalendarId"] = primaryId;
        metadata["primary"] = entry["primary"]?.DeepClone() ?? (entry["accessRole"]?.GetValue<string>() == "none" ? null : JsonValue.Create(false));
        metadata["isDataOwner"] = owner is not null && primaryId is not null ? JsonValue.Create(string.Equals(owner, primaryId, StringComparison.OrdinalIgnoreCase)) : null;
        return metadata;
    }

    private async Task<SyncRemoteEntity?> GetAccessItemAsync(SyncBinding binding, string id, CancellationToken token)
    {
        var result = await base.GetAsync(binding, id, token).ConfigureAwait(false);
        if (result?.Fields is null) return null;
        var metadata = await AccessMetadataAsync(binding, GoogleCalendarAccess.IsSubscriptions(binding) ? id : SyncContainers.ParentId(binding)!, token,
            GoogleCalendarAccess.IsSubscriptions(binding) ? Reconstruct(result) : null).ConfigureAwait(false);
        return result with { Metadata = metadata };
    }
    private static JsonObject Reconstruct(SyncRemoteEntity item)
    {
        var value = (JsonObject?)item.Metadata?.DeepClone() ?? new();
        foreach (var pair in item.Fields!) value[pair.Key] = pair.Value?.DeepClone();
        value["id"] = item.Id; return value;
    }

    private async Task<SyncPage> ReadAccessPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token)
    {
        var uri = CollectionUri(binding).AbsoluteUri + "?maxResults=250&showDeleted=true" + (GoogleCalendarAccess.IsSubscriptions(binding) ? "&showHidden=true" : "");
        if (cursor is not null) uri += "&syncToken=" + Uri.EscapeDataString(cursor);
        if (page is not null) uri += "&pageToken=" + Uri.EscapeDataString(page);
        using var response = await SendAsync(binding, HttpMethod.Get, new Uri(uri), null, null, false, token).ConfigureAwait(false);
        await EnsureReadAsync(response).ConfigureAwait(false); var body = await BodyAsync(response, token).ConfigureAwait(false);
        var items = AccessItems(body);
        var result = new List<SyncRemoteEntity>();
        foreach (var value in items.OfType<JsonObject>())
        {
            var item = Parse(binding, value);
            result.Add(item);
        }
        return new(result, body["nextPageToken"]?.GetValue<string>(), body["nextSyncToken"]?.GetValue<string>(), cursor is null);
    }

    public async Task<SyncResourceMetadata?> ReadMetadataAsync(SyncBinding binding, CancellationToken token)
    {
        if (GoogleCalendarAccess.IsSharing(binding)) return new(Shared: await AccessMetadataAsync(binding, SyncContainers.ParentId(binding)!, token).ConfigureAwait(false));
        if (!GoogleCalendarAccess.IsSubscriptions(binding)) return null;
        var primary = await ReadPrimaryIdAsync(binding, token).ConfigureAwait(false);
        var items = new Dictionary<string, JsonObject>(StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal); string? page = null;
        do
        {
            var uri = new Uri(CollectionUri(binding).AbsoluteUri + "?maxResults=250&showHidden=true" + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page)));
            using var response = await SendAsync(binding, HttpMethod.Get, uri, null, null, false, token).ConfigureAwait(false);
            await EnsureReadAsync(response).ConfigureAwait(false); var body = await BodyAsync(response, token).ConfigureAwait(false);
            var rows = AccessItems(body);
            foreach (var entry in rows.OfType<JsonObject>())
            {
                var id = entry["id"]?.GetValue<string>() ?? throw new InvalidDataException("Missing calendar ID.");
                items[id] = MetadataFromEntry(entry, primary, id);
            }
            page = body["nextPageToken"]?.GetValue<string>();
            if (page is not null && (!seen.Add(page) || seen.Count > 10000)) throw new InvalidDataException("Invalid metadata pagination.");
        } while (page is not null);
        return new(Items: items);
    }

    private static JsonArray AccessItems(JsonObject body) => body["items"] is null ? [] :
        body["items"] is JsonArray items && items.All(i => i is JsonObject) ? items : throw new InvalidDataException("Invalid calendar access collection.");

    protected override async Task<SyncWriteResult?> ValidateWriteAsync(SyncBinding binding, SyncOperation operation, SyncRemoteEntity? current, JsonObject? patch, CancellationToken token)
    {
        if (GoogleCalendarAccess.IsSubscriptions(binding))
        {
            if (operation.Kind == SyncOperationKind.Delete) return GoogleCalendarAccess.CanUnsubscribe(current?.Metadata) ? null : new(SyncWriteStatus.Forbidden, ErrorCode: "subscription-primary-or-owned");
            var desiredId = operation.Desired?["id"]?.GetValue<string>() ?? "";
            if (!GoogleCalendarAccess.ValidCalendarId(desiredId) || operation.Kind != SyncOperationKind.Create && desiredId != current?.Id)
                return new(SyncWriteStatus.Failed, ErrorCode: "subscription-id-immutable");
            return null;
        }
        if (GoogleCalendarAccess.IsSharing(binding))
        {
            var metadata = current?.Metadata ?? await AccessMetadataAsync(binding, SyncContainers.ParentId(binding)!, token).ConfigureAwait(false);
            var fields = operation.Desired ?? current?.Fields;
            if (!GoogleCalendarAccess.CanChangeRule(fields, metadata)) return new(SyncWriteStatus.Forbidden, ErrorCode: "sharing-owner-or-self-protected");
            if (operation.Kind != SyncOperationKind.Delete && (!GoogleSharingDraft.Read(operation.Desired).TryApply(null, out _) ||
                current is not null && !GoogleCalendarAccess.SameScope(current.Fields?["scope"], operation.Desired?["scope"])))
                return new(SyncWriteStatus.Failed, ErrorCode: "sharing-scope-immutable");
            return null;
        }
        return await base.ValidateWriteAsync(binding, operation, current, patch, token).ConfigureAwait(false);
    }
}
