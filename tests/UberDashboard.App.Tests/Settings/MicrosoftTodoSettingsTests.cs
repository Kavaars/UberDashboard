using System.Text.Json;
using UberDashboard.App.Features.Todo;
using UberDashboard.App.Panels;
using UberDashboard.Core.Layout;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.App.Tests.Settings;

public sealed class MicrosoftTodoSettingsTests
{
    [Fact]
    public void Uninitialized_sync_state_does_not_replace_cached_tasks_but_confirmed_empty_state_does()
    {
        var link = new MicrosoftTodoLink(Guid.NewGuid(), "account", "list");
        var view = new MicrosoftTodoListViewModel(new TodoPanelViewModel(new UberDashboard.App.Tests.TestSupport.FakePanelNotifier()));
        view.Load(link, MicrosoftTodoPreferences.Empty(link.ClientId).Refresh(link.ClientId, link.AccountId, [Snapshot(link, "cached")], DateTimeOffset.UtcNow));
        var binding = new SyncBinding(Guid.NewGuid(), SyncProviderKind.MicrosoftTodo, link.ClientId.ToString("D"), link.AccountId, "lists/list/tasks", "list", true);
        view.ApplyLocalState(new SyncState(binding));
        Assert.Equal("cached", Assert.Single(view.Items).Text);
        view.ApplyLocalState(new SyncState(binding, Cursor: "confirmed"));
        Assert.Empty(view.Items);
    }
    [Fact]
    public void Sync_errors_and_paused_state_remain_visible_after_preferences_reload()
    {
        var link = new MicrosoftTodoLink(Guid.NewGuid(), "account", "list");
        var preferences = MicrosoftTodoPreferences.Empty(link.ClientId).Refresh(link.ClientId, link.AccountId, [Snapshot(link, "cached")], DateTimeOffset.UtcNow);
        var view = new MicrosoftTodoListViewModel(new TodoPanelViewModel(new UberDashboard.App.Tests.TestSupport.FakePanelNotifier()));
        view.Load(link, preferences);
        var binding = new SyncBinding(Guid.NewGuid(), SyncProviderKind.MicrosoftTodo, link.ClientId.ToString("D"), link.AccountId, "lists/list/tasks", "list", true);
        view.ApplyLocalState(new SyncState(binding, ConnectionStatus: SyncConnectionStatus.AuthenticationRequired));
        Assert.Equal(UberDashboard.Core.Localization.UiText.Get("SyncReconnectRequired"), view.Status);
        Assert.Equal("cached", Assert.Single(view.Items).Text);
        view.Load(link, preferences);
        Assert.Equal(UberDashboard.Core.Localization.UiText.Get("SyncReconnectRequired"), view.Status);
        view.ApplyLocalState(new SyncState(binding with { Enabled = false }));
        Assert.Equal(UberDashboard.Core.Localization.UiText.Get("SyncPaused"), view.Status);
    }

    [Fact]
    public void Refresh_replaces_the_selected_snapshot_and_keeps_disabled_lists_readable()
    {
        var client = Guid.NewGuid();
        var first = new MicrosoftTodoLink(client, "account", "first");
        var second = first with { ListId = "second" };
        var otherAccount = first with { AccountId = "different" };
        var old = MicrosoftTodoPreferences.Empty(client).Refresh(client, "account",
            [Snapshot(first, "old"), Snapshot(second, "retained"), Snapshot(otherAccount, "other")], DateTimeOffset.UtcNow);

        var updated = old.Refresh(client, "account", [Snapshot(first, "new")], DateTimeOffset.UtcNow);
        var restored = JsonSerializer.Deserialize<MicrosoftTodoPreferences>(JsonSerializer.Serialize(updated))!;
        Assert.Equal(["first"], restored.SelectedLists);
        Assert.Equal(3, restored.Snapshots.Length);
        var view = new MicrosoftTodoListViewModel(new TodoPanelViewModel(new UberDashboard.App.Tests.TestSupport.FakePanelNotifier()));
        view.Load(first, restored);
        Assert.Equal("new", Assert.Single(view.Items).Text);
        Assert.Contains("Tylko odczyt", view.Status);
        view.Load(second, restored);
        Assert.Equal("retained", Assert.Single(view.Items).Text);
        Assert.Contains("wyłączona", view.Status);
        view.Load(otherAccount, restored);
        Assert.Equal("other", Assert.Single(view.Items).Text);
        Assert.Contains("wyłączona", view.Status);
    }

