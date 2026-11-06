using System.Text.Json;
using System.Text.Json.Nodes;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Tests.Synchronization;

public sealed class SyncUnknownReviewTests
{
    [Theory]
    [InlineData(SyncProviderKind.MicrosoftTodo, SyncOperationKind.Update)]
    [InlineData(SyncProviderKind.MicrosoftTodo, SyncOperationKind.Delete)]
    [InlineData(SyncProviderKind.GoogleCalendar, SyncOperationKind.Update)]
    [InlineData(SyncProviderKind.GoogleCalendar, SyncOperationKind.Delete)]
    public async Task A_review_prepares_a_conflict_without_repeating_the_unknown_write(SyncProviderKind providerKind, SyncOperationKind kind)
    {
        var (store, provider, coordinator, operation, entity) = Setup(providerKind, kind);
        var review = await coordinator.ReadUnknownReviewAsync(store.State.Binding.Id, operation, entity);
        Assert.Equal(SyncOperationState.UnknownOutcome, store.State.Queue[0].State); Assert.Equal(0, provider.Writes);
        var prepared = await coordinator.PrepareUnknownConflictAsync(store.State.Binding.Id, review);
        Assert.Contains("$unknown-result", prepared.Conflicts!); Assert.Equal(entity.Revision + 1, prepared.Revision);
        Assert.Equal(operation.RemoteId, prepared.RemoteId); Assert.True(JsonNode.DeepEquals(provider.Remote!.Fields, prepared.Remote));
        Assert.Equal(operation.Id, store.State.Queue[0].Id); Assert.Equal(SyncOperationState.Conflict, store.State.Queue[0].State);
        Assert.Contains(store.State.History, h => h.Reason == "unknown-write-draft" && JsonNode.DeepEquals(h.Value, operation.Desired));
        Assert.Equal(2, provider.Reads); Assert.Equal(0, provider.Writes);
        await coordinator.ResolveAsync(store.State.Binding.Id, prepared.LocalId, prepared.Remote, expected: prepared);
        Assert.Empty(store.State.Queue); Assert.Equal(0, provider.Writes);
    }

