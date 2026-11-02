using UberDashboard.Application.Abstractions;
using UberDashboard.Core.Layout;
using UberDashboard.Infrastructure.Tests.TestSupport;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class PanelInstanceStoreTests
{
    [Fact]
    public async Task Upsert_then_get_roundtrips_record()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Pulpit", fixture.Clock.UtcNow);
        var record = CreateRecord(dashboard.DashboardId, PanelInstanceId.New(), "todo", fixture);

        await fixture.Panels.UpsertAsync(record);
        var loaded = await fixture.Panels.GetAsync(record.InstanceId);

        Assert.Equal(record, loaded);
    }

    [Fact]
    public async Task Upsert_updates_existing_instance()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Pulpit", fixture.Clock.UtcNow);
        var instanceId = PanelInstanceId.New();
        await fixture.Panels.UpsertAsync(CreateRecord(dashboard.DashboardId, instanceId, "todo", fixture));

        var updated = CreateRecord(dashboard.DashboardId, instanceId, "todo", fixture) with
        {
            DisplayName = "Zmieniona",
            StateJson = "{\"items\":1}",
            ConfigJson = "{\"theme\":\"dark\"}",
        };
        await fixture.Panels.UpsertAsync(updated);

        var loaded = await fixture.Panels.GetAsync(instanceId);

        Assert.Equal("Zmieniona", loaded!.DisplayName);
        Assert.Equal("{\"items\":1}", loaded.StateJson);
        Assert.Equal("{\"theme\":\"dark\"}", loaded.ConfigJson);
    }

    [Fact]
    public async Task List_returns_only_instances_of_dashboard()
    {
        await using var fixture = new DatabaseFixture();
        var first = await fixture.Dashboards.CreateAsync("A", fixture.Clock.UtcNow);
        var second = await fixture.Dashboards.CreateAsync("B", fixture.Clock.UtcNow);

        var kept = CreateRecord(first.DashboardId, PanelInstanceId.New(), "todo", fixture);
        var other = CreateRecord(second.DashboardId, PanelInstanceId.New(), "note", fixture);
        await fixture.Panels.UpsertAsync(kept);
        await fixture.Panels.UpsertAsync(other);

        var listed = await fixture.Panels.ListForDashboardAsync(first.DashboardId);

        Assert.Equal(new[] { kept.InstanceId }, listed.Select(r => r.InstanceId));
    }

    [Fact]
    public async Task SetHidden_updates_flag_and_restore_hint()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Pulpit", fixture.Clock.UtcNow);
        var instanceId = PanelInstanceId.New();
        await fixture.Panels.UpsertAsync(CreateRecord(dashboard.DashboardId, instanceId, "todo", fixture));

        await fixture.Panels.SetHiddenAsync(instanceId, true, "{\"slot\":3}", fixture.Clock.UtcNow);

        var loaded = await fixture.Panels.GetAsync(instanceId);
        Assert.True(loaded!.IsHidden);
        Assert.Equal("{\"slot\":3}", loaded.RestoreHintJson);
    }

    [Fact]
    public async Task Delete_removes_instance()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Pulpit", fixture.Clock.UtcNow);
        var instanceId = PanelInstanceId.New();
        await fixture.Panels.UpsertAsync(CreateRecord(dashboard.DashboardId, instanceId, "todo", fixture));

        await fixture.Panels.DeleteAsync(instanceId);

        Assert.Null(await fixture.Panels.GetAsync(instanceId));
    }

    [Fact]
    public async Task Two_instances_of_same_type_are_independent()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Pulpit", fixture.Clock.UtcNow);
        var firstId = PanelInstanceId.New();
        var secondId = PanelInstanceId.New();
        await fixture.Panels.UpsertAsync(CreateRecord(dashboard.DashboardId, firstId, "todo", fixture));
        await fixture.Panels.UpsertAsync(CreateRecord(dashboard.DashboardId, secondId, "todo", fixture));

        await fixture.Panels.SetHiddenAsync(firstId, true, null, fixture.Clock.UtcNow);

        Assert.True((await fixture.Panels.GetAsync(firstId))!.IsHidden);
        Assert.False((await fixture.Panels.GetAsync(secondId))!.IsHidden);
    }

    private static PanelInstanceRecord CreateRecord(
        Guid dashboardId,
        PanelInstanceId instanceId,
        string typeId,
        DatabaseFixture fixture) => new(
        instanceId,
        dashboardId,
        "com.example.plugin",
        typeId,
        "Panel",
        false,
        null,
        null,
        null,
        1,
        fixture.Clock.UtcNow,
        fixture.Clock.UtcNow);
}
