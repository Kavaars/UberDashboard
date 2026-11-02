using Microsoft.Data.Sqlite;
using UberDashboard.Application.Abstractions;

namespace UberDashboard.Infrastructure.Database.Stores;

/// <summary>
/// Magazyn pulpitów. Każda operacja trafia na kolejkę pracy i wykonuje się na
/// wątku roboczym; kontrakt pozostaje asynchroniczny, mimo że SQLite działa synchronicznie.
/// </summary>
public sealed class DashboardStore : IDashboardStore
{
    private const string SelectColumns = "DashboardId, Name, CreatedAtUtc, UpdatedAtUtc, IsActive";

    private readonly IWorkQueue _queue;
    private readonly SqliteDatabase _database;
    private readonly IClock _clock;

    /// <summary>Tworzy magazyn oparty o kolejkę pracy i kontrolowane połączenie.</summary>
    public DashboardStore(IWorkQueue queue, SqliteDatabase database, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(clock);

        _queue = queue;
        _database = database;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DashboardSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        _queue.EnqueueAsync<IReadOnlyList<DashboardSummary>>(
            _ =>
            {
                _database.EnsureInitialized();

                using var command = _database.Connection.CreateCommand();
                command.CommandText = $"SELECT {SelectColumns} FROM Dashboard ORDER BY CreatedAtUtc, DashboardId;";

                using var reader = command.ExecuteReader();
                var dashboards = new List<DashboardSummary>();
                while (reader.Read())
                {
                    dashboards.Add(Map(reader));
                }

                return dashboards;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<DashboardSummary?> GetAsync(Guid dashboardId, CancellationToken cancellationToken = default) =>
        _queue.EnqueueAsync(
            _ =>
            {
                _database.EnsureInitialized();
                return FindById(dashboardId);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<DashboardSummary> CreateAsync(
        string name,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var safeName = RequireName(name);

        return _queue.EnqueueAsync(
            _ =>
            {
                _database.EnsureInitialized();

                using var transaction = _database.Connection.BeginTransaction();
                var dashboardId = Guid.NewGuid();
                var isFirst = CountAll(transaction) == 0;

                using (var insert = _database.Connection.CreateCommand())
                {
                    insert.Transaction = transaction;
                    insert.CommandText = """
                        INSERT INTO Dashboard (DashboardId, Name, CreatedAtUtc, UpdatedAtUtc, IsActive)
                        VALUES ($id, $name, $createdAtUtc, $updatedAtUtc, $isActive);
                        """;
                    insert.Parameters.AddWithValue("$id", dashboardId.ToString("D"));
                    insert.Parameters.AddWithValue("$name", safeName);
                    insert.Parameters.AddWithValue("$createdAtUtc", SqliteText.ToIso(nowUtc));
                    insert.Parameters.AddWithValue("$updatedAtUtc", SqliteText.ToIso(nowUtc));
                    insert.Parameters.AddWithValue("$isActive", isFirst);
                    insert.ExecuteNonQuery();
                }

                transaction.Commit();
                return new DashboardSummary(dashboardId, safeName, nowUtc, nowUtc, isFirst);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<DashboardSummary> RenameAsync(
        Guid dashboardId,
        string name,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var safeName = RequireName(name);

        return _queue.EnqueueAsync(
            _ =>
            {
                _database.EnsureInitialized();

                using (var update = _database.Connection.CreateCommand())
                {
                    update.CommandText = """
                        UPDATE Dashboard
                        SET Name = $name, UpdatedAtUtc = $updatedAtUtc
                        WHERE DashboardId = $id;
                        """;
                    update.Parameters.AddWithValue("$id", dashboardId.ToString("D"));
                    update.Parameters.AddWithValue("$name", safeName);
                    update.Parameters.AddWithValue("$updatedAtUtc", SqliteText.ToIso(nowUtc));

                    if (update.ExecuteNonQuery() == 0)
                    {
                        throw new KeyNotFoundException($"Pulpit {dashboardId:D} nie istnieje.");
                    }
                }

                return FindById(dashboardId)
                    ?? throw new KeyNotFoundException($"Pulpit {dashboardId:D} nie istnieje.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid dashboardId, CancellationToken cancellationToken = default) =>
        _queue.EnqueueAsync(
            _ =>
            {
                _database.EnsureInitialized();

                using var command = _database.Connection.CreateCommand();
                command.CommandText = "DELETE FROM Dashboard WHERE DashboardId = $id;";
                command.Parameters.AddWithValue("$id", dashboardId.ToString("D"));
                command.ExecuteNonQuery();
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<Guid?> GetActiveDashboardIdAsync(CancellationToken cancellationToken = default) =>
        _queue.EnqueueAsync(
            _ =>
            {
                _database.EnsureInitialized();

                using var command = _database.Connection.CreateCommand();
                command.CommandText = "SELECT DashboardId FROM Dashboard WHERE IsActive = 1 LIMIT 1;";
                var value = command.ExecuteScalar() as string;
                return value is null ? (Guid?)null : Guid.Parse(value);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task SetActiveDashboardAsync(Guid dashboardId, CancellationToken cancellationToken = default) =>
        _queue.EnqueueAsync(
            _ =>
            {
                _database.EnsureInitialized();

                using var transaction = _database.Connection.BeginTransaction();

                using (var clear = _database.Connection.CreateCommand())
                {
                    clear.Transaction = transaction;
                    clear.CommandText = "UPDATE Dashboard SET IsActive = 0 WHERE IsActive <> 0;";
                    clear.ExecuteNonQuery();
                }

                using (var activate = _database.Connection.CreateCommand())
                {
                    activate.Transaction = transaction;
                    activate.CommandText = """
                        UPDATE Dashboard
                        SET IsActive = 1, UpdatedAtUtc = $updatedAtUtc
                        WHERE DashboardId = $id;
                        """;
                    activate.Parameters.AddWithValue("$id", dashboardId.ToString("D"));
                    activate.Parameters.AddWithValue("$updatedAtUtc", SqliteText.ToIso(_clock.UtcNow));

                    if (activate.ExecuteNonQuery() == 0)
                    {
                        throw new KeyNotFoundException($"Pulpit {dashboardId:D} nie istnieje.");
                    }
                }

                transaction.Commit();
            },
            cancellationToken);

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Nazwa pulpitu nie może być pusta.", nameof(name));
        }

        return name.Trim();
    }

    private static DashboardSummary Map(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        SqliteText.ParseIso(reader.GetString(2)),
        SqliteText.ParseIso(reader.GetString(3)),
        reader.GetInt64(4) != 0);

    private DashboardSummary? FindById(Guid dashboardId)
    {
        using var command = _database.Connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM Dashboard WHERE DashboardId = $id;";
        command.Parameters.AddWithValue("$id", dashboardId.ToString("D"));

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    private long CountAll(SqliteTransaction transaction)
    {
        using var command = _database.Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM Dashboard;";
        return (long)(command.ExecuteScalar() ?? 0L);
    }
}
