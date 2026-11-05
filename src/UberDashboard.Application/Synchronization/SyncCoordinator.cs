using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using UberDashboard.Application.Abstractions;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Synchronization;

/// <summary>Pull/merge/push. No network call occurs within a database transaction.</summary>
public sealed class SyncCoordinator(ISyncStore store, IEnumerable<ISyncProvider> providers, IClock clock) : ISyncCoordinator
{
    private readonly ConcurrentDictionary<(SyncProviderKind Provider, string Client, string Account), SemaphoreSlim> _gates = new();
    private readonly IReadOnlyDictionary<SyncProviderKind, ISyncProvider> _providers = providers.ToDictionary(p => p.Kind);

    public Task SetEnabledAsync(Guid bindingId, bool enabled, CancellationToken token = default) =>
        UpdateAsync(bindingId, s => s with { Binding = s.Binding with { Enabled = enabled } }, token);

    public async Task EditAsync(Guid bindingId, Guid entityId, JsonObject? desired, CancellationToken token = default) =>
        await UpdateAsync(bindingId, s => SyncEdits.Edit(s, entityId, desired, clock.UtcNow), token).ConfigureAwait(false);

    public Task EditFromSnapshotAsync(Guid bindingId, Guid entityId, JsonObject? expectedLocal, long expectedRevision,
        JsonObject? desired, CancellationToken token = default) => UpdateAsync(bindingId, state =>
    {
        var current = state.Items.Single(e => e.LocalId == entityId);
        if (current.Revision != expectedRevision || !JsonNode.DeepEquals(current.Local, expectedLocal))
            throw new InvalidOperationException("The item changed while the editor was open. Reload before saving.");
        return SyncEdits.Edit(state, entityId, desired, clock.UtcNow);
    }, token);

    public async Task ApproveAsync(Guid bindingId, Guid operationId, CancellationToken token = default) =>
        await UpdateAsync(bindingId, s => SyncEdits.Approve(s, operationId), token).ConfigureAwait(false);

