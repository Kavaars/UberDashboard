using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Tests.Synchronization;

public sealed class FileSyncCoordinatorTests
{
    [Fact]
    public async Task Google_upload_finishes_before_first_event_write_and_links_into_the_latest_local_revision()
    {
        var setup = new Setup(SyncProviderKind.GoogleCalendar, draft: true); var entity = setup.Store.State.Items[0];
        await setup.Coordinator.AddFileAsync(setup.Binding.Id, entity.LocalId, File);
        setup.Files.BeforeUpload = async () => await setup.Coordinator.EditAsync(setup.Binding.Id, entity.LocalId, new JsonObject { ["summary"] = "New edit" });
        await setup.Coordinator.SynchronizeAsync(setup.Binding.Id);
        var sent = Assert.Single(setup.Root.Writes); Assert.Equal("New edit", sent.Desired?["summary"]?.GetValue<string>());
        Assert.Equal("file", sent.Desired?["attachments"]?[0]?["fileId"]?.GetValue<string>());
        Assert.Equal(SyncTransferState.Completed, Assert.Single(setup.Store.State.FileTransfers).State); Assert.Empty(setup.Store.State.Queue);
    }

    [Fact]
    public async Task Microsoft_waits_for_root_create_and_does_not_replace_the_task_when_attachment_completes()
    {
        var s = new Setup(SyncProviderKind.MicrosoftTodo, draft: true); var entity = s.Store.State.Items[0];
        await s.Coordinator.AddFileAsync(s.Binding.Id, entity.LocalId, File); await s.Coordinator.SynchronizeAsync(s.Binding.Id);
        Assert.Single(s.Root.Writes); Assert.Equal("server", s.Files.SeenOwner); Assert.Equal(SyncTransferState.Completed, s.Store.State.FileTransfers[0].State);
        var child = Assert.Single(s.Store.State.Items, e => e.Kind == SyncEntityKind.FileAttachment); Assert.Equal(entity.LocalId, child.ParentId); Assert.Equal("file", child.RemoteId);
        Assert.Empty(s.Store.State.Queue); Assert.Equal("Title", s.Store.State.Items.Single(e => e.LocalId == entity.LocalId).Local?["title"]?.GetValue<string>());
    }

    [Theory]
    [InlineData(SyncProviderKind.GoogleCalendar)]
    [InlineData(SyncProviderKind.MicrosoftTodo)]
    public async Task Active_file_job_blocks_deleting_the_owner_and_survives_remote_delete_as_a_conflict(SyncProviderKind kind)
    {
        var s = new Setup(kind); var entity = s.Store.State.Items[0]; await s.Coordinator.AddFileAsync(s.Binding.Id, entity.LocalId, File);
        await Assert.ThrowsAsync<SyncDependentChangesException>(() => s.Coordinator.EditAsync(s.Binding.Id, entity.LocalId, null));
        s.Root.Page = new([new("server", null)], null, "next", false); await s.Coordinator.SynchronizeAsync(s.Binding.Id);
        var saved = s.Store.State.Items.Single(e => e.LocalId == entity.LocalId); Assert.Contains("$deleted", saved.Conflicts!); Assert.NotNull(saved.Local);
        Assert.Equal(0, s.Files.Uploads); Assert.True(s.Store.State.FileTransfers[0].IsActive);
    }

    [Fact]
    public async Task Interrupted_final_Microsoft_chunk_stays_unknown_after_restart_and_manual_retry_never_replays_it()
    {
        var s = new Setup(SyncProviderKind.MicrosoftTodo); await s.Coordinator.AddFileAsync(s.Binding.Id, s.Store.State.Items[0].LocalId, File);
        s.Files.Interrupt = true; await s.Coordinator.SynchronizeAsync(s.Binding.Id); var job = Assert.Single(s.Store.State.FileTransfers);
        Assert.Equal(SyncTransferState.UnknownOutcome, job.State); Assert.Equal(1, s.Files.Uploads);
        await s.NewCoordinator().RetryFileAsync(s.Binding.Id, job.Id); await s.NewCoordinator().SynchronizeAsync(s.Binding.Id);
        Assert.Equal(SyncTransferState.UnknownOutcome, s.Store.State.FileTransfers[0].State); Assert.Equal(1, s.Files.Uploads); Assert.Null(s.Store.State.LastSuccess);
        await s.Coordinator.AdoptMicrosoftFileAsync(s.Binding.Id, job.Id, "observed"); await s.Coordinator.SynchronizeAsync(s.Binding.Id);
        Assert.Equal(1, s.Files.Uploads); Assert.Equal("observed", s.Store.State.FileTransfers[0].RemoteId); Assert.Equal(SyncTransferState.Completed, s.Store.State.FileTransfers[0].State);
    }

