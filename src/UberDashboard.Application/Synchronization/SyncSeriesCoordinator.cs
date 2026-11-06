using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Synchronization;

public sealed partial class SyncCoordinator
{
    public async Task LoadOccurrencesAsync(Guid bindingId, Guid entityId, DateTimeOffset from, DateTimeOffset until, CancellationToken token = default)
    {
        var binding = (await RequiredAsync(bindingId, token).ConfigureAwait(false)).Binding;
        var gate = _gates.GetOrAdd((binding.Provider, binding.ClientId, binding.AccountId), _ => new(1, 1)); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = await RequiredAsync(bindingId, token).ConfigureAwait(false); var master = state.Items.Single(e => e.LocalId == entityId);
            if (binding.Provider != SyncProviderKind.GoogleCalendar || master.RemoteId is null || _providers[binding.Provider] is not IGoogleSeriesProvider provider ||
                until <= from || until - from > TimeSpan.FromDays(3660)) throw new InvalidOperationException("The series or range is unavailable.");
            var remoteMaster = await _providers[binding.Provider].GetAsync(binding, master.RemoteId, token).ConfigureAwait(false);
            if (remoteMaster?.Fields?["recurrence"] is not JsonArray { Count: > 0 }) throw new InvalidOperationException("The series no longer exists.");
            var entries = new Dictionary<string, SyncRemoteEntity>(StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal); string? page = null;
            do
            {
                // Fetch a guard interval so all-day and far-away IANA zones cannot lose
                // occurrences near the user's date boundaries. The UI filters its exact range.
                var response = await provider.ReadInstancesAsync(binding, master.RemoteId, from.AddDays(-2), until.AddDays(2), page, token).ConfigureAwait(false);
                foreach (var item in response.Items)
                {
                    if (item.Metadata?["recurringEventId"]?.GetValue<string>() != master.RemoteId) throw new InvalidDataException("Instance identity mismatch.");
                    entries[item.Id] = item;
                }
                page = response.NextPage;
                if (page is not null && (!seen.Add(page) || seen.Count > 10000)) throw new InvalidDataException("Invalid occurrence pagination.");
            } while (page is not null);
            var verified = await _providers[binding.Provider].GetAsync(binding, master.RemoteId, token).ConfigureAwait(false);
            if (verified?.ETag != remoteMaster.ETag || !JsonNode.DeepEquals(verified?.Fields, remoteMaster.Fields)) throw new InvalidOperationException("The series changed during retrieval.");
            await UpdateAsync(bindingId, s =>
            {
                var projected = entries.ToDictionary(p => p.Key, p =>
                {
                    var metadata = (JsonObject?)p.Value.Metadata?.DeepClone() ?? new();
                    var previous = s.Items.FirstOrDefault(e => e.RemoteId == p.Key);
                    if (previous is null || previous.Metadata?["projectionInstance"]?.GetValue<bool>() == true) metadata["projectionInstance"] = true;
                    return p.Value with { Metadata = metadata };
                }, StringComparer.Ordinal);
                projected[master.RemoteId] = remoteMaster;
                var merged = ApplyRemote(s, projected, false, s.Cursor, _providers[binding.Provider].AtomicGroups);
                return merged with { OccurrenceWindows = merged.InstanceWindows.Where(w => !(w.SeriesId == master.RemoteId && w.From == from && w.Until == until))
                    .Append(new SyncOccurrenceWindow(master.RemoteId, from, until, clock.UtcNow, entries.Keys.ToArray(), remoteMaster.ETag)).ToArray() };
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async Task<Guid> PrepareSeriesAsync(Guid bindingId, SyncEntity expectedMaster, SyncEntity expectedInstance, JsonObject desiredFollowing, CancellationToken token = default)
    {
        var binding = (await RequiredAsync(bindingId, token).ConfigureAwait(false)).Binding;
        var gate = _gates.GetOrAdd((binding.Provider, binding.ClientId, binding.AccountId), _ => new(1, 1)); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (binding.Provider != SyncProviderKind.GoogleCalendar || expectedMaster.RemoteId is null || expectedInstance.RemoteId is null) throw new InvalidOperationException();
            var provider = _providers[binding.Provider];
            var state = await RequiredAsync(bindingId, token).ConfigureAwait(false);
            await CheckContainerAsync(state, expectedMaster.LocalId, desiredFollowing, token).ConfigureAwait(false);
            await PullAsync(binding, provider, state.Cursor, token).ConfigureAwait(false);
            var remote = await provider.GetAsync(binding, expectedMaster.RemoteId, token).ConfigureAwait(false);
            var instance = await provider.GetAsync(binding, expectedInstance.RemoteId, token).ConfigureAwait(false);
            if (remote?.Fields is null || remote.ETag != expectedMaster.ETag || !JsonNode.DeepEquals(remote.Fields, expectedMaster.Local) ||
                instance?.Metadata?["recurringEventId"]?.GetValue<string>() != expectedMaster.RemoteId ||
                !JsonNode.DeepEquals(instance.Metadata?["originalStartTime"], expectedInstance.Metadata?["originalStartTime"]))
                throw new InvalidOperationException("The reviewed series changed.");
            if (!GoogleEventOptions.CanManageAttendees(remote.Fields, remote.Metadata)) throw new SyncContainerAccessException();
            var originalStart = instance.Metadata!["originalStartTime"]!.AsObject();
            var split = await Task.Run(() => GoogleRecurrence.Split(remote.Fields, originalStart, token), token).ConfigureAwait(false);
            await Task.Run(() => GoogleRecurrence.Validate(desiredFollowing), token).ConfigureAwait(false);
            var id = Guid.NewGuid();
            await UpdateAsync(bindingId, s =>
            {
                var master = s.Items.Single(e => e.LocalId == expectedMaster.LocalId); EnsureSeriesReady(s, master);
                if (master.Revision != expectedMaster.Revision || !JsonNode.DeepEquals(master.Local, remote.Fields)) throw new InvalidOperationException("The local series changed.");
                var cut = GoogleRecurrence.Time(originalStart, remote.Fields["start"]?["timeZone"]?.GetValue<string>());
                var archived = s.Items.Where(e => e.Metadata?["recurringEventId"]?.GetValue<string>() == master.RemoteId && e.Metadata?["originalStartTime"] is JsonObject time &&
                    GoogleRecurrence.Time(time, cut.TzId) >= cut).ToArray();
                var plan = new SyncSeriesChange(id, master.LocalId, instance.Id, (JsonObject)originalStart.DeepClone(), (JsonObject)remote.Fields.DeepClone(), split.Trimmed,
                    Guid.NewGuid().ToString("N"), (JsonObject)desiredFollowing.DeepClone(), remote.ETag, ArchivedExceptions: archived, CreatedAt: clock.UtcNow);
                return s with { SeriesChanges = s.SeriesPlans.Append(plan).ToArray() };
            }, token).ConfigureAwait(false); return id;
        }
        finally { gate.Release(); }
    }

    private static void EnsureSeriesReady(SyncState state, SyncEntity master)
    {
        var related = state.Items.Where(e => e.LocalId == master.LocalId || e.Metadata?["recurringEventId"]?.GetValue<string>() == master.RemoteId).Select(e => e.LocalId).ToHashSet();
        if (GoogleRecurrence.Locked(state, master) || master.Local is null || state.Queue.Any(o => related.Contains(o.EntityId)) ||
            state.FileTransfers.Any(t => t.IsActive && related.Contains(t.EntityId)) || state.Items.Any(e => related.Contains(e.LocalId) && e.Conflicts is { Length: > 0 })) throw new SyncDependentChangesException();
    }

    public Task ApproveSeriesAsync(Guid bindingId, SyncSeriesChange expected, CancellationToken token = default) => UpdateAsync(bindingId, s =>
    {
        var current = s.SeriesPlans.Single(p => p.Id == expected.Id);
        if (current.State != SyncSeriesState.AwaitingApproval || SeriesHash(current) != SeriesHash(expected)) throw new InvalidOperationException("The reviewed split changed.");
        return s with { SeriesChanges = s.SeriesPlans.Select(p => p.Id == current.Id ? p with { State = SyncSeriesState.Pending, ApprovalHash = SeriesHash(p), RetryAt = null, ErrorCode = null } : p).ToArray() };
    }, token);

    public async Task EditSeriesDraftAsync(Guid bindingId, SyncSeriesChange expected, JsonObject following, CancellationToken token = default)
    {
        await Task.Run(() => GoogleRecurrence.Validate(following), token).ConfigureAwait(false);
        await UpdateAsync(bindingId, s =>
        {
            var plan = s.SeriesPlans.Single(p => p.Id == expected.Id);
            if (plan.Attempts != 0 || plan.State != SyncSeriesState.AwaitingApproval || SeriesHash(plan) != SeriesHash(expected)) throw new InvalidOperationException("The split changed.");
            return s with { SeriesChanges = s.SeriesPlans.Select(p => p.Id == plan.Id ? p with { FollowingMaster = (JsonObject)following.DeepClone(), ApprovalHash = null } : p).ToArray() };
        }, token).ConfigureAwait(false);
    }

    public async Task<Guid> RestoreSeriesExceptionAsync(Guid bindingId, Guid planId, SyncEntity expected, JsonObject desired, CancellationToken token = default)
    {
        if (desired["recurrence"] is JsonArray { Count: > 0 } || desired["status"]?.GetValue<string>() == "cancelled") throw new InvalidOperationException("Recovery creates a single active event.");
        var id = Guid.NewGuid();
        await GuardedEditAsync(bindingId, id, desired, s =>
        {
            var snapshot = s.SeriesPlans.Single(p => p.Id == planId).ArchivedExceptions?.Single(e => e.LocalId == expected.LocalId);
            if (snapshot is null || !JsonNode.DeepEquals(snapshot.Local, expected.Local) || !JsonNode.DeepEquals(snapshot.Metadata, expected.Metadata)) throw new InvalidOperationException("The recovery snapshot changed.");
            return SyncEdits.Edit(s, id, desired, clock.UtcNow);
        }, token).ConfigureAwait(false); return id;
    }

    public async Task CancelSeriesAsync(Guid bindingId, Guid planId, CancellationToken token = default)
    {
        var binding = (await RequiredAsync(bindingId, token).ConfigureAwait(false)).Binding;
        var gate = _gates.GetOrAdd((binding.Provider, binding.ClientId, binding.AccountId), _ => new(1, 1)); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await UpdateAsync(bindingId, s =>
            {
                var plan = s.SeriesPlans.Single(p => p.Id == planId);
                if (plan.Attempts > 0 || plan.Stage != SyncSeriesStage.TrimOriginal || plan.State is SyncSeriesState.Sending or SyncSeriesState.UnknownOutcome)
                    throw new InvalidOperationException("A started split must be reconciled and completed.");
                return s with { SeriesChanges = s.SeriesPlans.Select(p => p.Id == planId ? p with { State = SyncSeriesState.Canceled, CompletedAt = clock.UtcNow } : p).ToArray() };
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async Task<(SyncRemoteEntity? Master, SyncRemoteEntity? Following)> ReadSeriesReviewAsync(Guid bindingId, Guid planId, CancellationToken token = default)
    {
        var state = await RequiredAsync(bindingId, token).ConfigureAwait(false); var plan = state.SeriesPlans.Single(p => p.Id == planId);
        var provider = _providers[state.Binding.Provider]; var masterId = state.Items.Single(e => e.LocalId == plan.EntityId).RemoteId!;
        var master = await provider.GetAsync(state.Binding, masterId, token).ConfigureAwait(false);
        var following = await provider.GetAsync(state.Binding, plan.FollowingId, token).ConfigureAwait(false); return (master, following);
    }

    public async Task RebaseSeriesAsync(Guid bindingId, SyncSeriesChange expected, SyncRemoteEntity? expectedMaster,
        JsonObject following, CancellationToken token = default)
    {
        var binding = (await RequiredAsync(bindingId, token).ConfigureAwait(false)).Binding;
        var gate = _gates.GetOrAdd((binding.Provider, binding.ClientId, binding.AccountId), _ => new(1, 1)); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var observed = await ReadSeriesReviewAsync(bindingId, expected.Id, token).ConfigureAwait(false);
            if (observed.Following is not null || observed.Master?.ETag != expectedMaster?.ETag || !JsonNode.DeepEquals(observed.Master?.Fields, expectedMaster?.Fields))
                throw new InvalidOperationException("The remote split changed during review.");
            await Task.Run(() => GoogleRecurrence.Validate(following), token).ConfigureAwait(false);
            var split = observed.Master?.Fields is { } master ? await Task.Run(() => GoogleRecurrence.Split(master, expected.OriginalStart, token), token).ConfigureAwait(false) : ((JsonObject Trimmed, JsonObject Following)?)null;
            await UpdateAsync(bindingId, s =>
            {
                var current = s.SeriesPlans.Single(p => p.Id == expected.Id);
                if (!GoogleRecurrence.Active(current) || current.State == SyncSeriesState.Sending || SeriesHash(current) != SeriesHash(expected) || current.Stage != expected.Stage || current.State != expected.State)
                    throw new InvalidOperationException("The local split changed during review.");
                if (current.Stage == SyncSeriesStage.TrimOriginal && observed.Master?.Fields is null) throw new InvalidOperationException("The series was deleted.");
                var needsTrim = current.Stage == SyncSeriesStage.TrimOriginal || observed.Master?.Fields is { } value &&
                    !JsonNode.DeepEquals(value["recurrence"], current.TrimmedMaster["recurrence"]);
                var next = current with { FollowingMaster = (JsonObject)following.DeepClone(), State = SyncSeriesState.AwaitingApproval, ApprovalHash = null,
                    ErrorCode = null, RetryAt = null, LastMaster = observed.Master?.Fields, LastETag = observed.Master?.ETag };
                if (needsTrim) next = next with { Stage = SyncSeriesStage.TrimOriginal, OriginalMaster = (JsonObject)observed.Master!.Fields!.DeepClone(), OriginalETag = observed.Master.ETag, TrimmedMaster = split!.Value.Trimmed };
                return s with { SeriesChanges = s.SeriesPlans.Select(p => p.Id == current.Id ? next : p).ToArray() };
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async Task AcceptExistingSeriesAsync(Guid bindingId, SyncSeriesChange expected, SyncRemoteEntity expectedFollowing, CancellationToken token = default)
    {
        var state = await RequiredAsync(bindingId, token).ConfigureAwait(false); var binding = state.Binding;
        var gate = _gates.GetOrAdd((binding.Provider, binding.ClientId, binding.AccountId), _ => new(1, 1)); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var current = await _providers[binding.Provider].GetAsync(binding, expected.FollowingId, token).ConfigureAwait(false);
            if (current?.Fields is null || current.ETag != expectedFollowing.ETag || !JsonNode.DeepEquals(current.Fields, expectedFollowing.Fields)) throw new InvalidOperationException("The following series changed.");
            await UpdateAsync(bindingId, s =>
            {
                var plan = s.SeriesPlans.Single(p => p.Id == expected.Id);
                if (!GoogleRecurrence.Active(plan) || plan.State == SyncSeriesState.Sending || SeriesHash(plan) != SeriesHash(expected)) throw new InvalidOperationException("The split changed.");
                var merged = ApplyRemote(s, new(StringComparer.Ordinal) { [current.Id] = current }, false, s.Cursor, _providers[binding.Provider].AtomicGroups);
                return merged with { SeriesChanges = merged.SeriesPlans.Select(p => p.Id == plan.Id ? p with { Stage = SyncSeriesStage.Done, State = SyncSeriesState.Completed, RetryAt = null, CompletedAt = clock.UtcNow } : p).ToArray() };
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async Task DiscardMissingSeriesAsync(Guid bindingId, SyncSeriesChange expected, CancellationToken token = default)
    {
        var state = await RequiredAsync(bindingId, token).ConfigureAwait(false); var binding = state.Binding;
        var gate = _gates.GetOrAdd((binding.Provider, binding.ClientId, binding.AccountId), _ => new(1, 1)); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var observed = await ReadSeriesReviewAsync(bindingId, expected.Id, token).ConfigureAwait(false);
            if (observed.Master is not null || observed.Following is not null) throw new InvalidOperationException("A split resource still exists.");
            await UpdateAsync(bindingId, s =>
            {
                var plan = s.SeriesPlans.Single(p => p.Id == expected.Id);
                if (!GoogleRecurrence.Active(plan) || SeriesHash(plan) != SeriesHash(expected)) throw new InvalidOperationException("The split changed.");
                return s with { SeriesChanges = s.SeriesPlans.Select(p => p.Id == plan.Id ? p with { Stage = SyncSeriesStage.Done, State = SyncSeriesState.Canceled, RetryAt = null, ErrorCode = "split-resources-deleted", CompletedAt = clock.UtcNow } : p).ToArray() };
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public Task RetrySeriesAsync(Guid bindingId, Guid planId, CancellationToken token = default) => UpdateAsync(bindingId, s => s with
    { SeriesChanges = s.SeriesPlans.Select(p => p.Id == planId && p.State is SyncSeriesState.AuthenticationRequired or SyncSeriesState.Forbidden or SyncSeriesState.Retry
        ? p with { State = SyncSeriesState.Pending, RetryAt = null } : p).ToArray() }, token);

    private static string SeriesHash(SyncSeriesChange plan) => SyncEdits.Hash(new JsonObject
    {
        ["id"] = plan.Id.ToString(), ["instance"] = plan.InstanceId, ["original"] = plan.OriginalMaster.DeepClone(), ["etag"] = plan.OriginalETag,
        ["cut"] = plan.OriginalStart.DeepClone(), ["trimmed"] = plan.TrimmedMaster.DeepClone(), ["followingId"] = plan.FollowingId,
        ["following"] = plan.FollowingMaster.DeepClone(), ["observed"] = plan.LastMaster?.DeepClone(), ["observedEtag"] = plan.LastETag
    });

    private async Task ProcessSeriesAsync(Guid bindingId, ISyncProvider provider, CancellationToken token)
    {
        for (var step = 0; step < 12; step++)
        {
            var state = await RequiredAsync(bindingId, token).ConfigureAwait(false);
            if (!state.Binding.Enabled || state.Binding.NotBefore > clock.UtcNow) return;
            var plan = state.SeriesPlans.FirstOrDefault(p => p.State is SyncSeriesState.Pending or SyncSeriesState.Retry or SyncSeriesState.UnknownOutcome && (p.RetryAt is null || p.RetryAt <= clock.UtcNow) ||
                p.State == SyncSeriesState.Sending && p.RetryAt <= clock.UtcNow);
            if (plan is null || plan.RetryAt > clock.UtcNow && plan.State != SyncSeriesState.Sending) return;
            var master = state.Items.Single(e => e.LocalId == plan.EntityId);
            if (plan.State is SyncSeriesState.UnknownOutcome or SyncSeriesState.Sending)
            {
                var remote = await provider.GetAsync(state.Binding, plan.Stage == SyncSeriesStage.TrimOriginal ? master.RemoteId! : plan.FollowingId, token).ConfigureAwait(false);
                var desired = plan.Stage == SyncSeriesStage.TrimOriginal ? plan.TrimmedMaster : plan.FollowingMaster;
                if (remote?.Fields is { } fields && desired.All(p => (fields.ContainsKey(p.Key) || p.Value is null) && JsonNode.DeepEquals(fields[p.Key], p.Value)))
                    await UpdateAsync(bindingId, s => CompleteSeries(s, plan, new(SyncWriteStatus.Success, remote)), token).ConfigureAwait(false);
                else if (plan.Stage == SyncSeriesStage.CreateFollowing && remote is null || plan.Stage == SyncSeriesStage.TrimOriginal && JsonNode.DeepEquals(remote?.Fields, plan.OriginalMaster))
                    await SaveSeriesAsync(bindingId, plan with { State = SyncSeriesState.Pending, RetryAt = null }, token).ConfigureAwait(false);
                else await SaveSeriesAsync(bindingId, plan with { State = SyncSeriesState.Conflict, ErrorCode = "split-unknown-review-required" }, token).ConfigureAwait(false);
                continue;
            }
            try { await CheckContainerAsync(state, master.LocalId, plan.FollowingMaster, token).ConfigureAwait(false); }
            catch (Exception error) when (error is SyncContainerAccessException or SyncContainerChangesException)
            { await SaveSeriesAsync(bindingId, plan with { State = SyncSeriesState.Forbidden, ErrorCode = "split-container-unavailable" }, token).ConfigureAwait(false); continue; }
            if (plan.ApprovalHash != SeriesHash(plan))
            { await SaveSeriesAsync(bindingId, plan with { State = SyncSeriesState.AwaitingApproval, ApprovalHash = null }, token).ConfigureAwait(false); continue; }
            if (plan.Stage == SyncSeriesStage.CreateFollowing)
            {
                var current = await provider.GetAsync(state.Binding, master.RemoteId!, token).ConfigureAwait(false);
                if (current?.ETag != plan.LastETag || !JsonNode.DeepEquals(current?.Fields, plan.LastMaster))
                { await SaveSeriesAsync(bindingId, plan with { State = SyncSeriesState.Conflict, ErrorCode = "split-trimmed-series-changed" }, token).ConfigureAwait(false); continue; }
            }
            var sent = plan with { State = SyncSeriesState.Sending, RetryAt = clock.UtcNow.AddMinutes(5), Attempts = plan.Attempts + 1 };
            await SaveSeriesAsync(bindingId, sent, token).ConfigureAwait(false);
            var fieldsToSend = plan.Stage == SyncSeriesStage.TrimOriginal ? plan.TrimmedMaster : plan.FollowingMaster;
            var operation = new SyncOperation(plan.Id, master.LocalId, 0, plan.Stage == SyncSeriesStage.TrimOriginal ? SyncOperationKind.Update : SyncOperationKind.Create,
                fieldsToSend, plan.Stage == SyncSeriesStage.TrimOriginal ? master.RemoteId : plan.FollowingId, plan.Stage == SyncSeriesStage.TrimOriginal ? plan.OriginalETag : null,
                ApprovalHash: SyncEdits.Hash(fieldsToSend), Baseline: plan.Stage == SyncSeriesStage.TrimOriginal ? plan.OriginalMaster : null);
            SyncWriteResult result;
            try { result = await provider.WriteAsync(state.Binding, operation, token).ConfigureAwait(false); }
            catch (SyncAuthenticationException) { result = new(SyncWriteStatus.AuthenticationRequired); }
            catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException or System.Text.Json.JsonException)
            { result = new(SyncWriteStatus.UnknownOutcome, ErrorCode: "split-write-interrupted"); }
            await UpdateAsync(bindingId, s => CompleteSeries(s, sent, result), CancellationToken.None).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (result.Status != SyncWriteStatus.Success) return;
        }
    }

    private Task SaveSeriesAsync(Guid bindingId, SyncSeriesChange plan, CancellationToken token) => UpdateAsync(bindingId, s => s with
    { SeriesChanges = s.SeriesPlans.Select(p => p.Id == plan.Id ? plan : p).ToArray() }, token);

    private SyncState CompleteSeries(SyncState state, SyncSeriesChange sent, SyncWriteResult result)
    {
        var plan = state.SeriesPlans.Single(p => p.Id == sent.Id);
        if (plan.State is SyncSeriesState.Canceled or SyncSeriesState.Completed) return state;
        if (result.Status != SyncWriteStatus.Success)
        {
            var status = result.Status switch { SyncWriteStatus.AuthenticationRequired => SyncSeriesState.AuthenticationRequired, SyncWriteStatus.Forbidden => SyncSeriesState.Forbidden,
                SyncWriteStatus.Retry => SyncSeriesState.Retry, SyncWriteStatus.UnknownOutcome => SyncSeriesState.UnknownOutcome, _ => SyncSeriesState.Conflict };
            var delay = result.RetryAfter ?? TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, Math.Min(sent.Attempts, 10))) + Random.Shared.NextDouble() * 3);
            var retry = status == SyncSeriesState.Retry ? clock.UtcNow + delay : (DateTimeOffset?)null;
            return state with { SeriesChanges = state.SeriesPlans.Select(p => p.Id == plan.Id ? p with { State = status, ErrorCode = result.ErrorCode, Attempts = sent.Attempts, RetryAt = retry } : p).ToArray(),
                Binding = retry is { } at ? state.Binding with { NotBefore = at } : state.Binding };
        }
        var remote = result.Entity ?? throw new InvalidDataException("Missing series acknowledgment.");
        var next = plan.Stage == SyncSeriesStage.TrimOriginal ? plan with { Stage = SyncSeriesStage.CreateFollowing, LastMaster = remote.Fields, LastETag = remote.ETag,
            State = result.ErrorCode == "post-write-changed" ? SyncSeriesState.Conflict : SyncSeriesState.Pending, RetryAt = null, ErrorCode = result.ErrorCode }
            : plan with { Stage = SyncSeriesStage.Done, State = SyncSeriesState.Completed, RetryAt = null, ErrorCode = null, CompletedAt = clock.UtcNow };
        next = next with { ApprovalHash = SeriesHash(next) };
        // The acknowledged old/new master and workflow stage commit together; no ordinary outbox replay.
        var merged = ApplyRemote(state, new(StringComparer.Ordinal) { [remote.Id] = remote }, false, state.Cursor, _providers[state.Binding.Provider].AtomicGroups);
        return merged with { SeriesChanges = merged.SeriesPlans.Select(p => p.Id == plan.Id ? next : p).ToArray(),
            Recovery = merged.History.Append(new SyncRecovery(plan.EntityId, clock.UtcNow, plan.OriginalMaster, "series-split-original")).ToArray() };
    }
}
