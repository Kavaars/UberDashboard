using System.Security.Cryptography;
using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Synchronization;

public sealed partial class SyncCoordinator
{
    public Task AddFileAsync(Guid bindingId, Guid entityId, SyncStagedFile file, CancellationToken token = default) =>
        GuardedEditAsync(bindingId, entityId, (new JsonObject()), state =>
        {
            var owner = FileOwner(state, entityId);
            if (!SyncFileRules.Valid(file, state.Binding.Provider)) throw new InvalidDataException("Invalid staged file.");
            if (state.Binding.Provider == SyncProviderKind.GoogleCalendar && ((owner.Local?["attachments"] as JsonArray)?.Count ?? 0) +
                state.FileTransfers.Count(t => t.EntityId == entityId && t.IsActive) >= 25) throw new InvalidOperationException("Calendar attachment limit reached.");
            return state with { Transfers = state.FileTransfers.Append(new SyncTransfer(Guid.NewGuid(), entityId, file)).ToArray() };
        }, token);

    private static SyncEntity FileOwner(SyncState state, Guid entityId)
    {
        var owner = state.Items.Single(e => e.LocalId == entityId);
        if (!SyncFileRules.IsContentBinding(state.Binding) || owner.Kind != SyncEntityKind.Item || owner.Local is null || owner.Conflicts is { Length: > 0 } || GoogleRecurrence.Locked(state, owner))
            throw new InvalidOperationException("The file owner is unavailable.");
        if (state.Binding.Provider == SyncProviderKind.GoogleCalendar && (!GoogleEventOptions.IsStandard(owner.Local) || !GoogleEventOptions.CanEditMain(owner.Local, owner.Metadata)))
            throw new SyncContainerAccessException();
        return owner;
    }

    public Task AttachDriveFileAsync(Guid bindingId, Guid entityId, JsonObject attachment, CancellationToken token = default) =>
        GuardedEditAsync(bindingId, entityId, new JsonObject(), state => LinkDriveFile(state, entityId, attachment), token);

    private SyncState LinkDriveFile(SyncState state, Guid entityId, JsonObject attachment)
    {
        var owner = FileOwner(state, entityId);
        if (state.Binding.Provider != SyncProviderKind.GoogleCalendar || string.IsNullOrWhiteSpace(attachment["fileId"]?.GetValue<string>()) ||
            !Uri.TryCreate(attachment["fileUrl"]?.GetValue<string>(), UriKind.Absolute, out var url) || url.Scheme != "https" ||
            url.UserInfo.Length != 0 || url.Host is not ("drive.google.com" or "docs.google.com")) throw new InvalidDataException("Invalid Drive attachment.");
        var desired = (JsonObject)owner.Local!.DeepClone(); var attachments = desired["attachments"] as JsonArray ?? [];
        if (attachments.Any(a => a?["fileId"]?.GetValue<string>() == attachment["fileId"]!.GetValue<string>())) return state;
        if (attachments.Count >= 25) throw new InvalidOperationException("Calendar attachment limit reached.");
        attachments.Add(attachment.DeepClone()); desired["attachments"] = attachments;
        return SyncEdits.Edit(state, entityId, desired, clock.UtcNow);
    }

