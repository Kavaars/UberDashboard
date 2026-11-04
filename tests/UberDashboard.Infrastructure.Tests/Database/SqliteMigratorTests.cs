using UberDashboard.Infrastructure.Database;
using UberDashboard.Infrastructure.Tests.TestSupport;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class SqliteMigratorTests
{
    [Fact]
    public async Task Todo_upgrade_preserves_duplicate_tasks_and_ids_across_updates()
    {
        await using var fixture = new DatabaseFixture();
        var panel = UberDashboard.Core.Layout.PanelInstanceId.New();
        const string original = """{"Items":[{"Text":"Próba","IsDone":true},{"Text":"Próba","IsDone":false}],"ShowSummary":false}""";
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={fixture.Database.DatabaseFile};Pooling=False"))
        {
            connection.Open();
            SqliteDatabase.Execute(connection, "CREATE TABLE SchemaMigration (Version INTEGER PRIMARY KEY, Name TEXT NOT NULL, AppliedAtUtc TEXT NOT NULL);");
            foreach (var migration in SqliteSchema.Migrations.Take(2))
                foreach (var sql in migration.Statements)
                    SqliteDatabase.Execute(connection, sql);
            SqliteDatabase.Execute(connection, """
                INSERT INTO SchemaMigration VALUES (1, 'initial', '2026-11-01'), (2, 'tabs', '2026-11-01');
                INSERT INTO Dashboard VALUES ('a9d19477-cd72-4cf3-b47d-8f5aedec06d7', 'Pulpit', '2026-11-01', '2026-11-01', 1);
                """);
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO PanelInstance (InstanceId, DashboardId, PluginId, PanelTypeId, DisplayName, StateJson, IsHidden, CreatedAtUtc, UpdatedAtUtc)
                VALUES ($id, 'a9d19477-cd72-4cf3-b47d-8f5aedec06d7', 'pl.uberdashboard.builtin', 'todo', 'ToDo', $state, 1, '2026-11-01', '2026-11-01');
                """;
            command.Parameters.AddWithValue("$id", panel.ToString());
            command.Parameters.AddWithValue("$state", original);
            command.ExecuteNonQuery();
        }

        var loaded = await fixture.Panels.GetAsync(panel);
        var state = System.Text.Json.Nodes.JsonNode.Parse(loaded!.StateJson!)!;
        var items = state["Items"]!.AsArray();
        Assert.Equal(2, items.Count);
        Assert.True(items[0]!["IsDone"]!.GetValue<bool>());
        Assert.False(items[1]!["IsDone"]!.GetValue<bool>());
        Assert.False(state["ShowSummary"]!.GetValue<bool>());
        var id = items[0]!["Id"]!.GetValue<Guid>();
        Assert.NotEqual(id, items[1]!["Id"]!.GetValue<Guid>());
        Assert.Equal(0L, Count(fixture.Database, "SELECT COUNT(*) FROM PanelInstance WHERE json_type(StateJson,'$.Items') IS NOT NULL;"));
        Assert.Equal(1L, Count(fixture.Database, "SELECT COUNT(*) FROM TodoLegacySnapshot;"));
        Assert.Single(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(fixture.Database.DatabaseFile)!, "Backups"), "pre-todo-v3-*.db"));

        items[0]!["Text"] = "Zmieniono";
        await fixture.Panels.UpdateStateAsync(panel, state.ToJsonString(), fixture.Clock.UtcNow);
        var after = System.Text.Json.Nodes.JsonNode.Parse((await fixture.Panels.GetAsync(panel))!.StateJson!)!;
        Assert.Equal(id, after["Items"]![0]!["Id"]!.GetValue<Guid>());
        Assert.Equal("Zmieniono", after["Items"]![0]!["Text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Upgrading_version_one_preserves_existing_panel_state()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = Guid.NewGuid().ToString("D");
        var panel = Guid.NewGuid().ToString("D");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={fixture.Database.DatabaseFile};Pooling=False"))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "CREATE TABLE SchemaMigration (Version INTEGER NOT NULL PRIMARY KEY, Name TEXT NOT NULL, AppliedAtUtc TEXT NOT NULL);";
            command.ExecuteNonQuery();
            foreach (var sql in SqliteSchema.Migrations[0].Statements) { command.CommandText = sql; command.ExecuteNonQuery(); }
            command.CommandText = "INSERT INTO SchemaMigration VALUES (1, 'initial-schema', '2026-11-01T00:00:00Z');"; command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO Dashboard VALUES ($dashboard, 'Pulpit', '2026-11-01T00:00:00Z', '2026-11-01T00:00:00Z', 1);";
            command.Parameters.AddWithValue("$dashboard", dashboard); command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO PanelInstance (InstanceId, DashboardId, PluginId, PanelTypeId, DisplayName, StateJson, CreatedAtUtc, UpdatedAtUtc) VALUES ($id, $dashboard, 'builtin', 'notes', 'Notatnik', $state, '2026-11-01T00:00:00Z', '2026-11-01T00:00:00Z');";
            command.Parameters.AddWithValue("$id", panel); command.Parameters.AddWithValue("$state", "{\"Text\":\"oryginał\"}"); command.ExecuteNonQuery();
            transaction.Commit();
        }
        fixture.Database.EnsureInitialized();
        var record = await fixture.Panels.GetAsync(new UberDashboard.Core.Layout.PanelInstanceId(Guid.Parse(panel)));
        Assert.Equal("{\"Text\":\"oryginał\"}", record!.StateJson);
        Assert.Equal(SqliteSchema.CurrentVersion, ReadMaxSchemaVersion(fixture.Database));
        Assert.Equal(0L, Count(fixture.Database, "SELECT COUNT(*) FROM PanelTabGroup;"));
    }

    [Fact]
    public async Task Initialize_records_supported_schema_version()
    {
        await using var fixture = new DatabaseFixture();

        fixture.Database.EnsureInitialized();

        Assert.True(fixture.Database.IsInitialized);
        Assert.Equal(SqliteSchema.CurrentVersion, ReadMaxSchemaVersion(fixture.Database));
        Assert.Equal((long)SqliteSchema.CurrentVersion, Count(fixture.Database, "SELECT COUNT(*) FROM SchemaMigration;"));
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
            Assert.Equal((long)SqliteSchema.CurrentVersion, Count(reopened, "SELECT COUNT(*) FROM SchemaMigration;"));
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
