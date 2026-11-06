using System.Text.Json;
using System.Text.Json.Nodes;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Tests.Synchronization;

public sealed class GoogleAccessSyncTests
{
    [Fact]
    public async Task Canceling_an_unsent_subscription_draft_does_not_unsubscribe_an_existing_calendar()
    {
        var (store, provider, coordinator, binding) = Setup(false); var id = Guid.NewGuid();
        await coordinator.EditAsync(binding.Id, id, Subscription()); await coordinator.EditAsync(binding.Id, id, null);
        Assert.Empty(store.Get(binding.Id).Queue); await coordinator.SynchronizeAsync(binding.Id); Assert.Empty(provider.Writes);
    }
    [Fact]
    public async Task Undo_during_unsubscribe_retains_the_known_identity_for_uncertain_resubscription()
    {
        var (store, provider, coordinator, binding) = Setup(false);
        var member = new SyncEntity(Guid.NewGuid(), Calendar, Subscription(), Subscription(), Metadata: Metadata()); store.Set(new(binding, Entities: [member]));
        provider.Page = new([new(Calendar, Subscription())], null, "cursor", true);
        await coordinator.EditAsync(binding.Id, member.LocalId, null); await coordinator.ApproveAsync(binding.Id, store.Get(binding.Id).Queue[0].Id);
        provider.OnWrite = async operation =>
        {
            if (operation.Kind != SyncOperationKind.Delete) return;
            await coordinator.EditAsync(binding.Id, member.LocalId, Subscription()); provider.Unknown = true;
        };
        await coordinator.SynchronizeAsync(binding.Id);
        var pending = Assert.Single(store.Get(binding.Id).Queue);
        Assert.Equal(SyncOperationState.UnknownOutcome, pending.State); Assert.Equal(Calendar, pending.RemoteId);
        provider.Found[Calendar] = new(Calendar, Subscription(), Metadata: Metadata());
        await coordinator.SynchronizeAsync(binding.Id); Assert.Empty(store.Get(binding.Id).Queue); Assert.Equal(2, provider.Writes.Count);
    }
    [Fact]
    public async Task Subscribing_an_existing_membership_uses_its_id_without_posting_or_duplicating_it()
    {
        var (store, provider, coordinator, binding) = Setup(false);
        var id = Guid.NewGuid(); await coordinator.EditAsync(binding.Id, id, Subscription());
        Assert.Equal(Calendar, Assert.Single(store.Get(binding.Id).Queue).RemoteId);
        provider.Page = new([new(Calendar, Subscription())], null, "cursor", true);
        await coordinator.SynchronizeAsync(binding.Id);
        Assert.Empty(provider.Writes); Assert.Empty(store.Get(binding.Id).Queue); Assert.Equal(id, Assert.Single(store.Get(binding.Id).Items).LocalId);
    }
    [Fact]
    public async Task An_unknown_subscription_create_is_checked_by_the_supplied_calendar_id_after_restart()
    {
        var (store, provider, coordinator, binding) = Setup(false);
        var id = Guid.NewGuid(); await coordinator.EditAsync(binding.Id, id, Subscription()); provider.Unknown = true;
        await coordinator.SynchronizeAsync(binding.Id); Assert.Equal(SyncOperationState.UnknownOutcome, store.Get(binding.Id).Queue[0].State);
        var remote = new SyncRemoteEntity(Calendar, Subscription(), Metadata: Metadata()); provider.Found[Calendar] = remote; provider.Page = new([remote], null, "next", true);
        await new SyncCoordinator(store, [provider], new Clock()).SynchronizeAsync(binding.Id);
        Assert.Single(provider.Writes); Assert.Empty(store.Get(binding.Id).Queue); Assert.Equal(id, Assert.Single(store.Get(binding.Id).Items).LocalId);
    }
    [Fact]
    public async Task Unsubscribe_blocks_pending_contents_and_resubscription_unlocks_the_same_binding()
    {
        var (store, provider, coordinator, binding) = Setup(false); var member = new SyncEntity(Guid.NewGuid(), Calendar, Subscription(), Subscription(), Metadata: Metadata());
        store.Set(new(binding, Entities: [member])); provider.Page = new([new(Calendar, Subscription())], null, "cursor", true);
        var contents = binding with { Id = Guid.NewGuid(), ResourcePath = "calendars/" + Uri.EscapeDataString(Calendar) + "/events" };
        var item = new SyncEntity(Guid.NewGuid(), "event", new JsonObject { ["summary"] = "Original" }, new JsonObject { ["summary"] = "Original" }); store.Set(new(contents, Entities: [item]));
        await coordinator.EditAsync(contents.Id, item.LocalId, new JsonObject { ["summary"] = "Pending" });
        await Assert.ThrowsAsync<SyncContainerChangesException>(() => coordinator.EditAsync(binding.Id, member.LocalId, null));
        store.Set(new(contents, Entities: [item]));
        await coordinator.EditAsync(binding.Id, member.LocalId, null); await coordinator.ApproveAsync(binding.Id, store.Get(binding.Id).Queue[0].Id);
        await coordinator.SynchronizeAsync(binding.Id);
        await Assert.ThrowsAsync<SyncContainerChangesException>(() => coordinator.EditAsync(contents.Id, item.LocalId, new JsonObject { ["summary"] = "Blocked" }));
        Assert.Equal(Calendar, store.Get(binding.Id).Items[0].Metadata!["unsubscribedCalendarId"]!.GetValue<string>());
        await coordinator.EditAsync(binding.Id, member.LocalId, Subscription()); provider.Page = new([], null, "cursor", true);
        await coordinator.SynchronizeAsync(binding.Id);
        Assert.Equal(Calendar, store.Get(binding.Id).Items[0].RemoteId);
        await coordinator.EditAsync(contents.Id, item.LocalId, new JsonObject { ["summary"] = "Allowed" });
        Assert.Single(store.Get(contents.Id).Queue); Assert.Equal(GoogleCalendarAccess.Subscriptions, provider.Writes.Last().Path);
    }
    [Fact]
    public async Task A_remote_unsubscribe_conflicts_with_unsent_events_and_preserves_them_when_accepted()
    {
        var (store, provider, coordinator, binding) = Setup(false); var member = new SyncEntity(Guid.NewGuid(), Calendar, Subscription(), Subscription(), Metadata: Metadata()); store.Set(new(binding, Entities: [member]));
        var contents = binding with { Id = Guid.NewGuid(), ResourcePath = "calendars/" + Uri.EscapeDataString(Calendar) + "/events" };
        var item = new SyncEntity(Guid.NewGuid(), "event", new JsonObject { ["summary"] = "Original" }, new JsonObject { ["summary"] = "Original" }); store.Set(new(contents, Entities: [item]));
        await coordinator.EditAsync(contents.Id, item.LocalId, new JsonObject { ["summary"] = "Pending" });
        await coordinator.SynchronizeAsync(binding.Id); var conflict = store.Get(binding.Id).Items[0]; Assert.Contains("$deleted", conflict.Conflicts!);
        await coordinator.ResolveAsync(binding.Id, member.LocalId, null, expected: conflict);
        Assert.Single(store.Get(contents.Id).Queue); Assert.Empty(provider.Writes);
    }
    [Fact]
    public async Task Missing_global_calendar_catalog_entry_is_verified_instead_of_treated_as_deleted()
    {
        var (store, provider, coordinator, source) = Setup(false);
        var catalog = source with { ResourcePath = "calendars" }; var fields = new JsonObject { ["summary"] = "Still exists" };
        store.Set(new(catalog, Entities: [new(Guid.NewGuid(), Calendar, fields, fields)]));
        provider.Found[Calendar] = new(Calendar, fields, Metadata: Metadata());
        await coordinator.SynchronizeAsync(catalog.Id);
        Assert.Equal("Still exists", Assert.Single(store.Get(catalog.Id).Items).Local!["summary"]!.GetValue<string>());
        Assert.Empty(store.Get(catalog.Id).Queue);
    }
    [Fact]
    public async Task Empty_incremental_feed_still_updates_permissions_and_blocks_approved_writes()
    {
        var (store, provider, coordinator, binding) = Setup(true); var guest = Rule("guest@example.invalid", "reader");
        var entity = new SyncEntity(Guid.NewGuid(), "guest-rule", guest, guest, Metadata: Metadata()); store.Set(new(binding, Cursor: "old", Entities: [entity]));
        await coordinator.EditAsync(binding.Id, entity.LocalId, Rule("guest@example.invalid", "writer")); await coordinator.ApproveAsync(binding.Id, store.Get(binding.Id).Queue[0].Id);
        provider.Page = new([], null, "new", false); provider.Metadata = new(Shared: Metadata("writer"));
        await coordinator.SynchronizeAsync(binding.Id);
        Assert.Equal("writer", store.Get(binding.Id).Items[0].Metadata!["accessRole"]!.GetValue<string>());
        Assert.Equal(SyncOperationState.Forbidden, Assert.Single(store.Get(binding.Id).Queue).State); Assert.Empty(provider.Writes);
    }
    [Fact]
    public async Task Sharing_drafts_are_reviewed_editable_and_cancelable_while_self_rules_are_protected()
    {
        var (store, _, coordinator, binding) = Setup(true); var own = Rule("me@example.invalid", "owner");
        store.Set(new(binding, Entities: [new(Guid.NewGuid(), "self", own, own, Metadata: Metadata())]));
        await Assert.ThrowsAsync<SyncContainerAccessException>(() => coordinator.EditAsync(binding.Id, Guid.NewGuid(), Rule("me@example.invalid", "reader")));
        var id = Guid.NewGuid(); await coordinator.EditAsync(binding.Id, id, Rule("guest@example.invalid", "reader"));
        Assert.Equal(SyncOperationState.AwaitingApproval, Assert.Single(store.Get(binding.Id).Queue).State);
        await coordinator.EditAsync(binding.Id, id, Rule("guest@example.invalid", "writer")); await coordinator.EditAsync(binding.Id, id, null);
        Assert.Empty(store.Get(binding.Id).Queue);
    }
    [Fact]
    public async Task Metadata_failure_keeps_the_cursor_and_local_changes_for_a_complete_retry()
    {
        var (store, provider, coordinator, binding) = Setup(true); var guest = Rule("guest@example.invalid", "reader"); var entity = new SyncEntity(Guid.NewGuid(), "rule", guest, guest, Metadata: Metadata());
        store.Set(new(binding, Cursor: "old", Entities: [entity])); await coordinator.EditAsync(binding.Id, entity.LocalId, Rule("guest@example.invalid", "writer"));
        provider.Page = new([], null, "new", false); provider.MetadataError = true;
        await Assert.ThrowsAsync<System.IO.IOException>(() => coordinator.SynchronizeAsync(binding.Id));
        Assert.Equal("old", store.Get(binding.Id).Cursor); Assert.Equal("writer", store.Get(binding.Id).Items[0].Local!["role"]!.GetValue<string>()); Assert.Empty(provider.Writes);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unknown_sharing_creation_only_links_the_same_audience_without_another_write(bool sameAudience)
    {
        var (store, provider, coordinator, binding) = Setup(true); var fields = Rule("guest@example.invalid", "reader");
        var target = new SyncEntity(Guid.NewGuid(), null, null, fields, Revision: 1, Metadata: Metadata());
        var candidateFields = Rule(sameAudience ? "guest@example.invalid" : "someone-else@example.invalid", "reader");
        var candidate = new SyncEntity(Guid.NewGuid(), "service-rule", candidateFields, candidateFields, Metadata: Metadata());
        var operation = new SyncOperation(Guid.NewGuid(), target.LocalId, 1, SyncOperationKind.Create, fields, null, null, SyncOperationState.UnknownOutcome);
        store.Set(new(binding, Entities: [target, candidate], Operations: [operation])); provider.Found[candidate.RemoteId!] = new(candidate.RemoteId!, candidateFields, Metadata: Metadata());
        if (sameAudience)
        {
            await coordinator.LinkUnknownCreateAsync(binding.Id, operation, candidate);
            Assert.Empty(store.Get(binding.Id).Queue); Assert.Equal("service-rule", Assert.Single(store.Get(binding.Id).Items).RemoteId);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.LinkUnknownCreateAsync(binding.Id, operation, candidate));
            Assert.Equal(SyncOperationState.UnknownOutcome, Assert.Single(store.Get(binding.Id).Queue).State);
        }
        Assert.Empty(provider.Writes);
    }
    private const string Calendar = "calendar@example.invalid";
    private static JsonObject Subscription() => new() { ["id"] = Calendar };
    private static JsonObject Rule(string target, string role) => new GoogleSharingDraft("user", target, role).Fields();
    private static JsonObject Metadata(string role = "owner") => new() { ["accessRole"] = role, ["primary"] = false, ["isDataOwner"] = false, ["actingCalendarId"] = "me@example.invalid", ["dataOwner"] = "owner@example.invalid", ["calendarId"] = Calendar };
    private static (Store Store, Provider Provider, SyncCoordinator Coordinator, SyncBinding Binding) Setup(bool sharing)
    {
        var binding = new SyncBinding(Guid.NewGuid(), SyncProviderKind.GoogleCalendar, "client", "account", sharing ? "calendars/" + Uri.EscapeDataString(Calendar) + "/acl" : GoogleCalendarAccess.Subscriptions, "Access", true);
        var store = new Store(); store.Set(new(binding)); var provider = new Provider(); return (store, provider, new(store, [provider], new Clock()), binding);
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Store : ISyncStore
    {
        private readonly Dictionary<Guid, SyncState> _states = [];
        private static SyncState Clone(SyncState state) => JsonSerializer.Deserialize<SyncState>(JsonSerializer.Serialize(state))!;
        public SyncState Get(Guid id) => Clone(_states[id]);
        public void Set(SyncState state) => _states[state.Binding.Id] = Clone(state);
        public Task<IReadOnlyList<SyncBinding>> ListAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<SyncBinding>>(_states.Values.Select(s => s.Binding).ToArray());
        public Task<SyncState?> ReadAsync(Guid id, CancellationToken token = default) => Task.FromResult<SyncState?>(_states.TryGetValue(id, out var s) ? Clone(s) : null);
        public Task<bool> SaveAsync(SyncState state, long expectedVersion, CancellationToken token = default) { if (_states[state.Binding.Id].Version != expectedVersion) return Task.FromResult(false); Set(state with { Version = expectedVersion + 1 }); return Task.FromResult(true); }
    }
    private sealed class Provider : ISyncProvider, ISyncResourceMetadataProvider
    {
        public SyncProviderKind Kind => SyncProviderKind.GoogleCalendar;
        public IReadOnlyList<string[]> AtomicGroups => [];
        public SyncPage Page { get; set; } = new([], null, "cursor", true);
        public SyncResourceMetadata? Metadata { get; set; }
        public bool MetadataError { get; set; }
        public bool Unknown { get; set; }
        public Func<SyncOperation, Task>? OnWrite { get; set; }
        public Dictionary<string, SyncRemoteEntity> Found { get; } = [];
        public List<(string Path, SyncOperation Operation)> Writes { get; } = [];
        public Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token) => Task.FromResult(Page);
        public Task<SyncResourceMetadata?> ReadMetadataAsync(SyncBinding binding, CancellationToken token) => MetadataError ? throw new System.IO.IOException("Test metadata failure") : Task.FromResult(Metadata);
        public Task<SyncRemoteEntity?> GetAsync(SyncBinding binding, string id, CancellationToken token) => Task.FromResult(Found.GetValueOrDefault(id));
        public async Task<SyncWriteResult> WriteAsync(SyncBinding binding, SyncOperation operation, CancellationToken token)
        { Writes.Add((binding.ResourcePath, operation)); var unknown = Unknown; if (OnWrite is not null) await OnWrite(operation); return unknown ? new(SyncWriteStatus.UnknownOutcome) : new(SyncWriteStatus.Success, operation.Desired is null ? null : new(operation.RemoteId ?? "created", operation.Desired, Metadata: GoogleAccessSyncTests.Metadata())); }
    }
}