    public async Task CancelFileAsync(Guid bindingId, Guid transferId, CancellationToken token = default)
    {
        var binding = (await RequiredAsync(bindingId, token).ConfigureAwait(false)).Binding;
        var gate = _gates.GetOrAdd((binding.Provider, binding.ClientId, binding.AccountId), _ => new(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = await RequiredAsync(bindingId, token).ConfigureAwait(false); var transfer = state.FileTransfers.Single(t => t.Id == transferId);
            if (!transfer.IsActive) return;
            // Canceling the local job is durable even if the temporary cloud session cannot be removed.
            await UpdateAsync(bindingId, s => s with { Transfers = s.FileTransfers.Select(t => t.Id == transferId
                ? t with { State = SyncTransferState.Canceled, ProtectedSession = null, RetryAt = null } : t).ToArray() }, token).ConfigureAwait(false);
            if (_fileProviders.TryGetValue(binding.Provider, out var provider))
            {
                try { await provider.CancelSessionAsync(binding, transfer, token).ConfigureAwait(false); }
                catch (Exception e) when (e is SyncFileException or SyncAuthenticationException or HttpRequestException or IOException or OperationCanceledException) { }
            }
        }
        finally { gate.Release(); }
    }

    public Task RetryFileAsync(Guid bindingId, Guid transferId, CancellationToken token = default) => UpdateAsync(bindingId, state => state with
    {
        Transfers = state.FileTransfers.Select(t => t.Id == transferId && t.IsActive && t.State != SyncTransferState.Sending &&
            !(state.Binding.Provider == SyncProviderKind.MicrosoftTodo && t.State == SyncTransferState.UnknownOutcome)
            ? t with { State = SyncTransferState.Pending, RetryAt = null, ErrorCode = null } : t).ToArray()
    }, token);

    public async Task AdoptMicrosoftFileAsync(Guid bindingId, Guid transferId, string remoteId, CancellationToken token = default)
    {
        var binding = (await RequiredAsync(bindingId, token).ConfigureAwait(false)).Binding;
        var gate = _gates.GetOrAdd((binding.Provider, binding.ClientId, binding.AccountId), _ => new(1, 1)); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = await RequiredAsync(bindingId, token).ConfigureAwait(false); var transfer = state.FileTransfers.Single(t => t.Id == transferId);
            var owner = FileOwner(state, transfer.EntityId);
            if (binding.Provider != SyncProviderKind.MicrosoftTodo || transfer.State != SyncTransferState.UnknownOutcome ||
                transfer.OwnerRemoteId != owner.RemoteId) throw new InvalidOperationException("The uncertain transfer changed.");
            await using var buffer = new MemoryStream();
            await _fileProviders[binding.Provider].DownloadAsync(binding, owner, remoteId, buffer, token).ConfigureAwait(false);
            buffer.Position = 0; var hash = Convert.ToHexString(await SHA256.HashDataAsync(buffer, token).ConfigureAwait(false));
            if (buffer.Length != transfer.File.Size || !hash.Equals(transfer.File.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The selected attachment has different content.");
            await UpdateAsync(bindingId, s => s with { Transfers = s.FileTransfers.Select(t => t.Id == transferId && t.State == transfer.State
                ? t with { RemoteId = remoteId, Stage = SyncTransferStage.Link, State = SyncTransferState.Pending,
                    ProtectedSession = null, Attachment = new JsonObject { ["name"] = t.File.Name, ["size"] = t.File.Size, ["contentType"] = t.File.ContentType } } : t).ToArray() }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private async Task ProcessTransfersAsync(Guid bindingId, CancellationToken token)
    {
        var initial = await RequiredAsync(bindingId, token).ConfigureAwait(false);
        if (!_fileProviders.TryGetValue(initial.Binding.Provider, out var provider)) return;
        await UpdateAsync(bindingId, s => s with { Transfers = s.FileTransfers.Select(t => t.State == SyncTransferState.Sending && t.RetryAt <= clock.UtcNow
            ? InterruptedTransfer(s.Binding, t) : t).ToArray() }, token).ConfigureAwait(false);
        for (var count = 0; count < 100; count++)
        {
            var state = await RequiredAsync(bindingId, token).ConfigureAwait(false);
            if (!state.Binding.Enabled || state.Binding.NotBefore > clock.UtcNow) return;
            var next = state.FileTransfers.FirstOrDefault(t => t.State is SyncTransferState.Pending or SyncTransferState.Retry && (t.RetryAt is null || t.RetryAt <= clock.UtcNow) &&
                state.Items.Any(e => e.LocalId == t.EntityId && e.Local is not null && e.Conflicts is not { Length: > 0 } &&
                    (state.Binding.Provider == SyncProviderKind.GoogleCalendar || e.RemoteId is not null && e.Baseline is not null && !state.Queue.Any(o => o.EntityId == e.LocalId))));
            if (next is null) return;
            var owner = state.Items.Single(e => e.LocalId == next.EntityId);
            try { await CheckContainerAsync(state, owner.LocalId, owner.Local, token).ConfigureAwait(false); }
            catch (Exception error) when (error is SyncContainerAccessException or SyncContainerChangesException)
            { await UpdateAsync(bindingId, s => s with { Transfers = s.FileTransfers.Select(t => t.Id == next.Id ? t with { State = SyncTransferState.Forbidden, ErrorCode = "file-container-unavailable" } : t).ToArray() }, token).ConfigureAwait(false); continue; }
            if (next.Stage == SyncTransferStage.Link)
            {
                try { await UpdateAsync(bindingId, s => CompleteFile(s, next), token).ConfigureAwait(false); }
                catch (Exception error) when (error is InvalidOperationException or InvalidDataException or SyncContainerAccessException)
                { await UpdateAsync(bindingId, s => s with { Transfers = s.FileTransfers.Select(t => t.Id == next.Id ? t with { State = SyncTransferState.Failed, ErrorCode = "file-link-review-required" } : t).ToArray() }, token).ConfigureAwait(false); }
                continue;
            }
            var claimed = next with { State = SyncTransferState.Sending, Attempts = next.Attempts + 1, RetryAt = clock.UtcNow.AddMinutes(5),
                FinalChunkSent = next.Stage == SyncTransferStage.Upload && !next.ProbeRequired && next.Uploaded + SyncFileRules.ChunkSize >= next.File.Size ||
                    state.Binding.Provider == SyncProviderKind.MicrosoftTodo && next.Stage == SyncTransferStage.StartSession && next.File.Size == 0 };
            await UpdateAsync(bindingId, s => s with { Transfers = s.FileTransfers.Select(t => t.Id == next.Id ? claimed : t).ToArray() }, token).ConfigureAwait(false);
            SyncTransfer result;
            try { result = await provider.AdvanceAsync(state.Binding, owner, claimed, token).ConfigureAwait(false); }
            catch (SyncAuthenticationException) { result = claimed with { State = SyncTransferState.AuthenticationRequired, ErrorCode = "file-authentication-required" }; }
            catch (SyncFileException error)
            {
                result = claimed with { State = error.State, ErrorCode = error.Code, RetryAt = error.RetryAfter is { } delay ? clock.UtcNow + delay : null };
                if (error.State == SyncTransferState.Retry) result = result with { ProbeRequired = claimed.Stage == SyncTransferStage.Upload && error.Code is "invalid-file-acknowledgment" };
            }
            catch (Exception error) when (error is InvalidDataException or CryptographicException or FormatException)
            { result = claimed with { State = state.Binding.Provider == SyncProviderKind.MicrosoftTodo && claimed.FinalChunkSent ? SyncTransferState.UnknownOutcome : SyncTransferState.Failed,
                ProbeRequired = claimed.Stage == SyncTransferStage.Upload, ErrorCode = "invalid-file-checkpoint-or-response" }; }
            catch (Exception error) when (error is OperationCanceledException or HttpRequestException or IOException or System.Text.Json.JsonException)
            { result = InterruptedTransfer(state.Binding, claimed); }
            result = result with { RetryAt = result.State == SyncTransferState.Retry ? result.RetryAt ?? clock.UtcNow.AddSeconds(Math.Min(3600, Math.Pow(2, Math.Min(result.Attempts, 10))) + Random.Shared.NextDouble() * 3) : null };
            await UpdateAsync(bindingId, s => s with { Transfers = s.FileTransfers.Select(t => t.Id == next.Id ? result : t).ToArray(),
                Binding = result.State == SyncTransferState.Retry && result.RetryAt is { } retry ? s.Binding with { NotBefore = retry } : s.Binding }, CancellationToken.None).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (result.State != SyncTransferState.Pending) return;
        }
    }

    private static SyncTransfer InterruptedTransfer(SyncBinding binding, SyncTransfer transfer) => transfer with
    {
        State = binding.Provider == SyncProviderKind.MicrosoftTodo && transfer.FinalChunkSent ? SyncTransferState.UnknownOutcome : SyncTransferState.Retry,
        ProbeRequired = transfer.Stage == SyncTransferStage.Upload, RetryAt = null, ErrorCode = "file-transfer-interrupted"
    };

    private SyncState CompleteFile(SyncState state, SyncTransfer expected)
    {
        var transfer = state.FileTransfers.Single(t => t.Id == expected.Id); var owner = FileOwner(state, transfer.EntityId);
        if (transfer.Stage != SyncTransferStage.Link || transfer.Attachment is null || transfer.RemoteId is null) throw new InvalidDataException("Incomplete file acknowledgment.");
        if (state.Binding.Provider == SyncProviderKind.GoogleCalendar) state = LinkDriveFile(state, owner.LocalId, transfer.Attachment);
        else
        {
            if (owner.RemoteId != transfer.OwnerRemoteId) throw new InvalidOperationException("The task identity changed.");
            if (!state.Items.Any(e => e.Kind == SyncEntityKind.FileAttachment && e.ParentId == owner.LocalId && e.RemoteId == transfer.RemoteId))
                state = state with { Entities = state.Items.Append(new SyncEntity(Guid.NewGuid(), transfer.RemoteId, transfer.Attachment, transfer.Attachment,
                    Kind: SyncEntityKind.FileAttachment, ParentId: owner.LocalId)).ToArray() };
        }
        return state with { Transfers = state.FileTransfers.Select(t => t.Id == transfer.Id ? t with
            { State = SyncTransferState.Completed, Stage = SyncTransferStage.Done, ProtectedSession = null, Uploaded = t.File.Size, RetryAt = null } : t).ToArray() };
    }
}
