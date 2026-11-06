using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Infrastructure.Integrations;

/// <summary>Small bounded requests. No automatic redirects, response bodies or session URLs in errors.</summary>
public abstract class SyncFileTransport(HttpClient http, ISyncFileStore files, ISyncSessionProtector protector) : ISyncFileProvider
{
    public abstract SyncProviderKind Kind { get; }
    protected ISyncFileStore Files => files;
    protected ISyncSessionProtector Protector => protector;
    protected abstract Task<string?> AccessAsync(SyncBinding binding, CancellationToken token);
    protected abstract Uri Trusted(SyncBinding binding, Uri uri);
    public abstract Task<SyncTransfer> AdvanceAsync(SyncBinding binding, SyncEntity owner, SyncTransfer transfer, CancellationToken token);
    public abstract Task CancelSessionAsync(SyncBinding binding, SyncTransfer transfer, CancellationToken token);
    public abstract Task DownloadAsync(SyncBinding binding, SyncEntity owner, string fileId, Stream destination, CancellationToken token, string? exportMimeType = null);
    public virtual Task<JsonObject> ReadDriveFileAsync(SyncBinding binding, string fileId, CancellationToken token) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<JsonObject>> ListDriveFilesAsync(SyncBinding binding, CancellationToken token) => throw new NotSupportedException();

    protected async Task<HttpResponseMessage> SendAsync(SyncBinding binding, HttpMethod method, Uri uri, HttpContent? content, CancellationToken token)
    {
        Trusted(binding, uri); // Validate before asking for credentials.
        var access = await AccessAsync(binding, token).ConfigureAwait(false) ?? throw new SyncAuthenticationException();
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
    }

    protected static async Task<JsonObject> BodyAsync(HttpResponseMessage response, CancellationToken token)
    {
        await response.Content.LoadIntoBufferAsync(36L * 1024 * 1024, token).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: token).ConfigureAwait(false) ?? throw new InvalidDataException("Missing file response.");
    }

    protected static async Task EnsureAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        string? reason = null;
        try { reason = (await BodyAsync(response, token).ConfigureAwait(false))["error"]?["errors"]?[0]?["reason"]?.GetValue<string>(); }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException) { }
        var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
        var state = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => SyncTransferState.AuthenticationRequired,
            HttpStatusCode.Forbidden when reason is "rateLimitExceeded" or "userRateLimitExceeded" => SyncTransferState.Retry,
            HttpStatusCode.Forbidden => SyncTransferState.Forbidden,
            HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout => SyncTransferState.Retry,
            _ when (int)response.StatusCode >= 500 => SyncTransferState.Retry,
            _ => SyncTransferState.Failed
        };
        throw new SyncFileException(state, "file-http-" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture), delay > TimeSpan.Zero ? delay : null);
    }

    protected static string Id(string value) => string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.Any(char.IsControl)
        ? throw new InvalidDataException("Invalid file identity.") : Uri.EscapeDataString(value);
    protected static bool SafeOrigin(Uri uri, string host) => uri.IsAbsoluteUri && uri.Scheme == "https" && uri.Host == host && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
    protected static SyncTransfer Next(SyncTransfer transfer, SyncTransferStage stage) => transfer with
    { Stage = stage, State = SyncTransferState.Pending, ErrorCode = null, RetryAt = null, ProbeRequired = false, FinalChunkSent = false };

    protected async Task<ByteArrayContent> ChunkAsync(SyncTransfer transfer, CancellationToken token)
    {
        var count = checked((int)Math.Min(SyncFileRules.ChunkSize, transfer.File.Size - transfer.Uploaded));
        var bytes = new byte[count];
        await using var stream = await files.OpenAsync(transfer.File, token).ConfigureAwait(false);
        stream.Seek(transfer.Uploaded, SeekOrigin.Begin);
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        if (count > 0) content.Headers.ContentRange = new ContentRangeHeaderValue(transfer.Uploaded, transfer.Uploaded + count - 1, transfer.File.Size);
        else content.Headers.TryAddWithoutValidation("Content-Range", "bytes */0");
        return content;
    }

    protected static JsonObject FileMetadata(SyncStagedFile file) => new() { ["name"] = file.Name, ["contentType"] = file.ContentType, ["size"] = file.Size };
}