    [Fact]
    public async Task Adopting_a_different_attachment_does_not_clear_the_unknown_transfer()
    {
        var s = new Setup(SyncProviderKind.MicrosoftTodo); await s.Coordinator.AddFileAsync(s.Binding.Id, s.Store.State.Items[0].LocalId, File); s.Files.Interrupt = true;
        await s.Coordinator.SynchronizeAsync(s.Binding.Id); s.Files.Download = [99];
        await Assert.ThrowsAsync<InvalidDataException>(() => s.Coordinator.AdoptMicrosoftFileAsync(s.Binding.Id, s.Store.State.FileTransfers[0].Id, "other"));
        Assert.Equal(SyncTransferState.UnknownOutcome, s.Store.State.FileTransfers[0].State);
    }

    [Fact]
    public async Task Lost_Google_final_response_retries_a_probe_with_the_same_remote_identity()
    {
        var s = new Setup(SyncProviderKind.GoogleCalendar); await s.Coordinator.AddFileAsync(s.Binding.Id, s.Store.State.Items[0].LocalId, File); s.Files.Interrupt = true;
        await s.Coordinator.SynchronizeAsync(s.Binding.Id); var first = s.Store.State.FileTransfers[0]; Assert.Equal(SyncTransferState.Retry, first.State); Assert.True(first.ProbeRequired);
        s.Clock.UtcNow = s.Clock.UtcNow.AddHours(2); s.Files.Interrupt = false; await s.NewCoordinator().SynchronizeAsync(s.Binding.Id);
        Assert.True(s.Files.SawProbe); Assert.Equal(first.RemoteId, s.Store.State.FileTransfers[0].RemoteId); Assert.Equal(SyncTransferState.Completed, s.Store.State.FileTransfers[0].State);
    }

    [Theory]
    [InlineData(SyncProviderKind.MicrosoftTodo, true, SyncTransferState.UnknownOutcome)]
    [InlineData(SyncProviderKind.MicrosoftTodo, false, SyncTransferState.Completed)]
    [InlineData(SyncProviderKind.GoogleCalendar, true, SyncTransferState.Completed)]
    public async Task Expired_sending_lease_recovers_after_restart_using_the_persisted_final_chunk_marker(SyncProviderKind kind, bool final, SyncTransferState expected)
    {
        var s = new Setup(kind); var owner = s.Store.State.Items[0];
        s.Store.State = s.Store.State with { Transfers = [new(Guid.NewGuid(), owner.LocalId, File, SyncTransferStage.Upload, SyncTransferState.Sending, "file", Uploaded: 0,
            FinalChunkSent: final, RetryAt: s.Clock.UtcNow.AddMinutes(-1), OwnerRemoteId: owner.RemoteId)] };
        await s.NewCoordinator().SynchronizeAsync(s.Binding.Id); Assert.Equal(expected, s.Store.State.FileTransfers[0].State);
        Assert.Equal(expected == SyncTransferState.UnknownOutcome ? 0 : 1, s.Files.Uploads);
    }

    [Fact]
    public async Task Google_attachment_limit_counts_queued_files_and_does_not_discard_existing_metadata()
    {
        var s = new Setup(SyncProviderKind.GoogleCalendar); var entity = s.Store.State.Items[0];
        var fields = new JsonObject { ["summary"] = "Title", ["attachments"] = new JsonArray(Enumerable.Range(0, 25).Select(n => (JsonNode)new JsonObject { ["fileId"] = n.ToString(), ["fileUrl"] = "https://drive.google.com/file/d/" + n + "/view" }).ToArray()) };
        s.Store.State = s.Store.State with { Entities = [entity with { Local = fields, Baseline = fields }] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Coordinator.AddFileAsync(s.Binding.Id, entity.LocalId, File)); Assert.Empty(s.Store.State.FileTransfers);
        Assert.Equal(25, s.Store.State.Items[0].Local?["attachments"]?.AsArray().Count);
    }

    [Fact]
    public async Task Cancel_is_durable_even_when_the_temporary_cloud_session_cannot_be_deleted()
    {
        var s = new Setup(SyncProviderKind.MicrosoftTodo); var id = s.Store.State.Items[0].LocalId; await s.Coordinator.AddFileAsync(s.Binding.Id, id, File);
        s.Files.CancelFails = true; await s.Coordinator.CancelFileAsync(s.Binding.Id, s.Store.State.FileTransfers[0].Id);
        Assert.Equal(SyncTransferState.Canceled, s.Store.State.FileTransfers[0].State); await s.Coordinator.EditAsync(s.Binding.Id, id, null); Assert.Single(s.Store.State.Queue);
    }

