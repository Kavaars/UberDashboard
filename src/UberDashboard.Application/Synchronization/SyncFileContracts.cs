using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Synchronization;

public interface ISyncFileStore
{
    Task<SyncStagedFile> StageAsync(string source, long maximum, CancellationToken token = default);
    Task<Stream> OpenAsync(SyncStagedFile file, CancellationToken token = default);
    Task<bool> VerifyAsync(SyncStagedFile file, CancellationToken token = default);
    Task DeleteAsync(Guid id, CancellationToken token = default);
}

public interface ISyncSessionProtector
{
    string Protect(SyncBinding binding, Guid transferId, string value);
    string Unprotect(SyncBinding binding, Guid transferId, string envelope);
}

public interface IIntegrationFileAccountService
{
    Task<string?> GetFileAccessTokenAsync(SyncBinding binding, CancellationToken token);
}

public interface ISyncFileProvider
{
    SyncProviderKind Kind { get; }
    Task<SyncTransfer> AdvanceAsync(SyncBinding binding, SyncEntity owner, SyncTransfer transfer, CancellationToken token);
    Task CancelSessionAsync(SyncBinding binding, SyncTransfer transfer, CancellationToken token);
    Task<System.Text.Json.Nodes.JsonObject> ReadDriveFileAsync(SyncBinding binding, string fileId, CancellationToken token);
    Task<IReadOnlyList<System.Text.Json.Nodes.JsonObject>> ListDriveFilesAsync(SyncBinding binding, CancellationToken token);
    Task DownloadAsync(SyncBinding binding, SyncEntity owner, string fileId, Stream destination, CancellationToken token, string? exportMimeType = null);
}

public sealed class SyncFileException(SyncTransferState state, string code, TimeSpan? retryAfter = null) : Exception(code)
{
    public SyncTransferState State { get; } = state;
    public string Code { get; } = code;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
