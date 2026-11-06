using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Synchronization;

public sealed partial class SyncCoordinator
{
    public Task PruneRecoveryAsync(Guid bindingId, CancellationToken token = default) =>
        UpdateAsync(bindingId, state => SyncEdits.PruneHistory(state, clock.UtcNow), token);

    /// <summary>Restores a reviewed copy as an ordinary offline edit, never as a direct provider write.</summary>
    public Task RestoreRecoveryAsync(Guid bindingId, SyncRecovery expectedEntry, SyncEntity expectedCurrent,
        JsonObject chosen, CancellationToken token = default) =>
        GuardedEditAsync(bindingId, expectedEntry.EntityId, chosen, state =>
        {
            var current = state.Items.Single(e => e.LocalId == expectedEntry.EntityId);
            if (expectedEntry.Value is null || !state.History.Any(h => SameRecovery(h, expectedEntry)) ||
                !SameReview(current, expectedCurrent) || current.Conflicts is { Length: > 0 })
                throw new InvalidOperationException("The history copy or current item changed. Review it again.");
            var changed = SyncEdits.Edit(state, current.LocalId, chosen, clock.UtcNow);
            return changed with { Recovery = changed.History.Append(new(current.LocalId, clock.UtcNow,
                (JsonObject)chosen.DeepClone(), "history-restored")).ToArray() };
        }, token);

    /// <summary>Removes only the selected local history copy; content and outbox remain unchanged.</summary>
    public Task RemoveRecoveryAsync(Guid bindingId, SyncRecovery expected, CancellationToken token = default) =>
        UpdateAsync(bindingId, state =>
        {
            var index = state.History.ToList().FindIndex(h => SameRecovery(h, expected));
            if (index < 0) throw new InvalidOperationException("The history copy changed or was already removed.");
            return state with { Recovery = state.History.Where((_, i) => i != index).ToArray() };
        }, token);

    private static bool SameRecovery(SyncRecovery a, SyncRecovery b) => a.EntityId == b.EntityId &&
        a.CreatedAt == b.CreatedAt && a.Reason == b.Reason && JsonNode.DeepEquals(a.Value, b.Value);

    private static bool SameReview(SyncEntity a, SyncEntity b) => a.LocalId == b.LocalId && a.Revision == b.Revision &&
        a.RemoteId == b.RemoteId && a.ETag == b.ETag && a.Kind == b.Kind && a.ParentId == b.ParentId &&
        JsonNode.DeepEquals(a.Baseline, b.Baseline) && JsonNode.DeepEquals(a.Local, b.Local) && JsonNode.DeepEquals(a.Remote, b.Remote) &&
        JsonNode.DeepEquals(a.Metadata, b.Metadata) && (a.Conflicts ?? []).SequenceEqual(b.Conflicts ?? [], StringComparer.Ordinal);
}
