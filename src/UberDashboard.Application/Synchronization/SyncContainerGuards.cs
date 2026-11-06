using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Synchronization;

public sealed class SyncContainerChangesException : Exception;
public sealed class SyncContainerAccessException : Exception;

public sealed partial class SyncCoordinator
{
    // Short local transactions have a separate gate; edits never wait for network synchronization.
    private readonly ConcurrentDictionary<(SyncProviderKind Provider, string Client, string Account), SemaphoreSlim> _editGates = new();
    private SemaphoreSlim EditGate(SyncBinding binding) => _editGates.GetOrAdd((binding.Provider, binding.ClientId, binding.AccountId), _ => new(1, 1));
    private static bool SameAccount(SyncBinding a, SyncBinding b) => a.Provider == b.Provider && a.ClientId == b.ClientId && a.AccountId == b.AccountId;

    private async Task GuardedEditAsync(Guid bindingId, Guid entityId, JsonObject? desired, Func<SyncState, SyncState> change, CancellationToken token)
    {
        var identity = (await RequiredAsync(bindingId, token).ConfigureAwait(false)).Binding;
        var gate = EditGate(identity); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = await RequiredAsync(bindingId, token).ConfigureAwait(false);
            await CheckContainerAsync(state, entityId, desired, token).ConfigureAwait(false);
            await UpdateAsync(bindingId, change, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private async Task<HashSet<string>> PendingContainerContentsAsync(SyncBinding catalog, CancellationToken token)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in (await store.ListAsync(token).ConfigureAwait(false)).Where(b => SameAccount(b, catalog)))
        {
            if (SyncContainers.ParentId(binding) is not { } parentId) continue;
            var document = await RequiredAsync(binding.Id, token).ConfigureAwait(false);
            if (document.Queue.Count > 0 || document.FileTransfers.Any(t => t.IsActive) || document.SeriesPlans.Any(GoogleRecurrence.Active) || document.Items.Any(e => e.Conflicts is { Length: > 0 })) ids.Add(parentId);
        }
        return ids;
    }

    private async Task CheckContainerAsync(SyncState state, Guid entityId, JsonObject? desired, CancellationToken token)
    {
        if (GoogleCalendarAccess.IsSharing(state.Binding))
        {
            var entity = state.Items.FirstOrDefault(e => e.LocalId == entityId);
            var metadata = entity?.Metadata ?? state.Items.Select(e => e.Metadata).FirstOrDefault(m => m?["actingCalendarId"] is not null);
            var acceptsRemote = entity?.Conflicts is { Length: > 0 } && JsonNode.DeepEquals(desired, entity.Remote);
            if (!acceptsRemote && !(desired is null && entity?.Baseline is null) && !GoogleCalendarAccess.CanChangeRule(desired ?? entity?.Local, metadata))
                throw new SyncContainerAccessException();
        }
        if (SyncContainers.IsCatalog(state.Binding) || GoogleCalendarAccess.IsSubscriptions(state.Binding))
        {
            var entity = state.Items.FirstOrDefault(e => e.LocalId == entityId);
            // A verified remote deletion can be accepted without discarding drafts in the old binding.
            if (desired is null && entity?.RemoteId is { } id && entity.Baseline is not null &&
                !(entity.Conflicts?.Contains("$deleted") == true && entity.Remote is null) &&
                (await PendingContainerContentsAsync(state.Binding, token).ConfigureAwait(false)).Contains(id)) throw new SyncContainerChangesException();
            return;
        }
        if (SyncContainers.ParentId(state.Binding) is not { } parent) return;
        var subscriptionBinding = (await store.ListAsync(token).ConfigureAwait(false)).FirstOrDefault(b => SameAccount(b, state.Binding) && GoogleCalendarAccess.IsSubscriptions(b));
        if (subscriptionBinding is not null)
        {
            var subscriptions = await RequiredAsync(subscriptionBinding.Id, token).ConfigureAwait(false);
            var entries = subscriptions.Items.Where(e => e.RemoteId == parent || e.Metadata?["unsubscribedCalendarId"]?.GetValue<string>() == parent).ToArray();
            if (subscriptions.Queue.Any(o => o.Kind == SyncOperationKind.Delete && o.RemoteId == parent) ||
                entries.Length > 0 && !entries.Any(e => e.RemoteId == parent && e.Local is not null && e.Baseline is not null && e.Conflicts is not { Length: > 0 }))
                throw new SyncContainerChangesException();
        }
        var catalogBinding = (await store.ListAsync(token).ConfigureAwait(false)).FirstOrDefault(b => SameAccount(b, state.Binding) && SyncContainers.IsCatalog(b));
        if (catalogBinding is null) return;
        var catalog = await RequiredAsync(catalogBinding.Id, token).ConfigureAwait(false);
        if (catalog.Items.Any(e => e.RemoteId == parent && e.Metadata?["accessRole"] is not null &&
            !SyncContainers.CanWriteContents(state.Binding.Provider, e.Metadata))) throw new SyncContainerAccessException();
        if (catalog.Items.Any(e => (e.Metadata?["deletedResourceIds"] as JsonArray)?.Any(id => id?.GetValue<string>() == parent) == true ||
            e.RemoteId == parent && (e.Local is null || e.Conflicts?.Contains("$deleted") == true ||
                catalog.Queue.Any(o => o.EntityId == e.LocalId && o.Kind == SyncOperationKind.Delete))))
            throw new SyncContainerChangesException();
    }

    private async Task ApplyCatalogPullAsync(SyncBinding binding, Func<IReadOnlySet<string>, SyncState, SyncState> apply, CancellationToken token)
    {
        var gate = EditGate(binding); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var pending = await PendingContainerContentsAsync(binding, token).ConfigureAwait(false);
            await UpdateAsync(binding.Id, s => apply(pending, s), token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private static JsonObject? ContainerMetadata(SyncBinding binding, SyncEntity entity, JsonObject? remoteMetadata, string? deletedId = null)
    {
        if (GoogleCalendarAccess.IsSubscriptions(binding))
        {
            if (deletedId is not null && entity.Baseline is not null) return new JsonObject { ["unsubscribedCalendarId"] = deletedId };
            return remoteMetadata;
        }
        if (!SyncContainers.IsCatalog(binding)) return remoteMetadata;
        var ids = (entity.Metadata?["deletedResourceIds"] as JsonArray)?.OfType<JsonValue>().Select(v => v.GetValue<string>()).ToList() ?? [];
        if (deletedId is not null && !ids.Contains(deletedId, StringComparer.Ordinal)) ids.Add(deletedId);
        if (ids.Count == 0) return remoteMetadata;
        var metadata = (JsonObject?)remoteMetadata?.DeepClone() ?? new();
        metadata["deletedResourceIds"] = new JsonArray(ids.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()); return metadata;
    }
}
