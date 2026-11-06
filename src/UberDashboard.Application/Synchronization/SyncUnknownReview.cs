using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Synchronization;

public sealed record SyncUnknownReview(SyncBinding Binding, string ScopePath, SyncOperation Operation,
    SyncEntity Entity, SyncRemoteEntity? Remote);

public sealed partial class SyncCoordinator
{
    /// <summary>Observes a known identity. The observation does not prove whether the earlier request ran.</summary>
    public async Task<SyncUnknownReview> ReadUnknownReviewAsync(Guid bindingId, SyncOperation expectedOperation,
        SyncEntity expectedEntity, CancellationToken token = default)
    {
        var identity = (await RequiredAsync(bindingId, token).ConfigureAwait(false)).Binding;
        var gate = _gates.GetOrAdd((identity.Provider, identity.ClientId, identity.AccountId), _ => new(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = await RequiredAsync(bindingId, token).ConfigureAwait(false);
            if (!SameUnknownBinding(state.Binding, identity)) throw new InvalidOperationException("The resource identity changed.");
            var (operation, entity) = ValidateUnknown(state, expectedOperation, expectedEntity);
            var scope = MicrosoftTodoRelations.Scope(state, entity);
            var remote = await ReadUnknownRemoteAsync(state.Binding, scope, operation.RemoteId!, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var latest = await RequiredAsync(bindingId, token).ConfigureAwait(false);
            if (!SameUnknownBinding(latest.Binding, identity)) throw new InvalidOperationException("The resource identity changed during observation.");
            ValidateUnknown(latest, operation, entity);
            if (MicrosoftTodoRelations.Scope(latest, entity).ResourcePath != scope.ResourcePath)
                throw new InvalidOperationException("The parent identity changed during observation.");
            return new(state.Binding, scope.ResourcePath, operation, entity, remote);
        }
        finally { gate.Release(); }
    }

    /// <summary>Retires uncertainty only into a user-resolved conflict. Never sends a provider write.</summary>
    public async Task<SyncEntity> PrepareUnknownConflictAsync(Guid bindingId, SyncUnknownReview reviewed, CancellationToken token = default)
    {
        var identity = (await RequiredAsync(bindingId, token).ConfigureAwait(false)).Binding;
        if (!SameUnknownBinding(identity, reviewed.Binding))
            throw new InvalidOperationException("The reviewed resource identity changed.");
        var gate = _gates.GetOrAdd((identity.Provider, identity.ClientId, identity.AccountId), _ => new(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = await RequiredAsync(bindingId, token).ConfigureAwait(false);
            if (!SameUnknownBinding(state.Binding, reviewed.Binding)) throw new InvalidOperationException("The resource identity changed.");
            var (operation, entity) = ValidateUnknown(state, reviewed.Operation, reviewed.Entity);
            var scope = MicrosoftTodoRelations.Scope(state, entity);
            if (scope.ResourcePath != reviewed.ScopePath) throw new InvalidOperationException("The reviewed parent identity changed.");
            var remote = await ReadUnknownRemoteAsync(state.Binding, scope, operation.RemoteId!, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!SameUnknownRemote(remote, reviewed.Remote)) throw new InvalidOperationException("The service snapshot changed. Review it again.");
            SyncEntity? prepared = null;
            await UpdateAsync(bindingId, current =>
            {
                if (!SameUnknownBinding(current.Binding, reviewed.Binding)) throw new InvalidOperationException("The resource identity changed during confirmation.");
                var (latestOperation, latest) = ValidateUnknown(current, reviewed.Operation, reviewed.Entity);
                if (MicrosoftTodoRelations.Scope(current, latest).ResourcePath != reviewed.ScopePath)
                    throw new InvalidOperationException("The parent changed during confirmation.");
                var merged = SyncMerge.Merge(latest.Baseline, latest.Local, remote?.Fields, _providers[current.Binding.Provider].AtomicGroups);
                if (latest.Revision > latestOperation.Revision)
                {
                    // A later edit can intentionally restore the original base. Compare it with what
                    // was actually sent as well, so the first merge cannot mistake that for no edit.
                    var newer = SyncMerge.Merge(latestOperation.Desired, latest.Local, merged.Value, _providers[current.Binding.Provider].AtomicGroups);
                    merged = new(newer.Value, merged.Conflicts.Concat(newer.Conflicts).Distinct(StringComparer.Ordinal).ToArray());
                }
                prepared = latest with { RemoteId = latestOperation.RemoteId, Local = merged.Value, Remote = remote?.Fields,
                    ETag = remote?.ETag, Revision = checked(latest.Revision + 1),
                    Conflicts = merged.Conflicts.Append("$unknown-result").Distinct(StringComparer.Ordinal).ToArray(),
                    Metadata = ContainerMetadata(current.Binding, latest, remote?.Metadata, remote?.Fields is null ? latestOperation.RemoteId : null) };
                return current with
                {
                    Entities = current.Items.Select(e => e.LocalId == latest.LocalId ? prepared : e).ToArray(),
                    Operations = current.Queue.Select(o => o.Id == latestOperation.Id ? o with { State = SyncOperationState.Conflict,
                        ErrorCode = "unknown-result-reviewed", ApprovalHash = null } : o).ToArray(),
                    Recovery = current.History.Concat(new[] {
                        new SyncRecovery(latest.LocalId, clock.UtcNow, latestOperation.Desired, "unknown-write-draft"),
                        new SyncRecovery(latest.LocalId, clock.UtcNow, latest.Local, "unknown-review-local"),
                        new SyncRecovery(latest.LocalId, clock.UtcNow, remote?.Fields, "unknown-review-service") }).ToArray()
                };
            }, token).ConfigureAwait(false);
            return prepared!;
        }
        finally { gate.Release(); }
    }

    private async Task<SyncRemoteEntity?> ReadUnknownRemoteAsync(SyncBinding identity, SyncBinding scope, string id, CancellationToken token)
    {
        var remote = await ReadWithQuotaAsync(identity, scope, id, token).ConfigureAwait(false);
        if (remote is not null && remote.Id != id) throw new InvalidDataException("The service returned a different resource identity.");
        return remote?.Fields is null ? null : remote with { Fields = (JsonObject)remote.Fields.DeepClone(), Metadata = (JsonObject?)remote.Metadata?.DeepClone() };
    }

    private static (SyncOperation Operation, SyncEntity Entity) ValidateUnknown(SyncState state, SyncOperation expected, SyncEntity expectedEntity)
    {
        var operation = state.Queue.SingleOrDefault(o => o.Id == expected.Id);
        var entity = operation is null ? null : state.Items.SingleOrDefault(e => e.LocalId == operation.EntityId);
        if (operation is null || entity is null || operation.State != SyncOperationState.UnknownOutcome || operation.Kind is not (SyncOperationKind.Update or SyncOperationKind.Delete) ||
            string.IsNullOrWhiteSpace(operation.RemoteId) || entity.RemoteId != operation.RemoteId ||
            entity.Conflicts is { Length: > 0 } || !SameUnknownOperation(operation, expected) || !SameReview(entity, expectedEntity) ||
            state.Queue.Any(o => o.EntityId == entity.LocalId && o.Id != operation.Id && o.State is SyncOperationState.Sending or SyncOperationState.UnknownOutcome))
            throw new InvalidOperationException("The unknown operation or local snapshot changed.");
        if (!MicrosoftTodoRelations.CanSend(state, entity)) throw new SyncDependentChangesException();
        return (operation, entity);
    }

    private static bool SameUnknownRemote(SyncRemoteEntity? a, SyncRemoteEntity? b) => a?.Id == b?.Id && a?.ETag == b?.ETag &&
        JsonNode.DeepEquals(a?.Fields, b?.Fields) && JsonNode.DeepEquals(a?.Metadata, b?.Metadata);

    private static bool SameUnknownBinding(SyncBinding a, SyncBinding b) => a.Id == b.Id && a.Provider == b.Provider &&
        a.ClientId == b.ClientId && a.AccountId == b.AccountId && a.ResourcePath == b.ResourcePath;

    private static bool SameUnknownOperation(SyncOperation a, SyncOperation b) => a.Id == b.Id && a.EntityId == b.EntityId && a.Revision == b.Revision &&
        a.Kind == b.Kind && a.RemoteId == b.RemoteId && a.ETag == b.ETag && a.State == b.State && a.Attempts == b.Attempts &&
        a.RetryAt == b.RetryAt && a.ErrorCode == b.ErrorCode && a.DependsOn == b.DependsOn && a.ApprovalHash == b.ApprovalHash &&
        JsonNode.DeepEquals(a.Desired, b.Desired) && JsonNode.DeepEquals(a.Baseline, b.Baseline);
}
