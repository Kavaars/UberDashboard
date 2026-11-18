using UberDashboard.Core.Localization;
using System.Globalization;
using Microsoft.Data.Sqlite;
using UberDashboard.Application.Abstractions;
using UberDashboard.Infrastructure.Database;

namespace UberDashboard.Infrastructure.Backup;

/// <summary>Wynik operacji kopii bezpieczeństwa.</summary>
public sealed record BackupResult(bool IsSuccess, string? BackupFile, string? Message)
{
    /// <summary>Kopia utworzona poprawnie.</summary>
    public static BackupResult Ok(string file) => new(true, file, null);

    /// <summary>Kopia nie powstała; bieżąca baza pozostaje nienaruszona.</summary>
    public static BackupResult Fail(string message) => new(false, null, message);
}

/// <summary>
/// Kopie bezpieczeństwa przez SQLite online backup API. Operacja jest zadaniem
/// ekskluzywnym kolejki: nowe zapisy czekają, a kopia nie jest tworzona przez
/// kopiowanie aktywnego pliku bazy ani plików <c>-wal</c>/<c>-shm</c>.
/// </summary>
public sealed class SqliteBackupService
{
    private const string BackupFilePattern = "dashboard-*.db";
    private const string BackupFilePrefix = "dashboard-";

    private readonly IWorkQueue _queue;
    private readonly SqliteDatabase _database;
    private readonly IAppPaths _paths;
    private readonly IClock _clock;

    /// <summary>Tworzy usługę kopii.</summary>
    public SqliteBackupService(IWorkQueue queue, SqliteDatabase database, IAppPaths paths, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(clock);

        _queue = queue;
        _database = database;
        _paths = paths;
        _clock = clock;
    }

    /// <summary>
    /// Tworzy kopię do pliku tymczasowego, sprawdza integralność i dopiero wtedy
    /// promuje plik do nazwy z datą.
    /// </summary>
    public Task<BackupResult> CreateBackupAsync(
        int retentionCount = 10,
        CancellationToken cancellationToken = default) =>
        _queue.EnqueueExclusiveAsync(
            token =>
            {
                _database.EnsureInitialized();
                Directory.CreateDirectory(_paths.BackupsDirectory);

                var stamp = _clock.UtcNow.ToUniversalTime()
                    .ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture);
                var finalPath = Path.Combine(_paths.BackupsDirectory, $"{BackupFilePrefix}{stamp}-{Guid.NewGuid():N}.db");
                var temporaryPath = finalPath + ".tmp";

                try
                {
                    using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
                    {
                        DataSource = temporaryPath,
                        Mode = SqliteOpenMode.ReadWriteCreate,
                        Pooling = false,
                    }.ToString()))
                    {
                        destination.Open();
                        _database.Connection.BackupDatabase(destination);

                        if (!CheckIntegrity(destination))
                        {
                            return BackupResult.Fail(UiText.Get("TheBackupFailedItsIntegrityCheck"));
                        }
                    }

                    token.ThrowIfCancellationRequested();
                    File.Move(temporaryPath, finalPath);
                    // The backup already exists; a failed cleanup of old files must not report it as failed.
                    try { ApplyRetention(retentionCount); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    return BackupResult.Ok(finalPath);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
            },
            cancellationToken);

    private static bool CheckIntegrity(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = command.ExecuteScalar() as string;
        return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyRetention(int keep)
    {
        if (keep <= 0)
        {
            return;
        }

        var stale = Directory.GetFiles(_paths.BackupsDirectory, BackupFilePattern)
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .Skip(keep);

        foreach (var file in stale)
        {
            File.Delete(file);
        }
    }
}
