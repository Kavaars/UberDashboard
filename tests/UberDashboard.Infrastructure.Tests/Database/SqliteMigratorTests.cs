using UberDashboard.Infrastructure.Database;
using UberDashboard.Infrastructure.Tests.TestSupport;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class SqliteMigratorTests
{
    [Fact]
    public async Task Initialize_records_supported_schema_version()
    {
        await using var fixture = new DatabaseFixture();

        fixture.Database.EnsureInitialized();

        Assert.True(fixture.Database.IsInitialized);
        Assert.Equal(SqliteSchema.CurrentVersion, ReadMaxSchemaVersion(fixture.Database));
        Assert.Equal(1L, Count(fixture.Database, "SELECT COUNT(*) FROM SchemaMigration;"));
    }

    [Fact]
    public async Task Initialize_creates_expected_tables()
    {
        await using var fixture = new DatabaseFixture();

        fixture.Database.EnsureInitialized();

        foreach (var table in new[]
                 {
                     "Dashboard", "DashboardLayout", "DesktopPlacement", "PanelInstance",
                     "TodoItem", "NoteDocument", "WeatherConfig", "WeatherCache",
                     "AppSetting", "PendingCleanup", "SchemaMigration",
                 })
        {
            Assert.Equal(
                1L,
                Count(fixture.Database, $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';"));
        }
    }

    [Fact]
    public async Task Initialize_is_idempotent_for_existing_database()
    {
        await using var fixture = new DatabaseFixture();
        fixture.Database.EnsureInitialized();

        var reopened = new SqliteDatabase(fixture.Database.DatabaseFile, fixture.Clock);
        try
        {
            reopened.EnsureInitialized();

            Assert.Equal(SqliteSchema.CurrentVersion, ReadMaxSchemaVersion(reopened));
            Assert.Equal(1L, Count(reopened, "SELECT COUNT(*) FROM SchemaMigration;"));
        }
        finally
        {
            reopened.Dispose();
        }
    }

    [Fact]
    public void Schema_definition_is_consistent()
    {
        Assert.True(SqliteSchema.CurrentVersion >= 1);
        Assert.NotEmpty(SqliteSchema.Migrations);
        Assert.Equal(Enumerable.Range(1, SqliteSchema.CurrentVersion), SqliteSchema.Migrations.Select(m => m.Version));
    }

    private static int ReadMaxSchemaVersion(SqliteDatabase database) =>
        (int)Count(database, "SELECT COALESCE(MAX(Version), 0) FROM SchemaMigration;");

    private static long Count(SqliteDatabase database, string sql)
    {
        using var command = database.Connection.CreateCommand();
        command.CommandText = sql;
        return (long)(command.ExecuteScalar() ?? 0L);
    }
}
