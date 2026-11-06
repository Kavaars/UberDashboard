using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace UberDashboard.Core.Synchronization;

public sealed record SyncStagedFile(Guid Id, string Name, string ContentType, long Size, string Sha256);
public enum SyncTransferStage { AllocateIdentity, StartSession, Upload, Link, Done }
public enum SyncTransferState { Pending, Sending, Retry, AuthenticationRequired, Forbidden, Failed, UnknownOutcome, Completed, Canceled }

/// <summary>Binary content stays in the managed profile. Session is an opaque protected envelope, never a URL.</summary>
public sealed record SyncTransfer(Guid Id, Guid EntityId, SyncStagedFile File,
    SyncTransferStage Stage = SyncTransferStage.AllocateIdentity, SyncTransferState State = SyncTransferState.Pending,
    string? RemoteId = null, string? ProtectedSession = null, long Uploaded = 0, DateTimeOffset? SessionExpires = null,
    bool FinalChunkSent = false, bool ProbeRequired = false, DateTimeOffset? RetryAt = null, int Attempts = 0,
    string? ErrorCode = null, JsonObject? Attachment = null, string? OwnerRemoteId = null)
{
    [JsonIgnore] public bool IsActive => State is not (SyncTransferState.Completed or SyncTransferState.Canceled);
}

public sealed record SyncOccurrenceWindow(string SeriesId, DateTimeOffset From, DateTimeOffset Until,
    DateTimeOffset FetchedAt, IReadOnlyList<string> Ids, string? MasterETag = null);

public enum SyncSeriesStage { TrimOriginal, CreateFollowing, Done }
public enum SyncSeriesState { AwaitingApproval, Pending, Sending, Retry, AuthenticationRequired, Forbidden, Conflict, UnknownOutcome, Completed, Canceled }
public sealed record SyncSeriesChange(Guid Id, Guid EntityId, string InstanceId, JsonObject OriginalStart,
    JsonObject OriginalMaster, JsonObject TrimmedMaster, string FollowingId, JsonObject FollowingMaster, string? OriginalETag,
    SyncSeriesStage Stage = SyncSeriesStage.TrimOriginal, SyncSeriesState State = SyncSeriesState.AwaitingApproval,
    string? ApprovalHash = null, DateTimeOffset? RetryAt = null, int Attempts = 0, string? ErrorCode = null,
    JsonObject? LastMaster = null, string? LastETag = null, IReadOnlyList<SyncEntity>? ArchivedExceptions = null,
    DateTimeOffset? CreatedAt = null, DateTimeOffset? CompletedAt = null);

public static class SyncFileRules
{
    public const int ChunkSize = 1024 * 1024;
    public const long MicrosoftMaximum = 25L * 1024 * 1024;
    public const long DriveMaximum = 5L * 1024 * 1024 * 1024 * 1024;
    public static bool IsContentBinding(SyncBinding binding) => binding.Provider == SyncProviderKind.MicrosoftTodo
        ? MicrosoftTodoRelations.TaskCollectionPath(binding.ResourcePath) == binding.ResourcePath : binding.ResourcePath.StartsWith("calendars/", StringComparison.Ordinal) && binding.ResourcePath.EndsWith("/events", StringComparison.Ordinal);
    public static long Maximum(SyncProviderKind provider) => provider == SyncProviderKind.MicrosoftTodo ? MicrosoftMaximum : DriveMaximum;
    public static bool Valid(SyncStagedFile file, SyncProviderKind provider) => file.Id != Guid.Empty && file.Size >= 0 && file.Size <= Maximum(provider) &&
        !string.IsNullOrWhiteSpace(file.Name) && file.Name.Length <= 255 && !file.Name.Any(c => char.IsControl(c) || c is '/' or '\\') &&
        file.Sha256.Length == 64 && file.Sha256.All(Uri.IsHexDigit) && !string.IsNullOrWhiteSpace(file.ContentType) && !file.ContentType.Any(char.IsControl);
    public static bool HasPending(SyncState state, Guid owner) => state.FileTransfers.Any(t => t.EntityId == owner && t.IsActive);
    public static bool BlocksWrite(SyncState state, SyncOperation operation) => state.Binding.Provider == SyncProviderKind.GoogleCalendar && HasPending(state, operation.EntityId);
}
