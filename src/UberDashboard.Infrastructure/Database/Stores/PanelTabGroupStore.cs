using UberDashboard.Core.Localization;
using Microsoft.Data.Sqlite;
using UberDashboard.Application.Abstractions;
using UberDashboard.Core.Layout;
using UberDashboard.Core.Panels;

namespace UberDashboard.Infrastructure.Database.Stores;

/// <summary>Transakcyjny magazyn składu grupy i niezależnych stanów jej zakładek.</summary>
public sealed class PanelTabGroupStore(IWorkQueue queue, SqliteDatabase database) : IPanelTabGroupStore
{
    public Task CreateAsync(PanelTabGroup group, IReadOnlyList<PanelInstanceRecord> records, string? originalStateJson, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count != group.Tabs.Length || records.Select(r => r.InstanceId).Distinct().Count() != records.Count || group.Tabs.Any(t => records.All(r => r.InstanceId != t.InstanceId)))
            throw new ArgumentException(UiText.Get("TheSavedStatesDoNotMatchThe"), nameof(records));
        return queue.EnqueueAsync(_ =>
        {
            database.EnsureInitialized();
            using var tx = database.Connection.BeginTransaction();
            using (var existing = database.Connection.CreateCommand())
            {
                existing.Transaction = tx;
                existing.CommandText = "SELECT DashboardId, PluginId, PanelTypeId, " + TodoStorageSchema.ProjectedState +
                    " FROM PanelInstance WHERE InstanceId = $id;";
                existing.Parameters.AddWithValue("$id", group.GroupId.ToString());
                using var reader = existing.ExecuteReader();
                if (!reader.Read()) throw new InvalidOperationException(UiText.Get("TheSourcePanelWasNotFound"));
                var dashboard = reader.GetString(0);
                var plugin = reader.GetString(1);
                var type = reader.GetString(2);
                var oldState = reader.IsDBNull(3) ? null : reader.GetString(3);
                if (!string.Equals(oldState, originalStateJson, StringComparison.Ordinal)) throw new InvalidOperationException(UiText.Get("TheSourceStateChangedRetryTheMigration"));
                if (records.Any(r => r.DashboardId.ToString("D") != dashboard || r.PluginId != plugin || r.PanelTypeId != type))
                    throw new InvalidOperationException(UiText.Get("AllTabsMustHaveTheSameType"));
            }
            foreach (var record in records)
            {
                if (record.InstanceId != group.GroupId)
                {
                    using var collision = database.Connection.CreateCommand();
                    collision.Transaction = tx;
                    collision.CommandText = "SELECT COUNT(*) FROM PanelInstance WHERE InstanceId = $id;";
                    collision.Parameters.AddWithValue("$id", record.InstanceId.ToString());
                    if ((long)collision.ExecuteScalar()! != 0) throw new InvalidOperationException(UiText.Get("TheTabIdAlreadyExists"));
                }
                using var command = database.Connection.CreateCommand();
                command.Transaction = tx;
                command.CommandText = """
                    INSERT INTO PanelInstance (InstanceId, DashboardId, PluginId, PanelTypeId, DisplayName, IsHidden, ConfigJson, StateJson, RestoreHintJson, DataSchemaVersion, CreatedAtUtc, UpdatedAtUtc)
                    VALUES ($id, $dashboard, $plugin, $type, $name, $hidden, $config, $state, $hint, $version, $created, $updated)
                    ON CONFLICT(InstanceId) DO UPDATE SET StateJson = excluded.StateJson, UpdatedAtUtc = excluded.UpdatedAtUtc;
                    """;
                command.Parameters.AddWithValue("$id", record.InstanceId.ToString());
                command.Parameters.AddWithValue("$dashboard", record.DashboardId.ToString("D"));
                command.Parameters.AddWithValue("$plugin", record.PluginId);
                command.Parameters.AddWithValue("$type", record.PanelTypeId);
                command.Parameters.AddWithValue("$name", record.DisplayName);
                command.Parameters.AddWithValue("$hidden", record.IsHidden);
                command.Parameters.AddWithValue("$config", (object?)record.ConfigJson ?? DBNull.Value);
                command.Parameters.AddWithValue("$state", (object?)record.StateJson ?? DBNull.Value);
                command.Parameters.AddWithValue("$hint", (object?)record.RestoreHintJson ?? DBNull.Value);
                command.Parameters.AddWithValue("$version", record.DataSchemaVersion);
                command.Parameters.AddWithValue("$created", SqliteText.ToIso(record.CreatedAtUtc));
                command.Parameters.AddWithValue("$updated", SqliteText.ToIso(record.UpdatedAtUtc));
                command.ExecuteNonQuery();
            }
            using (var command = database.Connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText = "INSERT INTO PanelTabGroup (GroupId, ActiveInstanceId, OriginalStateJson) VALUES ($id, $active, $original);";
                command.Parameters.AddWithValue("$id", group.GroupId.ToString());
                command.Parameters.AddWithValue("$active", group.ActiveId.ToString());
                command.Parameters.AddWithValue("$original", (object?)originalStateJson ?? DBNull.Value);
                command.ExecuteNonQuery();
            }
            for (var i = 0; i < group.Tabs.Length; i++)
            {
                using var command = database.Connection.CreateCommand();
                command.Transaction = tx;
                command.CommandText = "INSERT INTO PanelTabMember (GroupId, InstanceId, Name, SortOrder) VALUES ($group, $id, $name, $order);";
                command.Parameters.AddWithValue("$group", group.GroupId.ToString());
                command.Parameters.AddWithValue("$id", group.Tabs[i].InstanceId.ToString());
                command.Parameters.AddWithValue("$name", group.Tabs[i].Name);
                command.Parameters.AddWithValue("$order", i);
                command.ExecuteNonQuery();
            }
            tx.Commit();
        }, cancellationToken);
    }

    public Task DeleteAsync(PanelTabGroup expected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return queue.EnqueueAsync(_ =>
        {
            database.EnsureInitialized();
            using var tx = database.Connection.BeginTransaction();
            CheckExpected(expected, tx);
            // Usuwamy najpierw relację, następnie instancje; transakcja przywraca wszystko przy błędzie.
            using (var group = database.Connection.CreateCommand())
            {
                group.Transaction = tx;
                group.CommandText = "DELETE FROM PanelTabGroup WHERE GroupId = $id;";
                group.Parameters.AddWithValue("$id", expected.GroupId.ToString());
                if (group.ExecuteNonQuery() != 1) throw new InvalidOperationException("Nie znaleziono grupy.");
            }
            foreach (var id in expected.Tabs.Select(x => x.InstanceId).Append(expected.GroupId).Distinct())
            {
                using var delete = database.Connection.CreateCommand();
                delete.Transaction = tx;
                delete.CommandText = "DELETE FROM PanelInstance WHERE InstanceId = $id;";
                delete.Parameters.AddWithValue("$id", id.ToString());
                if (delete.ExecuteNonQuery() != 1) throw new InvalidOperationException("Nie znaleziono instancji panelu.");
            }
            tx.Commit();
        }, cancellationToken);
    }

    public Task AddAsync(PanelTabGroup expected, PanelTabGroup updated, PanelInstanceRecord added, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(added);
        ValidateMembershipChange(expected, updated, added.InstanceId, adding: true);
        return InsertTabsAsync(expected, updated, [added], cancellationToken);
    }

    public Task AddBatchAsync(PanelTabGroup expected, PanelTabGroup updated, IReadOnlyList<PanelInstanceRecord> added, CancellationToken cancellationToken = default)
    {
        var records = added.ToArray();
        if (records.Length == 0 || expected.GroupId != updated.GroupId || expected.ActiveId != updated.ActiveId ||
            !expected.Tabs.SequenceEqual(updated.Tabs.Take(expected.Tabs.Length)) ||
            !records.Select(record => record.InstanceId)
                .SequenceEqual(updated.Tabs.Skip(expected.Tabs.Length).Select(tab => tab.InstanceId)))
            throw new InvalidOperationException(UiText.Get("InvalidTabBatch"));
        return InsertTabsAsync(expected, updated, records, cancellationToken);
    }

    private Task InsertTabsAsync(PanelTabGroup expected, PanelTabGroup updated, PanelInstanceRecord[] records,
        CancellationToken cancellationToken)
    {
        return queue.EnqueueAsync(_ =>
        {
            database.EnsureInitialized();
            using var tx = database.Connection.BeginTransaction();
            CheckExpected(expected, tx);
            foreach (var record in records)
            {
                using var command = database.Connection.CreateCommand();
                command.Transaction = tx;
                command.CommandText = """
                    INSERT INTO PanelInstance (InstanceId, DashboardId, PluginId, PanelTypeId, DisplayName, IsHidden, ConfigJson, StateJson, RestoreHintJson, DataSchemaVersion, CreatedAtUtc, UpdatedAtUtc)
                    SELECT $id, DashboardId, PluginId, PanelTypeId, $name, 1, $config, $state, NULL, $version, $created, $updated
                    FROM PanelInstance WHERE InstanceId = $group AND DashboardId = $dashboard AND PluginId = $plugin AND PanelTypeId = $type;
                    """;
                command.Parameters.AddWithValue("$id", record.InstanceId.ToString());
                command.Parameters.AddWithValue("$name", record.DisplayName);
                command.Parameters.AddWithValue("$config", (object?)record.ConfigJson ?? DBNull.Value);
                command.Parameters.AddWithValue("$state", (object?)record.StateJson ?? DBNull.Value);
                command.Parameters.AddWithValue("$version", record.DataSchemaVersion);
                command.Parameters.AddWithValue("$created", SqliteText.ToIso(record.CreatedAtUtc));
                command.Parameters.AddWithValue("$updated", SqliteText.ToIso(record.UpdatedAtUtc));
                command.Parameters.AddWithValue("$group", expected.GroupId.ToString());
                command.Parameters.AddWithValue("$dashboard", record.DashboardId.ToString("D"));
                command.Parameters.AddWithValue("$plugin", record.PluginId);
                command.Parameters.AddWithValue("$type", record.PanelTypeId);
                if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException(UiText.Get("InvalidTabTypeOrDashboard"));
            }
            WriteGroup(updated, tx);
            tx.Commit();
        }, cancellationToken);
    }

    public Task RemoveAsync(PanelTabGroup expected, PanelTabGroup updated, PanelInstanceId removed, CancellationToken cancellationToken = default)
    {
        ValidateMembershipChange(expected, updated, removed, adding: false);
        return queue.EnqueueAsync(_ =>
        {
            database.EnsureInitialized();
            using var tx = database.Connection.BeginTransaction();
            CheckExpected(expected, tx);
            WriteGroup(updated, tx);
            // Rekord kotwicy pozostaje właścicielem układu i wspólnych ustawień panelu.
            if (removed != expected.GroupId)
            {
                using var delete = database.Connection.CreateCommand();
                delete.Transaction = tx;
                delete.CommandText = "DELETE FROM PanelInstance WHERE InstanceId = $id;";
                delete.Parameters.AddWithValue("$id", removed.ToString());
                if (delete.ExecuteNonQuery() != 1) throw new InvalidOperationException("Nie znaleziono usuwanej instancji.");
            }
            else
            {
                using var clear = database.Connection.CreateCommand();
                clear.Transaction = tx;
                clear.CommandText = "UPDATE PanelInstance SET StateJson = NULL, ConfigJson = NULL WHERE InstanceId = $id;";
                clear.Parameters.AddWithValue("$id", removed.ToString());
                if (clear.ExecuteNonQuery() != 1) throw new InvalidOperationException("Nie znaleziono kotwicy panelu.");
            }
            tx.Commit();
        }, cancellationToken);
    }

    private static void ValidateMembershipChange(PanelTabGroup expected, PanelTabGroup updated, PanelInstanceId id, bool adding)
    {
        ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(updated);
        if (expected.GroupId != updated.GroupId) throw new InvalidOperationException("Zmieniono identyfikator grupy.");
        var before = expected.Tabs.Select(x => x.InstanceId).ToHashSet();
        var after = updated.Tabs.Select(x => x.InstanceId).ToHashSet();
        var valid = adding ? !before.Contains(id) && after.Remove(id) && before.SetEquals(after)
                           : before.Remove(id) && !after.Contains(id) && before.SetEquals(after);
        if (!valid) throw new InvalidOperationException(UiText.Get("InvalidChangeToTheTabGroup"));
    }

    private void CheckExpected(PanelTabGroup expected, SqliteTransaction tx)
    {
        using var header = database.Connection.CreateCommand(); header.Transaction = tx;
        header.CommandText = "SELECT ActiveInstanceId FROM PanelTabGroup WHERE GroupId = $id;";
        header.Parameters.AddWithValue("$id", expected.GroupId.ToString());
        if (!string.Equals(header.ExecuteScalar() as string, expected.ActiveId.ToString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(UiText.Get("TheGroupChangedTryAgain"));
        using var read = database.Connection.CreateCommand(); read.Transaction = tx;
        read.CommandText = "SELECT InstanceId, Name FROM PanelTabMember WHERE GroupId = $id ORDER BY SortOrder;";
        read.Parameters.AddWithValue("$id", expected.GroupId.ToString());
        using var reader = read.ExecuteReader();
        var items = new List<PanelTab>();
        while (reader.Read()) items.Add(new PanelTab(new PanelInstanceId(Guid.Parse(reader.GetString(0))), reader.GetString(1)));
        if (!items.SequenceEqual(expected.Tabs)) throw new InvalidOperationException(UiText.Get("TheGroupChangedTryAgain"));
    }

    private void WriteGroup(PanelTabGroup updated, SqliteTransaction tx)
    {
        using (var delete = database.Connection.CreateCommand())
        {
            delete.Transaction = tx; delete.CommandText = "DELETE FROM PanelTabMember WHERE GroupId = $id;";
            delete.Parameters.AddWithValue("$id", updated.GroupId.ToString()); delete.ExecuteNonQuery();
        }
        for (var i = 0; i < updated.Tabs.Length; i++)
        {
            using var insert = database.Connection.CreateCommand(); insert.Transaction = tx;
            insert.CommandText = "INSERT INTO PanelTabMember (GroupId, InstanceId, Name, SortOrder) VALUES ($group, $id, $name, $order);";
            insert.Parameters.AddWithValue("$group", updated.GroupId.ToString()); insert.Parameters.AddWithValue("$id", updated.Tabs[i].InstanceId.ToString());
            insert.Parameters.AddWithValue("$name", updated.Tabs[i].Name); insert.Parameters.AddWithValue("$order", i); insert.ExecuteNonQuery();
        }
        using var update = database.Connection.CreateCommand(); update.Transaction = tx;
        update.CommandText = "UPDATE PanelTabGroup SET ActiveInstanceId = $active WHERE GroupId = $id;";
        update.Parameters.AddWithValue("$active", updated.ActiveId.ToString()); update.Parameters.AddWithValue("$id", updated.GroupId.ToString()); update.ExecuteNonQuery();
    }

    /// <summary>Transakcyjnie zapisuje nazwę, kolejność i wybór bez zmiany stanów instancji.</summary>
    public Task UpdateAsync(PanelTabGroup expected, PanelTabGroup updated, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(updated);
        if (expected.GroupId != updated.GroupId || !expected.Tabs.Select(t => t.InstanceId).ToHashSet().SetEquals(updated.Tabs.Select(t => t.InstanceId)))
            throw new InvalidOperationException(UiText.Get("ChangingTheGroupRequiresAddingOrDeleting"));
        return queue.EnqueueAsync(_ =>
        {
            database.EnsureInitialized();
            using var tx = database.Connection.BeginTransaction();
            using (var header = database.Connection.CreateCommand())
            {
                header.Transaction = tx;
                header.CommandText = "SELECT ActiveInstanceId FROM PanelTabGroup WHERE GroupId = $id;";
                header.Parameters.AddWithValue("$id", expected.GroupId.ToString());
                if (!string.Equals(header.ExecuteScalar() as string, expected.ActiveId.ToString(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(UiText.Get("TheGroupChangedTryAgain"));
            }
            var existing = new List<PanelTab>();
            using (var read = database.Connection.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = "SELECT InstanceId, Name FROM PanelTabMember WHERE GroupId = $id ORDER BY SortOrder;";
                read.Parameters.AddWithValue("$id", expected.GroupId.ToString());
                using var reader = read.ExecuteReader();
                while (reader.Read()) existing.Add(new PanelTab(new PanelInstanceId(Guid.Parse(reader.GetString(0))), reader.GetString(1)));
            }
            if (!existing.SequenceEqual(expected.Tabs)) throw new InvalidOperationException(UiText.Get("TheGroupChangedTryAgain"));
            using (var delete = database.Connection.CreateCommand())
            {
                delete.Transaction = tx;
                delete.CommandText = "DELETE FROM PanelTabMember WHERE GroupId = $id;";
                delete.Parameters.AddWithValue("$id", updated.GroupId.ToString());
                delete.ExecuteNonQuery();
            }
            for (var i = 0; i < updated.Tabs.Length; i++)
            {
                using var insert = database.Connection.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = "INSERT INTO PanelTabMember (GroupId, InstanceId, Name, SortOrder) VALUES ($group, $id, $name, $order);";
                insert.Parameters.AddWithValue("$group", updated.GroupId.ToString());
                insert.Parameters.AddWithValue("$id", updated.Tabs[i].InstanceId.ToString());
                insert.Parameters.AddWithValue("$name", updated.Tabs[i].Name);
                insert.Parameters.AddWithValue("$order", i);
                insert.ExecuteNonQuery();
            }
            using (var update = database.Connection.CreateCommand())
            {
                update.Transaction = tx;
                update.CommandText = "UPDATE PanelTabGroup SET ActiveInstanceId = $active WHERE GroupId = $id;";
                update.Parameters.AddWithValue("$active", updated.ActiveId.ToString());
                update.Parameters.AddWithValue("$id", updated.GroupId.ToString());
                update.ExecuteNonQuery();
            }
            tx.Commit();
        }, cancellationToken);
    }

    public Task<IReadOnlySet<PanelInstanceId>> GetChildInstanceIdsAsync(Guid dashboardId, CancellationToken cancellationToken = default) =>
        queue.EnqueueAsync<IReadOnlySet<PanelInstanceId>>(_ =>
        {
            database.EnsureInitialized();
            using var command = database.Connection.CreateCommand();
            command.CommandText = "SELECT m.InstanceId FROM PanelTabMember m JOIN PanelInstance root ON root.InstanceId = m.GroupId WHERE root.DashboardId = $dashboard AND m.InstanceId <> m.GroupId;";
            command.Parameters.AddWithValue("$dashboard", dashboardId.ToString("D"));
            using var reader = command.ExecuteReader();
            var ids = new HashSet<PanelInstanceId>();
            while (reader.Read()) ids.Add(new PanelInstanceId(Guid.Parse(reader.GetString(0))));
            return ids;
        }, cancellationToken);

    public Task<StoredPanelTabGroup?> GetAsync(PanelInstanceId groupId, CancellationToken cancellationToken = default) =>
        queue.EnqueueAsync(_ =>
        {
            database.EnsureInitialized();
            using var header = database.Connection.CreateCommand();
            header.CommandText = "SELECT ActiveInstanceId, OriginalStateJson FROM PanelTabGroup WHERE GroupId = $id;";
            header.Parameters.AddWithValue("$id", groupId.ToString());
            using var reader = header.ExecuteReader();
            if (!reader.Read()) return null;
            var active = new PanelInstanceId(Guid.Parse(reader.GetString(0)));
            var original = reader.IsDBNull(1) ? null : reader.GetString(1);
            reader.Close();
            using var members = database.Connection.CreateCommand();
            members.CommandText = "SELECT InstanceId, Name FROM PanelTabMember WHERE GroupId = $id ORDER BY SortOrder;";
            members.Parameters.AddWithValue("$id", groupId.ToString());
            using var items = members.ExecuteReader();
            var tabs = new List<PanelTab>();
            while (items.Read()) tabs.Add(new PanelTab(new PanelInstanceId(Guid.Parse(items.GetString(0))), items.GetString(1)));
            return new StoredPanelTabGroup(PanelTabGroup.Restore(groupId, tabs, active), original);
        }, cancellationToken);
}
