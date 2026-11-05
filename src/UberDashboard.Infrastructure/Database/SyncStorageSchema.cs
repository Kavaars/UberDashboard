namespace UberDashboard.Infrastructure.Database;

internal static class SyncStorageSchema
{
    public static SqliteMigration CreateRelatedItems() => new(7, "typed-task-relations", [
        // Bump CAS together with the format so an already-open older writer cannot replace the new shape.
        "UPDATE SyncBinding SET StateJson = json_set(StateJson, '$.formatVersion', 2, '$.version', Version + 1), Version = Version + 1 WHERE COALESCE(json_extract(StateJson, '$.formatVersion'), 1) = 1;",
    ]);
    public static SqliteMigration Create() => new(4, "durable-integration-sync", [
        """
        CREATE TABLE SyncBinding (
            BindingId TEXT NOT NULL PRIMARY KEY,
            Provider INTEGER NOT NULL,
            ClientId TEXT NOT NULL,
            AccountId TEXT NOT NULL,
            ResourcePath TEXT NOT NULL,
            Version INTEGER NOT NULL,
            StateJson TEXT NOT NULL CHECK(json_valid(StateJson)),
            UNIQUE(Provider, ClientId, AccountId, ResourcePath)
        );
        """,
        """
        CREATE TABLE SyncLegacySnapshot (
            SnapshotId TEXT NOT NULL PRIMARY KEY,
            ValueJson TEXT NOT NULL
        );
        """,
        "INSERT INTO SyncLegacySnapshot SELECT SettingKey, ValueJson FROM AppSetting WHERE SettingKey = 'integration.microsoft-todo';",
    ]);

    public static SqliteMigration CreateBindingMetadata() => new(5, "sync-binding-metadata", [
        "ALTER TABLE SyncBinding RENAME TO SyncBindingV4;",
        """
        CREATE TABLE SyncBinding (
            BindingId TEXT NOT NULL PRIMARY KEY,
            Provider INTEGER NOT NULL,
            ClientId TEXT NOT NULL,
            AccountId TEXT NOT NULL,
            ResourcePath TEXT NOT NULL,
            BindingJson TEXT NOT NULL CHECK(json_valid(BindingJson)),
            Version INTEGER NOT NULL,
            StateJson TEXT NOT NULL CHECK(json_valid(StateJson)),
            UNIQUE(Provider, ClientId, AccountId, ResourcePath)
        );
        """,
        """
        INSERT INTO SyncBinding (BindingId, Provider, ClientId, AccountId, ResourcePath, BindingJson, Version, StateJson)
        SELECT BindingId, Provider, ClientId, AccountId, ResourcePath,
            COALESCE(json_extract(StateJson, '$.binding'), '{}'), Version, StateJson FROM SyncBindingV4;
        """,
        "DROP TABLE SyncBindingV4;",
    ]);
}
