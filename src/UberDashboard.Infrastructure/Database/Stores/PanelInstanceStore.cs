using Microsoft.Data.Sqlite;
using UberDashboard.Application.Abstractions;
using UberDashboard.Core.Layout;

namespace UberDashboard.Infrastructure.Database.Stores;

/// <summary>
/// Magazyn instancji paneli. Stan właściwy panelu kluczowany jest po
/// <see cref="PanelInstanceId"/>, dzięki czemu operacja na jednej instancji nie
/// dotyka danych innej instancji tego samego typu.
/// </summary>
public sealed class PanelInstanceStore : IPanelInstanceStore
{
    private const string SelectColumns =
        "InstanceId, DashboardId, PluginId, PanelTypeId, DisplayName, IsHidden, " +
        "ConfigJson, " + TodoStorageSchema.ProjectedState + ", RestoreHintJson, DataSchemaVersion, CreatedAtUtc, UpdatedAtUtc";

    private readonly IWorkQueue _queue;
    private readonly SqliteDatabase _database;

    /// <summary>Tworzy magazyn instancji paneli.</summary>
    public PanelInstanceStore(IWorkQueue queue, SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(database);

        _queue = queue;
        _database = database;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PanelInstanceRecord>> ListForDashboardAsync(
        Guid dashboardId,
        CancellationToken cancellationToken = default) =>
        _queue.EnqueueAsync<IReadOnlyList<PanelInstanceRecord>>(
            _ =>
            {
                _database.EnsureInitialized();

                using var command = _database.Connection.CreateCommand();
                command.CommandText =
                    $"SELECT {SelectColumns} FROM PanelInstance WHERE DashboardId = $id ORDER BY CreatedAtUtc, InstanceId;";
                command.Parameters.AddWithValue("$id", dashboardId.ToString("D"));

                using var reader = command.ExecuteReader();
                var records = new List<PanelInstanceRecord>();
                while (reader.Read())
                {
                    records.Add(Map(reader));
                }

                return records;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<PanelInstanceRecord?> GetAsync(
        PanelInstanceId instanceId,
        CancellationToken cancellationToken = default) =>
        _queue.EnqueueAsync(
            _ =>
            {
                _database.EnsureInitialized();

                using var command = _database.Connection.CreateCommand();
                command.CommandText = $"SELECT {SelectColumns} FROM PanelInstance WHERE InstanceId = $id;";
                command.Parameters.AddWithValue("$id", instanceId.ToString());

                using var reader = command.ExecuteReader();
                return reader.Read() ? Map(reader) : null;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task UpsertAsync(PanelInstanceRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        return _queue.EnqueueAsync(
            _ =>
            {
                _database.EnsureInitialized();

                using var command = _database.Connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO PanelInstance (
                        InstanceId, DashboardId, PluginId, PanelTypeId, DisplayName, IsHidden,
                        ConfigJson, StateJson, RestoreHintJson, DataSchemaVersion, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (
                        $instanceId, $dashboardId, $pluginId, $panelTypeId, $displayName, $isHidden,
                        $configJson, $stateJson, $restoreHintJson, $dataSchemaVersion, $createdAtUtc, $updatedAtUtc)
                    ON CONFLICT(InstanceId) DO UPDATE SET
                        DashboardId = excluded.DashboardId,
                        PluginId = excluded.PluginId,
                        PanelTypeId = excluded.PanelTypeId,
                        DisplayName = excluded.DisplayName,
                        IsHidden = excluded.IsHidden,
                        ConfigJson = excluded.ConfigJson,
                        StateJson = excluded.StateJson,
                        RestoreHintJson = excluded.RestoreHintJson,
                        DataSchemaVersion = excluded.DataSchemaVersion,
                        UpdatedAtUtc = excluded.UpdatedAtUtc;
                    """;
                command.Parameters.AddWithValue("$instanceId", record.InstanceId.ToString());
                command.Parameters.AddWithValue("$dashboardId", record.DashboardId.ToString("D"));
                command.Parameters.AddWithValue("$pluginId", record.PluginId);
                command.Parameters.AddWithValue("$panelTypeId", record.PanelTypeId);
                command.Parameters.AddWithValue("$displayName", record.DisplayName);
                command.Parameters.AddWithValue("$isHidden", record.IsHidden);
                command.Parameters.AddWithValue("$configJson", (object?)record.ConfigJson ?? DBNull.Value);
                command.Parameters.AddWithValue("$stateJson", (object?)record.StateJson ?? DBNull.Value);
                command.Parameters.AddWithValue("$restoreHintJson", (object?)record.RestoreHintJson ?? DBNull.Value);
                command.Parameters.AddWithValue("$dataSchemaVersion", record.DataSchemaVersion);
                command.Parameters.AddWithValue("$createdAtUtc", SqliteText.ToIso(record.CreatedAtUtc));
                command.Parameters.AddWithValue("$updatedAtUtc", SqliteText.ToIso(record.UpdatedAtUtc));
                command.ExecuteNonQuery();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SetHiddenAsync(
        PanelInstanceId instanceId,
        bool isHidden,
        string? restoreHintJson,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) =>
        _queue.EnqueueAsync(
            _ =>
            {
                _database.EnsureInitialized();

                using var command = _database.Connection.CreateCommand();
                command.CommandText = """
                    UPDATE PanelInstance
                    SET IsHidden = $isHidden, RestoreHintJson = $restoreHintJson, UpdatedAtUtc = $updatedAtUtc
                    WHERE InstanceId = $id;
                    """;
                command.Parameters.AddWithValue("$id", instanceId.ToString());
                command.Parameters.AddWithValue("$isHidden", isHidden);
                command.Parameters.AddWithValue("$restoreHintJson", (object?)restoreHintJson ?? DBNull.Value);
                command.Parameters.AddWithValue("$updatedAtUtc", SqliteText.ToIso(nowUtc));
                command.ExecuteNonQuery();
            },
            cancellationToken);

    /// <inheritdoc />
    public Task UpdateStateAsync(PanelInstanceId instanceId, string stateJson, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        _queue.EnqueueAsync(_ =>
        {
            _database.EnsureInitialized();
            using var command = _database.Connection.CreateCommand();
            command.CommandText = "UPDATE PanelInstance SET StateJson = $state, UpdatedAtUtc = $updated WHERE InstanceId = $id;";
            command.Parameters.AddWithValue("$state", stateJson);
            command.Parameters.AddWithValue("$updated", SqliteText.ToIso(nowUtc));
            command.Parameters.AddWithValue("$id", instanceId.ToString());
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Instancja nie istnieje; nie zapisano treści panelu.");
        }, cancellationToken);

    public Task DeleteAsync(PanelInstanceId instanceId, CancellationToken cancellationToken = default) =>
        _queue.EnqueueAsync(
            _ =>
            {
                _database.EnsureInitialized();

                using var command = _database.Connection.CreateCommand();
                command.CommandText = "DELETE FROM PanelInstance WHERE InstanceId = $id;";
                command.Parameters.AddWithValue("$id", instanceId.ToString());
                command.ExecuteNonQuery();
            },
            cancellationToken);

    private static PanelInstanceRecord Map(SqliteDataReader reader) => new(
        new PanelInstanceId(Guid.Parse(reader.GetString(0))),
        Guid.Parse(reader.GetString(1)),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetInt64(5) != 0,
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.GetInt32(9),
        SqliteText.ParseIso(reader.GetString(10)),
        SqliteText.ParseIso(reader.GetString(11)));
}
