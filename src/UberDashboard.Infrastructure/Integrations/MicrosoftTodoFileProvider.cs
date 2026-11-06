using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Infrastructure.Integrations;

public sealed class MicrosoftTodoFileProvider(HttpClient http, IIntegrationAccountService accounts, ISyncFileStore files, ISyncSessionProtector protector)
    : SyncFileTransport(http, files, protector)
{
    public override SyncProviderKind Kind => SyncProviderKind.MicrosoftTodo;
    protected override Task<string?> AccessAsync(SyncBinding binding, CancellationToken token) => accounts.GetAccessTokenAsync(binding, true, token);
    protected override Uri Trusted(SyncBinding binding, Uri uri)
    {
        if (binding.Provider != Kind || !SyncFileRules.IsContentBinding(binding) || !SafeOrigin(uri, "graph.microsoft.com")) throw new InvalidDataException("Untrusted file endpoint.");
        var path = Segments(uri); var bound = binding.ResourcePath.Split('/').Select(Uri.UnescapeDataString).ToArray();
        // Opaque URLs can use /users/{id} instead of /me and a server-selected API version.
        // They are only accepted after being returned by the stable createUploadSession endpoint.
        var start = path.Length > 2 && path[0] == "v1.0" && path[1] == "me" ? 2
            : path.Length > 3 && path[0] is "v1.0" or "beta" && path[1] == "users" ? 3 : -1;
        if (bound.Length != 3 || bound[0] != "lists" || bound[2] != "tasks" || start < 0 || path.Length < start + 6 ||
            path[start] != "todo" || path[start + 1] != "lists" || path[start + 2] != bound[1] || path[start + 3] != "tasks")
            throw new InvalidDataException("File endpoint belongs to another resource.");
        if (path.Any(p => p is "." or "..") || path[start + 5] is not ("attachments" or "Attachments" or "attachmentSessions")) throw new InvalidDataException("Invalid file endpoint.");
        return uri;
    }

    private static string[] Segments(Uri uri) => uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();

    private static Uri Collection(SyncBinding binding, string owner) => new("https://graph.microsoft.com/v1.0/me/todo/" + binding.ResourcePath + "/" + Id(owner) + "/attachments");

    private Uri Session(SyncBinding binding, SyncTransfer transfer)
    {
        var uri = Trusted(binding, new Uri(Protector.Unprotect(binding, transfer.Id, transfer.ProtectedSession ?? throw new InvalidDataException("Missing upload checkpoint."))));
        var parts = Segments(uri);
        if (transfer.OwnerRemoteId is null || parts.Length < 4 || parts[^2] != "attachmentSessions" || parts[^3] != transfer.OwnerRemoteId || parts[^4] != "tasks")
            throw new InvalidDataException("Upload session owner changed.");
        return uri;
    }

    public override async Task<SyncTransfer> AdvanceAsync(SyncBinding binding, SyncEntity owner, SyncTransfer transfer, CancellationToken token)
    {
        if (owner.RemoteId is null || transfer.OwnerRemoteId is not null && transfer.OwnerRemoteId != owner.RemoteId)
            throw new SyncFileException(SyncTransferState.Failed, "file-owner-changed");
        if (transfer.Stage == SyncTransferStage.AllocateIdentity)
            return Next(transfer with { OwnerRemoteId = owner.RemoteId }, SyncTransferStage.StartSession);
        if (transfer.Stage == SyncTransferStage.StartSession)
        {
            if (!await Files.VerifyAsync(transfer.File, token).ConfigureAwait(false)) throw new SyncFileException(SyncTransferState.Failed, "staged-file-changed");
            if (transfer.File.Size == 0)
            {
                using var emptyResponse = await SendAsync(binding, HttpMethod.Post, Collection(binding, owner.RemoteId), JsonContent.Create(new JsonObject
                { ["@odata.type"] = "#microsoft.graph.taskFileAttachment", ["name"] = transfer.File.Name, ["contentType"] = transfer.File.ContentType, ["contentBytes"] = "" }), token).ConfigureAwait(false);
                if ((int)emptyResponse.StatusCode >= 500 || emptyResponse.StatusCode == HttpStatusCode.RequestTimeout)
                    throw new SyncFileException(SyncTransferState.UnknownOutcome, "file-create-uncertain");
                await EnsureAsync(emptyResponse, token).ConfigureAwait(false);
                var body = await BodyAsync(emptyResponse, token).ConfigureAwait(false);
                return Next(transfer with { RemoteId = body["id"]?.GetValue<string>() ?? throw new InvalidDataException("Missing attachment identity."), Attachment = FileMetadata(transfer.File) }, SyncTransferStage.Link);
            }
            using var response = await SendAsync(binding, HttpMethod.Post, new Uri(Collection(binding, owner.RemoteId) + "/createUploadSession"), JsonContent.Create(new JsonObject
            { ["attachmentInfo"] = new JsonObject { ["attachmentType"] = "file", ["name"] = transfer.File.Name, ["size"] = transfer.File.Size } }), token).ConfigureAwait(false);
            await EnsureAsync(response, token).ConfigureAwait(false);
            var session = await BodyAsync(response, token).ConfigureAwait(false);
            var url = Trusted(binding, new Uri(session["uploadUrl"]!.GetValue<string>()));
            var checkpoint = transfer with { OwnerRemoteId = owner.RemoteId, ProtectedSession = Protector.Protect(binding, transfer.Id, url.AbsoluteUri), Uploaded = 0,
                SessionExpires = DateTimeOffset.TryParse(session["expirationDateTime"]?.GetValue<string>(), CultureInfo.InvariantCulture, out var expires) ? expires : null };
            _ = Session(binding, checkpoint);
            return Next(checkpoint, SyncTransferStage.Upload);
        }
        if (transfer.Stage != SyncTransferStage.Upload) return transfer;
        if (transfer.ProbeRequired)
        {
            // To Do does not document a GET session-status endpoint. Only an unfinished
            // non-final chunk can safely be discarded and restarted without duplicating a file.
            if (transfer.FinalChunkSent) return transfer with { State = SyncTransferState.UnknownOutcome, ErrorCode = "file-final-result-unknown" };
            await CancelSessionAsync(binding, transfer, token).ConfigureAwait(false);
            return Next(transfer with { ProtectedSession = null, Uploaded = 0 }, SyncTransferStage.StartSession);
        }
        if (transfer.SessionExpires <= DateTimeOffset.UtcNow)
            return Next(transfer with { ProtectedSession = null, Uploaded = 0 }, SyncTransferStage.StartSession);
        var sessionUri = Session(binding, transfer);
        if (transfer.FinalChunkSent && !await Files.VerifyAsync(transfer.File, token).ConfigureAwait(false)) throw new SyncFileException(SyncTransferState.Failed, "staged-file-changed");
        using var content = await ChunkAsync(transfer, token).ConfigureAwait(false);
        using var uploaded = await SendAsync(binding, HttpMethod.Put, new Uri(sessionUri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/content" + sessionUri.Query), content, token).ConfigureAwait(false);
        if ((int)uploaded.StatusCode >= 500 || uploaded.StatusCode == HttpStatusCode.RequestTimeout)
            return transfer with { State = transfer.FinalChunkSent ? SyncTransferState.UnknownOutcome : SyncTransferState.Retry, ProbeRequired = true, ErrorCode = "file-chunk-uncertain" };
        if (uploaded.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            return transfer.FinalChunkSent ? transfer with { State = SyncTransferState.UnknownOutcome, ErrorCode = "file-session-expired-after-final" }
                : Next(transfer with { ProtectedSession = null, Uploaded = 0 }, SyncTransferStage.StartSession);
        await EnsureAsync(uploaded, token).ConfigureAwait(false);
        if (uploaded.StatusCode == HttpStatusCode.Created)
        {
            var location = uploaded.Headers.Location ?? throw new InvalidDataException("Missing attachment location.");
            _ = Trusted(binding, location);
            var parts = Segments(location);
            if (parts.Length < 4 || !parts[^2].Equals("attachments", StringComparison.OrdinalIgnoreCase) || parts[^3] != owner.RemoteId || parts[^4] != "tasks") throw new InvalidDataException("Unexpected attachment location.");
            return Next(transfer with { RemoteId = Uri.UnescapeDataString(location.Segments[^1]), Uploaded = transfer.File.Size,
                ProtectedSession = null, Attachment = FileMetadata(transfer.File) }, SyncTransferStage.Link);
        }
        var progress = await BodyAsync(uploaded, token).ConfigureAwait(false);
        var ranges = (progress["nextExpectedRanges"] ?? progress["NextExpectedRanges"]) as JsonArray;
        if (ranges is not { Count: 1 } || !long.TryParse(ranges[0]?.GetValue<string>().Split('-')[0], CultureInfo.InvariantCulture, out var offset) ||
            offset != transfer.Uploaded + Math.Min(SyncFileRules.ChunkSize, transfer.File.Size - transfer.Uploaded) || offset >= transfer.File.Size)
            throw new SyncFileException(transfer.FinalChunkSent ? SyncTransferState.UnknownOutcome : SyncTransferState.Retry, "invalid-file-acknowledgment");
        return Next(transfer with { Uploaded = offset }, SyncTransferStage.Upload);
    }

    public override async Task CancelSessionAsync(SyncBinding binding, SyncTransfer transfer, CancellationToken token)
    {
        if (transfer.ProtectedSession is null) return;
        using var response = await SendAsync(binding, HttpMethod.Delete, Session(binding, transfer), null, token).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return;
        await EnsureAsync(response, token).ConfigureAwait(false);
    }

    public override async Task DownloadAsync(SyncBinding binding, SyncEntity owner, string fileId, Stream destination, CancellationToken token, string? exportMimeType = null)
    {
        if (owner.RemoteId is null) throw new InvalidOperationException("The task has no remote identity.");
        using var response = await SendAsync(binding, HttpMethod.Get, new Uri(Collection(binding, owner.RemoteId) + "/" + Id(fileId)), null, token).ConfigureAwait(false);
        await EnsureAsync(response, token).ConfigureAwait(false);
        var body = await BodyAsync(response, token).ConfigureAwait(false);
        if (body["id"]?.GetValue<string>() != fileId) throw new InvalidDataException("Attachment identity mismatch.");
        var encoded = body["contentBytes"]?.GetValue<string>() ?? throw new InvalidDataException("Missing attachment bytes.");
        if (encoded.Length > (SyncFileRules.MicrosoftMaximum + 2) / 3 * 4) throw new InvalidDataException("Attachment exceeds provider limit.");
        var bytes = Convert.FromBase64String(encoded);
        await destination.WriteAsync(bytes, token).ConfigureAwait(false);
    }
}
