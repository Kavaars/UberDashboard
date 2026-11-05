using System.Text.Json;
using System.Text.Json.Nodes;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Tests.Synchronization;

public sealed class SyncCoordinatorTests
{
    [Fact]
    public async Task Pull_merges_independent_changes_before_sending_and_commits_cursor()
    {
        var (store, provider, coordinator, binding) = Setup();
        var id = Guid.NewGuid();
        store.State = store.State with { Entities = [new(id, "remote", Obj("a", false), Obj("local", false), "v1", 1)],
            Operations = [new(Guid.NewGuid(), id, 1, SyncOperationKind.Update, Obj("local", false), "remote", "v1")] };
        provider.Page = new([new("remote", Obj("a", true), "v2")], null, "cursor", true);
        await coordinator.SynchronizeAsync(binding.Id);
        Assert.Equal("local", provider.Written!.Desired!["title"]!.GetValue<string>());
        Assert.True(provider.Written.Desired["done"]!.GetValue<bool>());
        Assert.Empty(store.State.Queue);
        Assert.Equal("cursor", store.State.Cursor);
    }

    [Fact]
    public async Task Conflicting_field_is_not_sent_and_survives_another_pull()
    {
        var (store, provider, coordinator, binding) = Setup();
        store.State = store.State with { Entities = [new(Guid.NewGuid(), "remote", Obj("base", false), Obj("local", false))] };
        provider.Page = new([new("remote", Obj("remote", false), "v2")], null, "cursor", true);
        await coordinator.SynchronizeAsync(binding.Id);
        await coordinator.SynchronizeAsync(binding.Id);
        Assert.Null(provider.Written);
        Assert.Equal(SyncOperationState.Conflict, Assert.Single(store.State.Queue).State);
        Assert.Equal("base", store.State.Items[0].Baseline!["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task Edit_during_send_is_not_acknowledged_as_already_uploaded()
    {
        var (store, provider, coordinator, binding) = Setup();
        var id = Guid.NewGuid();
        store.State = SyncEdits.Edit(store.State, id, Obj("first", false), DateTimeOffset.UtcNow);
        provider.OnWrite = async () => { provider.OnWrite = null; await coordinator.EditAsync(binding.Id, id, Obj("second", false)); };
        provider.FailAfterFirst = true;
        await coordinator.SynchronizeAsync(binding.Id);
        Assert.Equal("second", store.State.Items[0].Local!["title"]!.GetValue<string>());
        Assert.Equal("first", store.State.Items[0].Baseline!["title"]!.GetValue<string>());
        Assert.Equal(SyncOperationState.UnknownOutcome, Assert.Single(store.State.Queue).State);
    }

    [Fact]
    public async Task Unknown_create_is_never_automatically_repeated()
    {
        var (store, provider, coordinator, binding) = Setup();
        store.State = SyncEdits.Edit(store.State, Guid.NewGuid(), Obj("new", false), DateTimeOffset.UtcNow);
        provider.FailAfterFirst = true;
        provider.Writes = 1;
        await coordinator.SynchronizeAsync(binding.Id);
        await coordinator.SynchronizeAsync(binding.Id);
        Assert.Equal(2, provider.Writes);
        Assert.Equal(SyncOperationState.UnknownOutcome, Assert.Single(store.State.Queue).State);
        Assert.Null(store.State.LastSuccess);
    }

    [Fact]
    public async Task Expired_cursor_rebuild_keeps_local_pending_create_and_empty_snapshot_finishes()
    {
        var (store, provider, coordinator, binding) = Setup();
        store.State = store.State with { Cursor = "expired" };
        provider.ExpireOnce = true;
        await coordinator.SynchronizeAsync(binding.Id);
        Assert.Equal(2, provider.Reads);
        Assert.Empty(store.State.Items);
    }

    [Fact]
    public async Task Failed_pagination_does_not_commit_partial_snapshot_or_cursor()
    {
        var (store, provider, coordinator, binding) = Setup();
        store.State = store.State with { Cursor = "old" };
        provider.Page = new([new("remote", Obj("remote", false))], "repeated-page", null, false);
        await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.SynchronizeAsync(binding.Id));
        Assert.Equal("old", store.State.Cursor);
        Assert.Empty(store.State.Items);
    }

    [Fact]
    public void Approved_operation_is_invalidated_by_a_new_local_edit()
    {
        var (store, _, _, _) = Setup();
        var state = store.State with { Binding = store.State.Binding with { Provider = SyncProviderKind.GoogleCalendar } };
        var id = Guid.NewGuid();
        var fields = new JsonObject { ["attendees"] = new JsonArray(new JsonObject { ["email"] = "test@example.invalid" }), ["summary"] = "one" };
        state = SyncEdits.Edit(state, id, fields, DateTimeOffset.UtcNow);
        state = SyncEdits.Approve(state, state.Queue[0].Id);
        fields["summary"] = "two";
        state = SyncEdits.Edit(state, id, fields, DateTimeOffset.UtcNow);
        Assert.Equal(SyncOperationState.AwaitingApproval, state.Queue[0].State);
        Assert.Null(state.Queue[0].ApprovalHash);
    }

    private static JsonObject Obj(string title, bool done) => new() { ["title"] = title, ["done"] = done };

    [Fact]
    public async Task A_choice_from_an_old_conflict_cannot_overwrite_a_new_remote_version()
    {
        var (store, provider, coordinator, binding) = Setup();
        var id = Guid.NewGuid();
        var old = new SyncEntity(id, "remote", Obj("base", false), Obj("local", false), Remote: Obj("old remote", false), Conflicts: ["title"]);
        store.State = store.State with { Entities = [old with { Remote = Obj("new remote", false) }] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ResolveAsync(binding.Id, id, old.Remote, expected: old));
        Assert.Equal("new remote", store.State.Items[0].Remote!["title"]!.GetValue<string>());
        Assert.Equal(0, provider.Writes);
    }

    [Fact]
    public async Task Read_retry_after_is_persisted_and_manual_refresh_cannot_bypass_it()
    {
        var (store, provider, coordinator, binding) = Setup();
        provider.ReadError = new SyncReadException(TimeSpan.FromHours(1));
        await Assert.ThrowsAsync<SyncReadException>(() => coordinator.SynchronizeAsync(binding.Id));
        Assert.Equal(SyncConnectionStatus.Throttled, store.State.ConnectionStatus);
        Assert.True(store.State.Binding.NotBefore > DateTimeOffset.UtcNow.AddMinutes(50));
        await coordinator.SynchronizeAsync(binding.Id);
        Assert.Equal(1, provider.Reads);
    }

    [Fact]
    public async Task Expired_consent_is_visible_and_does_not_discard_offline_changes()
    {
        var (store, provider, coordinator, binding) = Setup();
        store.State = SyncEdits.Edit(store.State, Guid.NewGuid(), Obj("offline", false), DateTimeOffset.UtcNow);
        provider.ReadError = new SyncAuthenticationException();
        await Assert.ThrowsAsync<SyncAuthenticationException>(() => coordinator.SynchronizeAsync(binding.Id));
        Assert.Equal(SyncConnectionStatus.AuthenticationRequired, store.State.ConnectionStatus);
        Assert.Single(store.State.Queue);
        Assert.Equal("offline", store.State.Items[0].Local!["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task Stale_editor_cannot_overwrite_an_item_changed_in_the_background()
    {
        var (store, _, coordinator, binding) = Setup();
        var id = Guid.NewGuid();
        store.State = store.State with { Entities = [new(id, "remote", Obj("new remote", false), Obj("new remote", false), Revision: 1)] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.EditFromSnapshotAsync(binding.Id, id,
            Obj("old snapshot", false), 1, Obj("edited stale snapshot", false)));
        Assert.Equal("new remote", store.State.Items[0].Local!["title"]!.GetValue<string>());
        Assert.Empty(store.State.Queue);
    }

    [Fact]
    public async Task Matching_editor_snapshot_commits_the_edit_and_operation_together()
    {
        var (store, _, coordinator, binding) = Setup();
        var id = Guid.NewGuid();
        store.State = store.State with { Entities = [new(id, "remote", Obj("before", false), Obj("before", false), Revision: 1)] };
        await coordinator.EditFromSnapshotAsync(binding.Id, id, Obj("before", false), 1, Obj("after", false));
        Assert.Equal("after", store.State.Items[0].Local!["title"]!.GetValue<string>());
        Assert.Equal(2, store.State.Items[0].Revision);
        Assert.Equal(2, Assert.Single(store.State.Queue).Revision);
    }

    [Fact]
    public async Task A_disabled_binding_never_pulls_or_pushes()
    {
        var (store, provider, coordinator, binding) = Setup();
        store.State = store.State with { Binding = binding with { Enabled = false } };
        await coordinator.SynchronizeAsync(binding.Id);
        Assert.Equal(0, provider.Reads);
        Assert.Equal(0, provider.Writes);
    }

    [Fact]
    public async Task Definite_transient_error_keeps_retry_state_and_does_not_claim_success()
    {
        var (store, provider, coordinator, binding) = Setup();
        store.State = SyncEdits.Edit(store.State, Guid.NewGuid(), Obj("new", false), DateTimeOffset.UtcNow);
        provider.Result = new(SyncWriteStatus.Retry, RetryAfter: TimeSpan.FromHours(1));
        await coordinator.SynchronizeAsync(binding.Id);
        await coordinator.SynchronizeAsync(binding.Id);
        Assert.Equal(1, provider.Writes);
        Assert.Equal(SyncOperationState.Retry, Assert.Single(store.State.Queue).State);
        Assert.Null(store.State.LastSuccess);
    }

    [Fact]
    public async Task Known_id_allows_reconciliation_without_repeating_the_create()
    {
        var (store, provider, coordinator, binding) = Setup();
        var id = Guid.NewGuid();
        store.State = SyncEdits.Edit(store.State, id, Obj("new", false), DateTimeOffset.UtcNow);
        store.State = store.State with { Operations = store.State.Queue.Select(o => o with
            { RemoteId = "server-id", State = SyncOperationState.UnknownOutcome }).ToArray() };
        provider.Page = new([new("server-id", Obj("new", false), "etag")], null, "cursor", true);
        await coordinator.SynchronizeAsync(binding.Id);
        Assert.Equal(0, provider.Writes);
        Assert.Empty(store.State.Queue);
        Assert.Equal(id, Assert.Single(store.State.Items).LocalId);
        Assert.Equal("server-id", store.State.Items[0].RemoteId);
    }

    [Fact]
    public async Task An_unknown_outcome_cannot_be_retried_as_a_definite_failure()
    {
        var (store, _, coordinator, binding) = Setup();
        store.State = SyncEdits.Edit(store.State, Guid.NewGuid(), Obj("new", false), DateTimeOffset.UtcNow);
        store.State = store.State with { Operations = store.State.Queue.Select(o => o with { State = SyncOperationState.UnknownOutcome }).ToArray() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RetryAsync(binding.Id, store.State.Queue[0].Id));
        Assert.Equal(SyncOperationState.UnknownOutcome, store.State.Queue[0].State);
    }
    private static (MemoryStore, Provider, SyncCoordinator, SyncBinding) Setup()
    {
        var binding = new SyncBinding(Guid.NewGuid(), SyncProviderKind.MicrosoftTodo, "client", "account", "lists/a/tasks", "Test", true);
        var store = new MemoryStore(new(binding));
        var provider = new Provider();
        return (store, provider, new SyncCoordinator(store, [provider], new Clock()), binding);
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class MemoryStore(SyncState state) : ISyncStore
    {
        public SyncState State { get; set; } = state;
        public Task<IReadOnlyList<SyncBinding>> ListAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<SyncBinding>>([State.Binding]);
        public Task<SyncState?> ReadAsync(Guid bindingId, CancellationToken token = default) => Task.FromResult(JsonSerializer.Deserialize<SyncState>(JsonSerializer.Serialize(State)));
        public Task<bool> SaveAsync(SyncState value, long expectedVersion, CancellationToken token = default)
        {
            if (expectedVersion != State.Version) return Task.FromResult(false);
            State = JsonSerializer.Deserialize<SyncState>(JsonSerializer.Serialize(value with { Version = expectedVersion + 1 }))!;
            return Task.FromResult(true);
        }
    }
    private sealed class Provider : ISyncProvider
    {
        public SyncProviderKind Kind => SyncProviderKind.MicrosoftTodo;
        public IReadOnlyList<string[]> AtomicGroups => [];
        public SyncPage Page { get; set; } = new([], null, null, true);
        public SyncOperation? Written { get; private set; }
        public Func<Task>? OnWrite { get; set; }
        public bool FailAfterFirst { get; set; }
        public bool ExpireOnce { get; set; }
        public SyncWriteResult? Result { get; set; }
        public Exception? ReadError { get; set; }
        public int Reads { get; private set; }
        public int Writes { get; set; }
        public Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token)
        { Reads++; if (ReadError is not null) throw ReadError; if (ExpireOnce) { ExpireOnce = false; throw new SyncCursorExpiredException(); } return Task.FromResult(Page); }
        public Task<SyncRemoteEntity?> GetAsync(SyncBinding binding, string remoteId, CancellationToken token) => Task.FromResult(Page.Items.FirstOrDefault(e => e.Id == remoteId));
        public async Task<SyncWriteResult> WriteAsync(SyncBinding binding, SyncOperation operation, CancellationToken token)
        {
            Written = operation; Writes++;
            if (OnWrite is not null) await OnWrite();
            if (Result is not null) return Result;
            if (FailAfterFirst && Writes > 1) throw new HttpRequestException();
            return new(SyncWriteStatus.Success, new("saved-id", operation.Desired, "new-etag"));
        }
    }
}
