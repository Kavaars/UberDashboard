using System.Text.Json;
using System.Text.Json.Nodes;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Tests.Synchronization;

public sealed class SeriesSyncTests
{
    [Fact]
    public async Task Approved_split_commits_two_stages_with_a_preallocated_following_id_and_preserved_exception_copies()
    {
        var s = new Setup(); var plan = await s.Prepare(); Assert.Equal(SyncSeriesState.AwaitingApproval, plan.State); Assert.NotEmpty(plan.ArchivedExceptions!);
        await s.Coordinator.SynchronizeAsync(s.Binding.Id); Assert.Empty(s.Provider.Writes);
        await s.Coordinator.ApproveSeriesAsync(s.Binding.Id, plan); await s.Coordinator.SynchronizeAsync(s.Binding.Id);
        var done = s.Plan; Assert.Equal(SyncSeriesState.Completed, done.State); Assert.Equal(SyncSeriesStage.Done, done.Stage);
        Assert.Equal(2, s.Provider.Writes.Count); Assert.Equal("master", s.Provider.Writes[0].RemoteId); Assert.Equal(plan.FollowingId, s.Provider.Writes[1].RemoteId);
        Assert.Null(s.Provider.Writes[1].ETag); Assert.NotNull(s.Provider.Writes[1].ApprovalHash); Assert.Empty(s.Store.State.Queue);
        Assert.Contains(s.Store.State.Items, e => e.RemoteId == plan.FollowingId); Assert.Equal("Changed future", s.Provider.Remote[plan.FollowingId].Fields?["summary"]?.GetValue<string>());
    }

    [Theory]
    [InlineData(SyncSeriesStage.CreateFollowing)]
    [InlineData(SyncSeriesStage.Done)]
    public async Task Crash_between_server_ack_and_SQLite_checkpoint_reconciles_without_repeating_the_acknowledged_stage(SyncSeriesStage crashAt)
    {
        var s = new Setup(); var plan = await s.Prepare(); await s.Coordinator.ApproveSeriesAsync(s.Binding.Id, plan); s.Store.FailStage = crashAt;
        await Assert.ThrowsAsync<IOException>(() => s.Coordinator.SynchronizeAsync(s.Binding.Id)); Assert.Equal(SyncSeriesState.Sending, s.Plan.State);
        var id = s.Plan.FollowingId; s.Clock.UtcNow = s.Clock.UtcNow.AddMinutes(6); await s.NewCoordinator().SynchronizeAsync(s.Binding.Id);
        Assert.Equal(SyncSeriesState.Completed, s.Plan.State); Assert.Equal(id, s.Plan.FollowingId);
        Assert.Single(s.Provider.Writes, o => o.Kind == SyncOperationKind.Update); Assert.Single(s.Provider.Writes, o => o.Kind == SyncOperationKind.Create);
    }

    [Fact]
    public async Task Timeout_after_a_successful_create_is_reconciled_by_the_same_preallocated_id()
    {
        var s = new Setup(); var plan = await s.Prepare(); await s.Coordinator.ApproveSeriesAsync(s.Binding.Id, plan); s.Provider.ThrowAfterCreate = true;
        await s.Coordinator.SynchronizeAsync(s.Binding.Id); Assert.Equal(SyncSeriesState.UnknownOutcome, s.Plan.State); Assert.Equal(SyncSeriesStage.CreateFollowing, s.Plan.Stage);
        await s.NewCoordinator().SynchronizeAsync(s.Binding.Id); Assert.Equal(SyncSeriesState.Completed, s.Plan.State); Assert.Single(s.Provider.Writes, o => o.Kind == SyncOperationKind.Create);
    }

    [Fact]
    public async Task An_independent_remote_edit_after_trimming_requires_fresh_review_and_is_carried_into_the_following_series()
    {
        var s = new Setup(); var plan = await s.Prepare(); await s.Coordinator.ApproveSeriesAsync(s.Binding.Id, plan);
        s.Provider.AfterTrim = () => { var current = s.Provider.Remote["master"]; var fields = (JsonObject)current.Fields!.DeepClone(); fields["description"] = "New remote note"; s.Provider.Remote["master"] = current with { Fields = fields, ETag = "concurrent" }; };
        await s.Coordinator.SynchronizeAsync(s.Binding.Id); Assert.Equal(SyncSeriesState.Conflict, s.Plan.State); Assert.Single(s.Provider.Writes);
        var observed = await s.Coordinator.ReadSeriesReviewAsync(s.Binding.Id, plan.Id); var desired = (JsonObject)s.Plan.FollowingMaster.DeepClone(); desired["description"] = "New remote note";
        await s.Coordinator.RebaseSeriesAsync(s.Binding.Id, s.Plan, observed.Master, desired); Assert.Equal(SyncSeriesState.AwaitingApproval, s.Plan.State);
        await s.Coordinator.SynchronizeAsync(s.Binding.Id); Assert.Single(s.Provider.Writes);
        await s.Coordinator.ApproveSeriesAsync(s.Binding.Id, s.Plan); await s.Coordinator.SynchronizeAsync(s.Binding.Id);
        Assert.Equal(SyncSeriesState.Completed, s.Plan.State); Assert.Equal("New remote note", s.Provider.Remote[plan.FollowingId].Fields?["description"]?.GetValue<string>());
    }

