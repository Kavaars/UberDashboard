namespace UberDashboard.Core.Synchronization;

/// <summary>Stable local parent identity selects the endpoint only after the parent has a confirmed remote ID.</summary>
public static class MicrosoftTodoRelations
{
    public static string? TaskCollectionPath(string path)
    {
        var parts = path.Split('/');
        return parts.Length is 3 or 5 && parts[0] == "lists" && parts[1].Length > 0 && parts[2] == "tasks" &&
            (parts.Length == 3 || parts[3].Length > 0 && parts[4] is "checklistItems" or "linkedResources" or "attachments")
            ? string.Join('/', parts.Take(3)) : null;
    }

    public static SyncBinding Scope(SyncState state, SyncEntity entity)
    {
        if (entity.Kind == SyncEntityKind.Item) return state.Binding;
        if (state.Binding.Provider != SyncProviderKind.MicrosoftTodo || !Enum.IsDefined(entity.Kind) || entity.ParentId is null ||
            TaskCollectionPath(state.Binding.ResourcePath) != state.Binding.ResourcePath)
            throw new InvalidDataException("Invalid related task collection.");
        var parent = state.Items.Single(e => e.LocalId == entity.ParentId && e.Kind == SyncEntityKind.Item);
        if (string.IsNullOrEmpty(parent.RemoteId)) throw new InvalidOperationException("The parent has not been uploaded yet.");
        return state.Binding with { ResourcePath = state.Binding.ResourcePath + "/" + Uri.EscapeDataString(parent.RemoteId) + "/" +
            (entity.Kind switch { SyncEntityKind.ChecklistItem => "checklistItems", SyncEntityKind.LinkedResource => "linkedResources", SyncEntityKind.FileAttachment => "attachments", _ => throw new InvalidDataException("Invalid task relation.") }) };
    }

    public static bool CanSend(SyncState state, SyncEntity entity) => entity.Kind == SyncEntityKind.Item ||
        state.Items.Any(p => p.LocalId == entity.ParentId && p.Kind == SyncEntityKind.Item && p.RemoteId is not null && p.Baseline is not null &&
            p.Local is not null && p.Conflicts is not { Length: > 0 } && !state.Queue.Any(o => o.EntityId == p.LocalId));

    public static bool HasPendingChildren(SyncState state, Guid parentId) =>
        state.Items.Any(e => e.ParentId == parentId && state.Queue.Any(o => o.EntityId == e.LocalId));
}
