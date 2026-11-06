using System.Text.Json;
using System.Text.Json.Nodes;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Tests.Synchronization;

public sealed class SyncRecoveryTests
{
    [Theory]
    [InlineData(SyncProviderKind.MicrosoftTodo)]
    [InlineData(SyncProviderKind.GoogleCalendar)]
    public async Task Restoring_history_is_offline_atomic_and_preserves_the_current_version_and_queue(SyncProviderKind kind)
    {
        var (store, provider, coordinator, entity, entry) = Setup(kind);
        await coordinator.RestoreRecoveryAsync(store.State.Binding.Id, entry, entity, entry.Value!);
        var saved = Assert.Single(store.State.Items); var queued = Assert.Single(store.State.Queue);
        Assert.True(JsonNode.DeepEquals(entry.Value, saved.Local)); Assert.Equal(entity.Revision + 1, saved.Revision);
        Assert.Equal(entity.RemoteId, queued.RemoteId); Assert.Equal(SyncOperationKind.Update, queued.Kind);
        Assert.Contains(store.State.History, h => JsonNode.DeepEquals(h.Value, entity.Local));
        Assert.Contains(store.State.History, h => h.Reason == "history-restored");
        Assert.Equal(0, provider.Reads); Assert.Equal(0, provider.Writes);
    }

