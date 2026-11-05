using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Infrastructure.Database.Stores;

/// <summary>Shared, derived projections. All I/O and serialization runs on the existing database queue.</summary>
public sealed class CalendarViewCache(IWorkQueue queue, SqliteDatabase database) : ICalendarViewCache
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public Task<CalendarViewSnapshot?> ReadAsync(CalendarViewIdentity identity, DateOnly start, DateOnly end, CancellationToken token = default) =>
        queue.EnqueueAsync(_ =>
        {
            ValidateIdentity(identity); if (end <= start) throw new ArgumentException("Invalid calendar view range.");
            database.EnsureInitialized();
            using var command = database.Connection.CreateCommand();
            command.CommandText = """
                SELECT PayloadJson FROM CalendarViewCache
                WHERE ClientId = $client AND AccountId = $account AND CalendarId = $calendar AND DisplayTimeZone = $zone
                  AND StartDay <= $start AND EndDay >= $end
                ORDER BY RequestedAtUtc DESC, (EndDay - StartDay) ASC LIMIT 1;
                """;
            IdentityParameters(command, identity);
            command.Parameters.AddWithValue("$start", start.DayNumber); command.Parameters.AddWithValue("$end", end.DayNumber);
            if (command.ExecuteScalar() is not string json) return null;
            var snapshot = JsonSerializer.Deserialize<CalendarViewSnapshot>(json, Options) ?? throw new InvalidDataException("Invalid calendar view cache.");
            Validate(snapshot);
            if (snapshot.Identity != identity || snapshot.Start > start || snapshot.End < end)
                throw new InvalidDataException("Calendar cache identity or coverage does not match.");
            return snapshot;
        }, token);

    public Task<bool> SaveAsync(CalendarViewSnapshot snapshot, CancellationToken token = default) => queue.EnqueueAsync(_ =>
    {
        Validate(snapshot); database.EnsureInitialized();
        using var command = database.Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CalendarViewCache (ClientId, AccountId, CalendarId, DisplayTimeZone, StartDay, EndDay, RequestedAtUtc, PayloadJson)
            VALUES ($client, $account, $calendar, $zone, $start, $end, $requested, $payload)
            ON CONFLICT (ClientId, AccountId, CalendarId, DisplayTimeZone, StartDay, EndDay)
            DO UPDATE SET RequestedAtUtc = excluded.RequestedAtUtc, PayloadJson = excluded.PayloadJson
            WHERE excluded.RequestedAtUtc >= CalendarViewCache.RequestedAtUtc;
            """;
        IdentityParameters(command, snapshot.Identity);
        command.Parameters.AddWithValue("$start", snapshot.Start.DayNumber); command.Parameters.AddWithValue("$end", snapshot.End.DayNumber);
        command.Parameters.AddWithValue("$requested", snapshot.RequestedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(snapshot, Options));
        return command.ExecuteNonQuery() == 1;
    }, token);

    private static void Validate(CalendarViewSnapshot snapshot)
    {
        ValidateIdentity(snapshot.Identity);
        if (snapshot.FormatVersion != 1 || snapshot.End <= snapshot.Start || snapshot.FetchedAt < snapshot.RequestedAt ||
            snapshot.Entries is null || snapshot.Entries.Any(e => e.End <= e.Start))
            throw new InvalidDataException("Unsupported or incomplete calendar view snapshot.");
    }

    private static void ValidateIdentity(CalendarViewIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.ClientId) || string.IsNullOrWhiteSpace(identity.AccountId) ||
            string.IsNullOrWhiteSpace(identity.CalendarId) || string.IsNullOrWhiteSpace(identity.DisplayTimeZone))
            throw new InvalidDataException("Calendar cache requires an explicit account, calendar and zone.");
    }

    private static void IdentityParameters(SqliteCommand command, CalendarViewIdentity identity)
    {
        command.Parameters.AddWithValue("$client", identity.ClientId); command.Parameters.AddWithValue("$account", identity.AccountId);
        command.Parameters.AddWithValue("$calendar", identity.CalendarId); command.Parameters.AddWithValue("$zone", identity.DisplayTimeZone);
    }
}
