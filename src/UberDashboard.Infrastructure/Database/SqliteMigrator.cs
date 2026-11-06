using Microsoft.Data.Sqlite;
using UberDashboard.Application.Abstractions;
using UberDashboard.Core.Localization;

namespace UberDashboard.Infrastructure.Database;

/// <summary>
/// Runner migracji. Tworzy tabelę <c>SchemaMigration</c>, odczytuje zastosowane
/// wersje i wykonuje brakujące migracje w jednej transakcji każda, zapisując numer
/// wersji dopiero po udanym wykonaniu.
/// </summary>
internal static class SqliteMigrator
{
    private const string CreateMigrationTable = """
        CREATE TABLE IF NOT EXISTS SchemaMigration (
            Version INTEGER NOT NULL PRIMARY KEY,
            Name TEXT NOT NULL,
            AppliedAtUtc TEXT NOT NULL
        );
        """;

    /// <summary>Doprowadza bazę do <see cref="SqliteSchema.CurrentVersion"/>.</summary>
    public static void Apply(SqliteConnection connection, IClock clock, Action? backupBeforeTodoMigration = null, Action? backupBeforeSyncMigration = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(clock);

        SqliteDatabase.Execute(connection, CreateMigrationTable);

        var applied = ReadAppliedVersions(connection);
        if (applied.Any(version => version > SqliteSchema.CurrentVersion))
            throw new InvalidDataException(UiText.Get("NewerDatabaseVersion"));
        foreach (var migration in SqliteSchema.Migrations)
        {
            if (applied.Contains(migration.Version))
            {
                continue;
            }

            if (migration.Version == 3 && applied.Count > 0)
                backupBeforeTodoMigration?.Invoke();
            if (migration.Version is 4 or 5 or 6 or 7 or 8 or 9 && applied.Count > 0)
                backupBeforeSyncMigration?.Invoke();
            ApplyMigration(connection, migration, clock.UtcNow);
        }
    }

    private static void ApplyMigration(SqliteConnection connection, SqliteMigration migration, DateTimeOffset now)
    {
        using var transaction = connection.BeginTransaction();

        foreach (var statement in migration.Statements)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }

        using var record = connection.CreateCommand();
        record.Transaction = transaction;
        record.CommandText =
            "INSERT INTO SchemaMigration (Version, Name, AppliedAtUtc) VALUES ($version, $name, $appliedAtUtc);";
        record.Parameters.AddWithValue("$version", migration.Version);
        record.Parameters.AddWithValue("$name", migration.Name);
        record.Parameters.AddWithValue("$appliedAtUtc", SqliteText.ToIso(now));
        record.ExecuteNonQuery();

        transaction.Commit();
    }

    private static HashSet<int> ReadAppliedVersions(SqliteConnection connection)
    {
        var versions = new HashSet<int>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Version FROM SchemaMigration;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }
}
