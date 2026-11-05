using UberDashboard.Core.Localization;
using UberDashboard.Application.Abstractions;

namespace UberDashboard.Infrastructure.Database.Stores;

/// <summary>
/// Magazyn ustawień aplikacji. Wartość jest dokumentem JSON, więc model można
/// rozszerzać bez zmiany schematu tabeli.
/// </summary>
public sealed class AppSettingsStore : IAppSettingsStore
{
    private readonly IWorkQueue _queue;
    private readonly SqliteDatabase _database;

    /// <summary>Tworzy magazyn ustawień.</summary>
    public AppSettingsStore(IWorkQueue queue, SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(database);

        _queue = queue;
        _database = database;
    }

    /// <inheritdoc />
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var safeKey = RequireKey(key);

        return _queue.EnqueueAsync(
            _ =>
            {
                _database.EnsureInitialized();

                using var command = _database.Connection.CreateCommand();
                command.CommandText = "SELECT ValueJson FROM AppSetting WHERE SettingKey = $key;";
                command.Parameters.AddWithValue("$key", safeKey);
                return command.ExecuteScalar() as string;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SetAsync(
        string key,
        string valueJson,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var safeKey = RequireKey(key);
        ArgumentNullException.ThrowIfNull(valueJson);

        return _queue.EnqueueAsync(
            _ =>
            {
                _database.EnsureInitialized();

                using var command = _database.Connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO AppSetting (SettingKey, ValueJson, UpdatedAtUtc)
                    VALUES ($key, $valueJson, $updatedAtUtc)
                    ON CONFLICT(SettingKey) DO UPDATE SET
                        ValueJson = excluded.ValueJson,
                        UpdatedAtUtc = excluded.UpdatedAtUtc;
                    """;
                command.Parameters.AddWithValue("$key", safeKey);
                command.Parameters.AddWithValue("$valueJson", valueJson);
                command.Parameters.AddWithValue("$updatedAtUtc", SqliteText.ToIso(nowUtc));
                command.ExecuteNonQuery();
            },
            cancellationToken);
    }

    private static string RequireKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException(UiText.Get("TheSettingKeyCannotBeEmpty"), nameof(key));
        }

        return key;
    }
}
