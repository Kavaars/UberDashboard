namespace UberDashboard.Infrastructure.Database;

/// <summary>Pojedyncza, numerowana migracja schematu bazy.</summary>
internal sealed record SqliteMigration(int Version, string Name, IReadOnlyList<string> Statements);

/// <summary>
/// Definicje migracji schematu. Każda migracja ma numer i wykonuje się w jednej
/// transakcji; numer wersji zapisywany jest dopiero po udanym wykonaniu wszystkich
/// poleceń, więc przerwana migracja zostawia poprzednią bazę.
/// </summary>
internal static class SqliteSchema
{
    /// <summary>Wersja schematu obsługiwana przez tę wersję aplikacji.</summary>
    public const int CurrentVersion = 2;

    /// <summary>Migracje w kolejności wykonania.</summary>
    public static IReadOnlyList<SqliteMigration> Migrations { get; } = [CreateInitialSchema(), CreatePanelTabsSchema()];

    private static SqliteMigration CreatePanelTabsSchema() => new(2, "panel-tab-groups", [
        "CREATE TABLE PanelTabGroup (GroupId TEXT NOT NULL PRIMARY KEY REFERENCES PanelInstance(InstanceId) ON DELETE CASCADE, ActiveInstanceId TEXT NOT NULL, OriginalStateJson TEXT NULL);",
        "CREATE TABLE PanelTabMember (GroupId TEXT NOT NULL REFERENCES PanelTabGroup(GroupId) ON DELETE CASCADE, InstanceId TEXT NOT NULL UNIQUE REFERENCES PanelInstance(InstanceId) ON DELETE CASCADE, Name TEXT NOT NULL, SortOrder INTEGER NOT NULL, PRIMARY KEY (GroupId, InstanceId), UNIQUE (GroupId, SortOrder));"
    ]);

    private static SqliteMigration CreateInitialSchema() => new(
        1,
        "initial-schema",
        [
            """
            CREATE TABLE IF NOT EXISTS Dashboard (
                DashboardId TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 0
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS DashboardLayout (
                DashboardId TEXT NOT NULL PRIMARY KEY
                    REFERENCES Dashboard(DashboardId) ON DELETE CASCADE,
                SchemaVersion INTEGER NOT NULL,
                TreeJson TEXT NOT NULL,
                DisplayMode TEXT NOT NULL DEFAULT 'Window',
                MonitorId TEXT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS DesktopPlacement (
                DashboardId TEXT NOT NULL,
                InstanceId TEXT NOT NULL,
                MonitorId TEXT NULL,
                X REAL NOT NULL,
                Y REAL NOT NULL,
                Width REAL NOT NULL,
                Height REAL NOT NULL,
                ZOrder INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (DashboardId, InstanceId),
                FOREIGN KEY (DashboardId) REFERENCES Dashboard(DashboardId) ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS PanelInstance (
                InstanceId TEXT NOT NULL PRIMARY KEY,
                DashboardId TEXT NOT NULL REFERENCES Dashboard(DashboardId) ON DELETE CASCADE,
                PluginId TEXT NOT NULL,
                PanelTypeId TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                IsHidden INTEGER NOT NULL DEFAULT 0,
                ConfigJson TEXT NULL,
                StateJson TEXT NULL,
                RestoreHintJson TEXT NULL,
                DataSchemaVersion INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """,
            "CREATE INDEX IF NOT EXISTS IX_PanelInstance_DashboardId ON PanelInstance(DashboardId);",
            """
            CREATE TABLE IF NOT EXISTS TodoItem (
                TodoId TEXT NOT NULL PRIMARY KEY,
                InstanceId TEXT NOT NULL REFERENCES PanelInstance(InstanceId) ON DELETE CASCADE,
                Title TEXT NOT NULL,
                Details TEXT NULL,
                IsCompleted INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                DueAtUtc TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                CompletedAtUtc TEXT NULL
            );
            """,
            "CREATE INDEX IF NOT EXISTS IX_TodoItem_InstanceId ON TodoItem(InstanceId);",
            """
            CREATE TABLE IF NOT EXISTS NoteDocument (
                InstanceId TEXT NOT NULL PRIMARY KEY
                    REFERENCES PanelInstance(InstanceId) ON DELETE CASCADE,
                Title TEXT NOT NULL,
                Text TEXT NOT NULL,
                Revision INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS WeatherConfig (
                InstanceId TEXT NOT NULL PRIMARY KEY
                    REFERENCES PanelInstance(InstanceId) ON DELETE CASCADE,
                LocationName TEXT NOT NULL,
                Latitude REAL NOT NULL,
                Longitude REAL NOT NULL,
                TimeZone TEXT NULL,
                Units TEXT NOT NULL DEFAULT 'metric',
                ConsentAtUtc TEXT NULL,
                RefreshIntervalMinutes INTEGER NOT NULL DEFAULT 15
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS WeatherCache (
                CacheKey TEXT NOT NULL PRIMARY KEY,
                LocationKey TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                FetchedAtUtc TEXT NOT NULL,
                ValidUntilUtc TEXT NULL,
                Source TEXT NULL,
                Model TEXT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS AppSetting (
                SettingKey TEXT NOT NULL PRIMARY KEY,
                ValueJson TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS PendingCleanup (
                CleanupId TEXT NOT NULL PRIMARY KEY,
                PluginId TEXT NULL,
                InstanceId TEXT NULL,
                RelativePath TEXT NOT NULL,
                State TEXT NOT NULL,
                LastError TEXT NULL,
                Attempts INTEGER NOT NULL DEFAULT 0,
                UpdatedAtUtc TEXT NOT NULL
            );
            """,
        ]);
}
