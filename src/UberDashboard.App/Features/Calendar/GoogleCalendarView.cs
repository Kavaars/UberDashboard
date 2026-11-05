using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Google.Apis.Auth.OAuth2.Responses;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.App.Features.Calendar;

internal enum CalendarViewFailure { None, Offline, NotConnected, AuthenticationRequired, Forbidden, NotFound, ServiceError, CacheUnavailable }
internal sealed record CalendarViewReadResult(IReadOnlyList<CalendarEntry>? Entries, DateTimeOffset? FetchedAt,
    bool IsCached, CalendarViewFailure Failure);

internal sealed partial class GoogleCalendarClient
{
    internal async Task<CalendarViewReadResult> ReadViewAsync(string calendarId, CalendarPeriod period, CancellationToken token,
        IProgress<CalendarViewReadResult>? progress = null)
    {
        var session = await LoadSessionAsync(token).ConfigureAwait(false);
        if (session is null) return new(null, null, false, CalendarViewFailure.NotConnected);
        if (session.Token is null) return new(null, null, false, CalendarViewFailure.AuthenticationRequired);
        var identity = string.IsNullOrWhiteSpace(session.AccountId) ? null :
            new CalendarViewIdentity(session.ClientId, session.AccountId, calendarId, TimeZoneInfo.Local.Id);
        CalendarViewSnapshot? cached = null;
        var cacheFailed = false;
        if (identity is not null && viewCache is not null)
        {
            try { cached = await viewCache.ReadAsync(identity, period.Start, period.End, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { cacheFailed = true; }
            if (cached is not null && await SessionMatchesAsync(session, token).ConfigureAwait(false))
                progress?.Report(FromCache(cached, CalendarViewFailure.None));
        }

        var requested = DateTimeOffset.UtcNow;
        IReadOnlyList<CalendarEntry> entries;
        try { entries = await ReadEventsAsync(session, calendarId, period, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or
            JsonException or TokenResponseException or SyncAuthenticationException)
        {
            if (!await SessionMatchesAsync(session, token).ConfigureAwait(false))
                return new(null, null, false, CalendarViewFailure.AuthenticationRequired);
            var failure = error switch
            {
                TokenResponseException or SyncAuthenticationException => CalendarViewFailure.AuthenticationRequired,
                HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => CalendarViewFailure.AuthenticationRequired,
                HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => CalendarViewFailure.Forbidden,
                HttpRequestException { StatusCode: HttpStatusCode.NotFound } => CalendarViewFailure.NotFound,
                HttpRequestException { StatusCode: null } or OperationCanceledException => CalendarViewFailure.Offline,
                _ => CalendarViewFailure.ServiceError,
            };
            return cached is null ? new(null, null, false, failure) : FromCache(cached, failure);
        }

        token.ThrowIfCancellationRequested();
        if (!await SessionMatchesAsync(session, token).ConfigureAwait(false)) return new(null, null, false, CalendarViewFailure.AuthenticationRequired);
        var fetched = DateTimeOffset.UtcNow;
        CalendarViewSnapshot? newerSnapshot = null;
        if (identity is not null && viewCache is not null)
        {
            var snapshot = new CalendarViewSnapshot(identity, period.Start, period.End, requested, fetched,
                entries.Select(e => new CalendarViewEntry(e.Title, e.Start, e.End, e.Time, e.RemoteId, e.RecurringEventId, e.OriginalStart)).ToArray());
            try
            {
                if (!await viewCache.SaveAsync(snapshot, token).ConfigureAwait(false) &&
                    await viewCache.ReadAsync(identity, period.Start, period.End, token).ConfigureAwait(false) is { } newer)
                    newerSnapshot = newer;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { cacheFailed = true; }
        }
        if (!await SessionMatchesAsync(session, token).ConfigureAwait(false)) return new(null, null, false, CalendarViewFailure.AuthenticationRequired);
        if (newerSnapshot is not null) return FromCache(newerSnapshot, CalendarViewFailure.None);
        return new(entries, fetched, false, cacheFailed ? CalendarViewFailure.CacheUnavailable : CalendarViewFailure.None);
    }

    private async Task<bool> SessionMatchesAsync(Session expected, CancellationToken token)
    {
        var current = await LoadSessionAsync(token).ConfigureAwait(false);
        return current?.ClientId == expected.ClientId && current.AccountId == expected.AccountId;
    }

    private static CalendarViewReadResult FromCache(CalendarViewSnapshot snapshot, CalendarViewFailure failure) => new(
        snapshot.Entries.Select(e => new CalendarEntry(e.Title, e.Start, e.End, e.Time, e.RemoteId, e.RecurringEventId, e.OriginalStart)).ToArray(),
        snapshot.FetchedAt, true, failure);
}
