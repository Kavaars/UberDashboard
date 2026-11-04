namespace UberDashboard.Infrastructure.Database;

/// <summary>Stores task rows separately while preserving the panel snapshot contract.</summary>
internal static class TodoStorageSchema
{
    public const string ProjectedState = """
        CASE WHEN json_valid(StateJson) AND PanelTypeId = 'todo' AND PluginId = 'pl.uberdashboard.builtin'
          AND CASE WHEN json_valid(StateJson) THEN json_extract(StateJson, '$.TaskRows') END = 1
        THEN json_set(json_remove(StateJson, '$.TaskRows'), '$.Items', json((
            SELECT json_group_array(json_object('Id', TodoId, 'Text', Title, 'IsDone',
                json(CASE IsCompleted WHEN 1 THEN 'true' ELSE 'false' END)))
            FROM (SELECT * FROM TodoItem WHERE InstanceId = PanelInstance.InstanceId ORDER BY SortOrder)
        ))) ELSE StateJson END
        """;

    private const string NewId = """
        lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) ||
        '-' || hex(randomblob(2)) || '-' || hex(randomblob(6)))
        """;

    public static SqliteMigration Create() => new(3, "todo-task-rows", [
        """
        CREATE TABLE TodoLegacySnapshot (
            InstanceId TEXT NOT NULL PRIMARY KEY,
            StateJson TEXT NOT NULL
        );
        """,
        """
        CREATE TRIGGER TodoMigrationCollision BEFORE INSERT ON TodoLegacySnapshot
        WHEN EXISTS (SELECT 1 FROM TodoItem WHERE InstanceId = NEW.InstanceId)
          OR NOT json_valid(NEW.StateJson)
          OR json_type(NEW.StateJson, '$.Items') IS NOT 'array'
        BEGIN
            SELECT RAISE(ABORT, 'Nieprawidłowy stan źródłowy ToDo lub istniejące rekordy zadań.');
        END;
        """,
        """
        INSERT INTO TodoLegacySnapshot SELECT InstanceId, StateJson FROM PanelInstance
        WHERE PluginId = 'pl.uberdashboard.builtin' AND PanelTypeId = 'todo' AND StateJson IS NOT NULL;
        """,
        "DROP TRIGGER TodoMigrationCollision;",
        WriteTrigger("INSERT", "TodoRowsInsert"),
        WriteTrigger("UPDATE OF StateJson", "TodoRowsUpdate"),
        """
        CREATE TRIGGER TodoRowsClear AFTER UPDATE OF StateJson ON PanelInstance
        WHEN NEW.PluginId = 'pl.uberdashboard.builtin' AND NEW.PanelTypeId = 'todo' AND NEW.StateJson IS NULL
        BEGIN
            DELETE FROM TodoItem WHERE InstanceId = NEW.InstanceId;
        END;
        """,
        """
        UPDATE PanelInstance SET StateJson = StateJson
        WHERE InstanceId IN (SELECT InstanceId FROM TodoLegacySnapshot);
        """
    ]);

    private static string WriteTrigger(string operation, string name) => $"""
        CREATE TRIGGER {name} AFTER {operation} ON PanelInstance
        WHEN NEW.PluginId = 'pl.uberdashboard.builtin' AND NEW.PanelTypeId = 'todo' AND
             CASE WHEN json_valid(NEW.StateJson) THEN json_type(NEW.StateJson, '$.Items') = 'array' END
        BEGIN
            SELECT RAISE(ABORT, 'Nieprawidłowe dane zadania.')
            WHERE EXISTS (
                SELECT 1 FROM json_each(NEW.StateJson, '$.Items') AS item
                WHERE item.type <> 'object' OR json_type(item.value, '$.Text') IS NOT 'text'
                   OR COALESCE(json_type(item.value, '$.IsDone'), '') NOT IN ('true', 'false')
            );
            SELECT RAISE(ABORT, 'Powtórzony identyfikator zadania.')
            WHERE EXISTS (
                SELECT 1 FROM json_each(NEW.StateJson, '$.Items')
                WHERE json_extract(value, '$.Id') IS NOT NULL
                GROUP BY json_extract(value, '$.Id') HAVING count(*) > 1
            );
            SELECT RAISE(ABORT, 'Identyfikator zadania należy do innej listy.')
            WHERE EXISTS (
                SELECT 1 FROM json_each(NEW.StateJson, '$.Items') AS item
                JOIN TodoItem ON TodoId = json_extract(item.value, '$.Id')
                WHERE InstanceId <> NEW.InstanceId
            );
            DELETE FROM TodoItem WHERE InstanceId = NEW.InstanceId AND TodoId NOT IN (
                SELECT json_extract(value, '$.Id') FROM json_each(NEW.StateJson, '$.Items')
                WHERE json_extract(value, '$.Id') IS NOT NULL
            );
            INSERT INTO TodoItem (TodoId, InstanceId, Title, IsCompleted, SortOrder, CreatedAtUtc, UpdatedAtUtc)
            SELECT COALESCE(json_extract(value, '$.Id'), {NewId}), NEW.InstanceId,
                   json_extract(value, '$.Text'), json_extract(value, '$.IsDone'), CAST(key AS INTEGER),
                   NEW.UpdatedAtUtc, NEW.UpdatedAtUtc
            FROM json_each(NEW.StateJson, '$.Items') WHERE 1
            ON CONFLICT(TodoId) DO UPDATE SET Title = excluded.Title, IsCompleted = excluded.IsCompleted,
                SortOrder = excluded.SortOrder, UpdatedAtUtc = excluded.UpdatedAtUtc
            WHERE Title <> excluded.Title OR IsCompleted <> excluded.IsCompleted OR SortOrder <> excluded.SortOrder;
            UPDATE PanelInstance SET StateJson = json_set(json_remove(NEW.StateJson, '$.Items'), '$.TaskRows', 1),
                DataSchemaVersion = 3 WHERE InstanceId = NEW.InstanceId;
        END;
        """;
}
