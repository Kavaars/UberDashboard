using System.Text.Json;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Weather;
using UberDashboard.Infrastructure.Database;

namespace UberDashboard.Infrastructure.Weather;

/// <summary>
/// Magazyn cache pogodowego oparty o tabelę WeatherCache.
/// </summary>
public sealed class WeatherCacheStore : IWeatherCacheStore
{
    private readonly IWorkQueue _queue;
    private readonly SqliteDatabase _database;

    /// <summary>Tworzy magazyn cache pogodowego.</summary>
    public WeatherCacheStore(IWorkQueue queue, SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(database);
        _queue = queue;
        _database = database;
    }

    /// <inheritdoc />
    public Task<WeatherCacheEntry?> GetAsync(string locationKey, CancellationToken cancellationToken = default)
    {
        var safeKey = RequireLocationKey(locationKey);

        return _queue.EnqueueAsync(
            _ =>
            {
                _database.EnsureInitialized();

                using var command = _database.Connection.CreateCommand();
                command.CommandText = """
                    SELECT CacheKey, LocationKey, PayloadJson, FetchedAtUtc, ValidUntilUtc
                    FROM WeatherCache
                    WHERE LocationKey = $locationKey
                    ORDER BY FetchedAtUtc DESC
                    LIMIT 1;
                    """;
                command.Parameters.AddWithValue("$locationKey", safeKey);

                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    return null;
                }

                var snapshot = JsonSerializer.Deserialize<WeatherSnapshot>(reader.GetString(2));
                if (snapshot is null)
                {
                    return null;
                }

                return new WeatherCacheEntry(
                    reader.GetString(0),
                    reader.GetString(1),
                    snapshot with { IsFromCache = true },
                    SqliteText.ParseIso(reader.GetString(3)),
                    reader.IsDBNull(4) ? null : SqliteText.ParseIso(reader.GetString(4)));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task UpsertAsync(
        string locationKey,
        WeatherSnapshot snapshot,
        DateTimeOffset? validUntilUtc,
        CancellationToken cancellationToken = default)
    {
        var safeKey = RequireLocationKey(locationKey);
        ArgumentNullException.ThrowIfNull(snapshot);

        return _queue.EnqueueAsync(
            _ =>
            {
                _database.EnsureInitialized();

                using var command = _database.Connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO WeatherCache (CacheKey, LocationKey, PayloadJson, FetchedAtUtc, ValidUntilUtc, Source, Model)
                    VALUES ($cacheKey, $locationKey, $payloadJson, $fetchedAtUtc, $validUntilUtc, $source, $model)
                    ON CONFLICT(CacheKey) DO UPDATE SET
                        PayloadJson = excluded.PayloadJson,
                        FetchedAtUtc = excluded.FetchedAtUtc,
                        ValidUntilUtc = excluded.ValidUntilUtc,
                        Source = excluded.Source,
                        Model = excluded.Model;
                    """;
                command.Parameters.AddWithValue("$cacheKey", safeKey);
                command.Parameters.AddWithValue("$locationKey", safeKey);
                command.Parameters.AddWithValue("$payloadJson", JsonSerializer.Serialize(snapshot with { IsFromCache = false }));
                command.Parameters.AddWithValue("$fetchedAtUtc", SqliteText.ToIso(snapshot.FetchedAtUtc));
                command.Parameters.AddWithValue(
                    "$validUntilUtc",
                    validUntilUtc is null ? DBNull.Value : SqliteText.ToIso(validUntilUtc.Value));
                command.Parameters.AddWithValue("$source", "open-meteo");
                command.Parameters.AddWithValue("$model", DBNull.Value);
                command.ExecuteNonQuery();
            },
            cancellationToken);
    }

    private static string RequireLocationKey(string locationKey)
    {
        if (string.IsNullOrWhiteSpace(locationKey))
        {
            throw new ArgumentException("Weather cache location key cannot be blank.", nameof(locationKey));
        }

        return locationKey.Trim();
    }
}