    [Fact]
    public async Task Approval_is_bound_to_the_actual_draft_and_offline_editing_invalidates_an_older_review()
    {
        var s = new Setup(); var plan = await s.Prepare(); var desired = (JsonObject)plan.FollowingMaster.DeepClone(); desired["summary"] = "Changed again";
        await s.Coordinator.EditSeriesDraftAsync(s.Binding.Id, plan, desired);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Coordinator.ApproveSeriesAsync(s.Binding.Id, plan)); Assert.Empty(s.Provider.Writes);
        await s.Coordinator.ApproveSeriesAsync(s.Binding.Id, s.Plan); await s.Coordinator.SynchronizeAsync(s.Binding.Id); Assert.Equal("Changed again", s.Provider.Remote[plan.FollowingId].Fields?["summary"]?.GetValue<string>());
    }

    [Fact]
    public async Task Active_split_locks_master_and_occurrences_but_discarding_an_unstarted_draft_unlocks_them()
    {
        var s = new Setup(); var plan = await s.Prepare(); var desired = (JsonObject)s.Master.Local!.DeepClone(); desired["summary"] = "Other edit";
        await Assert.ThrowsAsync<SyncDependentChangesException>(() => s.Coordinator.EditAsync(s.Binding.Id, s.Master.LocalId, desired));
        var instance = s.Instance; var changed = (JsonObject)instance.Local!.DeepClone(); changed["summary"] = "Occurrence";
        await Assert.ThrowsAsync<SyncDependentChangesException>(() => s.Coordinator.EditAsync(s.Binding.Id, instance.LocalId, changed));
        await s.Coordinator.CancelSeriesAsync(s.Binding.Id, plan.Id); await s.Coordinator.EditAsync(s.Binding.Id, s.Master.LocalId, desired);
        Assert.Equal(SyncSeriesState.Canceled, s.Plan.State); Assert.Single(s.Store.State.Queue); Assert.Empty(s.Provider.Writes);
    }

    [Fact]
    public async Task A_started_split_cannot_be_discarded_and_existing_following_event_can_be_explicitly_accepted_without_overwriting_it()
    {
        var s = new Setup(); var plan = await s.Prepare(); await s.Coordinator.ApproveSeriesAsync(s.Binding.Id, plan);
        s.Provider.ThrowAfterCreate = true; await s.Coordinator.SynchronizeAsync(s.Binding.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Coordinator.CancelSeriesAsync(s.Binding.Id, plan.Id));
        var existing = s.Provider.Remote[plan.FollowingId]; var fields = (JsonObject)existing.Fields!.DeepClone(); fields["summary"] = "Edited on phone";
        s.Provider.Remote[plan.FollowingId] = existing with { Fields = fields, ETag = "phone" };
        await s.Coordinator.SynchronizeAsync(s.Binding.Id); Assert.Equal(SyncSeriesState.Conflict, s.Plan.State);
        await s.Coordinator.AcceptExistingSeriesAsync(s.Binding.Id, s.Plan, s.Provider.Remote[plan.FollowingId]);
        Assert.Equal(SyncSeriesState.Completed, s.Plan.State); Assert.Equal("Edited on phone", s.Provider.Remote[plan.FollowingId].Fields?["summary"]?.GetValue<string>()); Assert.Equal(2, s.Provider.Writes.Count);
    }

    [Fact]
    public async Task Full_root_snapshot_preserves_cached_generated_instances_and_their_offline_edits()
    {
        var s = new Setup(); await s.Coordinator.LoadOccurrencesAsync(s.Binding.Id, s.Master.LocalId, s.Clock.UtcNow, s.Clock.UtcNow.AddMonths(1));
        var generated = s.Store.State.Items.Single(e => e.RemoteId == "generated"); Assert.True(generated.Metadata?["projectionInstance"]?.GetValue<bool>());
        var desired = (JsonObject)generated.Local!.DeepClone(); desired["summary"] = "Offline edit";
        await s.Coordinator.EditAsync(s.Binding.Id, generated.LocalId, desired); await s.NewCoordinator().SynchronizeAsync(s.Binding.Id);
        var saved = s.Store.State.Items.Single(e => e.LocalId == generated.LocalId); Assert.Equal("Offline edit", saved.Local?["summary"]?.GetValue<string>()); Assert.Empty(saved.Conflicts ?? []);
        Assert.Equal(SyncOperationState.AwaitingApproval, Assert.Single(s.Store.State.Queue).State); Assert.Single(s.Store.State.InstanceWindows); Assert.Empty(s.Provider.Writes);
    }

    [Fact]
    public async Task Removing_the_master_from_a_full_snapshot_removes_unedited_generated_occurrences_too()
    {
        var s = new Setup(); await s.Coordinator.LoadOccurrencesAsync(s.Binding.Id, s.Master.LocalId, s.Clock.UtcNow, s.Clock.UtcNow.AddMonths(1));
        s.Provider.RootEmpty = true; await s.Coordinator.SynchronizeAsync(s.Binding.Id);
        Assert.All(s.Store.State.Items, e => Assert.Null(e.Local));
    }

    [Fact]
    public async Task Pending_occurrence_edits_prevent_split_preparation_and_guest_without_shared_edit_rights_cannot_split()
    {
        var s = new Setup(); var changed = (JsonObject)s.Instance.Local!.DeepClone(); changed["summary"] = "Draft";
        await s.Coordinator.EditAsync(s.Binding.Id, s.Instance.LocalId, changed); await Assert.ThrowsAsync<SyncDependentChangesException>(() => s.Prepare());
        s = new Setup(); var master = s.Provider.Remote["master"]; s.Provider.Remote["master"] = master with { Metadata = new JsonObject { ["organizer"] = new JsonObject { ["self"] = false } } };
        await Assert.ThrowsAsync<SyncContainerAccessException>(() => s.Prepare());
    }

    [Fact]
    public async Task Recovering_an_archived_exception_creates_a_new_single_event_and_preserves_the_archive()
    {
        var s = new Setup(); var plan = await s.Prepare(); var archived = Assert.Single(plan.ArchivedExceptions!);
        var fields = GoogleRecurrence.InstanceDraft(plan.OriginalMaster, archived, true); fields["summary"] = "Recovered exception";
        var id = await s.Coordinator.RestoreSeriesExceptionAsync(s.Binding.Id, plan.Id, archived, fields);
        var entity = s.Store.State.Items.Single(e => e.LocalId == id); Assert.Null(entity.RemoteId); Assert.Null(entity.Local?["recurrence"]); Assert.Null(entity.Metadata);
        Assert.Single(s.Plan.ArchivedExceptions!); Assert.NotEqual(archived.LocalId, id); Assert.NotEqual(s.Plan.FollowingId, Assert.Single(s.Store.State.Queue).RemoteId);
    }

    private sealed class Setup
    {
        public SyncBinding Binding { get; } = new(Guid.NewGuid(), SyncProviderKind.GoogleCalendar, "client", "account", "calendars/primary/events", "Test", true);
        public Clock Clock { get; } = new(); public MemoryStore Store { get; } public Provider Provider { get; } public SyncCoordinator Coordinator { get; }
        public SyncEntity Master => Store.State.Items.Single(e => e.RemoteId == "master"); public SyncEntity Instance => Store.State.Items.Single(e => e.RemoteId == "exception");
        public SyncSeriesChange Plan => Assert.Single(Store.State.SeriesPlans);
        public Setup()
        {
            var fields = JsonNode.Parse("""{"summary":"Series","description":"Note","start":{"dateTime":"2030-01-01T09:00:00Z","timeZone":"UTC"},"end":{"dateTime":"2030-01-01T10:00:00Z","timeZone":"UTC"},"recurrence":["RRULE:FREQ=DAILY;COUNT=10"]}""")!.AsObject();
            var metadata = JsonNode.Parse("""{"organizer":{"self":true}}""")!.AsObject();
            var exceptionMetadata = JsonNode.Parse("""{"organizer":{"self":true},"recurringEventId":"master","originalStartTime":{"dateTime":"2030-01-03T09:00:00Z","timeZone":"UTC"}}""")!.AsObject();
            var instanceFields = (JsonObject)fields.DeepClone(); instanceFields.Remove("recurrence"); instanceFields["start"] = exceptionMetadata["originalStartTime"]!.DeepClone(); instanceFields["end"] = new JsonObject { ["dateTime"] = "2030-01-03T10:00:00Z", ["timeZone"] = "UTC" };
            var master = new SyncEntity(Guid.NewGuid(), "master", fields, fields, "e0", Metadata: metadata); var instance = new SyncEntity(Guid.NewGuid(), "exception", instanceFields, instanceFields, "i0", Metadata: exceptionMetadata);
            Store = new(new(Binding, Entities: [master, instance])); Provider = new(new("master", fields, "e0", metadata), new("exception", instanceFields, "i0", exceptionMetadata)); Coordinator = NewCoordinator();
        }
        public SyncCoordinator NewCoordinator() => new(Store, [Provider], Clock);
        public async Task<SyncSeriesChange> Prepare()
        {
            var master = Master; var instance = Instance; var split = GoogleRecurrence.Split(master.Local!, instance.Metadata!["originalStartTime"]!.AsObject()); split.Following["summary"] = "Changed future";
            await Coordinator.PrepareSeriesAsync(Binding.Id, master, instance, split.Following); return Plan;
        }
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero); }
    private sealed class MemoryStore(SyncState state) : ISyncStore
    {
        public SyncState State = state; public SyncSeriesStage? FailStage;
        public Task<IReadOnlyList<SyncBinding>> ListAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<SyncBinding>>([State.Binding]);
        public Task<SyncState?> ReadAsync(Guid bindingId, CancellationToken token = default) => Task.FromResult(JsonSerializer.Deserialize<SyncState>(JsonSerializer.Serialize(State)));
        public Task<bool> SaveAsync(SyncState value, long expectedVersion, CancellationToken token = default)
        {
            if (FailStage is { } stage && value.SeriesPlans.Any(p => p.Stage == stage)) { FailStage = null; throw new IOException("Simulated crash before checkpoint"); }
            if (expectedVersion != State.Version) return Task.FromResult(false); State = JsonSerializer.Deserialize<SyncState>(JsonSerializer.Serialize(value with { Version = expectedVersion + 1 }))!; return Task.FromResult(true);
        }
    }
    private sealed class Provider : ISyncProvider, IGoogleSeriesProvider
    {
        public SyncProviderKind Kind => SyncProviderKind.GoogleCalendar; public IReadOnlyList<string[]> AtomicGroups => [["start", "end"], ["recurrence"]];
        public Dictionary<string, SyncRemoteEntity> Remote = new(StringComparer.Ordinal); public List<SyncOperation> Writes = []; public bool ThrowAfterCreate, RootEmpty; public Action? AfterTrim;
        public Provider(SyncRemoteEntity master, SyncRemoteEntity exception) { Remote[master.Id] = master; Remote[exception.Id] = exception; }
        public Task<SyncRemoteEntity?> GetAsync(SyncBinding binding, string id, CancellationToken token) => Task.FromResult(Remote.GetValueOrDefault(id));
        public Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token) => Task.FromResult(new SyncPage(RootEmpty ? [] : Remote.Values.Where(r => r.Id != "generated").ToArray(), null, "cursor", true));
        public Task<SyncPage> ReadInstancesAsync(SyncBinding binding, string id, DateTimeOffset from, DateTimeOffset until, string? page, CancellationToken token)
        { var source = Remote["exception"]; return Task.FromResult(new SyncPage([source, source with { Id = "generated" }], null, null, true)); }
        public Task<SyncWriteResult> WriteAsync(SyncBinding binding, SyncOperation operation, CancellationToken token)
        {
            if (operation.Kind == SyncOperationKind.Update && (!Remote.TryGetValue(operation.RemoteId!, out var old) || !JsonNode.DeepEquals(old.Fields, operation.Baseline))) return Task.FromResult(new SyncWriteResult(SyncWriteStatus.Conflict));
            Writes.Add(operation); var remote = new SyncRemoteEntity(operation.RemoteId!, (JsonObject)operation.Desired!.DeepClone(), "e" + Writes.Count, new JsonObject { ["organizer"] = new JsonObject { ["self"] = true } }); Remote[remote.Id] = remote;
            if (operation.Kind == SyncOperationKind.Update) AfterTrim?.Invoke();
            if (operation.Kind == SyncOperationKind.Create && ThrowAfterCreate) throw new HttpRequestException();
            return Task.FromResult(new SyncWriteResult(SyncWriteStatus.Success, remote));
        }
    }
}
