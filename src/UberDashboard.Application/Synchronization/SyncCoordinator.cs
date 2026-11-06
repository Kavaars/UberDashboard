using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using UberDashboard.Application.Abstractions;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Synchronization;

/// <summary>Pull/merge/push. No network call occurs within a database transaction.</summary>
public sealed partial class SyncCoordinator(ISyncStore store, IEnumerable<ISyncProvider> providers, IClock clock) : ISyncCoordinator
{
    private readonly ConcurrentDictionary<(SyncProviderKind Provider, string Client, string Account), SemaphoreSlim> _gates = new();
    private readonly IReadOnlyDictionary<SyncProviderKind, ISyncProvider> _providers = providers.ToDictionary(p => p.Kind);

    public Task SetEnabledAsync(Guid bindingId, bool enabled, CancellationToken token = default) =>
        UpdateAsync(bindingId, s => s with { Binding = s.Binding with { Enabled = enabled }, ConnectionStatus = !enabled ? SyncConnectionStatus.Idle :
            s.Binding.NotBefore > clock.UtcNow ? SyncConnectionStatus.Throttled : s.ConnectionStatus }, token);

    public async Task EditAsync(Guid bindingId, Guid entityId, JsonObject? desired, CancellationToken token = default) =>
        await GuardedEditAsync(bindingId, entityId, desired, s => SyncEdits.Edit(s, entityId, desired, clock.UtcNow), token).ConfigureAwait(false);

    public Task EditChildAsync(Guid bindingId, Guid parentId, Guid entityId, SyncEntityKind kind, JsonObject? desired, CancellationToken token = default) =>
        GuardedEditAsync(bindingId, entityId, desired, s => SyncEdits.EditChild(s, parentId, entityId, kind, desired, clock.UtcNow), token);

    public Task EditFromSnapshotAsync(Guid bindingId, Guid entityId, JsonObject? expectedLocal, long expectedRevision,
        JsonObject? desired, CancellationToken token = default) => GuardedEditAsync(bindingId, entityId, desired, state =>
    {
        var current = state.Items.Single(e => e.LocalId == entityId);
        if (current.Revision != expectedRevision || !JsonNode.DeepEquals(current.Local, expectedLocal))
            throw new InvalidOperationException("The item changed while the editor was open. Reload before saving.");
        return SyncEdits.Edit(state, entityId, desired, clock.UtcNow);
    }, token);

    public async Task ApproveAsync(Guid bindingId, Guid operationId, CancellationToken token = default, SyncOperation? expected = null) =>
        await UpdateAsync(bindingId, s => SyncEdits.Approve(s, operationId, expected), token).ConfigureAwait(false);

