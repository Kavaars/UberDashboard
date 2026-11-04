using UberDashboard.Application.Abstractions;
using UberDashboard.Core.Layout;
using UberDashboard.Core.Panels;
using UberDashboard.Infrastructure.Database.Stores;
using UberDashboard.Infrastructure.Tests.TestSupport;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class PanelTabGroupStoreTests
{
    [Fact]
    public async Task Batch_import_rolls_back_earlier_tabs_when_a_later_record_fails()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Pulpit", fixture.Clock.UtcNow);
        var first = PanelInstanceId.New();
        var second = PanelInstanceId.New();
        var third = PanelInstanceId.New();
        var original = new PanelInstanceRecord(first, dashboard.DashboardId, "builtin", "todo", "Zadania", false,
            null, "original", null, 1, fixture.Clock.UtcNow, fixture.Clock.UtcNow);
        await fixture.Panels.UpsertAsync(original);
        var store = new PanelTabGroupStore(fixture.Queue, fixture.Database);
        var initial = PanelTabGroup.Create(first, "A");
        await store.CreateAsync(initial, [original], original.StateJson);
        var updated = initial.Add(second, "B").Add(third, "C");
        var imported = original with { InstanceId = second, StateJson = "imported" };
        var invalid = original with { InstanceId = third, PanelTypeId = "notes" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddBatchAsync(initial, updated, [imported, invalid]));

        Assert.Null(await fixture.Panels.GetAsync(second));
        Assert.Single((await store.GetAsync(first))!.Group.Tabs);
        await store.AddBatchAsync(initial, updated, [imported, invalid with { PanelTypeId = "todo" }]);
        Assert.Equal("imported", (await fixture.Panels.GetAsync(second))!.StateJson);
        Assert.Equal(first, (await store.GetAsync(first))!.Group.ActiveId);
    }

    [Fact]
    public async Task Saves_and_restores_group_and_instances_atomically()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Pulpit", fixture.Clock.UtcNow);
        var first = PanelInstanceId.New();
        var second = PanelInstanceId.New();
        var original = new PanelInstanceRecord(first, dashboard.DashboardId, "builtin", "todo", "Zadania", false, null, "{\"old\":true}", null, 1, fixture.Clock.UtcNow, fixture.Clock.UtcNow);
        await fixture.Panels.UpsertAsync(original);
        var group = PanelTabGroup.Create(first, "Pierwsza").Add(second, "Druga").Select(second);
        var store = new PanelTabGroupStore(fixture.Queue, fixture.Database);
        await store.CreateAsync(group, [original with { StateJson = "{\"items\":1}" }, original with { InstanceId = second, StateJson = "{\"items\":2}" }], original.StateJson);
        var restored = await store.GetAsync(first);
        Assert.NotNull(restored);
        Assert.Equal(second, restored.Group.ActiveId);
        Assert.Equal([first, second], restored.Group.Tabs.Select(x => x.InstanceId));
        Assert.Equal(original.StateJson, restored.OriginalStateJson);
        Assert.Equal("{\"items\":2}", (await fixture.Panels.GetAsync(second))!.StateJson);
    }

    [Fact]
    public async Task Failed_creation_rolls_back_all_changes()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Pulpit", fixture.Clock.UtcNow);
        var first = PanelInstanceId.New();
        var second = PanelInstanceId.New();
        var original = new PanelInstanceRecord(first, dashboard.DashboardId, "builtin", "todo", "Zadania", false, null, "original", null, 1, fixture.Clock.UtcNow, fixture.Clock.UtcNow);
        await fixture.Panels.UpsertAsync(original);
        var store = new PanelTabGroupStore(fixture.Queue, fixture.Database);
        var group = PanelTabGroup.Create(first, "A").Add(second, "B");
        await Assert.ThrowsAnyAsync<Exception>(() => store.CreateAsync(group, [original with { StateJson = "changed" }, original with { InstanceId = second, DisplayName = null! }], original.StateJson));
        Assert.Null(await store.GetAsync(first));
        Assert.Equal("original", (await fixture.Panels.GetAsync(first))!.StateJson);
        Assert.Null(await fixture.Panels.GetAsync(second));
    }
    [Fact]
    public async Task Collision_or_changed_source_never_overwrites_existing_data()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Pulpit", fixture.Clock.UtcNow);
        var first = PanelInstanceId.New();
        var second = PanelInstanceId.New();
        var original = new PanelInstanceRecord(first, dashboard.DashboardId, "builtin", "todo", "Zadania", false, null, "old", null, 1, fixture.Clock.UtcNow, fixture.Clock.UtcNow);
        var existing = original with { InstanceId = second, StateJson = "other" };
        await fixture.Panels.UpsertAsync(original);
        await fixture.Panels.UpsertAsync(existing);
        var group = PanelTabGroup.Create(first, "A").Add(second, "B");
        var store = new PanelTabGroupStore(fixture.Queue, fixture.Database);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(group, [original with { StateJson = "changed" }, existing with { StateJson = "new" }], "old"));
        Assert.Equal("old", (await fixture.Panels.GetAsync(first))!.StateJson);
        Assert.Equal("other", (await fixture.Panels.GetAsync(second))!.StateJson);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(group, [original, existing], "stale"));
        Assert.Null(await store.GetAsync(first));
    }
    [Fact]
    public async Task Update_preserves_instance_states_and_rejects_stale_or_unknown_members()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Pulpit", fixture.Clock.UtcNow);
        var first = PanelInstanceId.New(); var second = PanelInstanceId.New();
        var original = new PanelInstanceRecord(first, dashboard.DashboardId, "builtin", "todo", "Zadania", false, null, "old", null, 1, fixture.Clock.UtcNow, fixture.Clock.UtcNow);
        await fixture.Panels.UpsertAsync(original);
        var store = new PanelTabGroupStore(fixture.Queue, fixture.Database);
        var group = PanelTabGroup.Create(first, "A").Add(second, "B");
        await store.CreateAsync(group, [original, original with { InstanceId = second, StateJson = "second" }], "old");
        var updated = group.Move(second, 0).Rename(second, "Nowa").Select(second);
        await store.UpdateAsync(group, updated);
        var loaded = await store.GetAsync(first);
        Assert.Equal([second, first], loaded!.Group.Tabs.Select(t => t.InstanceId));
        Assert.Equal("Nowa", loaded.Group.Tabs[0].Name);
        Assert.Equal(second, loaded.Group.ActiveId);
        Assert.Equal("second", (await fixture.Panels.GetAsync(second))!.StateJson);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(group, updated));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(updated, updated.Add(PanelInstanceId.New(), "Nieznana")));
        Assert.Equal(second, (await store.GetAsync(first))!.Group.ActiveId);
    }
    [Fact]
    public async Task Adding_and_removing_members_updates_group_and_records_atomically()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Pulpit", fixture.Clock.UtcNow);
        var first = PanelInstanceId.New(); var second = PanelInstanceId.New();
        var original = new PanelInstanceRecord(first, dashboard.DashboardId, "builtin", "todo", "Zadania", false, null, "old", null, 1, fixture.Clock.UtcNow, fixture.Clock.UtcNow);
        await fixture.Panels.UpsertAsync(original);
        var store = new PanelTabGroupStore(fixture.Queue, fixture.Database);
        var initial = PanelTabGroup.Create(first, "A");
        await store.CreateAsync(initial, [original], "old");
        var added = initial.Add(second, "B").Select(second);
        await store.AddAsync(initial, added, original with { InstanceId = second, StateJson = "new" });
        Assert.Equal("new", (await fixture.Panels.GetAsync(second))!.StateJson);
        Assert.Equal(second, (await store.GetAsync(first))!.Group.ActiveId);
        var removed = added.Remove(second);
        await store.RemoveAsync(added, removed, second);
        Assert.Null(await fixture.Panels.GetAsync(second));
        Assert.Equal(first, (await store.GetAsync(first))!.Group.ActiveId);
        Assert.Equal("old", (await fixture.Panels.GetAsync(first))!.StateJson);
    }
    [Fact]
    public async Task Removing_group_anchor_retains_anchor_record_and_selected_member()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Pulpit", fixture.Clock.UtcNow);
        var first = PanelInstanceId.New(); var second = PanelInstanceId.New();
        var root = new PanelInstanceRecord(first, dashboard.DashboardId, "builtin", "todo", "Zadania", false, null, "anchor", null, 1, fixture.Clock.UtcNow, fixture.Clock.UtcNow);
        await fixture.Panels.UpsertAsync(root);
        var store = new PanelTabGroupStore(fixture.Queue, fixture.Database);
        var one = PanelTabGroup.Create(first, "A");
        await store.CreateAsync(one, [root], "anchor");
        var both = one.Add(second, "B");
        await store.AddAsync(one, both, root with { InstanceId = second, StateJson = "child" });
        var remaining = both.Remove(first);
        await store.RemoveAsync(both, remaining, first);
        Assert.Null((await fixture.Panels.GetAsync(first))!.StateJson);
        Assert.Equal("child", (await fixture.Panels.GetAsync(second))!.StateJson);
        Assert.Equal(second, (await store.GetAsync(first))!.Group.ActiveId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(one, both, root with { InstanceId = PanelInstanceId.New() }));
    }
    [Fact]
    public async Task Deleting_group_removes_its_child_records_and_anchor_atomically()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Pulpit", fixture.Clock.UtcNow);
        var first = PanelInstanceId.New(); var second = PanelInstanceId.New();
        var root = new PanelInstanceRecord(first, dashboard.DashboardId, "builtin", "todo", "Zadania", false, null, "anchor", null, 1, fixture.Clock.UtcNow, fixture.Clock.UtcNow);
        await fixture.Panels.UpsertAsync(root);
        var store = new PanelTabGroupStore(fixture.Queue, fixture.Database);
        var group = PanelTabGroup.Create(first, "A").Add(second, "B");
        await store.CreateAsync(group, [root, root with { InstanceId = second, StateJson = "child" }], "anchor");
        await store.DeleteAsync(group);
        Assert.Null(await store.GetAsync(first));
        Assert.Null(await fixture.Panels.GetAsync(first));
        Assert.Null(await fixture.Panels.GetAsync(second));
    }
}
