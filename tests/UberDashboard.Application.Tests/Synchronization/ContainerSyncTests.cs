using System.Text.Json;
using System.Text.Json.Nodes;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Tests.Synchronization;

public sealed class ContainerSyncTests
{
    [Fact]
    public async Task A_permission_loss_blocks_already_queued_content_writes_before_sending()
    {
        var (store, provider, coordinator, catalog, contents, parent, item) = Setup(SyncProviderKind.GoogleCalendar);
        await coordinator.EditAsync(contents.Id, item.LocalId, Item(contents.Provider, "Unsent"));
        store.Set(store.Get(catalog.Id) with { Entities = [parent with { Metadata = new JsonObject { ["accessRole"] = "reader" } }] });
        provider.ContentsPage = new([new(item.RemoteId!, item.Baseline)], null, null, true);
        await coordinator.SynchronizeAsync(contents.Id);
        Assert.Equal(SyncOperationState.Forbidden, Assert.Single(store.Get(contents.Id).Queue).State); Assert.Empty(provider.Writes);
        Assert.Equal("Unsent", store.Get(contents.Id).Items[0].Local!["summary"]!.GetValue<string>());
    }

    [Fact]
    public async Task Local_edits_do_not_wait_for_the_account_network_gate()
    {
        var (store, provider, coordinator, _, contents, _, item) = Setup(SyncProviderKind.MicrosoftTodo);
        await coordinator.EditAsync(contents.Id, item.LocalId, Item(contents.Provider, "First"));
        provider.ContentsPage = new([new(item.RemoteId!, item.Baseline)], null, null, true);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.OnWrite = async operation => { started.TrySetResult(true); await release.Task; return new(SyncWriteStatus.Success, new(operation.RemoteId!, operation.Desired)); };
        var cycle = coordinator.SynchronizeAsync(contents.Id);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await coordinator.EditAsync(contents.Id, item.LocalId, Item(contents.Provider, "Second")).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("Second", store.Get(contents.Id).Items[0].Local!["title"]!.GetValue<string>());
        }
        finally { release.TrySetResult(true); await cycle; }
        Assert.Equal("Second", store.Get(contents.Id).Items[0].Local!["title"]!.GetValue<string>()); Assert.Empty(store.Get(contents.Id).Queue);
    }
    [Theory]
    [InlineData(SyncProviderKind.MicrosoftTodo)]
    [InlineData(SyncProviderKind.GoogleCalendar)]
    public async Task Manual_create_reconciliation_cannot_adopt_a_protected_resource_after_ownership_changes(SyncProviderKind kind)
    {
        var (store, provider, coordinator, catalog, _, parent, _) = Setup(kind);
        var owned = kind == SyncProviderKind.MicrosoftTodo ? new JsonObject { ["isOwner"] = true, ["wellknownListName"] = "none" }
            : new JsonObject { ["accessRole"] = "owner", ["primary"] = false, ["isDataOwner"] = true };
        var candidate = parent with { Metadata = owned };
        var target = new SyncEntity(Guid.NewGuid(), null, null, Container(kind, "Uncertain"), Revision: 1);
        var operation = new SyncOperation(Guid.NewGuid(), target.LocalId, 1, SyncOperationKind.Create, target.Local, null, null, SyncOperationState.UnknownOutcome);
        store.Set(new(catalog, Entities: [candidate, target], Operations: [operation], FormatVersion: 3));
        var protectedMetadata = kind == SyncProviderKind.MicrosoftTodo ? new JsonObject { ["isOwner"] = true, ["wellknownListName"] = "defaultList" }
            : new JsonObject { ["accessRole"] = "owner", ["primary"] = true, ["isDataOwner"] = false };
        provider.Page = new([new(candidate.RemoteId!, candidate.Baseline, Metadata: protectedMetadata)], null, null, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.LinkUnknownCreateAsync(catalog.Id, operation, candidate));
        Assert.Null(store.Get(catalog.Id).Items.Single(e => e.LocalId == target.LocalId).RemoteId);
        Assert.Equal(SyncOperationState.UnknownOutcome, Assert.Single(store.Get(catalog.Id).Queue).State); Assert.Empty(provider.Writes);
    }
    [Fact]
    public async Task Known_readonly_calendar_role_blocks_local_writes_without_discarding_existing_data()
    {
        var (store, _, coordinator, catalog, contents, parent, item) = Setup(SyncProviderKind.GoogleCalendar);
        store.Set(store.Get(catalog.Id) with { Entities = [parent with { Metadata = new JsonObject { ["accessRole"] = "reader" } }] });
        await Assert.ThrowsAsync<SyncContainerAccessException>(() => coordinator.EditAsync(contents.Id, item.LocalId, Item(contents.Provider, "Unsent")));
        Assert.Empty(store.Get(contents.Id).Queue); Assert.True(JsonNode.DeepEquals(item.Local, store.Get(contents.Id).Items[0].Local));
    }
    [Theory]
    [InlineData(SyncProviderKind.MicrosoftTodo)]
    [InlineData(SyncProviderKind.GoogleCalendar)]
    public async Task Pending_contents_block_deleting_a_container_even_in_a_different_view(SyncProviderKind kind)
    {
        var (store, _, coordinator, catalog, contents, parent, item) = Setup(kind);
        await coordinator.EditAsync(contents.Id, item.LocalId, Item(kind, "Unsent"));
        await Assert.ThrowsAsync<SyncContainerChangesException>(() => coordinator.EditAsync(catalog.Id, parent.LocalId, null));
        Assert.NotNull(store.Get(catalog.Id).Items[0].Local); Assert.Single(store.Get(contents.Id).Queue);
    }

    [Theory]
    [InlineData(SyncProviderKind.MicrosoftTodo)]
    [InlineData(SyncProviderKind.GoogleCalendar)]
    public async Task Queued_and_confirmed_deletion_block_new_edits_and_keep_a_durable_identity_tombstone(SyncProviderKind kind)
    {
        var (store, provider, coordinator, catalog, contents, parent, item) = Setup(kind);
        await coordinator.EditAsync(catalog.Id, parent.LocalId, null);
        await Assert.ThrowsAsync<SyncContainerChangesException>(() => coordinator.EditFromSnapshotAsync(contents.Id, item.LocalId, item.Local, item.Revision, Item(kind, "Unsent")));
        await coordinator.ApproveAsync(catalog.Id, Assert.Single(store.Get(catalog.Id).Queue).Id);
        await coordinator.SynchronizeAsync(catalog.Id);
        Assert.Equal(SyncOperationKind.Delete, Assert.Single(provider.Writes).Kind);
        Assert.Empty(store.Get(catalog.Id).Queue);
        var reopened = new SyncCoordinator(store, [provider], new Clock());
        await Assert.ThrowsAsync<SyncContainerChangesException>(() => reopened.EditAsync(contents.Id, item.LocalId, Item(kind, "New")));
        Assert.Equal("container", store.Get(catalog.Id).Items[0].Metadata!["deletedResourceIds"]![0]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(item.Local, store.Get(contents.Id).Items[0].Local));
    }

    [Theory]
    [InlineData(SyncProviderKind.MicrosoftTodo)]
    [InlineData(SyncProviderKind.GoogleCalendar)]
    public async Task Concurrent_local_edit_and_container_deletion_cannot_both_commit(SyncProviderKind kind)
    {
        var (store, _, coordinator, catalog, contents, parent, item) = Setup(kind);
        async Task Attempt(Func<Task> action) { try { await action(); } catch (SyncContainerChangesException) { } }
        await Task.WhenAll(Task.Run(() => Attempt(() => coordinator.EditAsync(catalog.Id, parent.LocalId, null))),
            Task.Run(() => Attempt(() => coordinator.EditAsync(contents.Id, item.LocalId, Item(kind, "Unsent")))));
        Assert.True((store.Get(catalog.Id).Queue.Count == 1) ^ (store.Get(contents.Id).Queue.Count == 1));
    }

    [Theory]
    [InlineData(SyncProviderKind.MicrosoftTodo)]
    [InlineData(SyncProviderKind.GoogleCalendar)]
    public async Task Remote_container_deletion_conflicts_with_unsent_contents_and_accepting_it_keeps_the_drafts(SyncProviderKind kind)
    {
        var (store, provider, coordinator, catalog, contents, parent, item) = Setup(kind);
        await coordinator.EditAsync(contents.Id, item.LocalId, Item(kind, "Unsent"));
        provider.Page = new([], null, null, true);
        await coordinator.SynchronizeAsync(catalog.Id);
        var conflict = store.Get(catalog.Id).Items[0]; Assert.Contains("$deleted", conflict.Conflicts!);
        await coordinator.ResolveAsync(catalog.Id, parent.LocalId, null, expected: conflict);
        Assert.Single(store.Get(contents.Id).Queue); Assert.Equal("Unsent", store.Get(contents.Id).Items[0].Local![kind == SyncProviderKind.MicrosoftTodo ? "title" : "summary"]!.GetValue<string>());
        await Assert.ThrowsAsync<SyncContainerChangesException>(() => coordinator.EditAsync(contents.Id, item.LocalId, Item(kind, "Next")));
        Assert.Empty(provider.Writes);
    }

    [Theory]
    [InlineData(SyncProviderKind.MicrosoftTodo)]
    [InlineData(SyncProviderKind.GoogleCalendar)]
    public async Task Before_sending_a_delete_the_engine_rechecks_for_unsent_contents(SyncProviderKind kind)
    {
        var (store, provider, coordinator, catalog, contents, parent, item) = Setup(kind);
        await coordinator.EditAsync(catalog.Id, parent.LocalId, null);
        await coordinator.ApproveAsync(catalog.Id, Assert.Single(store.Get(catalog.Id).Queue).Id);
        // Simulates changes restored by another writer after the deletion draft was prepared.
        store.Set(SyncEdits.Edit(store.Get(contents.Id), item.LocalId, Item(kind, "Recovered"), DateTimeOffset.UtcNow));
        await coordinator.SynchronizeAsync(catalog.Id);
        Assert.Empty(provider.Writes); Assert.Equal(SyncOperationState.Failed, Assert.Single(store.Get(catalog.Id).Queue).State);
        Assert.Single(store.Get(contents.Id).Queue);
    }

    [Theory]
    [InlineData(SyncProviderKind.MicrosoftTodo)]
    [InlineData(SyncProviderKind.GoogleCalendar)]
    public async Task Unknown_deletion_stays_blocked_then_reconciliation_preserves_the_tombstone(SyncProviderKind kind)
    {
        var (store, provider, coordinator, catalog, contents, parent, item) = Setup(kind);
        provider.Unknown = true;
        await coordinator.EditAsync(catalog.Id, parent.LocalId, null);
        await coordinator.ApproveAsync(catalog.Id, Assert.Single(store.Get(catalog.Id).Queue).Id);
        await coordinator.SynchronizeAsync(catalog.Id);
        Assert.Equal(SyncOperationState.UnknownOutcome, Assert.Single(store.Get(catalog.Id).Queue).State);
        await Assert.ThrowsAsync<SyncContainerChangesException>(() => coordinator.EditAsync(contents.Id, item.LocalId, Item(kind, "New")));
        provider.Page = new([], null, null, true);
        await coordinator.SynchronizeAsync(catalog.Id);
        Assert.Empty(store.Get(catalog.Id).Queue); Assert.Single(provider.Writes);
        await Assert.ThrowsAsync<SyncContainerChangesException>(() => coordinator.EditAsync(contents.Id, item.LocalId, Item(kind, "New")));
    }

    [Theory]
    [InlineData(SyncProviderKind.MicrosoftTodo)]
    [InlineData(SyncProviderKind.GoogleCalendar)]
    public async Task Recreating_container_properties_never_reassigns_the_old_content_binding(SyncProviderKind kind)
    {
        var (store, provider, coordinator, catalog, contents, parent, item) = Setup(kind);
        await coordinator.EditAsync(catalog.Id, parent.LocalId, null); await coordinator.ApproveAsync(catalog.Id, store.Get(catalog.Id).Queue[0].Id);
        await coordinator.SynchronizeAsync(catalog.Id);
        provider.Page = new([], null, null, true);
        await coordinator.EditAsync(catalog.Id, parent.LocalId, Container(kind, "Recreated")); await coordinator.SynchronizeAsync(catalog.Id);
        Assert.Equal("new-container", store.Get(catalog.Id).Items[0].RemoteId);
        await Assert.ThrowsAsync<SyncContainerChangesException>(() => coordinator.EditAsync(contents.Id, item.LocalId, Item(kind, "Unsent")));
        await coordinator.EditAsync(catalog.Id, parent.LocalId, null); await coordinator.ApproveAsync(catalog.Id, store.Get(catalog.Id).Queue[0].Id);
        provider.Page = new([new("new-container", Container(kind, "Recreated"))], null, null, true);
        await coordinator.SynchronizeAsync(catalog.Id);
        Assert.Equal(2, store.Get(catalog.Id).Items[0].Metadata!["deletedResourceIds"]!.AsArray().Count);
    }

    [Fact]
    public async Task Google_calendar_unknown_create_can_be_linked_to_an_explicit_server_id_without_repeating_post()
    {
        var (store, provider, coordinator, catalog, _, _, _) = Setup(SyncProviderKind.GoogleCalendar);
        store.Set(new(catalog, FormatVersion: 3)); provider.Page = new([], null, null, true); provider.Unknown = true;
        var localId = Guid.NewGuid(); await coordinator.EditAsync(catalog.Id, localId, Container(catalog.Provider, "Draft"));
        await coordinator.SynchronizeAsync(catalog.Id); var uncertain = Assert.Single(store.Get(catalog.Id).Queue);
        Assert.Null(uncertain.RemoteId);
        provider.Page = new([new("server-chosen-id", Container(catalog.Provider, "Confirmed calendar"), Metadata: new JsonObject { ["accessRole"] = "owner", ["primary"] = false, ["isDataOwner"] = true })], null, null, true);
        await coordinator.SynchronizeAsync(catalog.Id);
        var candidate = store.Get(catalog.Id).Items.Single(e => e.RemoteId == "server-chosen-id");
        await coordinator.LinkUnknownCreateAsync(catalog.Id, uncertain, candidate);
        Assert.Single(provider.Writes); Assert.Empty(store.Get(catalog.Id).Queue);
        Assert.Equal("server-chosen-id", store.Get(catalog.Id).Items.Single(e => e.LocalId == localId).RemoteId);
    }

    [Fact]
    public async Task Another_account_with_the_same_resource_id_does_not_block_deletion()
    {
        var (store, _, coordinator, catalog, contents, parent, item) = Setup(SyncProviderKind.MicrosoftTodo);
        var other = contents with { Id = Guid.NewGuid(), AccountId = "other" };
        store.Set(SyncEdits.Edit(new(other, Entities: [item]), item.LocalId, Item(other.Provider, "Other account draft"), DateTimeOffset.UtcNow));
        await coordinator.EditAsync(catalog.Id, parent.LocalId, null); Assert.Single(store.Get(catalog.Id).Queue);
    }

    private static JsonObject Item(SyncProviderKind kind, string name) => new() { [kind == SyncProviderKind.MicrosoftTodo ? "title" : "summary"] = name };
    private static JsonObject Container(SyncProviderKind kind, string name) => new() { [kind == SyncProviderKind.MicrosoftTodo ? "displayName" : "summary"] = name };
    private static (Store Store, Provider Provider, SyncCoordinator Coordinator, SyncBinding Catalog, SyncBinding Contents, SyncEntity Parent, SyncEntity Item) Setup(SyncProviderKind kind)
    {
        var catalog = new SyncBinding(Guid.NewGuid(), kind, "client", "account", SyncContainers.CatalogPath(kind), "Catalog", true);
        var contents = catalog with { Id = Guid.NewGuid(), ResourcePath = kind == SyncProviderKind.MicrosoftTodo ? "lists/container/tasks" : "calendars/container/events" };
        var parent = new SyncEntity(Guid.NewGuid(), "container", Container(kind, "Container"), Container(kind, "Container"));
        var item = new SyncEntity(Guid.NewGuid(), "item", Item(kind, "Original"), Item(kind, "Original"));
        var store = new Store(); store.Set(new(catalog, Entities: [parent], FormatVersion: 3)); store.Set(new(contents, Entities: [item]));
        var provider = new Provider(kind) { Page = new([new("container", parent.Baseline)], null, null, true) };
        return (store, provider, new(store, [provider], new Clock()), catalog, contents, parent, item);
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Store : ISyncStore
    {
        private readonly Dictionary<Guid, SyncState> _states = [];
        private static SyncState Clone(SyncState state) => JsonSerializer.Deserialize<SyncState>(JsonSerializer.Serialize(state))!;
        public SyncState Get(Guid id) { lock (_states) return Clone(_states[id]); }
        public void Set(SyncState state) { lock (_states) _states[state.Binding.Id] = Clone(state); }
        public Task<IReadOnlyList<SyncBinding>> ListAsync(CancellationToken token = default) { lock (_states) return Task.FromResult<IReadOnlyList<SyncBinding>>(_states.Values.Select(s => s.Binding).ToArray()); }
        public Task<SyncState?> ReadAsync(Guid id, CancellationToken token = default) { lock (_states) return Task.FromResult<SyncState?>(_states.TryGetValue(id, out var value) ? Clone(value) : null); }
        public Task<bool> SaveAsync(SyncState value, long expectedVersion, CancellationToken token = default)
        {
            lock (_states)
            {
                if (!_states.TryGetValue(value.Binding.Id, out var current) || current.Version != expectedVersion) return Task.FromResult(false);
                _states[value.Binding.Id] = Clone(value with { Version = expectedVersion + 1 }); return Task.FromResult(true);
            }
        }
    }
    private sealed class Provider(SyncProviderKind kind) : ISyncProvider
    {
        public SyncProviderKind Kind => kind;
        public IReadOnlyList<string[]> AtomicGroups => [];
        public SyncPage Page { get; set; } = new([], null, null, true);
        public SyncPage? ContentsPage { get; set; }
        public Func<SyncOperation, Task<SyncWriteResult>>? OnWrite { get; set; }
        public bool Unknown { get; set; }
        public List<SyncOperation> Writes { get; } = [];
        public Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token) => Task.FromResult(SyncContainers.IsCatalog(binding) ? Page : ContentsPage ?? Page);
        public Task<SyncRemoteEntity?> GetAsync(SyncBinding binding, string id, CancellationToken token) => Task.FromResult(Page.Items.FirstOrDefault(e => e.Id == id));
        public Task<SyncWriteResult> WriteAsync(SyncBinding binding, SyncOperation operation, CancellationToken token)
        {
            Writes.Add(operation);
            if (OnWrite is not null) return OnWrite(operation);
            return Task.FromResult(Unknown ? new SyncWriteResult(SyncWriteStatus.UnknownOutcome) : new(SyncWriteStatus.Success,
                operation.Kind == SyncOperationKind.Delete ? null : new(operation.Kind == SyncOperationKind.Create ? "new-container" : operation.RemoteId!, operation.Desired)));
        }
    }
}
