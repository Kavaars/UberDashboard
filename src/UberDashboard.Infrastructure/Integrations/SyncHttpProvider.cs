using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Infrastructure.Integrations;

/// <summary>Transport restricted to one API origin. Callers must disable automatic HTTP redirects.</summary>
public abstract partial class SyncHttpProvider(HttpClient http, IIntegrationAccountService accounts) : ISyncProvider
{
    public abstract SyncProviderKind Kind { get; }
    public abstract IReadOnlyList<string[]> AtomicGroups { get; }
    protected abstract Uri Root { get; }
    protected abstract bool ValidPath(string path);
    protected abstract IReadOnlySet<string> WritableFields(string path);
    protected virtual SyncWriteResult? ValidateWrite(SyncBinding binding, SyncOperation operation, SyncRemoteEntity? current, JsonObject? patch) => null;
    protected virtual JsonObject? PreparePayload(SyncBinding binding, JsonObject? payload, SyncRemoteEntity? current) => payload;

    public abstract Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token);

    public virtual async Task<SyncRemoteEntity?> GetAsync(SyncBinding binding, string remoteId, CancellationToken token)
    {
        using var response = await SendAsync(binding, HttpMethod.Get, ItemUri(binding, remoteId), null, null, false, token).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return null;
        await EnsureReadAsync(response).ConfigureAwait(false);
        return Parse(binding, await BodyAsync(response, token).ConfigureAwait(false));
    }

    public async Task<SyncWriteResult> WriteAsync(SyncBinding binding, SyncOperation operation, CancellationToken token)
    {
        if (operation.State is SyncOperationState.AwaitingApproval ||
            SyncEdits.RequiresApproval(binding, operation.Baseline, operation.Desired) && operation.ApprovalHash != SyncEdits.Hash(operation.Desired))
            return new(SyncWriteStatus.Failed, ErrorCode: "approval-required");
        var fields = WritableFields(binding.ResourcePath);
        if (operation.Desired?.Any(p => !fields.Contains(p.Key)) == true)
            return new(SyncWriteStatus.Failed, ErrorCode: "unsupported-field");

        string? etag = operation.ETag;
        JsonObject? patch = operation.Desired;
        SyncRemoteEntity? current = null;
        if (operation.Kind != SyncOperationKind.Create)
        {
            if (operation.RemoteId is null) return new(SyncWriteStatus.Failed, ErrorCode: "missing-remote-id");
            try { current = await GetAsync(binding, operation.RemoteId, token).ConfigureAwait(false); }
            catch (SyncReadException limited) { return new(SyncWriteStatus.Retry, RetryAfter: limited.RetryAfter); }
            catch (SyncAuthenticationException) { return new(SyncWriteStatus.AuthenticationRequired); }
            catch (HttpRequestException error)
            {
                return new(error.StatusCode == HttpStatusCode.Forbidden ? SyncWriteStatus.Forbidden :
                    error.StatusCode is null || (int)error.StatusCode >= 500 ? SyncWriteStatus.Retry : SyncWriteStatus.Failed,
                    ErrorCode: "preflight-failed");
            }
            catch (OperationCanceledException) { return new(SyncWriteStatus.Retry, ErrorCode: "preflight-canceled"); }
            if (current is null)
                return operation.Kind == SyncOperationKind.Delete ? new(SyncWriteStatus.Success) : new(SyncWriteStatus.Conflict, ErrorCode: "remote-deleted");
            var merge = SyncMerge.Merge(operation.Baseline, operation.Desired, current.Fields, AtomicGroups);
            if (merge.Conflicts.Count > 0) return new(SyncWriteStatus.Conflict, current, "remote-changed");
            // Re-approval is required if a remotely changed event affects the reviewed consequences.
            if (operation.ApprovalHash is not null && !JsonNode.DeepEquals(current.Fields, operation.Baseline))
                return new(SyncWriteStatus.Conflict, current, "approved-base-changed");
            etag = current.ETag;
            patch = operation.Kind == SyncOperationKind.Delete ? null : SyncMerge.Patch(current.Fields!, merge.Value!);
            if (patch is { Count: 0 }) return new(SyncWriteStatus.Success, current);
        }

        if (ValidateWrite(binding, operation, current, patch) is { } rejected) return rejected;
        patch = PreparePayload(binding, patch, current);
        var uri = operation.Kind == SyncOperationKind.Create ? CollectionUri(binding) : ItemUri(binding, operation.RemoteId!);
        if (Kind == SyncProviderKind.GoogleCalendar && binding.ResourcePath.EndsWith("/events", StringComparison.Ordinal))
        {
            uri = new Uri(uri + "?sendUpdates=all&conferenceDataVersion=1&supportsAttachments=true");
            if (operation.Kind == SyncOperationKind.Create)
            {
                if (operation.RemoteId is null || !GoogleId().IsMatch(operation.RemoteId))
                    return new(SyncWriteStatus.Failed, ErrorCode: "missing-idempotency-id");
                patch = (JsonObject?)patch?.DeepClone();
                patch!["id"] = operation.RemoteId;
            }
        }
        var method = operation.Kind switch { SyncOperationKind.Create => HttpMethod.Post, SyncOperationKind.Update => HttpMethod.Patch, _ => HttpMethod.Delete };
        using var response = await SendAsync(binding, method, uri, patch, etag, true, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return await FailureAsync(response, token).ConfigureAwait(false);
        if (operation.Kind == SyncOperationKind.Delete) return new(SyncWriteStatus.Success);
        var saved = Parse(binding, await BodyAsync(response, token).ConfigureAwait(false));
        // Read-after-write preserves server normalization and detects an observable concurrent write.
        SyncRemoteEntity? verified;
        try { verified = await GetAsync(binding, saved.Id, token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is SyncAuthenticationException or HttpRequestException or OperationCanceledException or SyncReadException or IOException or System.Text.Json.JsonException)
        { return new(SyncWriteStatus.Success, saved, "verification-interrupted-after-ack"); }
        // A valid success response already acknowledges the write. A subsequent edit/deletion
        // does not turn that acknowledged POST into an operation that might need replay.
        return new(SyncWriteStatus.Success, verified ?? saved,
            verified is null || !JsonNode.DeepEquals(saved.Fields, verified.Fields) ? "post-write-changed" : null);
    }

    protected Uri CollectionUri(SyncBinding binding)
    {
        if (binding.Provider != Kind || !ValidPath(binding.ResourcePath) ||
            binding.ResourcePath.Split('/').Any(p => Uri.UnescapeDataString(p) is "." or "..") || binding.ResourcePath.Contains('\\'))
            throw new InvalidDataException("Untrusted synchronization resource path.");
        return Trusted(new Uri(Root, binding.ResourcePath));
    }

    private Uri ItemUri(SyncBinding binding, string id) => Trusted(new Uri(CollectionUri(binding).AbsoluteUri + "/" + EncodeItemId(id)));
    protected static string EncodeItemId(string id) => string.IsNullOrWhiteSpace(id) || id is "." or ".." || id.Any(char.IsControl)
        ? throw new InvalidDataException("Invalid resource ID.") : Uri.EscapeDataString(id);

    protected Uri Trusted(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.Host != Root.Host || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || !uri.AbsolutePath.StartsWith(Root.AbsolutePath, StringComparison.Ordinal) || uri.Fragment.Length != 0)
            throw new InvalidDataException("Untrusted synchronization continuation URL.");
        return uri;
    }

    protected async Task<HttpResponseMessage> SendAsync(SyncBinding binding, HttpMethod method, Uri uri,
        JsonObject? body, string? etag, bool write, CancellationToken token)
    {
        Trusted(uri);
        var accessToken = await accounts.GetAccessTokenAsync(binding, write, token).ConfigureAwait(false)
            ?? throw new SyncAuthenticationException();
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (etag is not null && write) request.Headers.TryAddWithoutValidation("If-Match", etag);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await http.SendAsync(request, token).ConfigureAwait(false);
    }

    protected static async Task<JsonObject> BodyAsync(HttpResponseMessage response, CancellationToken token) =>
        await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: token).ConfigureAwait(false)
        ?? throw new InvalidDataException("Empty API response.");

    protected static async Task EnsureReadAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new SyncAuthenticationException();
        if (response.StatusCode == HttpStatusCode.Gone) throw new SyncCursorExpiredException();
        if (!response.IsSuccessStatusCode)
        {
            var failure = await FailureAsync(response, CancellationToken.None).ConfigureAwait(false);
            if (failure.Status == SyncWriteStatus.Retry || response.StatusCode == HttpStatusCode.RequestTimeout || (int)response.StatusCode >= 500)
                throw new SyncReadException(failure.RetryAfter);
        }
        response.EnsureSuccessStatusCode();
    }

    protected SyncRemoteEntity Parse(SyncBinding binding, JsonObject value)
    {
        var id = value["id"]?.GetValue<string>() ?? throw new InvalidDataException("Missing API resource ID.");
        if (value.ContainsKey("@removed") || value["status"]?.GetValue<string>() == "cancelled" ||
            value["deleted"]?.GetValue<bool>() == true) return new(id, null);
        var fields = WritableFields(binding.ResourcePath);
        var projected = new JsonObject();
        foreach (var pair in value.Where(p => fields.Contains(p.Key))) projected[pair.Key] = pair.Value?.DeepClone();
        var metadata = new JsonObject();
        foreach (var pair in value.Where(p => !fields.Contains(p.Key))) metadata[pair.Key] = pair.Value?.DeepClone();
        return new(id, projected, value["etag"]?.GetValue<string>() ?? value["@odata.etag"]?.GetValue<string>(), metadata);
    }

    private static async Task<SyncWriteResult> FailureAsync(HttpResponseMessage response, CancellationToken token)
    {
        string? reason = null;
        try
        {
            var body = await BodyAsync(response, token).ConfigureAwait(false);
            var error = body["error"] as JsonObject;
            reason = error?["code"]?.ToString();
            if (error?["errors"] is JsonArray errors) reason = errors.FirstOrDefault()?["reason"]?.ToString() ?? reason;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException) { }
        TimeSpan? retry = response.Headers.RetryAfter?.Delta;
        if (response.Headers.RetryAfter?.Date is { } date) retry = date - DateTimeOffset.UtcNow;
        if (retry < TimeSpan.Zero) retry = TimeSpan.Zero;
        var status = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => SyncWriteStatus.AuthenticationRequired,
            HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict => SyncWriteStatus.Conflict,
            HttpStatusCode.TooManyRequests => SyncWriteStatus.Retry,
            HttpStatusCode.RequestTimeout => SyncWriteStatus.UnknownOutcome,
            HttpStatusCode.Forbidden when reason is "rateLimitExceeded" or "userRateLimitExceeded" => SyncWriteStatus.Retry,
            HttpStatusCode.Forbidden => SyncWriteStatus.Forbidden,
            _ when (int)response.StatusCode >= 500 => SyncWriteStatus.UnknownOutcome,
            _ => SyncWriteStatus.Failed,
        };
        return new(status, ErrorCode: reason ?? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture), RetryAfter: retry);
    }

    [GeneratedRegex("^[0-9a-v]{5,1024}$", RegexOptions.CultureInvariant)] private static partial Regex GoogleId();
}
