using UberDashboard.Application.Abstractions;
using UberDashboard.Core.Layout;
using UberDashboard.Core.Layout.Serialization;

namespace UberDashboard.Infrastructure.Database.Stores;

/// <summary>
/// Magazyn wersjonowanych dokumentów układu. Odczyt i zapis jadą przez kolejkę
/// pracy; uszkodzony lub nowszy dokument nie jest nadpisywany, a jego treść
/// pozostaje w bazie do ręcznego odzyskania.
/// </summary>
public sealed class LayoutStore : ILayoutStore
{
    private const string DisplayModeWindow = "Window";

    private readonly IWorkQueue _queue;
    private readonly SqliteDatabase _database;
    private readonly IClock _clock;

    /// <summary>Tworzy magazyn układu.</summary>
    public LayoutStore(IWorkQueue queue, SqliteDatabase database, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(clock);

        _queue = queue;
        _database = database;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task<LayoutLoadResult> LoadAsync(Guid dashboardId, CancellationToken cancellationToken = default) =>
        _queue.EnqueueAsync(
            _ =>
            {
                _database.EnsureInitialized();

                using var command = _database.Connection.CreateCommand();
                command.CommandText =
                    "SELECT SchemaVersion, TreeJson FROM DashboardLayout WHERE DashboardId = $id;";
                command.Parameters.AddWithValue("$id", dashboardId.ToString("D"));

                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    return LayoutLoadResult.Empty(LayoutSerializer.CurrentSchemaVersion);
                }

                var storedVersion = reader.GetInt32(0);
                var json = reader.GetString(1);
                var result = LayoutSerializer.Deserialize(json, LayoutLimits.Default);

                if (result.IsSuccess)
                {
                    return new LayoutLoadResult(result.Tree!, result.SchemaVersion, LayoutErrorCode.None, null, false);
                }

                return LayoutLoadResult.Fail(
                    result.Error,
                    result.SchemaVersion > 0 ? result.SchemaVersion : storedVersion,
                    result.Message ?? "Nie udało się odczytać dokumentu układu.",
                    result.MustPreserveOriginalDocument);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task SaveAsync(Guid dashboardId, LayoutTree tree, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);

        return _queue.EnqueueAsync(
            _ =>
            {
                _database.EnsureInitialized();

                var json = LayoutSerializer.Serialize(tree, LayoutLimits.Default);

                using var transaction = _database.Connection.BeginTransaction();
                using var command = _database.Connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO DashboardLayout (DashboardId, SchemaVersion, TreeJson, DisplayMode, MonitorId, UpdatedAtUtc)
                    VALUES ($id, $schemaVersion, $treeJson, $displayMode, $monitorId, $updatedAtUtc)
                    ON CONFLICT(DashboardId) DO UPDATE SET
                        SchemaVersion = excluded.SchemaVersion,
                        TreeJson = excluded.TreeJson,
                        UpdatedAtUtc = excluded.UpdatedAtUtc;
                    """;
                command.Parameters.AddWithValue("$id", dashboardId.ToString("D"));
                command.Parameters.AddWithValue("$schemaVersion", LayoutSerializer.CurrentSchemaVersion);
                command.Parameters.AddWithValue("$treeJson", json);
                command.Parameters.AddWithValue("$displayMode", DisplayModeWindow);
                command.Parameters.AddWithValue("$monitorId", DBNull.Value);
                command.Parameters.AddWithValue("$updatedAtUtc", SqliteText.ToIso(_clock.UtcNow));
                command.ExecuteNonQuery();

                transaction.Commit();
            },
            cancellationToken);
    }
}