    private static readonly byte[] Bytes = [1, 2, 3];
    private static readonly SyncStagedFile File = new(Guid.NewGuid(), "file.bin", "application/octet-stream", 3, Convert.ToHexString(SHA256.HashData(Bytes)));
    private sealed class Setup
    {
        public SyncBinding Binding { get; }
        public MemoryStore Store { get; }
        public RootProvider Root { get; }
        public FileProvider Files { get; }
        public Clock Clock { get; } = new();
        public SyncCoordinator Coordinator { get; }
        public Setup(SyncProviderKind kind, bool draft = false)
        {
            Binding = new(Guid.NewGuid(), kind, "client", "account", kind == SyncProviderKind.MicrosoftTodo ? "lists/a/tasks" : "calendars/primary/events", "Test", true);
            var fields = new JsonObject { [kind == SyncProviderKind.MicrosoftTodo ? "title" : "summary"] = "Title" }; var id = Guid.NewGuid();
            var state = new SyncState(Binding, Entities: draft ? [] : [new(id, "server", fields, fields)]);
            if (draft) state = SyncEdits.Edit(state, id, fields, Clock.UtcNow);
            Store = new(state); Root = new(kind) { Page = new(draft ? [] : [new("server", fields)], null, "cursor", false) }; Files = new(kind); Coordinator = NewCoordinator();
        }
        public SyncCoordinator NewCoordinator() => new(Store, [Root], Clock, [Files]);
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero); }
    private sealed class MemoryStore(SyncState state) : ISyncStore
    {
        public SyncState State = state;
        public Task<IReadOnlyList<SyncBinding>> ListAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<SyncBinding>>([State.Binding]);
        public Task<SyncState?> ReadAsync(Guid bindingId, CancellationToken token = default) => Task.FromResult(JsonSerializer.Deserialize<SyncState>(JsonSerializer.Serialize(State)));
        public Task<bool> SaveAsync(SyncState value, long expectedVersion, CancellationToken token = default)
        { if (expectedVersion != State.Version) return Task.FromResult(false); State = JsonSerializer.Deserialize<SyncState>(JsonSerializer.Serialize(value with { Version = expectedVersion + 1 }))!; return Task.FromResult(true); }
    }
    private sealed class RootProvider(SyncProviderKind kind) : ISyncProvider
    {
        public SyncProviderKind Kind => kind; public IReadOnlyList<string[]> AtomicGroups => []; public SyncPage Page = new([], null, "cursor", false);
        public List<SyncOperation> Writes = [];
        public Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token) => Task.FromResult(Page);
        public Task<SyncRemoteEntity?> GetAsync(SyncBinding binding, string remoteId, CancellationToken token) => Task.FromResult(Page.Items.FirstOrDefault(e => e.Id == remoteId));
        public Task<SyncWriteResult> WriteAsync(SyncBinding binding, SyncOperation operation, CancellationToken token)
        { Writes.Add(operation); var remote = new SyncRemoteEntity("server", operation.Desired); Page = new([remote], null, "cursor", false); return Task.FromResult(new SyncWriteResult(SyncWriteStatus.Success, remote)); }
    }
    private sealed class FileProvider(SyncProviderKind kind) : ISyncFileProvider
    {
        public SyncProviderKind Kind => kind; public int Uploads; public bool Interrupt, SawProbe, CancelFails; public string? SeenOwner; public byte[] Download = Bytes; public Func<Task>? BeforeUpload;
        public async Task<SyncTransfer> AdvanceAsync(SyncBinding binding, SyncEntity owner, SyncTransfer t, CancellationToken token)
        {
            SeenOwner = owner.RemoteId;
            if (t.Stage == SyncTransferStage.AllocateIdentity) return t with { Stage = SyncTransferStage.StartSession, State = SyncTransferState.Pending, RemoteId = "file", OwnerRemoteId = owner.RemoteId };
            if (t.Stage == SyncTransferStage.StartSession) return t with { Stage = SyncTransferStage.Upload, State = SyncTransferState.Pending };
            Uploads++; SawProbe |= t.ProbeRequired; if (BeforeUpload is not null) await BeforeUpload(); if (Interrupt) throw new IOException();
            return t with { Stage = SyncTransferStage.Link, State = SyncTransferState.Pending, Attachment = kind == SyncProviderKind.MicrosoftTodo ? new JsonObject { ["name"] = "file.bin" }
                : new JsonObject { ["fileId"] = "file", ["fileUrl"] = "https://drive.google.com/file/d/file/view", ["title"] = "file.bin" } };
        }
        public Task CancelSessionAsync(SyncBinding binding, SyncTransfer transfer, CancellationToken token) => CancelFails ? Task.FromException(new IOException()) : Task.CompletedTask;
        public Task<JsonObject> ReadDriveFileAsync(SyncBinding binding, string fileId, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<JsonObject>> ListDriveFilesAsync(SyncBinding binding, CancellationToken token) => throw new NotSupportedException();
        public Task DownloadAsync(SyncBinding binding, SyncEntity owner, string fileId, Stream destination, CancellationToken token, string? exportMimeType = null) => destination.WriteAsync(Download, token).AsTask();
    }
}
