using UberDashboard.Core.Localization;
using Microsoft.Data.Sqlite;
using UberDashboard.Application.Abstractions;

namespace UberDashboard.Infrastructure.Database;

/// <summary>
/// Kontrolowane połączenie do bazy aplikacji. Połączenie jest otwierane raz,
/// konfigurowane jawnie (WAL, klucze obce, busy timeout) i używane wyłącznie z
/// wątku roboczego kolejki, co eliminuje współbieżny dostęp do <see cref="SqliteConnection"/>.
/// </summary>
public sealed class SqliteDatabase : IDisposable
{
    private readonly string _databaseFile;
    private readonly IClock _clock;
    private readonly string _backupsDirectory;
    private SqliteConnection? _connection;

    /// <summary>Tworzy obiekt bazy dla wskazanego pliku.</summary>
    public SqliteDatabase(string databaseFile, IClock clock, string? backupsDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFile);
        ArgumentNullException.ThrowIfNull(clock);

        _databaseFile = Path.GetFullPath(databaseFile);
        _clock = clock;
        _backupsDirectory = backupsDirectory ?? Path.Combine(Path.GetDirectoryName(_databaseFile)!, "Backups");
    }

    /// <summary>Pełna ścieżka pliku bazy.</summary>
    public string DatabaseFile => _databaseFile;

    /// <summary>Połączenie; dostępne dopiero po <see cref="EnsureInitialized"/>.</summary>
    public SqliteConnection Connection =>
        _connection ?? throw new InvalidOperationException(UiText.Get("TheDatabaseHasNotBeenInitialized"));

    /// <summary><see langword="true"/>, gdy połączenie zostało otwarte.</summary>
    public bool IsInitialized => _connection is not null;

    /// <summary>Otwiera połączenie, ustawia pragmy i wykonuje migracje; operacja idempotentna.</summary>
    public void EnsureInitialized()
    {
        if (_connection is not null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_databaseFile);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();

        try
        {
            ApplyPragmas(connection);
            SqliteMigrator.Apply(connection, _clock, () => BackupBeforeTodoMigration(connection));
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        _connection = connection;
    }

    private void BackupBeforeTodoMigration(SqliteConnection connection)
    {
        Directory.CreateDirectory(_backupsDirectory);
        var file = Path.Combine(_backupsDirectory, $"pre-todo-v3-{Guid.NewGuid():N}.db");
        using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = file, Pooling = false,
        }.ToString());
        backup.Open();
        connection.BackupDatabase(backup);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
        GC.SuppressFinalize(this);
    }

    internal static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void ApplyPragmas(SqliteConnection connection)
    {
        Execute(connection, "PRAGMA journal_mode = WAL;");
        Execute(connection, "PRAGMA foreign_keys = ON;");
        Execute(connection, "PRAGMA busy_timeout = 5000;");
        Execute(connection, "PRAGMA synchronous = NORMAL;");
    }
}
