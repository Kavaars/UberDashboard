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
        if (previous?.Kind == SyncEntityKind.Item && desired is null && MicrosoftTodoRelations.HasPendingChildren(state, entityId))
            throw new SyncDependentChangesException();
        if (previous is { Kind: not SyncEntityKind.Item }) ValidateParent(state, previous.ParentId);
        if (previous?.Conflicts is { Length: > 0 }) throw new InvalidOperationException("Resolve the conflict before editing this item.");
        var entity = previous is null
            ? new SyncEntity(entityId, GoogleCalendarAccess.IsSubscriptions(state.Binding) ? desired?["id"]?.GetValue<string>() : null, null, Clone(desired), Revision: 1)
            : previous with { Local = Clone(desired), Revision = previous.Revision + 1 };
        if (GoogleCalendarAccess.IsSubscriptions(state.Binding) && desired is not null)
        {
            var id = desired["id"]?.GetValue<string>();
            if (id is null || !GoogleCalendarAccess.ValidCalendarId(id) || previous?.Local is not null && previous.Local["id"]?.GetValue<string>() != id ||
                state.Items.Any(e => e.LocalId != entityId && e.RemoteId == id && e.Local is not null)) throw new InvalidOperationException("Invalid, duplicate or changed subscription identity.");
            entity = entity with { RemoteId = id };
        }
        var queue = state.Queue.Where(o => o.EntityId != entityId || o.State is SyncOperationState.Sending or SyncOperationState.UnknownOutcome).ToList();
        var predecessor = queue.LastOrDefault(o => o.EntityId == entityId);
        if (!JsonNode.DeepEquals(entity.Baseline, desired) || predecessor is not null)
        {
            var kind = entity.RemoteId is null || entity.Baseline is null ? SyncOperationKind.Create : desired is null ? SyncOperationKind.Delete : SyncOperationKind.Update;
            if (desired is not null || entity.RemoteId is not null || predecessor is not null)
                queue.Add(new(Guid.NewGuid(), entityId, entity.Revision, kind, Clone(desired), kind == SyncOperationKind.Create
                    ? state.Binding.Provider == SyncProviderKind.GoogleCalendar && state.Binding.ResourcePath.EndsWith("/events", StringComparison.Ordinal)
                        ? Guid.NewGuid().ToString("N") : GoogleCalendarAccess.IsSubscriptions(state.Binding) ? entity.RemoteId : null : entity.RemoteId,
                    entity.ETag, RequiresApproval(state.Binding, entity.Baseline, desired)
                        ? SyncOperationState.AwaitingApproval : SyncOperationState.Pending, DependsOn: predecessor?.Id, Baseline: Clone(entity.Baseline)));
        }
        var history = previous is null ? state.History : state.History.Append(new(entityId, now, Clone(previous.Local), "local-edit")).ToArray();
        var changed = state with
        {
            Entities = state.Items.Where(e => e.LocalId != entityId).Append(entity).ToArray(),
            Operations = queue,
            Recovery = history,
        };
        changed = PruneHistory(changed, now);
        return entity.ParentId is { } parentId ? TouchParent(changed, parentId) : changed;
    }

    public static SyncState PruneHistory(SyncState state, DateTimeOffset now)
    {
        var protectedIds = state.Queue.Select(o => o.EntityId).Concat(state.Items.Where(e => e.Conflicts is { Length: > 0 }).Select(e => e.LocalId)).ToHashSet();
        var retained = state.History.Where(h => h.CreatedAt >= now.AddDays(-30) || protectedIds.Contains(h.EntityId)).ToArray();
        return retained.Length == state.History.Count ? state : state with { Recovery = retained };
    }

    internal static SyncState TouchParent(SyncState state, Guid parentId) => state with
        { Entities = state.Items.Select(e => e.LocalId == parentId ? e with { Revision = checked(e.Revision + 1) } : e).ToArray() };

    public static SyncState EditChild(SyncState state, Guid parentId, Guid entityId, SyncEntityKind kind, JsonObject? desired, DateTimeOffset now)
    {
        if (state.Binding.Provider != SyncProviderKind.MicrosoftTodo || kind is not (SyncEntityKind.ChecklistItem or SyncEntityKind.LinkedResource) ||
            MicrosoftTodoRelations.TaskCollectionPath(state.Binding.ResourcePath) != state.Binding.ResourcePath)
            throw new InvalidOperationException("This collection does not support related task items.");
        ValidateParent(state, parentId);
        var existing = state.Items.FirstOrDefault(e => e.LocalId == entityId);
        if (existing is not null && (existing.ParentId != parentId || existing.Kind != kind))
            throw new InvalidOperationException("An item cannot change its parent or type.");
        if (existing is null && desired is not null)
            state = state with { Entities = state.Items.Append(new(entityId, null, null, null, Kind: kind, ParentId: parentId)).ToArray() };
        return Edit(state, entityId, desired, now);
    }

    /// <summary>A confirmed parent deletion or an explicit conflict choice resets remote identities, never titles.</summary>
    public static SyncState ResetChildren(SyncState state, Guid parentId, bool keepLocal, DateTimeOffset now)
    {
        var children = state.Items.Where(e => e.ParentId == parentId).ToArray();
        var ids = children.Select(e => e.LocalId).ToHashSet();
        if (state.Queue.Any(o => ids.Contains(o.EntityId) && o.State == SyncOperationState.Sending)) throw new SyncDependentChangesException();
        state = state with { Entities = state.Items.Select(e => ids.Contains(e.LocalId)
            ? e with { RemoteId = null, Baseline = null, Local = null, Remote = null, Conflicts = null, ETag = null, Metadata = null } : e).ToArray(),
            Operations = state.Queue.Where(o => !ids.Contains(o.EntityId)).ToArray(),
            Recovery = state.History.Concat(children.Where(e => e.Local is not null || e.Baseline is not null)
                .Select(e => new SyncRecovery(e.LocalId, now, Clone(e.Local ?? e.Baseline), "parent-deletion-resolved"))).ToArray() };
        if (keepLocal) foreach (var child in children.Where(e => e.Local is not null)) state = Edit(state, child.LocalId, child.Local, now);
        return state;
    }

    private static void ValidateParent(SyncState state, Guid? parentId)
    {
        if (!state.Items.Any(e => e.LocalId == parentId && e.Kind == SyncEntityKind.Item && e.Local is not null && e.Conflicts is not { Length: > 0 }) ||
            state.Queue.Any(o => o.EntityId == parentId && o.Kind == SyncOperationKind.Delete))
            throw new InvalidOperationException("The parent is unavailable or has an unresolved conflict.");
    }

    public static SyncState Approve(SyncState state, Guid operationId, SyncOperation? expected = null)
    {
        var operation = state.Queue.Single(o => o.Id == operationId);
        if (operation.State != SyncOperationState.AwaitingApproval) throw new InvalidOperationException("Operation is not awaiting approval.");
        if (expected is not null && (operation.Revision != expected.Revision || operation.Kind != expected.Kind || operation.RemoteId != expected.RemoteId ||
            !JsonNode.DeepEquals(operation.Desired, expected.Desired) || !JsonNode.DeepEquals(operation.Baseline, expected.Baseline)))
            throw new InvalidOperationException("The reviewed consequences changed.");
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
