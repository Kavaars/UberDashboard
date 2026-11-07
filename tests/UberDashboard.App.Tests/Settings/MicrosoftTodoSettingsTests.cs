using System.Text.Json;
using UberDashboard.App.Features.Todo;
using UberDashboard.App.Panels;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Tests.Settings;

public sealed class MicrosoftTodoSettingsTests
{
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

    private static MicrosoftTodoSnapshot Snapshot(MicrosoftTodoLink link, string task) =>
        new(link, link.ListId, new TodoDocument { Items = [new(task, false)] });
}