    [Fact]
    public async Task Restoring_while_a_write_is_unknown_preserves_it_and_adds_a_dependent_revision()
    {
        var (store, _, coordinator, entity, entry) = Setup();
        var uncertain = new SyncOperation(Guid.NewGuid(), entity.LocalId, entity.Revision, SyncOperationKind.Update, entity.Local, entity.RemoteId, null, SyncOperationState.UnknownOutcome);
        store.State = store.State with { Operations = [uncertain] };
        await coordinator.RestoreRecoveryAsync(store.State.Binding.Id, entry, entity, entry.Value!);
        Assert.Equal(2, store.State.Queue.Count); Assert.Equal(SyncOperationState.UnknownOutcome, store.State.Queue[0].State);
        Assert.Equal(uncertain.Id, store.State.Queue[1].DependsOn); Assert.Equal(entity.Revision + 1, store.State.Queue[1].Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restoring_or_resolving_google_participants_still_requires_effect_approval(bool conflict)
    {
        var (store, provider, coordinator, entity, entry) = Setup(SyncProviderKind.GoogleCalendar);
        var chosen = (JsonObject)entry.Value!.DeepClone(); chosen["attendees"] = new JsonArray(new JsonObject { ["email"] = "guest@example.invalid" });
        if (conflict)
        {
            entity = entity with { Remote = entity.Baseline, Conflicts = ["summary"] }; store.State = store.State with { Entities = [entity] };
            provider.Remote = new(entity.RemoteId!, entity.Remote);
            await coordinator.ResolveAsync(store.State.Binding.Id, entity.LocalId, chosen, expected: entity);
        }
        else
        {
            entry = entry with { Value = chosen }; store.State = store.State with { Recovery = [entry] };
            await coordinator.RestoreRecoveryAsync(store.State.Binding.Id, entry, entity, chosen);
        }
        Assert.Equal(SyncOperationState.AwaitingApproval, Assert.Single(store.State.Queue).State); Assert.Equal(0, provider.Writes);
    }

    [Fact]
    public async Task History_restore_failure_does_not_commit_content_or_outbox()
    {
        var (store, _, coordinator, entity, entry) = Setup(); store.FailSave = true;
        await Assert.ThrowsAsync<IOException>(() => coordinator.RestoreRecoveryAsync(store.State.Binding.Id, entry, entity, entry.Value!));
        Assert.Empty(store.State.Queue); Assert.True(JsonNode.DeepEquals(entity.Local, store.State.Items[0].Local)); Assert.Single(store.State.History);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expiry_removes_old_unprotected_copies_and_keeps_pending_or_conflicted_history(bool conflict)
    {
        var (store, _, coordinator, entity, entry) = Setup(); var old = entry with { CreatedAt = DateTimeOffset.UtcNow.AddDays(-45) };
        store.State = store.State with { Entities = [entity with { Conflicts = conflict ? ["title"] : null }],
            Operations = conflict ? [] : [new(Guid.NewGuid(), entity.LocalId, 1, SyncOperationKind.Update, entity.Local, entity.RemoteId, null)],
            Recovery = [old, old with { EntityId = Guid.NewGuid() }, entry] };
        await coordinator.PruneRecoveryAsync(store.State.Binding.Id);
        Assert.Equal(2, store.State.History.Count); Assert.Contains(store.State.History, h => h.EntityId == old.EntityId && h.CreatedAt == old.CreatedAt && JsonNode.DeepEquals(h.Value, old.Value));
    }

    [Fact]
    public async Task A_stale_history_preview_cannot_replace_a_newer_local_edit()
    {
        var (store, _, coordinator, entity, entry) = Setup();
        await coordinator.EditAsync(store.State.Binding.Id, entity.LocalId, new JsonObject { ["title"] = "Newer" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RestoreRecoveryAsync(store.State.Binding.Id, entry, entity, entry.Value!));
        Assert.Equal("Newer", store.State.Items[0].Local!["title"]!.GetValue<string>()); Assert.Single(store.State.Queue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_modified_history_cannot_be_restored_or_removed(bool modified)
    {
        var (store, _, coordinator, entity, entry) = Setup();
        store.State = store.State with { Recovery = modified ? [entry with { Value = new JsonObject { ["title"] = "Changed" } }] : [] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RestoreRecoveryAsync(store.State.Binding.Id, entry, entity, entry.Value!));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RemoveRecoveryAsync(store.State.Binding.Id, entry));
        Assert.Empty(store.State.Queue); Assert.True(JsonNode.DeepEquals(entity.Local, store.State.Items[0].Local));
    }

    [Fact]
    public async Task Removing_a_history_copy_does_not_remove_current_content_or_unsent_work()
    {
        var (store, provider, coordinator, entity, entry) = Setup();
        await coordinator.EditAsync(store.State.Binding.Id, entity.LocalId, new JsonObject { ["title"] = "Unsent" });
        var operation = store.State.Queue[0];
        await coordinator.RemoveRecoveryAsync(store.State.Binding.Id, entry with { Value = (JsonObject)entry.Value!.DeepClone() });
        Assert.DoesNotContain(store.State.History, h => h.CreatedAt == entry.CreatedAt && h.Reason == entry.Reason);
        Assert.Equal(operation.Id, Assert.Single(store.State.Queue).Id); Assert.Equal("Unsent", store.State.Items[0].Local!["title"]!.GetValue<string>());
        Assert.Equal(0, provider.Writes);
    }

    [Fact]
    public async Task Deletion_markers_and_unresolved_conflicts_require_their_own_decision()
    {
        var (store, _, coordinator, entity, entry) = Setup();
        var deletion = entry with { Value = null }; store.State = store.State with { Recovery = [deletion] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RestoreRecoveryAsync(store.State.Binding.Id, deletion, entity, entry.Value!));
        store.State = store.State with { Recovery = [entry], Entities = [entity with { Conflicts = ["title"], Remote = entry.Value }] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RestoreRecoveryAsync(store.State.Binding.Id, entry, store.State.Items[0], entry.Value!));
        Assert.Empty(store.State.Queue);
    }

    [Theory]
    [InlineData(SyncProviderKind.MicrosoftTodo)]
    [InlineData(SyncProviderKind.GoogleCalendar)]
    public async Task Manual_conflict_resolution_keeps_the_reviewed_server_base_and_only_queues_the_result(SyncProviderKind kind)
    {
        var (store, provider, coordinator, entity, _) = Setup(kind); var remote = new JsonObject { [Title(kind)] = "Server", ["future"] = 42 };
        entity = entity with { Remote = remote, Conflicts = [Title(kind)] }; store.State = store.State with { Entities = [entity] };
        provider.Remote = new(entity.RemoteId!, remote, "fresh-etag"); var chosen = (JsonObject)remote.DeepClone(); chosen[Title(kind)] = "Manually merged";
        await coordinator.ResolveAsync(store.State.Binding.Id, entity.LocalId, chosen, expected: entity);
        Assert.True(JsonNode.DeepEquals(remote, store.State.Items[0].Baseline)); Assert.Equal("fresh-etag", store.State.Items[0].ETag);
        Assert.True(JsonNode.DeepEquals(chosen, Assert.Single(store.State.Queue).Desired)); Assert.Equal(0, provider.Writes); Assert.Equal(1, provider.Reads);
        Assert.Contains(store.State.History, h => h.Reason == "conflict-base" && JsonNode.DeepEquals(h.Value, entity.Baseline));
        Assert.Contains(store.State.History, h => h.Reason == "conflict-service" && JsonNode.DeepEquals(h.Value, remote));
    }

    [Fact]
    public async Task A_nonconflicted_item_cannot_be_reset_through_the_conflict_api()
    {
        var (store, provider, coordinator, entity, _) = Setup(); provider.Remote = new(entity.RemoteId!, entity.Local);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ResolveAsync(store.State.Binding.Id, entity.LocalId, new JsonObject { ["title"] = "Unexpected" }, expected: entity));
        Assert.Empty(store.State.Queue); Assert.Equal(0, provider.Reads);
    }

    [Fact]
    public async Task A_changed_conflict_base_is_rejected_even_when_local_revision_and_remote_values_match()
    {
        var (store, provider, coordinator, entity, _) = Setup(); var remote = new JsonObject { ["title"] = "Server" };
        var expected = entity with { Remote = remote, Conflicts = ["title"] };
        store.State = store.State with { Entities = [expected with { Baseline = new JsonObject { ["title"] = "Different base" } }] };
        provider.Remote = new(entity.RemoteId!, remote);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ResolveAsync(store.State.Binding.Id, entity.LocalId, expected.Local, expected: expected));
        Assert.Empty(store.State.Queue); Assert.Equal(0, provider.Reads);
    }

    private static string Title(SyncProviderKind kind) => kind == SyncProviderKind.MicrosoftTodo ? "title" : "summary";
    private static (Store Store, Provider Provider, SyncCoordinator Coordinator, SyncEntity Entity, SyncRecovery Entry) Setup(SyncProviderKind kind = SyncProviderKind.MicrosoftTodo)
    {
        var binding = new SyncBinding(Guid.NewGuid(), kind, "client", "account", kind == SyncProviderKind.MicrosoftTodo ? "lists/a/tasks" : "calendars/a/events", "Test");
        var fields = new JsonObject { [Title(kind)] = "Current" }; var entity = new SyncEntity(Guid.NewGuid(), "remote", fields, fields, Revision: 2);
        var entry = new SyncRecovery(entity.LocalId, DateTimeOffset.UtcNow.AddDays(-1), new JsonObject { [Title(kind)] = "Older" }, "local-edit");
        var store = new Store(new(binding, Entities: [entity], Recovery: [entry])); var provider = new Provider(kind);
        return (store, provider, new(store, [provider], new Clock()), entity, entry);
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Store(SyncState state) : ISyncStore
    {
        public bool FailSave { get; set; }
        public SyncState State { get; set; } = state;
        public Task<IReadOnlyList<SyncBinding>> ListAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<SyncBinding>>([State.Binding]);
        public Task<SyncState?> ReadAsync(Guid id, CancellationToken token = default) => Task.FromResult<SyncState?>(JsonSerializer.Deserialize<SyncState>(JsonSerializer.Serialize(State)));
        public Task<bool> SaveAsync(SyncState value, long version, CancellationToken token = default)
        { if (FailSave) throw new IOException("Test storage failure"); if (version != State.Version) return Task.FromResult(false); State = value with { Version = version + 1 }; return Task.FromResult(true); }
    }
    private sealed class Provider(SyncProviderKind kind) : ISyncProvider
    {
        public SyncProviderKind Kind => kind; public IReadOnlyList<string[]> AtomicGroups => [];
        public int Reads { get; private set; } public int Writes { get; private set; } public SyncRemoteEntity? Remote { get; set; }
        public Task<SyncRemoteEntity?> GetAsync(SyncBinding binding, string id, CancellationToken token) { Reads++; return Task.FromResult(Remote); }
        public Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token) => throw new InvalidOperationException("No pull expected");
        public Task<SyncWriteResult> WriteAsync(SyncBinding binding, SyncOperation operation, CancellationToken token) { Writes++; throw new InvalidOperationException("No write expected"); }
    }
}
