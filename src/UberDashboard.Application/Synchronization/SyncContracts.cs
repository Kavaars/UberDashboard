using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Synchronization;

public interface ISyncStore
{
    Task<IReadOnlyList<SyncBinding>> ListAsync(CancellationToken token = default);
    Task<SyncState?> ReadAsync(Guid bindingId, CancellationToken token = default);
    /// <summary>Creates or replaces a snapshot only if its version still matches. The entire queue commits atomically.</summary>
    Task<bool> SaveAsync(SyncState state, long expectedVersion, CancellationToken token = default);
}

public interface ISyncProvider
{
    SyncProviderKind Kind { get; }
    IReadOnlyList<string[]> AtomicGroups { get; }
    Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token);
    Task<SyncRemoteEntity?> GetAsync(SyncBinding binding, string remoteId, CancellationToken token);
    Task<SyncWriteResult> WriteAsync(SyncBinding binding, SyncOperation operation, CancellationToken token);
}

public interface IMicrosoftTodoProvider : ISyncProvider;
public interface IGoogleCalendarProvider : ISyncProvider;
public interface ISyncRelationProvider
{
    Task<IReadOnlyList<SyncRelationSnapshot>> ReadRelationsAsync(SyncBinding binding, IReadOnlyList<SyncEntity> parents, CancellationToken token);
}

/// <summary>Readonly capabilities can change without appearing in a provider's incremental feed.</summary>
public interface ISyncResourceMetadataProvider
{
    Task<SyncResourceMetadata?> ReadMetadataAsync(SyncBinding binding, CancellationToken token);
}

/// <summary>Silent only. Interactive consent is initiated by the user interface.</summary>
public interface IIntegrationAccountService
{
    Task<string?> GetAccessTokenAsync(SyncBinding binding, bool write, CancellationToken token);
}

public interface ISyncCoordinator
{
    Task SynchronizeAsync(Guid bindingId, CancellationToken token = default);
    Task SetEnabledAsync(Guid bindingId, bool enabled, CancellationToken token = default);
}

public sealed class SyncCursorExpiredException : Exception;
public sealed class SyncAuthenticationException : Exception;
public sealed class SyncDependentChangesException : Exception;
public sealed class SyncReadException(TimeSpan? retryAfter) : Exception
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