    public async Task ResolveAsync(Guid bindingId, Guid entityId, JsonObject? chosen, CancellationToken token = default, SyncEntity? expected = null)
    {
        var identity = (await RequiredAsync(bindingId, token).ConfigureAwait(false)).Binding;
        var gate = _gates.GetOrAdd((identity.Provider, identity.ClientId, identity.AccountId), _ => new(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { await ResolveCoreAsync(bindingId, entityId, chosen, token, expected).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private async Task ResolveCoreAsync(Guid bindingId, Guid entityId, JsonObject? chosen, CancellationToken token, SyncEntity? expected)
    {
        var state = await RequiredAsync(bindingId, token).ConfigureAwait(false);
        var entity = state.Items.Single(e => e.LocalId == entityId);
        if (expected is not null && (expected.Revision != entity.Revision || !JsonNode.DeepEquals(expected.Local, entity.Local) ||
            !JsonNode.DeepEquals(expected.Remote, entity.Remote))) throw new InvalidOperationException("The conflict changed while it was being reviewed.");
        var remote = entity.RemoteId is null ? null : await ReadWithQuotaAsync(state.Binding,
            MicrosoftTodoRelations.Scope(state, entity), entity.RemoteId, token).ConfigureAwait(false);
        await GuardedEditAsync(bindingId, entityId, chosen, current =>
        {
            var latest = current.Items.Single(e => e.LocalId == entityId);
            if (latest.Revision != entity.Revision || !JsonNode.DeepEquals(latest.Local, entity.Local) ||
                !JsonNode.DeepEquals(remote?.Fields, latest.Remote))
                throw new InvalidOperationException("The remote item changed. Synchronize and review the conflict again.");
            var clean = latest with { Baseline = remote?.Fields, Local = remote?.Fields, ETag = remote?.ETag,
                RemoteId = remote?.Fields is null ? null : remote.Id, Conflicts = null, Remote = null,
                Metadata = ContainerMetadata(current.Binding, latest, remote?.Metadata, remote?.Fields is null ? latest.RemoteId : null) };
            var reset = current with { Entities = current.Items.Select(e => e.LocalId == entityId ? clean : e).ToArray(),
                Operations = current.Queue.Where(o => o.EntityId != entityId).ToArray(),
                Recovery = current.History.Append(new(entityId, clock.UtcNow, latest.Local, "conflict-resolution")).ToArray() };
            reset = SyncEdits.Edit(reset, entityId, chosen, clock.UtcNow);
            return entity.Kind == SyncEntityKind.Item && remote?.Fields is null
                ? SyncEdits.ResetChildren(reset, entityId, chosen is not null, clock.UtcNow) : reset;
        }, token).ConfigureAwait(false);
    }

    public async Task SynchronizeAsync(Guid bindingId, CancellationToken token = default)
    {
        var identity = (await RequiredAsync(bindingId, token).ConfigureAwait(false)).Binding;
        var gate = _gates.GetOrAdd((identity.Provider, identity.ClientId, identity.AccountId), _ => new(1, 1));
        if (!await gate.WaitAsync(0, token).ConfigureAwait(false)) return;
        try
        {
            var state = await RequiredAsync(bindingId, token).ConfigureAwait(false);
            if (!state.Binding.Enabled) return;
            if ((await store.ListAsync(token).ConfigureAwait(false)).Any(b => b.Provider == identity.Provider &&
                b.ClientId == identity.ClientId && b.AccountId == identity.AccountId && b.NotBefore > clock.UtcNow)) return;
            await UpdateAsync(bindingId, s => s with { ConnectionStatus = SyncConnectionStatus.Synchronizing, LastAttempt = clock.UtcNow }, token).ConfigureAwait(false);
            var provider = _providers[state.Binding.Provider];
            await UpdateAsync(bindingId, s => s with { Operations = s.Queue.Select(o =>
                o.State == SyncOperationState.Sending && o.RetryAt <= clock.UtcNow
                    ? o with { State = SyncOperationState.UnknownOutcome, ErrorCode = "interrupted-write" } : o).ToArray() }, token).ConfigureAwait(false);
            await ReconcileUnknownAsync(bindingId, provider, token).ConfigureAwait(false);
            await PullAsync(state.Binding, provider, state.Cursor, token).ConfigureAwait(false);
            if (provider is ISyncRelationProvider relations)
            {
                var relatedState = await RequiredAsync(bindingId, token).ConfigureAwait(false);
                var parents = relatedState.Items.Where(e => e.Kind == SyncEntityKind.Item && e.RemoteId is not null && e.Baseline is not null &&
                    !(e.Conflicts is { Length: > 0 } && e.Remote is null)).ToArray();
                var snapshots = await relations.ReadRelationsAsync(relatedState.Binding, parents, token).ConfigureAwait(false);
                await UpdateAsync(bindingId, s =>
                {
                    foreach (var snapshot in snapshots)
                    {
                        if (!parents.Any(p => p.LocalId == snapshot.ParentId) || !Enum.IsDefined(snapshot.Kind) || snapshot.Kind == SyncEntityKind.Item)
                            throw new InvalidDataException("Invalid related collection snapshot.");
                        s = ApplyRemote(s, snapshot.Items.ToDictionary(e => e.Id, StringComparer.Ordinal), true, s.Cursor,
                            provider.AtomicGroups, snapshot.Kind, snapshot.ParentId);
                    }
                    return s;
                }, token).ConfigureAwait(false);
            }

            // A bounded batch keeps a busy account from monopolizing the background worker.
            for (var count = 0; count < 100; count++)
            {
                state = await RequiredAsync(bindingId, token).ConfigureAwait(false);
                if (!state.Binding.Enabled) return;
                var next = state.Queue.FirstOrDefault(o =>
                    (o.State is SyncOperationState.Pending or SyncOperationState.Retry) &&
                    (o.RetryAt is null || o.RetryAt <= clock.UtcNow) &&
                    (o.DependsOn is null || state.Queue.All(p => p.Id != o.DependsOn)) &&
                    !state.Queue.Any(p => p.EntityId == o.EntityId && p.Id != o.Id &&
                        p.State is SyncOperationState.UnknownOutcome or SyncOperationState.Sending) &&
                    state.Items.Single(e => e.LocalId == o.EntityId).Conflicts is not { Length: > 0 } &&
                    MicrosoftTodoRelations.CanSend(state, state.Items.Single(e => e.LocalId == o.EntityId)));
                if (next is null) break;
                try { await CheckContainerAsync(state, next.EntityId, next.Desired, token).ConfigureAwait(false); }
                catch (SyncContainerAccessException)
                {
                    await UpdateAsync(bindingId, s => s with { Operations = s.Queue.Select(o => o.Id == next.Id
                        ? o with { State = SyncOperationState.Forbidden, ErrorCode = "container-read-only" } : o).ToArray() }, token).ConfigureAwait(false);
                    continue;
                }
                catch (SyncContainerChangesException)
                {
                    await UpdateAsync(bindingId, s => s with { Operations = s.Queue.Select(o => o.Id == next.Id
                        ? o with { State = SyncOperationState.Failed, ErrorCode = "container-has-changes-or-is-deleting" } : o).ToArray() }, token).ConfigureAwait(false);
                    continue;
                }
                var claimed = false;
                await UpdateAsync(bindingId, current =>
                {
                    claimed = false;
                    var candidate = current.Queue.FirstOrDefault(o => o.Id == next.Id);
                    if (!current.Binding.Enabled || candidate is null || candidate.Revision != next.Revision ||
                        candidate.State != next.State || candidate.Attempts != next.Attempts ||
                        !JsonNode.DeepEquals(candidate.Desired, next.Desired)) return current;
                    claimed = true;
                    return current with { Operations = current.Queue.Select(o => o.Id == next.Id
                        ? o with { State = SyncOperationState.Sending, Attempts = o.Attempts + 1, RetryAt = clock.UtcNow.AddMinutes(5) } : o).ToArray() };
                }, token).ConfigureAwait(false);
                if (!claimed) continue;

                SyncWriteResult result;
                try { result = await provider.WriteAsync(MicrosoftTodoRelations.Scope(state, state.Items.Single(e => e.LocalId == next.EntityId)), next, token).ConfigureAwait(false); }
                catch (SyncAuthenticationException) { result = new(SyncWriteStatus.AuthenticationRequired); }
                catch (Exception error) when (error is OperationCanceledException or HttpRequestException or IOException or System.Text.Json.JsonException)
                { result = new(SyncWriteStatus.UnknownOutcome, ErrorCode: "transport-interrupted"); }

                // Persist an uncertain result even when the user canceled the HTTP request.
                await UpdateAsync(bindingId, s => Complete(s, next, result), CancellationToken.None).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (result.Status is SyncWriteStatus.Conflict or SyncWriteStatus.Retry) break;
            }
            await UpdateAsync(bindingId, s => s with { LastSuccess = s.Queue.Count == 0 ? clock.UtcNow : s.LastSuccess,
                ConnectionStatus = s.Binding.NotBefore > clock.UtcNow ? SyncConnectionStatus.Throttled : SyncConnectionStatus.Idle }, token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var status = error switch
            {
                SyncAuthenticationException => SyncConnectionStatus.AuthenticationRequired,
                SyncReadException => SyncConnectionStatus.Throttled,
                HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden } => SyncConnectionStatus.Forbidden,
                HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } => SyncConnectionStatus.Throttled,
                InvalidDataException => SyncConnectionStatus.Failed,
                HttpRequestException or IOException => SyncConnectionStatus.Offline,
                OperationCanceledException when token.IsCancellationRequested => SyncConnectionStatus.Idle,
                OperationCanceledException => SyncConnectionStatus.Offline,
                _ => SyncConnectionStatus.Failed,
            };
            await UpdateAsync(bindingId, s => s with { ConnectionStatus = status,
                Binding = error is SyncReadException throttled ? s.Binding with { NotBefore = clock.UtcNow +
                    (throttled.RetryAfter ?? TimeSpan.FromMinutes(1)) } : s.Binding }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally { gate.Release(); }
    }

    /// <summary>Retries definite failures only. Unknown outcomes must be reconciled first.</summary>
    public Task RetryAsync(Guid bindingId, Guid operationId, CancellationToken token = default) => UpdateAsync(bindingId, state =>
    {
        var operation = state.Queue.Single(o => o.Id == operationId);
        if (operation.State is SyncOperationState.UnknownOutcome or SyncOperationState.Sending or SyncOperationState.Conflict or SyncOperationState.AwaitingApproval)
            throw new InvalidOperationException("This operation requires reconciliation or approval before retrying.");
        return state with { Operations = state.Queue.Select(o => o.Id == operationId
            ? o with { State = SyncOperationState.Pending, RetryAt = null, ErrorCode = null } : o).ToArray() };
    }, token);

    private async Task ReconcileUnknownAsync(Guid bindingId, ISyncProvider provider, CancellationToken token)
    {
        var state = await RequiredAsync(bindingId, token).ConfigureAwait(false);
        foreach (var operation in state.Queue.Where(o => o.State == SyncOperationState.UnknownOutcome && o.RemoteId is not null))
        {
            var entity = state.Items.Single(e => e.LocalId == operation.EntityId);
            if (!MicrosoftTodoRelations.CanSend(state, entity)) continue;
            var remote = await provider.GetAsync(MicrosoftTodoRelations.Scope(state, entity), operation.RemoteId!, token).ConfigureAwait(false);
            var intended = operation.Kind == SyncOperationKind.Update && operation.Baseline is not null && operation.Desired is not null
                ? SyncMerge.Patch(operation.Baseline, operation.Desired) : operation.Desired;
            var confirmed = operation.Kind == SyncOperationKind.Delete ? remote is null : remote?.Fields is { } fields &&
                intended is { } desired && desired.All(p => (fields.ContainsKey(p.Key) || p.Value is null) && JsonNode.DeepEquals(fields[p.Key], p.Value));
            if (!confirmed) continue;
            await UpdateAsync(bindingId, s => Complete(s, operation, new(SyncWriteStatus.Success, remote)), token).ConfigureAwait(false);
        }
    }

    private async Task PullAsync(SyncBinding binding, ISyncProvider provider, string? cursor, CancellationToken token)
    {
        var changes = new Dictionary<string, SyncRemoteEntity>(StringComparer.Ordinal);
        var pages = new HashSet<string>(StringComparer.Ordinal);
        string? page = null;
        string? nextCursor = null;
        var full = cursor is null;
        var reset = false;
        while (true)
        {
            SyncPage response;
            try { response = await provider.ReadPageAsync(binding, cursor, page, token).ConfigureAwait(false); }
            catch (SyncCursorExpiredException) when (!reset)
            {
                reset = true;
                cursor = page = null;
                full = true;
                changes.Clear();
                pages.Clear();
                continue;
            }
            full |= response.IsFullSnapshot;
            foreach (var entry in response.Items) changes[entry.Id] = entry;
            page = response.NextPage;
            nextCursor = response.Cursor;
            if (page is not null && (!pages.Add(page) || pages.Count > 10000)) throw new InvalidDataException("Invalid pagination.");
            if (page is null) break;
        }

        // Absence from CalendarList means unsubscribed, not necessarily a deleted global calendar.
        if (full && binding.Provider == SyncProviderKind.GoogleCalendar && SyncContainers.IsCatalog(binding))
            foreach (var known in (await RequiredAsync(binding.Id, token).ConfigureAwait(false)).Items.Where(e => e.RemoteId is not null && !changes.ContainsKey(e.RemoteId)))
                changes[known.RemoteId!] = await provider.GetAsync(binding, known.RemoteId!, token).ConfigureAwait(false) ?? new(known.RemoteId!, null);
        var metadata = provider is ISyncResourceMetadataProvider capabilities ? await capabilities.ReadMetadataAsync(binding, token).ConfigureAwait(false) : null;
        SyncState Apply(SyncState state, IReadOnlySet<string>? pending = null)
        {
            var merged = ApplyRemote(state, changes, full, nextCursor, provider.AtomicGroups, pendingContainers: pending);
            if (metadata is null) return merged;
            return merged with { Entities = merged.Items.Select(e => (e.Baseline is not null || e.Local is not null) &&
                (metadata.Shared ?? (e.RemoteId is { } id ? metadata.Items?.GetValueOrDefault(id) : null)) is { } fresh ? e with { Metadata = fresh } : e).ToArray() };
        }
        if (SyncContainers.IsCatalog(binding) || GoogleCalendarAccess.IsSubscriptions(binding))
            await ApplyCatalogPullAsync(binding, (pending, state) => Apply(state, pending), token).ConfigureAwait(false);
        else await UpdateAsync(binding.Id, state => Apply(state), token).ConfigureAwait(false);
    }

    private SyncState ApplyRemote(SyncState state, Dictionary<string, SyncRemoteEntity> changes, bool full,
        string? cursor, IReadOnlyList<string[]> groups, SyncEntityKind kindScope = SyncEntityKind.Item, Guid? parentScope = null, IReadOnlySet<string>? pendingContainers = null)
    {
        var entities = state.Items.ToList();
        var queue = state.Queue.ToList();
        var history = state.History.ToList();
        if (full) foreach (var entity in entities.Where(e => e.RemoteId is not null && e.Kind == kindScope && e.ParentId == parentScope))
            changes.TryAdd(entity.RemoteId!, new(entity.RemoteId!, null));
        foreach (var remote in changes.Values)
        {
            var index = entities.FindIndex(e => e.RemoteId == remote.Id && e.Kind == kindScope && e.ParentId == parentScope);
            if (index < 0)
            {
                if (remote.Fields is not null) entities.Add(new(Guid.NewGuid(), remote.Id, remote.Fields, remote.Fields, remote.ETag,
                    Metadata: remote.Metadata, Kind: kindScope, ParentId: parentScope));
                continue;
            }
            var entity = entities[index];
            if (queue.Any(o => o.EntityId == entity.LocalId && o.State is SyncOperationState.Sending or SyncOperationState.UnknownOutcome)) continue;
            // Keep the original base until a conflict has been explicitly resolved.
            var merge = SyncMerge.Merge(entity.Baseline, entity.Local, remote.Fields, groups);
            if (remote.Fields is null && entity.Baseline is not null && pendingContainers?.Contains(remote.Id) == true)
                merge = new(entity.Local ?? entity.Baseline, ["$deleted"]);
            if (entity.Kind == SyncEntityKind.Item && remote.Fields is null && entity.Local is not null &&
                MicrosoftTodoRelations.HasPendingChildren(state, entity.LocalId)) merge = new(entity.Local, ["$deleted"]);
            var conflicted = merge.Conflicts.Count > 0;
            entities[index] = entity with { Baseline = conflicted ? entity.Baseline : remote.Fields,
                Local = merge.Value, ETag = remote.ETag, Remote = conflicted ? remote.Fields : null,
                Conflicts = merge.Conflicts.ToArray(), Metadata = ContainerMetadata(state.Binding, entity, remote.Metadata, remote.Fields is null ? remote.Id : null) };
            if (!JsonNode.DeepEquals(entity.Local, merge.Value)) history.Add(new(entity.LocalId, clock.UtcNow, entity.Local, "remote-merge"));
            var previousOperation = queue.LastOrDefault(o => o.EntityId == entity.LocalId);
            queue.RemoveAll(o => o.EntityId == entity.LocalId);
            if (conflicted || !JsonNode.DeepEquals(merge.Value, remote.Fields))
            {
                if (!conflicted && previousOperation is not null && JsonNode.DeepEquals(previousOperation.Desired, merge.Value) &&
                    JsonNode.DeepEquals(entity.Baseline, remote.Fields) && entity.ETag == remote.ETag)
                {
                    queue.Add(previousOperation);
                    continue;
                }
                var kind = remote.Fields is null ? SyncOperationKind.Create : merge.Value is null ? SyncOperationKind.Delete : SyncOperationKind.Update;
                var identity = remote.Fields is not null ? remote.Id : GoogleCalendarAccess.IsSubscriptions(state.Binding) ? merge.Value?["id"]?.GetValue<string>() : null;
                queue.Add(new(Guid.NewGuid(), entity.LocalId, entity.Revision, kind, merge.Value, identity,
                    remote.ETag, conflicted ? SyncOperationState.Conflict : SyncEdits.RequiresApproval(state.Binding, remote.Fields, merge.Value)
                        ? SyncOperationState.AwaitingApproval : SyncOperationState.Pending, Baseline: remote.Fields));
            }
        }
        var mergedState = state with { Cursor = cursor, Entities = entities, Operations = queue,
            Recovery = history.Where(h => h.CreatedAt >= clock.UtcNow.AddDays(-30) ||
                queue.Any(o => o.EntityId == h.EntityId)).ToArray() };
        if (parentScope is { } parentId && entities.Any(e => e.ParentId == parentId &&
            !JsonNode.DeepEquals(e.Local, state.Items.FirstOrDefault(old => old.LocalId == e.LocalId)?.Local)))
            mergedState = SyncEdits.TouchParent(mergedState, parentId);
        if (kindScope == SyncEntityKind.Item)
            foreach (var parent in mergedState.Items.Where(e => e.Kind == SyncEntityKind.Item && e.Local is null && e.Baseline is null && e.Conflicts is not { Length: > 0 }))
                mergedState = SyncEdits.ResetChildren(mergedState, parent.LocalId, false, clock.UtcNow);
        return mergedState;
    }

    private SyncState Complete(SyncState state, SyncOperation sent, SyncWriteResult result)
    {
        if (state.Queue.All(o => o.Id != sent.Id)) return state;
        if (result.Status != SyncWriteStatus.Success)
        {
            var status = result.Status switch
            {
                SyncWriteStatus.Conflict => SyncOperationState.Retry,
                SyncWriteStatus.AuthenticationRequired => SyncOperationState.AuthenticationRequired,
                SyncWriteStatus.Forbidden => SyncOperationState.Forbidden,
                SyncWriteStatus.Retry => SyncOperationState.Retry,
                SyncWriteStatus.UnknownOutcome => SyncOperationState.UnknownOutcome,
                _ => SyncOperationState.Failed,
            };
            return state with { Binding = result.Status == SyncWriteStatus.Retry ? state.Binding with
                { NotBefore = clock.UtcNow + (result.RetryAfter ?? TimeSpan.FromMinutes(1)) } : state.Binding,
                Operations = state.Queue.Select(o => o.Id == sent.Id ? o with { State = status,
                RemoteId = result.Entity?.Id ?? o.RemoteId,
                RetryAt = clock.UtcNow + (result.RetryAfter ?? TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, Math.Min(o.Attempts, 10))))),
                ErrorCode = result.ErrorCode } : o).ToArray() };
        }
        var remote = result.Entity;
        var old = state.Items.Single(e => e.LocalId == sent.EntityId);
        var merge = old.Revision == sent.Revision ? new SyncMergeResult(remote?.Fields, []) :
            SyncMerge.Merge(sent.Desired, old.Local, remote?.Fields, _providers[state.Binding.Provider].AtomicGroups);
        var conflicted = merge.Conflicts.Count > 0;
        var updated = old with { RemoteId = remote?.Id, Baseline = conflicted ? sent.Desired : remote?.Fields,
            ETag = remote?.ETag, Metadata = ContainerMetadata(state.Binding, old, remote?.Metadata, sent.Kind == SyncOperationKind.Delete ? sent.RemoteId : null), Local = merge.Value,
            Remote = conflicted ? remote?.Fields : null, Conflicts = merge.Conflicts.ToArray() };
        var queue = state.Queue.Where(o => o.EntityId != old.LocalId).ToList();
        if (conflicted || !JsonNode.DeepEquals(updated.Local, remote?.Fields))
        {
            var waiting = state.Queue.LastOrDefault(o => o.EntityId == old.LocalId && o.Id != sent.Id);
            var kind = remote?.Fields is null ? SyncOperationKind.Create : updated.Local is null ? SyncOperationKind.Delete : SyncOperationKind.Update;
            var remoteId = remote?.Id;
            if (kind == SyncOperationKind.Create && state.Binding.Provider == SyncProviderKind.GoogleCalendar &&
                state.Binding.ResourcePath.EndsWith("/events", StringComparison.Ordinal)) remoteId = Guid.NewGuid().ToString("N");
            if (kind == SyncOperationKind.Create && GoogleCalendarAccess.IsSubscriptions(state.Binding)) remoteId = updated.Local?["id"]?.GetValue<string>();
            queue.Add(new(waiting?.Id ?? Guid.NewGuid(), old.LocalId, old.Revision, kind, updated.Local, remoteId, remote?.ETag,
                conflicted ? SyncOperationState.Conflict : SyncEdits.RequiresApproval(state.Binding, remote?.Fields, updated.Local)
                    ? SyncOperationState.AwaitingApproval : SyncOperationState.Pending, Baseline: remote?.Fields));
        }
        var history = state.History.Append(new(old.LocalId, clock.UtcNow, sent.Desired, result.ErrorCode ?? "write-acknowledged"));
        if (!JsonNode.DeepEquals(old.Local, updated.Local)) history = history.Append(new(old.LocalId, clock.UtcNow, old.Local, "write-rebase"));
        var completed = state with { Entities = state.Items.Select(e => e.LocalId == old.LocalId ? updated : e).ToArray(), Operations = queue,
            Recovery = history.ToArray() };
        if (old.ParentId is { } parentId) completed = SyncEdits.TouchParent(completed, parentId);
        return old.Kind == SyncEntityKind.Item && sent.Kind == SyncOperationKind.Delete && remote?.Fields is null
            ? SyncEdits.ResetChildren(completed, old.LocalId, updated.Local is not null, clock.UtcNow) : completed;
    }

    private async Task<SyncState> RequiredAsync(Guid id, CancellationToken token) =>
        await store.ReadAsync(id, token).ConfigureAwait(false) ?? throw new KeyNotFoundException("Sync binding not found.");

    public async Task UpdateAsync(Guid id, Func<SyncState, SyncState> change, CancellationToken token = default)
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var state = await RequiredAsync(id, token).ConfigureAwait(false);
            var updated = change(state);
            if (ReferenceEquals(state, updated) || await store.SaveAsync(updated, state.Version, token).ConfigureAwait(false)) return;
        }
        throw new InvalidOperationException("Concurrent synchronization changes exceeded retry limit.");
    }
}
