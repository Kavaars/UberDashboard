using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;
using UberDashboard.Application.Synchronization;
using UberDashboard.Infrastructure.Database.Stores;
using UberDashboard.Infrastructure.Tests.TestSupport;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class SyncStoreTests
{
    [Theory]
    [InlineData(SyncProviderKind.MicrosoftTodo)]
    [InlineData(SyncProviderKind.GoogleCalendar)]
    public async Task Current_protocol_rejects_older_formats_and_persists_deletion_guards_and_outbox(SyncProviderKind kind)
    {
        await using var fixture = new DatabaseFixture(); var store = new SyncStore(fixture.Queue, fixture.Database);
        var binding = new SyncBinding(Guid.NewGuid(), kind, "client", "account", SyncContainers.CatalogPath(kind), "Catalog");
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.SaveAsync(new(binding, FormatVersion: 3), -1));
        var fields = new JsonObject { [kind == SyncProviderKind.MicrosoftTodo ? "displayName" : "summary"] = "Name" };
        var entity = new SyncEntity(Guid.NewGuid(), "container", fields, fields, Metadata: new JsonObject { ["deletedResourceIds"] = new JsonArray("older-id") });
        var state = SyncEdits.Edit(new(binding, Entities: [entity], FormatVersion: 5), entity.LocalId, null, fixture.Clock.UtcNow);
        Assert.True(await store.SaveAsync(state, -1));
        var restored = (await new SyncStore(fixture.Queue, fixture.Database).ReadAsync(binding.Id))!;
        Assert.Equal(5, restored.FormatVersion); Assert.Single(restored.Queue); Assert.Equal(SyncOperationState.AwaitingApproval, restored.Queue[0].State);
        Assert.Equal("older-id", restored.Items[0].Metadata!["deletedResourceIds"]![0]!.GetValue<string>());
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.SaveAsync(restored with { FormatVersion = 2 }, restored.Version));
    }
    [Fact]
    public async Task Parent_child_identity_and_queued_offline_creation_commit_together_and_survive_reopening()
    {
        await using var fixture = new DatabaseFixture(); var store = new SyncStore(fixture.Queue, fixture.Database);
        var binding = new SyncBinding(Guid.NewGuid(), SyncProviderKind.MicrosoftTodo, "client", "account", "lists/a/tasks", "Test");
        var parent = Guid.NewGuid(); var child = Guid.NewGuid();
        var state = SyncEdits.Edit(new(binding), parent, new JsonObject { ["title"] = "Parent" }, fixture.Clock.UtcNow);
        state = SyncEdits.EditChild(state, parent, child, SyncEntityKind.ChecklistItem, new JsonObject { ["displayName"] = "Step" }, fixture.Clock.UtcNow);
        Assert.True(await store.SaveAsync(state, -1));
        var loaded = (await new SyncStore(fixture.Queue, fixture.Database).ReadAsync(binding.Id))!;
        Assert.Equal(parent, loaded.Items.Single(e => e.LocalId == child).ParentId); Assert.Equal(2, loaded.Queue.Count);
        var invalid = loaded with { Entities = loaded.Items.Where(e => e.LocalId != parent).ToArray() };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(invalid, loaded.Version));
        Assert.Equal(2, (await store.ReadAsync(binding.Id))!.Items.Count);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    public async Task Upgrade_preserves_pending_changes_from_both_version_four_variants_and_version_five(int version, bool earlyMetadataColumn)
    {
        await using var fixture = new DatabaseFixture();
        var binding = new SyncBinding(Guid.NewGuid(), SyncProviderKind.MicrosoftTodo, "client", "account", "lists/a/tasks", "Test", true);
        var original = SyncEdits.Edit(new(binding), Guid.NewGuid(), new JsonObject { ["title"] = "not uploaded" }, fixture.Clock.UtcNow) with { FormatVersion = 1 };
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={fixture.Database.DatabaseFile};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE SchemaMigration (Version INTEGER PRIMARY KEY, Name TEXT NOT NULL, AppliedAtUtc TEXT NOT NULL);";
            command.ExecuteNonQuery();
            foreach (var migration in UberDashboard.Infrastructure.Database.SqliteSchema.Migrations.Take(version))
            {
                foreach (var statement in migration.Statements) { command.CommandText = statement; command.ExecuteNonQuery(); }
                command.CommandText = $"INSERT INTO SchemaMigration VALUES ({migration.Version}, 'test', '2026-11-05');"; command.ExecuteNonQuery();
            }
            if (earlyMetadataColumn)
            { command.CommandText = "ALTER TABLE SyncBinding ADD COLUMN BindingJson TEXT NOT NULL DEFAULT '{}';"; command.ExecuteNonQuery(); }
            command.CommandText = version == 4
                ? "INSERT INTO SyncBinding (BindingId, Provider, ClientId, AccountId, ResourcePath, Version, StateJson) VALUES ($id, 0, 'client', 'account', 'lists/a/tasks', 0, $state);"
                : "INSERT INTO SyncBinding (BindingId, Provider, ClientId, AccountId, ResourcePath, BindingJson, Version, StateJson) VALUES ($id, 0, 'client', 'account', 'lists/a/tasks', $binding, 0, $state);";
            if (version >= 5) command.Parameters.AddWithValue("$binding", System.Text.Json.JsonSerializer.Serialize(binding, System.Text.Json.JsonSerializerOptions.Web));
            command.Parameters.AddWithValue("$id", binding.Id.ToString("D"));
            command.Parameters.AddWithValue("$state", System.Text.Json.JsonSerializer.Serialize(original, System.Text.Json.JsonSerializerOptions.Web));
            command.ExecuteNonQuery();
        }
        var store = new SyncStore(fixture.Queue, fixture.Database);
        var loaded = (await store.ReadAsync(binding.Id))!;
        Assert.Equal("not uploaded", Assert.Single(loaded.Items).Local!["title"]!.GetValue<string>());
        Assert.Equal(Assert.Single(original.Queue).Id, Assert.Single(loaded.Queue).Id);
        Assert.Equal(5, loaded.FormatVersion); Assert.Equal(2, loaded.Version);
        Assert.False(await store.SaveAsync(loaded, 0));
        Assert.Equal(binding.Id, Assert.Single(await store.ListAsync()).Id);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(fixture.Root, "Data", "Backups"), $"pre-sync-v{UberDashboard.Infrastructure.Database.SqliteSchema.CurrentVersion}-*.db"));
    }

    [Fact]
    public async Task Version_seven_upgrade_keeps_pending_work_and_backup_and_invalidates_open_older_writers()
    {
        await using var fixture = new DatabaseFixture();
        var binding = new SyncBinding(Guid.NewGuid(), SyncProviderKind.GoogleCalendar, "client", "account", "calendars/calendar/events", "Test");
        var original = SyncEdits.Edit(new(binding), Guid.NewGuid(), new JsonObject { ["summary"] = "offline" }, fixture.Clock.UtcNow)
            with { FormatVersion = 2, Version = 17, Cursor = "old-cursor" };
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={fixture.Database.DatabaseFile};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE SchemaMigration (Version INTEGER PRIMARY KEY, Name TEXT NOT NULL, AppliedAtUtc TEXT NOT NULL);"; command.ExecuteNonQuery();
            foreach (var migration in UberDashboard.Infrastructure.Database.SqliteSchema.Migrations.Take(7))
            {
                foreach (var statement in migration.Statements) { command.CommandText = statement; command.ExecuteNonQuery(); }
                command.CommandText = $"INSERT INTO SchemaMigration VALUES ({migration.Version}, 'test', '2026-11-06');"; command.ExecuteNonQuery();
            }
            command.CommandText = "INSERT INTO SyncBinding (BindingId, Provider, ClientId, AccountId, ResourcePath, BindingJson, Version, StateJson) VALUES ($id, 1, 'client', 'account', 'calendars/calendar/events', $binding, 17, $state);";
            command.Parameters.AddWithValue("$id", binding.Id.ToString("D"));
            command.Parameters.AddWithValue("$binding", System.Text.Json.JsonSerializer.Serialize(binding, System.Text.Json.JsonSerializerOptions.Web));
            command.Parameters.AddWithValue("$state", System.Text.Json.JsonSerializer.Serialize(original, System.Text.Json.JsonSerializerOptions.Web)); command.ExecuteNonQuery();
        }
        var store = new SyncStore(fixture.Queue, fixture.Database); var upgraded = (await store.ReadAsync(binding.Id))!;
        Assert.Equal(5, upgraded.FormatVersion); Assert.Equal(18, upgraded.Version); Assert.Equal("old-cursor", upgraded.Cursor);
        Assert.Equal(original.Queue[0].Id, upgraded.Queue[0].Id); Assert.Equal("offline", upgraded.Items[0].Local!["summary"]!.GetValue<string>());
        Assert.False(await store.SaveAsync(upgraded, 17));
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(fixture.Root, "Data", "Backups"), "pre-sync-v8-*.db"));
        using var saved = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={backup};Mode=ReadOnly;Pooling=False"); saved.Open(); using var read = saved.CreateCommand();
        read.CommandText = "SELECT StateJson FROM SyncBinding";
        var preserved = System.Text.Json.JsonSerializer.Deserialize<SyncState>((string)read.ExecuteScalar()!, System.Text.Json.JsonSerializerOptions.Web)!;
        Assert.Equal(2, preserved.FormatVersion); Assert.Equal(17, preserved.Version); Assert.Equal(original.Queue[0].Id, preserved.Queue[0].Id);
        read.CommandText = "SELECT MAX(Version) FROM SchemaMigration"; Assert.Equal(7L, read.ExecuteScalar());
    }

    [Fact]
    public async Task Edit_and_outbox_survive_reopening_and_stale_version_cannot_overwrite()
    {
        await using var fixture = new DatabaseFixture();
        var store = new SyncStore(fixture.Queue, fixture.Database);
        var binding = new SyncBinding(Guid.NewGuid(), SyncProviderKind.MicrosoftTodo, "client", "account", "lists/a/tasks", "Test");
        Assert.True(await store.SaveAsync(new(binding), -1));
        var original = (await store.ReadAsync(binding.Id))!;
        var changed = SyncEdits.Edit(original, Guid.NewGuid(), new JsonObject { ["title"] = "offline" }, fixture.Clock.UtcNow);
        Assert.True(await store.SaveAsync(changed, original.Version));
        Assert.False(await store.SaveAsync(original, original.Version));
        var reopened = new SyncStore(fixture.Queue, fixture.Database);
        var saved = (await reopened.ReadAsync(binding.Id))!;
        Assert.Equal("offline", Assert.Single(saved.Items).Local!["title"]!.GetValue<string>());
        Assert.Equal(Assert.Single(saved.Items).LocalId, Assert.Single(saved.Queue).EntityId);
        Assert.Equal(1, saved.Version);
        Assert.Single(await reopened.ListAsync());
    }

    [Fact]
    public async Task A_remote_collection_can_only_have_one_binding_per_identity()
    {
        await using var fixture = new DatabaseFixture();
        var store = new SyncStore(fixture.Queue, fixture.Database);
        var binding = new SyncBinding(Guid.NewGuid(), SyncProviderKind.GoogleCalendar, "client", "account", "calendars/primary/events", "Test");
        Assert.True(await store.SaveAsync(new(binding), -1));
        Assert.False(await store.SaveAsync(new(binding with { Id = Guid.NewGuid() }), -1));
        Assert.True(await store.SaveAsync(new(binding with { Id = Guid.NewGuid(), AccountId = "another-account" }), -1));
    }

    [Fact]
    public async Task Changing_identity_does_not_move_pending_changes_to_another_account()
    {
        await using var fixture = new DatabaseFixture();
        var store = new SyncStore(fixture.Queue, fixture.Database);
        var binding = new SyncBinding(Guid.NewGuid(), SyncProviderKind.GoogleCalendar, "client", "account", "calendars/primary/events", "Test");
        Assert.True(await store.SaveAsync(new(binding), -1));
        Assert.False(await store.SaveAsync(new(binding with { AccountId = "wrong" }), 0));
        Assert.Equal("account", (await store.ReadAsync(binding.Id))!.Binding.AccountId);
    }
}
