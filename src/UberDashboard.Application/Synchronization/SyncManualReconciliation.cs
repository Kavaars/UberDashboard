using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Synchronization;

public sealed partial class SyncCoordinator
{
    /// <summary>The user explicitly identifies an existing object; no POST is repeated or inferred from its title.</summary>
    public async Task LinkUnknownCreateAsync(Guid bindingId, SyncOperation expectedOperation, SyncEntity expectedCandidate, CancellationToken token = default, SyncEntity? expectedTarget = null)
    {
        var identity = (await RequiredAsync(bindingId, token).ConfigureAwait(false)).Binding;
        var gate = _gates.GetOrAdd((identity.Provider, identity.ClientId, identity.AccountId), _ => new(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = await RequiredAsync(bindingId, token).ConfigureAwait(false);
            var (operation, target, candidate) = ValidateLink(state, expectedOperation, expectedCandidate, expectedTarget);
            var scope = MicrosoftTodoRelations.Scope(state, target);
            var remote = await ReadWithQuotaAsync(state.Binding, scope, candidate.RemoteId!, token).ConfigureAwait(false);
            if (remote?.Fields is null || remote.Id != candidate.RemoteId || !JsonNode.DeepEquals(remote.Fields, candidate.Baseline) || !SyncContainers.CanIdentifyCreated(state.Binding, remote.Metadata))
                throw new InvalidOperationException("The selected service item changed. Synchronize and review it again.");
            await UpdateAsync(bindingId, current =>
            {
                var (_, latestTarget, latestCandidate) = ValidateLink(current, expectedOperation, expectedCandidate, expectedTarget);
                if (MicrosoftTodoRelations.Scope(current, latestTarget).ResourcePath != scope.ResourcePath)
                    throw new InvalidOperationException("The parent changed during reconciliation.");
                var adoptedChildren = current.Items.Where(e => e.ParentId == latestCandidate.LocalId).ToArray();
                if (adoptedChildren.Any(e => current.Items.Any(existing => existing.ParentId == latestTarget.LocalId &&
                    existing.Kind == e.Kind && existing.RemoteId is not null && existing.RemoteId == e.RemoteId)))
                    throw new SyncDependentChangesException();
                var prepared = current with { Entities = current.Items.Where(e => e.LocalId != latestCandidate.LocalId)
                    .Select(e => e.ParentId == latestCandidate.LocalId ? e with { ParentId = latestTarget.LocalId } : e).ToArray(),
                    Recovery = current.History.Append(new(latestCandidate.LocalId, clock.UtcNow, latestCandidate.Local, "manual-link-projection")).ToArray() };
                return Complete(prepared, operation, new(SyncWriteStatus.Success, remote, "manually-linked-unknown-create"));
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private static (SyncOperation Operation, SyncEntity Target, SyncEntity Candidate) ValidateLink(SyncState state,
        SyncOperation expectedOperation, SyncEntity expectedCandidate, SyncEntity? expectedTarget)
    {
        var operation = state.Queue.Single(o => o.Id == expectedOperation.Id);
        var target = state.Items.Single(e => e.LocalId == operation.EntityId);
        var candidate = state.Items.Single(e => e.LocalId == expectedCandidate.LocalId);
        if (!SyncContainers.CanManuallyLink(state.Binding) || operation.State != SyncOperationState.UnknownOutcome ||
            operation.Kind != SyncOperationKind.Create || operation.RemoteId is not null ||
            operation.Revision != expectedOperation.Revision || !JsonNode.DeepEquals(operation.Desired, expectedOperation.Desired) ||
            expectedTarget is not null && (target.Revision != expectedTarget.Revision || !JsonNode.DeepEquals(target.Local, expectedTarget.Local)) ||
            target.LocalId == candidate.LocalId || target.Kind != candidate.Kind || target.ParentId != candidate.ParentId ||
            candidate.Local is null || candidate.Baseline is null || !SyncContainers.CanIdentifyCreated(state.Binding, candidate.Metadata) || string.IsNullOrWhiteSpace(candidate.RemoteId) || candidate.RemoteId != expectedCandidate.RemoteId ||
            candidate.Revision != expectedCandidate.Revision || !JsonNode.DeepEquals(candidate.Local, expectedCandidate.Local) ||
            !JsonNode.DeepEquals(candidate.Baseline, expectedCandidate.Baseline) || !MicrosoftTodoRelations.CanSend(state, target))
            throw new InvalidOperationException("The uncertain operation or selected item changed.");
        if (candidate.Conflicts is { Length: > 0 } || state.Queue.Any(o => o.EntityId == candidate.LocalId) ||
            MicrosoftTodoRelations.HasPendingChildren(state, candidate.LocalId)) throw new SyncDependentChangesException();
        return (operation, target, candidate);
    }

    private async Task<SyncRemoteEntity?> ReadWithQuotaAsync(SyncBinding identity, SyncBinding scope, string remoteId, CancellationToken token)
    {
        var resume = (await store.ListAsync(token).ConfigureAwait(false)).Where(b => b.Provider == identity.Provider &&
            b.ClientId == identity.ClientId && b.AccountId == identity.AccountId && b.NotBefore > clock.UtcNow)
            .Select(b => b.NotBefore!.Value).DefaultIfEmpty(clock.UtcNow).Max();
        if (resume > clock.UtcNow) throw new SyncReadException(resume - clock.UtcNow);
        try { return await _providers[identity.Provider].GetAsync(scope, remoteId, token).ConfigureAwait(false); }
        catch (SyncReadException error)
        {
            await UpdateAsync(identity.Id, s => s with { Binding = s.Binding with { NotBefore = clock.UtcNow +
                (error.RetryAfter ?? TimeSpan.FromMinutes(1)) }, ConnectionStatus = SyncConnectionStatus.Throttled }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