    [Fact]
    public void Linked_tab_identity_is_stable_and_scoped_to_group_account_and_client()
    {
        var group = PanelInstanceId.New();
        var link = new MicrosoftTodoLink(Guid.NewGuid(), "account", "list");
        var id = PanelGroupSession.LinkedTabId(group, link);
        Assert.Equal(id, PanelGroupSession.LinkedTabId(group, link with { }));
        Assert.NotEqual(id, PanelGroupSession.LinkedTabId(PanelInstanceId.New(), link));
        Assert.NotEqual(id, PanelGroupSession.LinkedTabId(group, link with { AccountId = "other" }));
        Assert.NotEqual(id, PanelGroupSession.LinkedTabId(group, link with { ClientId = Guid.NewGuid() }));
        Assert.NotEqual(id, PanelGroupSession.LinkedTabId(group, link with { ListId = "other" }));
    }

    [Fact]
    public void Import_keeps_task_titles_and_completion_and_skips_empty_titles()
    {
        var document = MicrosoftTodoSettings.CreateDocument([
            new("1", "Zadanie", "completed"),
            new("2", "Drugie", "inProgress"),
            new("3", " ", "completed"),
        ]);
        Assert.Collection(document.Items,
            item => { Assert.Equal("Zadanie", item.Text); Assert.True(item.IsDone); },
            item => { Assert.Equal("Drugie", item.Text); Assert.False(item.IsDone); });
    }

    [Fact]
    public void Microsoft_tasks_are_sorted_and_filtered_by_dates()
    {
        var date = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        TodoItemState[] tasks = [new("old open", false, CreatedAt: date), new("old done", true, CompletedAt: date),
            new("new open", false, CreatedAt: date.AddDays(1)), new("new done", true, CompletedAt: date.AddDays(1))];
        Assert.Equal(new[] { "new open", "old open", "new done" }, MicrosoftTodoListViewModel.SortItems(tasks, false, 1).Select(x => x.Text));
        Assert.Equal(new[] { "new open", "old open" }, MicrosoftTodoListViewModel.SortItems(tasks, true, 50).Select(x => x.Text));
    }

    [Fact]
    public void Newly_created_pending_task_appears_before_older_remote_tasks()
    {
        var now = DateTimeOffset.Parse("2026-11-07T12:00:00Z");
        var binding = new SyncBinding(Guid.NewGuid(), SyncProviderKind.MicrosoftTodo, "client", "account", "lists/list/tasks", "list", true);
        var old = new SyncEntity(Guid.NewGuid(), "remote", null, new System.Text.Json.Nodes.JsonObject { ["title"] = "old" },
            Metadata: new System.Text.Json.Nodes.JsonObject { ["createdDateTime"] = now.AddDays(-1).ToString("O") });
        var state = UberDashboard.Application.Synchronization.SyncEdits.Edit(new SyncState(binding, Entities: [old]), Guid.NewGuid(),
            new System.Text.Json.Nodes.JsonObject { ["title"] = "new" }, now);
        var view = new MicrosoftTodoListViewModel(new TodoPanelViewModel(new UberDashboard.App.Tests.TestSupport.FakePanelNotifier()));
        view.ApplyLocalState(state);
        Assert.Equal(new[] { "new", "old" }, view.Items.Select(item => item.Text));
        Assert.Equal(SyncOperationKind.Create, Assert.Single(state.Queue).Kind);
    }

    private static MicrosoftTodoSnapshot Snapshot(MicrosoftTodoLink link, string task) =>
        new(link, link.ListId, new TodoDocument { Items = [new(task, false)] });
}
