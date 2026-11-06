using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Infrastructure.Integrations;

public sealed class GoogleDriveFileProvider(HttpClient http, IIntegrationFileAccountService accounts, ISyncFileStore files, ISyncSessionProtector protector)
    : SyncFileTransport(http, files, protector)
{
    private const string Fields = "id,name,mimeType,webViewLink,size,sha256Checksum,appProperties,capabilities,trashed,exportLinks";
    public override SyncProviderKind Kind => SyncProviderKind.GoogleCalendar;
    protected override Task<string?> AccessAsync(SyncBinding binding, CancellationToken token) => accounts.GetFileAccessTokenAsync(binding, token);
    protected override Uri Trusted(SyncBinding binding, Uri uri)
    {
        if (binding.Provider != Kind || !SyncFileRules.IsContentBinding(binding) || !SafeOrigin(uri, "www.googleapis.com") ||
            !(uri.AbsolutePath.StartsWith("/drive/v3/files/", StringComparison.Ordinal) || uri.AbsolutePath is "/drive/v3/files" or "/upload/drive/v3/files"))
            throw new InvalidDataException("Untrusted Drive file endpoint.");
        if (uri.AbsolutePath.Split('/').Any(p => Uri.UnescapeDataString(p) is "." or "..")) throw new InvalidDataException("Invalid Drive path.");
        return uri;
    }

    private static Uri FileUri(string id, string? query = null) => new("https://www.googleapis.com/drive/v3/files/" + Id(id) + "?supportsAllDrives=true&" + (query ?? "fields=" + Uri.EscapeDataString(Fields)));

    private async Task<JsonObject?> FindAsync(SyncBinding binding, string id, CancellationToken token)
    {
        using var response = await SendAsync(binding, HttpMethod.Get, FileUri(id), null, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureAsync(response, token).ConfigureAwait(false);
        var value = await BodyAsync(response, token).ConfigureAwait(false);
        if (value["id"]?.GetValue<string>() != id) throw new InvalidDataException("Drive identity mismatch.");
        return value;
    }

    public override async Task<JsonObject> ReadDriveFileAsync(SyncBinding binding, string fileId, CancellationToken token) =>
        await FindAsync(binding, fileId, token).ConfigureAwait(false) ?? throw new SyncFileException(SyncTransferState.Failed, "drive-file-unavailable");

    public override async Task<IReadOnlyList<JsonObject>> ListDriveFilesAsync(SyncBinding binding, CancellationToken token)
    {
        var items = new List<JsonObject>(); string? page = null; var seen = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            var uri = new Uri("https://www.googleapis.com/drive/v3/files?q=trashed%3Dfalse&pageSize=100&fields=" + Uri.EscapeDataString("nextPageToken,files(" + Fields + ")") +
                (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page)));
            using var response = await SendAsync(binding, HttpMethod.Get, uri, null, token).ConfigureAwait(false);
            await EnsureAsync(response, token).ConfigureAwait(false); var body = await BodyAsync(response, token).ConfigureAwait(false);
            items.AddRange((body["files"] as JsonArray ?? []).Select(n => n!.AsObject())); page = body["nextPageToken"]?.GetValue<string>();
            if (page is not null && (!seen.Add(page) || seen.Count > 10000)) throw new InvalidDataException("Invalid Drive pagination.");
        } while (page is not null);
        return items;
    }

    public static JsonObject Attachment(JsonObject file)
    {
        var id = file["id"]?.GetValue<string>() ?? throw new InvalidDataException("Missing Drive identity.");
        var url = file["webViewLink"]?.GetValue<string>();
        if (file["trashed"]?.GetValue<bool>() == true || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !SafeOrigin(uri, "drive.google.com") && !SafeOrigin(uri, "docs.google.com"))
            throw new InvalidDataException("Drive file has no usable attachment URL.");
        return new JsonObject { ["fileId"] = id, ["fileUrl"] = url, ["title"] = file["name"]?.DeepClone(), ["mimeType"] = file["mimeType"]?.DeepClone() };
    }

    private static SyncTransfer Linked(SyncTransfer transfer, JsonObject file)
    {
        if (file["appProperties"]?["uberDashboardTransfer"]?.GetValue<string>() != transfer.Id.ToString("N") ||
            !long.TryParse(file["size"]?.ToString(), CultureInfo.InvariantCulture, out var size) || size != transfer.File.Size ||
            file["sha256Checksum"] is { } hash && !hash.GetValue<string>().Equals(transfer.File.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new SyncFileException(SyncTransferState.UnknownOutcome, "drive-identity-content-mismatch");
        return Next(transfer with { Attachment = Attachment(file), Uploaded = transfer.File.Size, ProtectedSession = null }, SyncTransferStage.Link);
    }

    public override async Task<SyncTransfer> AdvanceAsync(SyncBinding binding, SyncEntity owner, SyncTransfer transfer, CancellationToken token)
    {
        if (transfer.Stage == SyncTransferStage.AllocateIdentity)
        {
            using var response = await SendAsync(binding, HttpMethod.Get, new Uri("https://www.googleapis.com/drive/v3/files/generateIds?count=1&space=drive&type=files"), null, token).ConfigureAwait(false);
            await EnsureAsync(response, token).ConfigureAwait(false); var body = await BodyAsync(response, token).ConfigureAwait(false);
            var id = body["ids"]?[0]?.GetValue<string>() ?? throw new InvalidDataException("Missing generated Drive identity.");
            _ = Id(id); return Next(transfer with { RemoteId = id }, SyncTransferStage.StartSession);
        }
        if (transfer.RemoteId is null) throw new InvalidDataException("Missing durable Drive identity.");
        if (transfer.Stage == SyncTransferStage.StartSession)
        {
            if (await FindAsync(binding, transfer.RemoteId, token).ConfigureAwait(false) is { } existing) return Linked(transfer, existing);
            if (!await Files.VerifyAsync(transfer.File, token).ConfigureAwait(false)) throw new SyncFileException(SyncTransferState.Failed, "staged-file-changed");
            var content = JsonContent.Create(new JsonObject { ["id"] = transfer.RemoteId, ["name"] = transfer.File.Name,
                ["mimeType"] = transfer.File.ContentType, ["appProperties"] = new JsonObject { ["uberDashboardTransfer"] = transfer.Id.ToString("N") } });
            content.Headers.TryAddWithoutValidation("X-Upload-Content-Type", transfer.File.ContentType);
            content.Headers.TryAddWithoutValidation("X-Upload-Content-Length", transfer.File.Size.ToString(CultureInfo.InvariantCulture));
            using var response = await SendAsync(binding, HttpMethod.Post, new Uri("https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&supportsAllDrives=true&fields=" + Uri.EscapeDataString(Fields)), content, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Conflict) return transfer with { State = SyncTransferState.Retry, ErrorCode = "drive-check-known-id" };
            await EnsureAsync(response, token).ConfigureAwait(false);
            var uri = Trusted(binding, response.Headers.Location ?? throw new InvalidDataException("Missing Drive session."));
            return Next(transfer with { ProtectedSession = Protector.Protect(binding, transfer.Id, uri.AbsoluteUri), Uploaded = 0 }, SyncTransferStage.Upload);
        }
        if (transfer.Stage != SyncTransferStage.Upload) return transfer;
        var session = Trusted(binding, new Uri(Protector.Unprotect(binding, transfer.Id, transfer.ProtectedSession ?? throw new InvalidDataException("Missing Drive session checkpoint."))));
        HttpContent chunk;
        if (transfer.ProbeRequired)
        {
            chunk = new ByteArrayContent([]);
            chunk.Headers.TryAddWithoutValidation("Content-Range", "bytes */" + transfer.File.Size.ToString(CultureInfo.InvariantCulture));
        }
        else chunk = await ChunkAsync(transfer, token).ConfigureAwait(false);
        if (transfer.FinalChunkSent && !await Files.VerifyAsync(transfer.File, token).ConfigureAwait(false))
        { chunk.Dispose(); throw new SyncFileException(SyncTransferState.Failed, "staged-file-changed"); }
        using var responseChunk = await SendAsync(binding, HttpMethod.Put, session, chunk, token).ConfigureAwait(false);
        if ((int)responseChunk.StatusCode == 308)
        {
            long offset = 0;
            if (responseChunk.Headers.TryGetValues("Range", out var ranges))
            {
                var range = ranges.Single();
                if (!range.StartsWith("bytes=0-", StringComparison.Ordinal) || !long.TryParse(range[8..], CultureInfo.InvariantCulture, out var last))
                    throw new InvalidDataException("Invalid Drive upload acknowledgment.");
                offset = checked(last + 1);
            }
            if (offset < 0 || offset > transfer.File.Size || !transfer.ProbeRequired && offset != transfer.Uploaded + Math.Min(SyncFileRules.ChunkSize, transfer.File.Size - transfer.Uploaded))
                throw new InvalidDataException("Invalid Drive upload position.");
            return Next(transfer with { Uploaded = offset }, SyncTransferStage.Upload);
        }
        if (responseChunk.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            return Next(transfer with { ProtectedSession = null, Uploaded = 0 }, SyncTransferStage.StartSession);
        await EnsureAsync(responseChunk, token).ConfigureAwait(false);
        // Re-read by the preallocated identity, checking ownership and content before attaching.
        return Linked(transfer, await ReadDriveFileAsync(binding, transfer.RemoteId, token).ConfigureAwait(false));
    }

    public override Task CancelSessionAsync(SyncBinding binding, SyncTransfer transfer, CancellationToken token) => Task.CompletedTask;
    // Drive documents expiration of abandoned sessions; canceling locally never deletes a Drive file.

    public override async Task DownloadAsync(SyncBinding binding, SyncEntity owner, string fileId, Stream destination, CancellationToken token, string? exportMimeType = null)
    {
        var metadata = await ReadDriveFileAsync(binding, fileId, token).ConfigureAwait(false);
        var native = metadata["mimeType"]?.GetValue<string>().StartsWith("application/vnd.google-apps.", StringComparison.Ordinal) == true;
        Uri uri;
        if (native)
        {
            if (exportMimeType is null || metadata["exportLinks"] is not JsonObject links || !links.ContainsKey(exportMimeType))
                throw new SyncFileException(SyncTransferState.Failed, "drive-select-export-format");
            uri = new Uri("https://www.googleapis.com/drive/v3/files/" + Id(fileId) + "/export?mimeType=" + Uri.EscapeDataString(exportMimeType));
        }
        else uri = FileUri(fileId, "alt=media");
        using var response = await SendAsync(binding, HttpMethod.Get, uri, null, token).ConfigureAwait(false);
        await EnsureAsync(response, token).ConfigureAwait(false);
        await response.Content.CopyToAsync(destination, token).ConfigureAwait(false);
    }
}