    [Fact]
    public async Task Related_items_use_the_reviewed_parent_identity_and_reject_a_changed_parent()
    {
        var (store, provider, coordinator, operation, entity) = Setup();
        var parent = new SyncEntity(Guid.NewGuid(), "task", Fields("Task"), Fields("Task")); entity = entity with { Kind = SyncEntityKind.ChecklistItem, ParentId = parent.LocalId };
        store.State = store.State with { Entities = [parent, entity] };
        var review = await coordinator.ReadUnknownReviewAsync(store.State.Binding.Id, operation, entity);
        Assert.Equal("lists/a/tasks/task/checklistItems", review.ScopePath);
        store.State = store.State with { Entities = [parent with { RemoteId = "moved" }, entity] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PrepareUnknownConflictAsync(store.State.Binding.Id, review));
        Assert.Equal(1, provider.Reads); Assert.Equal(SyncOperationState.UnknownOutcome, store.State.Queue[0].State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Account_changes_before_or_during_confirmation_do_not_adopt_another_accounts_data(bool duringRead)
    {
        var (store, provider, coordinator, operation, entity) = Setup();
        var review = await coordinator.ReadUnknownReviewAsync(store.State.Binding.Id, operation, entity);
        Task Change() { store.State = store.State with { Binding = store.State.Binding with { AccountId = "other" } }; return Task.CompletedTask; }
        if (duringRead) provider.OnRead = Change; else await Change();
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PrepareUnknownConflictAsync(store.State.Binding.Id, review));
        Assert.Equal(SyncOperationState.UnknownOutcome, store.State.Queue[0].State); Assert.Empty(store.State.History);
    }

    [Fact]
    public async Task Reverting_a_field_during_an_unknown_write_remains_an_explicit_newer_intent()
    {
        var (store, provider, coordinator, operation, entity) = Setup();
        await coordinator.EditAsync(store.State.Binding.Id, entity.LocalId, Fields("Base")); entity = store.State.Items[0];
        var review = await coordinator.ReadUnknownReviewAsync(store.State.Binding.Id, operation, entity);
        var conflict = await coordinator.PrepareUnknownConflictAsync(store.State.Binding.Id, review);
        Assert.Equal("Base", conflict.Local!["title"]!.GetValue<string>()); Assert.Equal(0, provider.Writes);
    }

    [Fact]
    public async Task A_newer_local_revision_and_its_dependency_survive_preparing_the_conflict()
    {
        var (store, provider, coordinator, operation, entity) = Setup();
        await coordinator.EditAsync(store.State.Binding.Id, entity.LocalId, Fields("Newer")); entity = store.State.Items[0];
        var newer = store.State.Queue[1]; var review = await coordinator.ReadUnknownReviewAsync(store.State.Binding.Id, operation, entity);
        await coordinator.PrepareUnknownConflictAsync(store.State.Binding.Id, review);
        Assert.Equal(2, store.State.Queue.Count); Assert.Equal(newer.Id, store.State.Queue[1].Id); Assert.Equal(operation.Id, store.State.Queue[1].DependsOn);
        Assert.Equal("Newer", store.State.Items[0].Local!["title"]!.GetValue<string>()); Assert.Equal(0, provider.Writes);
    }

    [Theory]
    [InlineData("fields")]
    [InlineData("etag")]
    [InlineData("metadata")]
    public async Task Changed_service_snapshots_require_another_review(string change)
    {
        var (store, provider, coordinator, operation, entity) = Setup();
        var review = await coordinator.ReadUnknownReviewAsync(store.State.Binding.Id, operation, entity);
        provider.Remote = change switch { "fields" => provider.Remote! with { Fields = Fields("Changed") }, "etag" => provider.Remote! with { ETag = "new" }, _ => provider.Remote! with { Metadata = new JsonObject { ["role"] = "reader" } } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PrepareUnknownConflictAsync(store.State.Binding.Id, review));
        Assert.Equal(SyncOperationState.UnknownOutcome, store.State.Queue[0].State); Assert.Empty(store.State.History); Assert.Equal(0, provider.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changes_before_or_during_the_confirmation_read_do_not_replace_local_work(bool duringRead)
    {
        var (store, provider, coordinator, operation, entity) = Setup();
        var review = await coordinator.ReadUnknownReviewAsync(store.State.Binding.Id, operation, entity);
        Task Change() => coordinator.EditAsync(store.State.Binding.Id, entity.LocalId, Fields("Newer"));
        if (duringRead) provider.OnRead = Change; else await Change();
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PrepareUnknownConflictAsync(store.State.Binding.Id, review));
        Assert.Equal("Newer", store.State.Items[0].Local!["title"]!.GetValue<string>()); Assert.Equal(SyncOperationState.UnknownOutcome, store.State.Queue[0].State);
    }

    [Theory]
    [InlineData("missing-id")]
    [InlineData("sending")]
    [InlineData("changed-operation")]
    [InlineData("wrong-identity")]
    [InlineData("create")]
    [InlineData("moved-mapping")]
    [InlineData("invalid-kind")]
    public async Task Unreviewable_or_stale_operations_are_rejected_before_network(string change)
    {
        var (store, provider, coordinator, operation, entity) = Setup();
        if (change == "missing-id") { operation = operation with { RemoteId = null }; store.State = store.State with { Operations = [operation] }; }
        if (change == "sending") { operation = operation with { State = SyncOperationState.Sending }; store.State = store.State with { Operations = [operation] }; }
        if (change == "changed-operation") store.State = store.State with { Operations = [operation with { Baseline = Fields("Different") }] };
        if (change == "wrong-identity") entity = entity with { LocalId = Guid.NewGuid() };
        if (change == "create") { operation = operation with { Kind = SyncOperationKind.Create }; store.State = store.State with { Operations = [operation] }; }
        if (change == "moved-mapping") { entity = entity with { RemoteId = "moved" }; store.State = store.State with { Entities = [entity] }; }
        if (change == "invalid-kind") { operation = operation with { Kind = (SyncOperationKind)99 }; store.State = store.State with { Operations = [operation] }; }
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ReadUnknownReviewAsync(store.State.Binding.Id, operation, entity));
        Assert.Equal(0, provider.Reads);
    }

    [Fact]
    public async Task A_wrong_service_identity_is_not_adopted()
    {
        var (store, provider, coordinator, operation, entity) = Setup(); provider.Remote = provider.Remote! with { Id = "different" };
        await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.ReadUnknownReviewAsync(store.State.Binding.Id, operation, entity));
        Assert.Equal(SyncOperationState.UnknownOutcome, store.State.Queue[0].State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_missing_or_deleted_remote_item_still_requires_a_local_decision(bool tombstone)
    {
        var (store, provider, coordinator, operation, entity) = Setup(kind: SyncOperationKind.Delete);
        provider.Remote = tombstone ? new(operation.RemoteId!, null) : null;
        var review = await coordinator.ReadUnknownReviewAsync(store.State.Binding.Id, operation, entity); Assert.Null(review.Remote);
        var conflict = await coordinator.PrepareUnknownConflictAsync(store.State.Binding.Id, review);
        Assert.Contains("$unknown-result", conflict.Conflicts!); Assert.Equal(SyncOperationState.Conflict, store.State.Queue[0].State);
        await coordinator.ResolveAsync(store.State.Binding.Id, conflict.LocalId, null, expected: conflict);
        Assert.Empty(store.State.Queue); Assert.Equal(0, provider.Writes);
    }

    [Fact]
    public async Task Quota_errors_keep_unknown_state_and_apply_the_account_retry_after()
    {
        var (store, provider, coordinator, operation, entity) = Setup(); provider.Error = new SyncReadException(TimeSpan.FromMinutes(2));
        await Assert.ThrowsAsync<SyncReadException>(() => coordinator.ReadUnknownReviewAsync(store.State.Binding.Id, operation, entity));
        Assert.True(store.State.Binding.NotBefore > DateTimeOffset.UtcNow.AddMinutes(1)); provider.Error = null;
        await Assert.ThrowsAsync<SyncReadException>(() => coordinator.ReadUnknownReviewAsync(store.State.Binding.Id, operation, entity));
        Assert.Equal(1, provider.Reads); Assert.Equal(SyncOperationState.UnknownOutcome, store.State.Queue[0].State);
    }

    [Fact]
    public async Task Cancellation_after_the_second_read_or_a_failed_database_save_cannot_commit_a_partial_transition()
    {
        foreach (var canceled in new[] { false, true })
        {
            var (store, provider, coordinator, operation, entity) = Setup(); using var cancellation = new CancellationTokenSource();
            var review = await coordinator.ReadUnknownReviewAsync(store.State.Binding.Id, operation, entity);
            if (canceled) provider.OnRead = () => { cancellation.Cancel(); return Task.CompletedTask; }; else store.FailSave = true;
            await Assert.ThrowsAnyAsync<Exception>(() => coordinator.PrepareUnknownConflictAsync(store.State.Binding.Id, review, cancellation.Token));
            Assert.Equal(SyncOperationState.UnknownOutcome, store.State.Queue[0].State); Assert.Empty(store.State.History);
        }
    }

    [Fact]
    public async Task Choosing_a_google_delete_again_requires_new_approval_and_does_not_reuse_the_old_hash()
    {
        var (store, provider, coordinator, operation, entity) = Setup(SyncProviderKind.GoogleCalendar, SyncOperationKind.Delete);
        provider.Remote!.Fields!["attendees"] = new JsonArray(new JsonObject { ["email"] = "guest@example.invalid" });
        var review = await coordinator.ReadUnknownReviewAsync(store.State.Binding.Id, operation, entity);
        var conflict = await coordinator.PrepareUnknownConflictAsync(store.State.Binding.Id, review);
        await coordinator.ResolveAsync(store.State.Binding.Id, conflict.LocalId, null, expected: conflict);
        Assert.Equal(SyncOperationState.AwaitingApproval, store.State.Queue[0].State); Assert.Null(store.State.Queue[0].ApprovalHash); Assert.Equal(0, provider.Writes);
    }

    private static JsonObject Fields(string title) => new() { ["title"] = title };
    private static (Store Store, Provider Provider, SyncCoordinator Coordinator, SyncOperation Operation, SyncEntity Entity) Setup(SyncProviderKind providerKind = SyncProviderKind.MicrosoftTodo, SyncOperationKind kind = SyncOperationKind.Update)
    {
        var binding = new SyncBinding(Guid.NewGuid(), providerKind, "client", "account", providerKind == SyncProviderKind.MicrosoftTodo ? "lists/a/tasks" : "calendars/a/events", "Test");
        JsonObject Value(string text) => new() { [providerKind == SyncProviderKind.MicrosoftTodo ? "title" : "summary"] = text };
        var baseline = kind == SyncOperationKind.Create ? null : Value("Base"); var desired = kind == SyncOperationKind.Delete ? null : Value("Sent");
        var entity = new SyncEntity(Guid.NewGuid(), kind == SyncOperationKind.Create ? null : "remote", baseline, desired, Revision: 1);
        var operation = new SyncOperation(Guid.NewGuid(), entity.LocalId, 1, kind, desired, "remote", "old-etag", SyncOperationState.UnknownOutcome, Attempts: 1, ApprovalHash: "old-approved", Baseline: baseline);
        var store = new Store(new(binding, Entities: [entity], Operations: [operation])); var provider = new Provider(providerKind) { Remote = new("remote", Value("Service"), "fresh-etag") };
        return (store, provider, new(store, [provider], new Clock()), operation, entity);
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Store(SyncState state) : ISyncStore
    {
        public SyncState State = state; public bool FailSave;
        public Task<IReadOnlyList<SyncBinding>> ListAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<SyncBinding>>([State.Binding]);
        public Task<SyncState?> ReadAsync(Guid id, CancellationToken token = default) => Task.FromResult<SyncState?>(JsonSerializer.Deserialize<SyncState>(JsonSerializer.Serialize(State)));
        public Task<bool> SaveAsync(SyncState next, long expected, CancellationToken token = default)
        { if (FailSave) throw new IOException("Test storage failure"); if (State.Version != expected) return Task.FromResult(false); State = next with { Version = expected + 1 }; return Task.FromResult(true); }
    }
    private sealed class Provider(SyncProviderKind kind) : ISyncProvider
    {
        public SyncProviderKind Kind => kind; public IReadOnlyList<string[]> AtomicGroups => []; public int Reads; public int Writes;
        public SyncRemoteEntity? Remote; public Exception? Error; public Func<Task>? OnRead;
        public async Task<SyncRemoteEntity?> GetAsync(SyncBinding binding, string id, CancellationToken token) { Reads++; if (Error is not null) throw Error; if (OnRead is not null) await OnRead(); return Remote; }
        public Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token) => throw new InvalidOperationException("No pull expected");
        public Task<SyncWriteResult> WriteAsync(SyncBinding binding, SyncOperation operation, CancellationToken token) { Writes++; throw new InvalidOperationException("No write expected"); }
    }
}
