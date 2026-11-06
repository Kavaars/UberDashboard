using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;
using UberDashboard.Infrastructure.Integrations;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class SyncFileProviderTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("a=")]
    public async Task Microsoft_upload_checkpoints_each_chunk_and_uses_stable_creation_then_opaque_session_and_final_location(string listId)
    {
        var files = new Files(SyncFileRules.ChunkSize + 7); var binding = Binding(SyncProviderKind.MicrosoftTodo) with { ResourcePath = "lists/" + Uri.EscapeDataString(listId) + "/tasks" }; var calls = 0;
        using var handler = new Handler(async request =>
        {
            calls++; Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            if (calls == 1)
            {
                Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal("/v1.0/me/todo/" + binding.ResourcePath + "/task/attachments/createUploadSession", request.RequestUri!.AbsolutePath);
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync()); Assert.Equal(files.Bytes.Length, body!["attachmentInfo"]!["size"]!.GetValue<int>());
                return Json(new JsonObject { ["uploadUrl"] = "https://graph.microsoft.com/beta/users/user/todo/lists/" + listId + "/tasks/task/attachmentSessions/session", ["nextExpectedRanges"] = new JsonArray("0-") }.ToJsonString());
            }
            Assert.Equal(HttpMethod.Put, request.Method); Assert.EndsWith("/attachmentSessions/session/content", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            var start = calls == 2 ? 0 : SyncFileRules.ChunkSize; var expected = files.Bytes[start..]; if (calls == 2) expected = expected[..SyncFileRules.ChunkSize];
            Assert.Equal(expected, await request.Content!.ReadAsByteArrayAsync()); Assert.Equal(start, request.Content.Headers.ContentRange?.From);
            if (calls == 2) return Json("""{"NextExpectedRanges":["1048576"]}""");
            var result = new HttpResponseMessage(HttpStatusCode.Created); result.Headers.Location = new("https://graph.microsoft.com/beta/users/user/todo/lists/" + listId + "/tasks/task/Attachments/final%3D"); return result;
        }); using var http = new HttpClient(handler); var provider = new MicrosoftTodoFileProvider(http, new Accounts(), files, new Protector());
        var job = new SyncTransfer(Guid.NewGuid(), Owner.LocalId, files.Info);
        job = await provider.AdvanceAsync(binding, Owner, job, default); Assert.Equal("task", job.OwnerRemoteId);
        job = await provider.AdvanceAsync(binding, Owner, job, default); Assert.StartsWith("dpapi1:", job.ProtectedSession); Assert.Equal(SyncTransferStage.Upload, job.Stage);
        job = await provider.AdvanceAsync(binding, Owner, job, default); Assert.Equal(SyncFileRules.ChunkSize, job.Uploaded);
        job = await provider.AdvanceAsync(binding, Owner, job with { FinalChunkSent = true }, default);
        Assert.Equal(SyncTransferStage.Link, job.Stage); Assert.Equal("final=", job.RemoteId); Assert.Null(job.ProtectedSession); Assert.Equal(files.Info.Size, job.Uploaded); Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Microsoft_empty_file_uses_a_single_documented_POST_and_an_uncertain_response_is_never_assumed_to_have_failed()
    {
        var files = new Files(0); var binding = Binding(SyncProviderKind.MicrosoftTodo); var calls = 0;
        using var handler = new Handler(async request =>
        {
            calls++; Assert.Equal(HttpMethod.Post, request.Method); Assert.EndsWith("/attachments", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync()); Assert.Equal("", body?["contentBytes"]?.GetValue<string>());
            return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") };
        }); using var http = new HttpClient(handler); var provider = new MicrosoftTodoFileProvider(http, new Accounts(), files, new Protector());
        var job = new SyncTransfer(Guid.NewGuid(), Owner.LocalId, files.Info, SyncTransferStage.StartSession, OwnerRemoteId: Owner.RemoteId);
        var failure = await Assert.ThrowsAsync<SyncFileException>(() => provider.AdvanceAsync(binding, Owner, job, default)); Assert.Equal(SyncTransferState.UnknownOutcome, failure.State); Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Microsoft_interrupted_nonfinal_restarts_only_after_cancel_while_final_is_never_replayed(bool final)
    {
        var calls = 0; using var handler = new Handler(request =>
        { calls++; Assert.Equal(HttpMethod.Delete, request.Method); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); });
        using var http = new HttpClient(handler); var binding = Binding(SyncProviderKind.MicrosoftTodo); var files = new Files(2); var protector = new Protector();
        var job = new SyncTransfer(Guid.NewGuid(), Owner.LocalId, files.Info, SyncTransferStage.Upload, OwnerRemoteId: "task", ProbeRequired: true, FinalChunkSent: final);
        job = job with { ProtectedSession = protector.Protect(binding, job.Id, "https://graph.microsoft.com/v1.0/me/todo/lists/a/tasks/task/attachmentSessions/session") };
        var result = await new MicrosoftTodoFileProvider(http, new Accounts(), files, protector).AdvanceAsync(binding, Owner, job, default);
        Assert.Equal(final ? 0 : 1, calls); Assert.Equal(final ? SyncTransferState.UnknownOutcome : SyncTransferState.Pending, result.State);
        Assert.Equal(final ? SyncTransferStage.Upload : SyncTransferStage.StartSession, result.Stage);
    }

    [Fact]
    public async Task Google_persists_generated_id_before_POST_and_reconciles_lost_final_ack_by_querying_session_and_known_identity()
    {
        var files = new Files(7); var binding = Binding(SyncProviderKind.GoogleCalendar); var calls = 0; Guid jobId = Guid.Empty;
        using var handler = new Handler(async request =>
        {
            calls++; var uri = request.RequestUri!;
            if (calls == 1) { Assert.Contains("generateIds", uri.AbsolutePath, StringComparison.Ordinal); return Json("""{"ids":["known"]}"""); }
            if (calls == 2) return new(HttpStatusCode.NotFound);
            if (calls == 3)
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync()); Assert.Equal("known", body!["id"]!.GetValue<string>());
                Assert.Equal(jobId.ToString("N"), body["appProperties"]!["uberDashboardTransfer"]!.GetValue<string>());
                Assert.Equal("7", request.Content.Headers.GetValues("X-Upload-Content-Length").Single());
                var response = Json("{}"); response.Headers.Location = new("https://www.googleapis.com/upload/drive/v3/files?upload_id=session"); return response;
            }
            if (calls == 4)
            {
                Assert.Equal(HttpMethod.Put, request.Method); Assert.Equal("bytes */7", request.Content!.Headers.GetValues("Content-Range").Single()); Assert.Empty(await request.Content.ReadAsByteArrayAsync());
                return Json("{}");
            }
            Assert.Equal(5, calls); Assert.EndsWith("/files/known", uri.AbsolutePath, StringComparison.Ordinal);
            return Json(new JsonObject { ["id"] = "known", ["size"] = "7", ["name"] = "test.bin", ["mimeType"] = "application/octet-stream", ["webViewLink"] = "https://drive.google.com/file/d/known/view",
                ["sha256Checksum"] = files.Info.Sha256, ["appProperties"] = new JsonObject { ["uberDashboardTransfer"] = jobId.ToString("N") } }.ToJsonString());
        }); using var http = new HttpClient(handler); var provider = new GoogleDriveFileProvider(http, new Accounts(), files, new Protector());
        var job = new SyncTransfer(Guid.NewGuid(), Owner.LocalId, files.Info); jobId = job.Id;
        job = await provider.AdvanceAsync(binding, Owner, job, default); Assert.Equal("known", job.RemoteId); Assert.Equal(1, calls);
        job = await provider.AdvanceAsync(binding, Owner, job, default); Assert.Equal(SyncTransferStage.Upload, job.Stage);
        job = await provider.AdvanceAsync(binding, Owner, job with { ProbeRequired = true }, default);
        Assert.Equal(SyncTransferStage.Link, job.Stage); Assert.Equal("known", job.Attachment?["fileId"]?.GetValue<string>()); Assert.Equal(5, calls);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("bytes=0-262143", 262144)]
    public async Task Google_resume_uses_acknowledged_range_not_guessed_chunk_size(string? range, long expected)
    {
        using var handler = new Handler(request =>
        { var response = new HttpResponseMessage((HttpStatusCode)308); if (range is not null) response.Headers.TryAddWithoutValidation("Range", range); return Task.FromResult(response); });
        using var http = new HttpClient(handler); var files = new Files(1024 * 1024); var binding = Binding(SyncProviderKind.GoogleCalendar); var protector = new Protector();
        var job = new SyncTransfer(Guid.NewGuid(), Owner.LocalId, files.Info, SyncTransferStage.Upload, RemoteId: "known", ProbeRequired: true);
        job = job with { ProtectedSession = protector.Protect(binding, job.Id, "https://www.googleapis.com/upload/drive/v3/files?upload_id=session") };
        var result = await new GoogleDriveFileProvider(http, new Accounts(), files, protector).AdvanceAsync(binding, Owner, job, default);
        Assert.Equal(expected, result.Uploaded); Assert.False(result.ProbeRequired);
    }

    [Theory]
    [InlineData("https://evil.test/upload")]
    [InlineData("https://www.googleapis.com.evil.test/upload/drive/v3/files")]
    [InlineData("http://www.googleapis.com/upload/drive/v3/files")]
    [InlineData("https://www.googleapis.com/calendar/v3/calendars")]
    public async Task Untrusted_session_never_receives_a_bearer_token(string session)
    {
        var calls = 0; using var handler = new Handler(_ => { calls++; throw new InvalidOperationException(); }); using var http = new HttpClient(handler);
        var binding = Binding(SyncProviderKind.GoogleCalendar); var files = new Files(1); var protector = new Protector(); var accounts = new Accounts();
        var job = new SyncTransfer(Guid.NewGuid(), Owner.LocalId, files.Info, SyncTransferStage.Upload, RemoteId: "known"); job = job with { ProtectedSession = protector.Protect(binding, job.Id, session) };
        await Assert.ThrowsAsync<InvalidDataException>(() => new GoogleDriveFileProvider(http, accounts, files, protector).AdvanceAsync(binding, Owner, job, default));
        Assert.Equal(0, calls); Assert.Equal(0, accounts.Calls);
    }

    [Theory]
    [InlineData(401, SyncTransferState.AuthenticationRequired)]
    [InlineData(403, SyncTransferState.Forbidden)]
    [InlineData(429, SyncTransferState.Retry)]
    [InlineData(503, SyncTransferState.Retry)]
    public async Task File_errors_preserve_retry_after_without_echoing_response_content(int code, SyncTransferState expected)
    {
        using var handler = new Handler(_ => { var response = Json("""{"error":{"message":"private content"}}"""); response.StatusCode = (HttpStatusCode)code; response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(73)); return Task.FromResult(response); });
        using var http = new HttpClient(handler); var files = new Files(1);
        var error = await Assert.ThrowsAsync<SyncFileException>(() => new GoogleDriveFileProvider(http, new Accounts(), files, new Protector()).AdvanceAsync(Binding(SyncProviderKind.GoogleCalendar), Owner, new(Guid.NewGuid(), Owner.LocalId, files.Info), default));
        Assert.Equal(expected, error.State); Assert.Equal(TimeSpan.FromSeconds(73), error.RetryAfter); Assert.DoesNotContain("private", error.Message, StringComparison.Ordinal);
    }

    private static readonly SyncEntity Owner = new(Guid.NewGuid(), "task", new JsonObject { ["title"] = "Task" }, new JsonObject { ["title"] = "Task" });
    private static SyncBinding Binding(SyncProviderKind kind) => new(Guid.NewGuid(), kind, "client", "account", kind == SyncProviderKind.MicrosoftTodo ? "lists/a/tasks" : "calendars/primary/events", "Test", true);
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => action(request); }
    private sealed class Accounts : IIntegrationAccountService, IIntegrationFileAccountService
    {
        public int Calls;
        public Task<string?> GetAccessTokenAsync(SyncBinding binding, bool write, CancellationToken token) { Calls++; return Task.FromResult<string?>("test-token"); }
        public Task<string?> GetFileAccessTokenAsync(SyncBinding binding, CancellationToken token) => GetAccessTokenAsync(binding, true, token);
    }
    private sealed class Protector : ISyncSessionProtector
    {
        public string Protect(SyncBinding binding, Guid transferId, string value) => "dpapi1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        public string Unprotect(SyncBinding binding, Guid transferId, string envelope) => Encoding.UTF8.GetString(Convert.FromBase64String(envelope[7..]));
    }
    private sealed class Files(int length) : ISyncFileStore
    {
        public byte[] Bytes { get; } = Enumerable.Range(0, length).Select(n => (byte)(n % 251)).ToArray();
        public SyncStagedFile Info => new(Guid.NewGuid(), "test.bin", "application/octet-stream", Bytes.Length, Convert.ToHexString(SHA256.HashData(Bytes)));
        public Task<Stream> OpenAsync(SyncStagedFile file, CancellationToken token = default) => Task.FromResult<Stream>(new MemoryStream(Bytes, false));
        public Task<bool> VerifyAsync(SyncStagedFile file, CancellationToken token = default) => Task.FromResult(true);
        public Task DeleteAsync(Guid id, CancellationToken token = default) => Task.CompletedTask;
        public Task<SyncStagedFile> StageAsync(string source, long maximum, CancellationToken token = default) => throw new NotSupportedException();
    }
}
