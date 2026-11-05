using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace UberDashboard.Core.Synchronization;

public enum SyncProviderKind { MicrosoftTodo, GoogleCalendar }
public enum SyncEntityKind { Item, ChecklistItem, LinkedResource }
public enum SyncOperationState { Pending, Sending, Retry, Conflict, AuthenticationRequired, Forbidden, Failed, UnknownOutcome, AwaitingApproval }
public enum SyncOperationKind { Create, Update, Delete }
public enum SyncConnectionStatus { Idle, Synchronizing, AuthenticationRequired, Forbidden, Offline, Throttled, Failed }

/// <summary>Identity contains no credentials. ResourcePath is relative to the provider's trusted API root.</summary>
public sealed record SyncBinding(Guid Id, SyncProviderKind Provider, string ClientId, string AccountId,
    string ResourcePath, string DisplayName, bool Enabled = false, bool Visible = false, DateTimeOffset? NotBefore = null);

public sealed record SyncEntity(Guid LocalId, string? RemoteId, JsonObject? Baseline, JsonObject? Local,
    string? ETag = null, long Revision = 0, JsonObject? Remote = null, string[]? Conflicts = null, JsonObject? Metadata = null,
    SyncEntityKind Kind = SyncEntityKind.Item, Guid? ParentId = null);

public sealed record SyncOperation(Guid Id, Guid EntityId, long Revision, SyncOperationKind Kind,
    JsonObject? Desired, string? RemoteId, string? ETag, SyncOperationState State = SyncOperationState.Pending,
    int Attempts = 0, DateTimeOffset? RetryAt = null, string? ErrorCode = null,
    Guid? DependsOn = null, string? ApprovalHash = null, JsonObject? Baseline = null);

public sealed record SyncRecovery(Guid EntityId, DateTimeOffset CreatedAt, JsonObject? Value, string Reason);

public sealed record SyncState(SyncBinding Binding, long Version = 0, string? Cursor = null,
    IReadOnlyList<SyncEntity>? Entities = null, IReadOnlyList<SyncOperation>? Operations = null,
    IReadOnlyList<SyncRecovery>? Recovery = null, DateTimeOffset? LastSuccess = null, int FormatVersion = 2,
    SyncConnectionStatus ConnectionStatus = SyncConnectionStatus.Idle, DateTimeOffset? LastAttempt = null)
{
    [JsonIgnore] public IReadOnlyList<SyncEntity> Items => Entities ?? [];
    [JsonIgnore] public IReadOnlyList<SyncOperation> Queue => Operations ?? [];
    [JsonIgnore] public IReadOnlyList<SyncRecovery> History => Recovery ?? [];
}

public sealed record SyncRemoteEntity(string Id, JsonObject? Fields, string? ETag = null, JsonObject? Metadata = null);
public sealed record SyncPage(IReadOnlyList<SyncRemoteEntity> Items, string? NextPage, string? Cursor, bool IsFullSnapshot);
public sealed record SyncRelationSnapshot(Guid ParentId, SyncEntityKind Kind, IReadOnlyList<SyncRemoteEntity> Items);
public enum SyncWriteStatus { Success, Conflict, AuthenticationRequired, Forbidden, Retry, Failed, UnknownOutcome }
public sealed record SyncWriteResult(SyncWriteStatus Status, SyncRemoteEntity? Entity = null,
    string? ErrorCode = null, TimeSpan? RetryAfter = null);
