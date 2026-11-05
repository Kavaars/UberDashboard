using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;
using UberDashboard.Application.Synchronization;
using UberDashboard.Infrastructure.Database.Stores;
using UberDashboard.Infrastructure.Tests.TestSupport;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class SyncStoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Version_four_upgrade_preserves_pending_changes_with_or_without_the_early_metadata_column(bool earlyMetadataColumn)
    {
        await using var fixture = new DatabaseFixture();
        var binding = new SyncBinding(Guid.NewGuid(), SyncProviderKind.MicrosoftTodo, "client", "account", "lists/a/tasks", "Test", true);
        var original = SyncEdits.Edit(new(binding), Guid.NewGuid(), new JsonObject { ["title"] = "not uploaded" }, fixture.Clock.UtcNow);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={fixture.Database.DatabaseFile};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE SchemaMigration (Version INTEGER PRIMARY KEY, Name TEXT NOT NULL, AppliedAtUtc TEXT NOT NULL);";
            command.ExecuteNonQuery();
            foreach (var migration in UberDashboard.Infrastructure.Database.SqliteSchema.Migrations.Take(4))
            {
                foreach (var statement in migration.Statements) { command.CommandText = statement; command.ExecuteNonQuery(); }
                command.CommandText = $"INSERT INTO SchemaMigration VALUES ({migration.Version}, 'test', '2026-11-05');"; command.ExecuteNonQuery();
            }
            if (earlyMetadataColumn)
            { command.CommandText = "ALTER TABLE SyncBinding ADD COLUMN BindingJson TEXT NOT NULL DEFAULT '{}';"; command.ExecuteNonQuery(); }
            command.CommandText = "INSERT INTO SyncBinding (BindingId, Provider, ClientId, AccountId, ResourcePath, Version, StateJson) VALUES ($id, 0, 'client', 'account', 'lists/a/tasks', 0, $state);";
            command.Parameters.AddWithValue("$id", binding.Id.ToString("D"));
            command.Parameters.AddWithValue("$state", System.Text.Json.JsonSerializer.Serialize(original, System.Text.Json.JsonSerializerOptions.Web));
            command.ExecuteNonQuery();
        }
        var store = new SyncStore(fixture.Queue, fixture.Database);
        var loaded = (await store.ReadAsync(binding.Id))!;
        Assert.Equal("not uploaded", Assert.Single(loaded.Items).Local!["title"]!.GetValue<string>());
        Assert.Equal(Assert.Single(original.Queue).Id, Assert.Single(loaded.Queue).Id);
        Assert.Equal(binding.Id, Assert.Single(await store.ListAsync()).Id);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(fixture.Root, "Data", "Backups"), "pre-sync-v5-*.db"));
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