    public async Task ResolveAsync(Guid bindingId, Guid entityId, JsonObject? chosen, CancellationToken token = default, SyncEntity? expected = null)
    {
        var state = await RequiredAsync(bindingId, token).ConfigureAwait(false);
        var entity = state.Items.Single(e => e.LocalId == entityId);
        if (expected is not null && (expected.Revision != entity.Revision || !JsonNode.DeepEquals(expected.Local, entity.Local) ||
            !JsonNode.DeepEquals(expected.Remote, entity.Remote))) throw new InvalidOperationException("The conflict changed while it was being reviewed.");
        var remote = entity.RemoteId is null ? null : await _providers[state.Binding.Provider]
            .GetAsync(state.Binding, entity.RemoteId, token).ConfigureAwait(false);
        await UpdateAsync(bindingId, current =>
        {
            var latest = current.Items.Single(e => e.LocalId == entityId);
            if (latest.Revision != entity.Revision || !JsonNode.DeepEquals(latest.Local, entity.Local) ||
                !JsonNode.DeepEquals(remote?.Fields, latest.Remote))
                throw new InvalidOperationException("The remote item changed. Synchronize and review the conflict again.");
            var clean = latest with { Baseline = remote?.Fields, Local = remote?.Fields, ETag = remote?.ETag,
                RemoteId = remote?.Id, Conflicts = null, Remote = null };
            var reset = current with { Entities = current.Items.Select(e => e.LocalId == entityId ? clean : e).ToArray(),
                Operations = current.Queue.Where(o => o.EntityId != entityId).ToArray(),
                Recovery = current.History.Append(new(entityId, clock.UtcNow, latest.Local, "conflict-resolution")).ToArray() };
            return SyncEdits.Edit(reset, entityId, chosen, clock.UtcNow);
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
                    state.Items.Single(e => e.LocalId == o.EntityId).Conflicts is not { Length: > 0 });
                if (next is null) break;
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
                try { result = await provider.WriteAsync(state.Binding, next, token).ConfigureAwait(false); }
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
            var remote = await provider.GetAsync(state.Binding, operation.RemoteId!, token).ConfigureAwait(false);
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

        await UpdateAsync(binding.Id, state => ApplyRemote(state, changes, full, nextCursor, provider.AtomicGroups), token).ConfigureAwait(false);
    }

    private SyncState ApplyRemote(SyncState state, Dictionary<string, SyncRemoteEntity> changes, bool full,
        string? cursor, IReadOnlyList<string[]> groups)
    {
        var entities = state.Items.ToList();
        var queue = state.Queue.ToList();
        var history = state.History.ToList();
        if (full) foreach (var entity in entities.Where(e => e.RemoteId is not null))
            changes.TryAdd(entity.RemoteId!, new(entity.RemoteId!, null));
        foreach (var remote in changes.Values)
        {
            var index = entities.FindIndex(e => e.RemoteId == remote.Id);
            if (index < 0)
            {
                if (remote.Fields is not null) entities.Add(new(Guid.NewGuid(), remote.Id, remote.Fields, remote.Fields, remote.ETag, Metadata: remote.Metadata));
                continue;
            }
            var entity = entities[index];
            if (queue.Any(o => o.EntityId == entity.LocalId && o.State is SyncOperationState.Sending or SyncOperationState.UnknownOutcome)) continue;
            // Keep the original base until a conflict has been explicitly resolved.
            var merge = SyncMerge.Merge(entity.Baseline, entity.Local, remote.Fields, groups);
            var conflicted = merge.Conflicts.Count > 0;
            entities[index] = entity with { Baseline = conflicted ? entity.Baseline : remote.Fields,
                Local = merge.Value, ETag = remote.ETag, Remote = conflicted ? remote.Fields : null,
                Conflicts = merge.Conflicts.ToArray(), Metadata = remote.Metadata };
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
                queue.Add(new(Guid.NewGuid(), entity.LocalId, entity.Revision, kind, merge.Value, remote.Fields is null ? null : remote.Id,
                    remote.ETag, conflicted ? SyncOperationState.Conflict : SyncEdits.RequiresApproval(state.Binding, remote.Fields, merge.Value)
                        ? SyncOperationState.AwaitingApproval : SyncOperationState.Pending, Baseline: remote.Fields));
            }
        }
        return state with { Cursor = cursor, Entities = entities, Operations = queue,
            Recovery = history.Where(h => h.CreatedAt >= clock.UtcNow.AddDays(-30) ||
                queue.Any(o => o.EntityId == h.EntityId)).ToArray() };
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
            ETag = remote?.ETag, Metadata = remote?.Metadata, Local = merge.Value,
            Remote = conflicted ? remote?.Fields : null, Conflicts = merge.Conflicts.ToArray() };
        var queue = state.Queue.Where(o => o.EntityId != old.LocalId).ToList();
        if (conflicted || !JsonNode.DeepEquals(updated.Local, remote?.Fields))
        {
            var waiting = state.Queue.LastOrDefault(o => o.EntityId == old.LocalId && o.Id != sent.Id);
            var kind = remote?.Fields is null ? SyncOperationKind.Create : updated.Local is null ? SyncOperationKind.Delete : SyncOperationKind.Update;
            var remoteId = remote?.Id;
            if (kind == SyncOperationKind.Create && state.Binding.Provider == SyncProviderKind.GoogleCalendar &&
                state.Binding.ResourcePath.EndsWith("/events", StringComparison.Ordinal)) remoteId = Guid.NewGuid().ToString("N");
            queue.Add(new(waiting?.Id ?? Guid.NewGuid(), old.LocalId, old.Revision, kind, updated.Local, remoteId, remote?.ETag,
                conflicted ? SyncOperationState.Conflict : SyncEdits.RequiresApproval(state.Binding, remote?.Fields, updated.Local)
                    ? SyncOperationState.AwaitingApproval : SyncOperationState.Pending, Baseline: remote?.Fields));
        }
        var history = state.History.Append(new(old.LocalId, clock.UtcNow, sent.Desired, result.ErrorCode ?? "write-acknowledged"));
        if (!JsonNode.DeepEquals(old.Local, updated.Local)) history = history.Append(new(old.LocalId, clock.UtcNow, old.Local, "write-rebase"));
        return state with { Entities = state.Items.Select(e => e.LocalId == old.LocalId ? updated : e).ToArray(), Operations = queue,
            Recovery = history.ToArray() };
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
