using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Synchronization;

/// <summary>Pure state transitions used inside the store's optimistic transaction.</summary>
public static class SyncEdits
{
    public static SyncState Edit(SyncState state, Guid entityId, JsonObject? desired, DateTimeOffset now)
    {
        var previous = state.Items.FirstOrDefault(e => e.LocalId == entityId);
        if (previous is null && desired is null) return state;
        if (previous is not null && JsonNode.DeepEquals(previous.Local, desired)) return state;
        if (previous?.Conflicts is { Length: > 0 }) throw new InvalidOperationException("Resolve the conflict before editing this item.");
        var entity = previous is null
            ? new SyncEntity(entityId, null, null, Clone(desired), Revision: 1)
            : previous with { Local = Clone(desired), Revision = previous.Revision + 1 };
        var queue = state.Queue.Where(o => o.EntityId != entityId || o.State is SyncOperationState.Sending or SyncOperationState.UnknownOutcome).ToList();
        var predecessor = queue.LastOrDefault(o => o.EntityId == entityId);
        if (!JsonNode.DeepEquals(entity.Baseline, desired) || predecessor is not null)
        {
            var kind = entity.RemoteId is null ? SyncOperationKind.Create : desired is null ? SyncOperationKind.Delete : SyncOperationKind.Update;
            if (desired is not null || entity.RemoteId is not null || predecessor is not null)
                queue.Add(new(Guid.NewGuid(), entityId, entity.Revision, kind, Clone(desired), entity.RemoteId ??
                    (state.Binding.Provider == SyncProviderKind.GoogleCalendar && state.Binding.ResourcePath.EndsWith("/events", StringComparison.Ordinal)
                        ? Guid.NewGuid().ToString("N") : null),
                    entity.ETag, RequiresApproval(state.Binding, entity.Baseline, desired)
                        ? SyncOperationState.AwaitingApproval : SyncOperationState.Pending, DependsOn: predecessor?.Id, Baseline: Clone(entity.Baseline)));
        }
        var history = previous is null ? state.History : state.History.Append(new(entityId, now, Clone(previous.Local), "local-edit")).ToArray();
        return state with
        {
            Entities = state.Items.Where(e => e.LocalId != entityId).Append(entity).ToArray(),
            Operations = queue,
            Recovery = history.Where(h => h.CreatedAt >= now.AddDays(-30) ||
                queue.Any(o => o.EntityId == h.EntityId)).ToArray(),
        };
    }

    public static SyncState Approve(SyncState state, Guid operationId)
    {
        var operation = state.Queue.Single(o => o.Id == operationId);
        if (operation.State != SyncOperationState.AwaitingApproval) throw new InvalidOperationException("Operation is not awaiting approval.");
        return state with { Operations = state.Queue.Select(o => o.Id == operationId
            ? o with { State = SyncOperationState.Pending, ApprovalHash = Hash(o.Desired) } : o).ToArray() };
    }

    public static bool RequiresApproval(SyncBinding binding, JsonObject? baseline, JsonObject? desired) =>
        binding.Provider == SyncProviderKind.GoogleCalendar &&
            (binding.ResourcePath.Contains("/acl", StringComparison.Ordinal) ||
             (baseline?["attendees"] as JsonArray)?.Count > 0 || (desired?["attendees"] as JsonArray)?.Count > 0) ||
        (desired is null && (binding.ResourcePath is "lists" or "calendars" or "users/me/calendarList"));

    public static string Hash(JsonObject? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value?.ToJsonString() ?? "null")));
    private static JsonObject? Clone(JsonObject? value) => (JsonObject?)value?.DeepClone();
}
