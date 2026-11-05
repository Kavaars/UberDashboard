using System.Text.Json;
using System.Text.Json.Nodes;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Tests.Synchronization;

public sealed class RelatedSyncTests
{
    [Fact]
    public async Task A_manual_link_review_cannot_apply_after_the_current_local_draft_changes()
    {
        var (store, provider, coordinator, operation, candidate) = await UnknownRootAsync();
        var reviewed = store.State.Items.Single(e => e.LocalId == operation.EntityId);
        await coordinator.EditAsync(store.State.Binding.Id, operation.EntityId, Fields("title", "Edited after review"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.LinkUnknownCreateAsync(store.State.Binding.Id, operation, candidate, expectedTarget: reviewed));
        Assert.Single(provider.Writes); Assert.Contains(store.State.Queue, o => o.Id == operation.Id && o.State == SyncOperationState.UnknownOutcome);
    }

    [Fact]
    public async Task Pausing_preserves_parent_child_queue_and_resuming_does_not_clear_the_account_limit()
    {
        var (store, provider, coordinator) = Setup(); var parent = Guid.NewGuid();
        await coordinator.EditAsync(store.State.Binding.Id, parent, Fields("title", "Task"));
        await coordinator.EditChildAsync(store.State.Binding.Id, parent, Guid.NewGuid(), SyncEntityKind.ChecklistItem, Fields("displayName", "Step"));
        store.State = store.State with { ConnectionStatus = SyncConnectionStatus.Synchronizing, Binding = store.State.Binding with { NotBefore = DateTimeOffset.UtcNow.AddHours(1) } };
        await coordinator.SetEnabledAsync(store.State.Binding.Id, false);
        Assert.Equal(SyncConnectionStatus.Idle, store.State.ConnectionStatus); Assert.Equal(2, store.State.Queue.Count);
        await coordinator.SetEnabledAsync(store.State.Binding.Id, true);
        Assert.Equal(SyncConnectionStatus.Throttled, store.State.ConnectionStatus); Assert.Empty(provider.Writes);
    }

    [Fact]
    public async Task Explicit_unknown_create_link_adopts_the_selected_id_without_repeating_post_or_matching_titles()
    {
        var (store, provider, coordinator, operation, candidate) = await UnknownRootAsync();
        await coordinator.LinkUnknownCreateAsync(store.State.Binding.Id, operation, candidate);
        Assert.Single(provider.Writes); Assert.Empty(store.State.Queue);
        var item = Assert.Single(store.State.Items);
        Assert.Equal(operation.EntityId, item.LocalId); Assert.Equal("server-id", item.RemoteId);
        Assert.Equal("Different service title", item.Local!["title"]!.ToString());
        Assert.Contains(store.State.History, h => h.Reason == "manually-linked-unknown-create");
    }

    [Fact]
    public async Task Manual_link_keeps_newer_local_edits_and_reparents_read_only_child_projections()
    {
        var (store, provider, coordinator, operation, candidate) = await UnknownRootAsync();
        await coordinator.EditAsync(store.State.Binding.Id, operation.EntityId, Fields("title", "Newer offline edit"));
        var cachedChild = new SyncEntity(Guid.NewGuid(), "remote-step", Fields("displayName", "Cached step"), Fields("displayName", "Cached step"),
            Kind: SyncEntityKind.ChecklistItem, ParentId: candidate.LocalId);
        store.State = store.State with { Entities = store.State.Items.Append(cachedChild).ToArray() };
        await coordinator.LinkUnknownCreateAsync(store.State.Binding.Id, operation, candidate);
        Assert.Single(provider.Writes);
        Assert.Equal(operation.EntityId, store.State.Items.Single(e => e.LocalId == cachedChild.LocalId).ParentId);
        Assert.Equal("Newer offline edit", store.State.Items.Single(e => e.LocalId == operation.EntityId).Local!["title"]!.ToString());
        Assert.Contains(store.State.Queue, o => o.State == SyncOperationState.Conflict); // Both sides changed this title.
    }

    [Fact]
    public async Task A_candidate_with_local_work_or_a_changed_server_snapshot_cannot_be_absorbed()
    {
        var (store, provider, coordinator, operation, candidate) = await UnknownRootAsync();
        provider.Page = provider.Page with { Items = [new("server-id", Fields("title", "Changed again"))] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.LinkUnknownCreateAsync(store.State.Binding.Id, operation, candidate));
        Assert.Contains(store.State.Queue, o => o.Id == operation.Id && o.State == SyncOperationState.UnknownOutcome);
        await coordinator.EditAsync(store.State.Binding.Id, candidate.LocalId, Fields("title", "Local work"));
        var changed = store.State.Items.Single(e => e.LocalId == candidate.LocalId);
        await Assert.ThrowsAsync<SyncDependentChangesException>(() => coordinator.LinkUnknownCreateAsync(store.State.Binding.Id, operation, changed));
        Assert.Equal(2, store.State.Items.Count); Assert.Single(provider.Writes);
    }

    [Fact]
    public async Task Unknown_checklist_create_can_be_linked_to_an_explicitly_selected_step()
    {
        var (store, provider, coordinator) = Setup(); var parent = Parent("remote"); var id = Guid.NewGuid();
        store.State = store.State with { Entities = [parent], Cursor = "cursor" };
        await coordinator.EditChildAsync(store.State.Binding.Id, parent.LocalId, id, SyncEntityKind.ChecklistItem, Fields("displayName", "Step"));
        provider.Unknown = true; await coordinator.SynchronizeAsync(store.State.Binding.Id);
        var operation = store.State.Queue.Single(o => o.EntityId == id);
        provider.Relations = [new(parent.LocalId, SyncEntityKind.ChecklistItem, [new("created-step", Fields("displayName", "Step"))])];
        await coordinator.SynchronizeAsync(store.State.Binding.Id);
        var candidate = store.State.Items.Single(e => e.RemoteId == "created-step");
        await coordinator.LinkUnknownCreateAsync(store.State.Binding.Id, operation, candidate);
        Assert.Empty(store.State.Queue); Assert.Single(provider.Writes);
        Assert.Equal("created-step", store.State.Items.Single(e => e.LocalId == id).RemoteId);
        Assert.Equal(2, store.State.Items.Count);
    }

    [Fact]
    public async Task Manual_link_respects_retry_after_and_retains_the_uncertain_operation()
    {
        var (store, provider, coordinator, operation, candidate) = await UnknownRootAsync();
        provider.GetError = new SyncReadException(TimeSpan.FromHours(1));
        await Assert.ThrowsAsync<SyncReadException>(() => coordinator.LinkUnknownCreateAsync(store.State.Binding.Id, operation, candidate));
        Assert.True(store.State.Binding.NotBefore > DateTimeOffset.UtcNow.AddMinutes(50));
        var gets = provider.Gets;
        await Assert.ThrowsAsync<SyncReadException>(() => coordinator.LinkUnknownCreateAsync(store.State.Binding.Id, operation, candidate));
        Assert.Equal(gets, provider.Gets); Assert.Contains(store.State.Queue, o => o.Id == operation.Id);
    }

    private static async Task<(Store, Provider, SyncCoordinator, SyncOperation, SyncEntity)> UnknownRootAsync()
    {
        var (store, provider, coordinator) = Setup(); var id = Guid.NewGuid();
        await coordinator.EditAsync(store.State.Binding.Id, id, Fields("title", "Original draft"));
        provider.Unknown = true; await coordinator.SynchronizeAsync(store.State.Binding.Id);
        var operation = Assert.Single(store.State.Queue);
        provider.Page = new([new("server-id", Fields("title", "Different service title"))], null, "cursor", false);
        await coordinator.SynchronizeAsync(store.State.Binding.Id);
        return (store, provider, coordinator, operation, store.State.Items.Single(e => e.RemoteId == "server-id"));
    }

    [Fact]
    public async Task Offline_child_waits_for_its_parent_and_is_sent_to_the_confirmed_task_id()
    {
        var (store, provider, coordinator) = Setup(); var parent = Guid.NewGuid(); var child = Guid.NewGuid();
        await coordinator.EditAsync(store.State.Binding.Id, parent, Fields("title", "Task"));
        await coordinator.EditChildAsync(store.State.Binding.Id, parent, child, SyncEntityKind.ChecklistItem, Fields("displayName", "Step"));
        Assert.Equal(2, store.State.Queue.Count);
        await coordinator.SynchronizeAsync(store.State.Binding.Id);
        Assert.Equal(new[] { "lists/list/tasks", "lists/list/tasks/parent-id/checklistItems" }, provider.Writes.Select(w => w.Path));
        Assert.Empty(store.State.Queue);
        Assert.Equal(parent, store.State.Items.Single(e => e.LocalId == child).ParentId);
    }

    [Fact]
    public async Task Unknown_parent_create_blocks_its_children_after_restart_without_repeating_post()
    {
        var (store, provider, coordinator) = Setup(); var parent = Guid.NewGuid();
        await coordinator.EditAsync(store.State.Binding.Id, parent, Fields("title", "Task"));
        await coordinator.EditChildAsync(store.State.Binding.Id, parent, Guid.NewGuid(), SyncEntityKind.LinkedResource, Fields("displayName", "Source"));
        provider.Unknown = true;
        await coordinator.SynchronizeAsync(store.State.Binding.Id);
        await new SyncCoordinator(store, [provider], new Clock()).SynchronizeAsync(store.State.Binding.Id);
        Assert.Single(provider.Writes);
        Assert.Contains(store.State.Queue, o => o.State == SyncOperationState.UnknownOutcome);
        Assert.Contains(store.State.Queue, o => o.State == SyncOperationState.Pending);
    }

    [Fact]
    public async Task Relations_are_refreshed_even_when_the_parent_delta_is_empty_and_ids_are_scoped_by_parent()
    {
        var (store, provider, coordinator) = Setup(); var first = Parent("one"); var second = Parent("two");
        store.State = store.State with { Entities = [first, second], Cursor = "cursor" };
        provider.Relations = [new(first.LocalId, SyncEntityKind.ChecklistItem, [new("same-id", Fields("displayName", "First"))]),
            new(second.LocalId, SyncEntityKind.ChecklistItem, [new("same-id", Fields("displayName", "Second"))])];
        await coordinator.SynchronizeAsync(store.State.Binding.Id);
        Assert.Equal(2, store.State.Items.Count(e => e.Kind == SyncEntityKind.ChecklistItem));
        provider.Relations = [new(first.LocalId, SyncEntityKind.ChecklistItem, []), provider.Relations[1]];
        await coordinator.SynchronizeAsync(store.State.Binding.Id);
        Assert.Null(store.State.Items.Single(e => e.ParentId == first.LocalId).Local);
        Assert.NotNull(store.State.Items.Single(e => e.ParentId == second.LocalId).Local);
    }

    [Fact]
    public async Task Root_full_snapshot_does_not_delete_unrelated_cached_children()
    {
        var (store, provider, coordinator) = Setup(); var parent = Parent("remote"); var child = Child(parent);
        store.State = store.State with { Entities = [parent, child] };
        provider.Page = new([new(parent.RemoteId!, parent.Local)], null, "cursor", true);
        provider.Relations = [new(parent.LocalId, child.Kind, [new(child.RemoteId!, child.Local)])];
        await coordinator.SynchronizeAsync(store.State.Binding.Id);
        Assert.NotNull(store.State.Items.Single(e => e.LocalId == child.LocalId).Local);
    }

    [Fact]
    public async Task Parent_delete_is_blocked_until_pending_child_changes_are_reviewed_or_synchronized()
    {
        var (store, _, coordinator) = Setup(); var parent = Parent("remote"); var child = Child(parent);
        store.State = store.State with { Entities = [parent, child] };
        await coordinator.EditAsync(store.State.Binding.Id, child.LocalId, Fields("displayName", "Offline edit"));
        await Assert.ThrowsAsync<SyncDependentChangesException>(() => coordinator.EditAsync(store.State.Binding.Id, parent.LocalId, null));
        Assert.NotNull(store.State.Items.Single(e => e.LocalId == parent.LocalId).Local); Assert.Single(store.State.Queue);
    }

    [Fact]
    public async Task Remote_parent_deletion_against_pending_child_edit_becomes_a_parent_conflict_and_keep_recreates_the_tree()
    {
        var (store, provider, coordinator) = Setup(); var parent = Parent("remote"); var child = Child(parent);
        store.State = store.State with { Entities = [parent, child] };
        await coordinator.EditAsync(store.State.Binding.Id, child.LocalId, Fields("displayName", "Keep this"));
        provider.Page = new([new("remote", null)], null, "cursor", false);
        await coordinator.SynchronizeAsync(store.State.Binding.Id);
        var conflicted = store.State.Items.Single(e => e.LocalId == parent.LocalId);
        Assert.Contains("$deleted", conflicted.Conflicts!); Assert.Empty(provider.Writes);
        await coordinator.ResolveAsync(store.State.Binding.Id, parent.LocalId, conflicted.Local, expected: conflicted);
        Assert.Null(store.State.Items.Single(e => e.LocalId == child.LocalId).RemoteId);
        provider.Page = new([], null, "cursor", false);
        await coordinator.SynchronizeAsync(store.State.Binding.Id);
        Assert.Equal(SyncOperationKind.Create, provider.Writes.First().Operation.Kind);
        Assert.Equal("lists/list/tasks/parent-id/checklistItems", provider.Writes.Last().Path);
        Assert.Equal(SyncOperationKind.Create, provider.Writes.Last().Operation.Kind);
    }

    [Fact]
    public async Task Remote_parent_delete_can_be_accepted_explicitly_with_child_drafts_archived()
    {
        var (store, provider, coordinator) = Setup(); var parent = Parent("remote"); var child = Child(parent);
        store.State = store.State with { Entities = [parent, child] };
        await coordinator.EditAsync(store.State.Binding.Id, child.LocalId, Fields("displayName", "Keep in history"));
        provider.Page = new([new("remote", null)], null, "cursor", false);
        await coordinator.SynchronizeAsync(store.State.Binding.Id);
        var snapshot = store.State.Items.Single(e => e.LocalId == parent.LocalId);
        await coordinator.ResolveAsync(store.State.Binding.Id, parent.LocalId, null, expected: snapshot);
        Assert.Empty(store.State.Queue); Assert.Null(store.State.Items.Single(e => e.LocalId == child.LocalId).Local);
        Assert.Contains(store.State.History, h => h.EntityId == child.LocalId && h.Value?["displayName"]?.ToString() == "Keep in history");
    }

    [Fact]
    public async Task Child_edit_against_remote_delete_is_a_conflict_and_an_uncertain_child_create_is_never_replayed()
    {
        var (store, provider, coordinator) = Setup(); var parent = Parent("remote"); var child = Child(parent);
        store.State = store.State with { Entities = [parent, child], Cursor = "cursor" };
        await coordinator.EditAsync(store.State.Binding.Id, child.LocalId, Fields("displayName", "Changed"));
        provider.Relations = [new(parent.LocalId, child.Kind, [])];
        await coordinator.SynchronizeAsync(store.State.Binding.Id);
        Assert.Equal(SyncOperationState.Conflict, Assert.Single(store.State.Queue).State);
        var other = Guid.NewGuid(); await coordinator.EditChildAsync(store.State.Binding.Id, parent.LocalId, other, SyncEntityKind.LinkedResource, Fields("displayName", "Link"));
        provider.Unknown = true;
        await coordinator.SynchronizeAsync(store.State.Binding.Id); await coordinator.SynchronizeAsync(store.State.Binding.Id);
        Assert.Single(provider.Writes);
        Assert.Equal(SyncOperationState.UnknownOutcome, store.State.Queue.Single(o => o.EntityId == other).State);
    }

    [Fact]
    public async Task Parent_editor_snapshot_is_invalidated_by_a_new_child_edit()
    {
        var (store, _, coordinator) = Setup(); var parent = Parent("remote"); var child = Child(parent);
        store.State = store.State with { Entities = [parent, child] };
        await coordinator.EditAsync(store.State.Binding.Id, child.LocalId, Fields("displayName", "New edit"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.EditFromSnapshotAsync(store.State.Binding.Id, parent.LocalId,
            parent.Local, parent.Revision, Fields("title", "Old parent draft")));
        Assert.Equal("New edit", store.State.Items.Single(e => e.LocalId == child.LocalId).Local!["displayName"]!.ToString());
    }

    [Fact]
    public async Task Child_cannot_change_parent_or_attach_to_a_deleted_task()
    {
        var (store, _, coordinator) = Setup(); var parent = Parent("remote"); var child = Child(parent); var other = Parent("other");
        store.State = store.State with { Entities = [parent, child, other] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.EditChildAsync(store.State.Binding.Id, other.LocalId, child.LocalId, child.Kind, child.Local));
        store.State = store.State with { Entities = [parent with { Local = null }, child] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.EditChildAsync(store.State.Binding.Id, parent.LocalId, Guid.NewGuid(), child.Kind, child.Local));
    }

    private static JsonObject Fields(string key, string value) => new() { [key] = value };
    private static SyncEntity Parent(string id) => new(Guid.NewGuid(), id, Fields("title", "Task"), Fields("title", "Task"));
    private static SyncEntity Child(SyncEntity parent) => new(Guid.NewGuid(), "child-id", Fields("displayName", "Step"), Fields("displayName", "Step"), Kind: SyncEntityKind.ChecklistItem, ParentId: parent.LocalId);
    private static (Store, Provider, SyncCoordinator) Setup()
    {
        var store = new Store(new(new(Guid.NewGuid(), SyncProviderKind.MicrosoftTodo, "client", "account", "lists/list/tasks", "Test", true)));
        var provider = new Provider(); return (store, provider, new(store, [provider], new Clock()));
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Store(SyncState state) : ISyncStore
    {
        public SyncState State { get; set; } = state;
        public Task<IReadOnlyList<SyncBinding>> ListAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<SyncBinding>>([State.Binding]);
        public Task<SyncState?> ReadAsync(Guid id, CancellationToken token = default) => Task.FromResult<SyncState?>(JsonSerializer.Deserialize<SyncState>(JsonSerializer.Serialize(State)));
        public Task<bool> SaveAsync(SyncState value, long expectedVersion, CancellationToken token = default)
        { if (expectedVersion != State.Version) return Task.FromResult(false); State = value with { Version = expectedVersion + 1 }; return Task.FromResult(true); }
    }
    private sealed class Provider : ISyncProvider, ISyncRelationProvider
    {
        public SyncProviderKind Kind => SyncProviderKind.MicrosoftTodo;
        public IReadOnlyList<string[]> AtomicGroups => [];
        public SyncPage Page { get; set; } = new([], null, "cursor", false);
        public IReadOnlyList<SyncRelationSnapshot> Relations { get; set; } = [];
        public bool Unknown { get; set; }
        public int Gets { get; private set; }
        public Exception? GetError { get; set; }
        public List<(string Path, SyncOperation Operation)> Writes { get; } = [];
        public Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token) => Task.FromResult(Page);
        public Task<IReadOnlyList<SyncRelationSnapshot>> ReadRelationsAsync(SyncBinding binding, IReadOnlyList<SyncEntity> parents, CancellationToken token) => Task.FromResult(Relations);
        public Task<SyncRemoteEntity?> GetAsync(SyncBinding binding, string id, CancellationToken token)
        { Gets++; if (GetError is not null) throw GetError; return Task.FromResult(Page.Items.Concat(Relations.SelectMany(r => r.Items)).FirstOrDefault(e => e.Id == id)); }
        public Task<SyncWriteResult> WriteAsync(SyncBinding binding, SyncOperation operation, CancellationToken token)
        {
            Writes.Add((binding.ResourcePath, operation));
            return Task.FromResult(Unknown ? new SyncWriteResult(SyncWriteStatus.UnknownOutcome) : new(SyncWriteStatus.Success,
                operation.Desired is null ? null : new(binding.ResourcePath.EndsWith("/tasks", StringComparison.Ordinal) ? "parent-id" : "child-id", operation.Desired)));
        }
    }
}
