using UberDashboard.Application.Abstractions;
using UberDashboard.Core.Layout;
using UberDashboard.Infrastructure.Tests.TestSupport;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class DashboardStoreTests
{
    [Fact]
    public async Task Create_marks_first_dashboard_active()
    {
        await using var fixture = new DatabaseFixture();

        var first = await fixture.Dashboards.CreateAsync("Pierwszy", fixture.Clock.UtcNow);
        var second = await fixture.Dashboards.CreateAsync("Drugi", fixture.Clock.UtcNow);

        Assert.True(first.IsActive);
        Assert.False(second.IsActive);
        Assert.Equal(first.DashboardId, await fixture.Dashboards.GetActiveDashboardIdAsync());
    }

    [Fact]
    public async Task List_returns_dashboards_in_creation_order()
    {
        await using var fixture = new DatabaseFixture();
        var clock = new TestClock(new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero));

        var a = await fixture.Dashboards.CreateAsync("A", clock.UtcNow);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var b = await fixture.Dashboards.CreateAsync("B", clock.UtcNow);

        var dashboards = await fixture.Dashboards.ListAsync();

        Assert.Equal(new[] { a.DashboardId, b.DashboardId }, dashboards.Select(d => d.DashboardId));
    }

    [Fact]
    public async Task Rename_updates_name_and_timestamp()
    {
        await using var fixture = new DatabaseFixture();
        var created = await fixture.Dashboards.CreateAsync("Stara", fixture.Clock.UtcNow);

        var renamed = await fixture.Dashboards.RenameAsync(
            created.DashboardId,
            "Nowa",
            fixture.Clock.UtcNow.AddMinutes(5));

        Assert.Equal("Nowa", renamed.Name);
        Assert.True(renamed.UpdatedAtUtc > created.UpdatedAtUtc);
    }

    [Fact]
    public async Task Rename_missing_dashboard_throws()
    {
        await using var fixture = new DatabaseFixture();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => fixture.Dashboards.RenameAsync(Guid.NewGuid(), "Nowa", fixture.Clock.UtcNow));
    }

    [Fact]
    public async Task Get_returns_null_for_unknown_dashboard()
    {
        await using var fixture = new DatabaseFixture();

        Assert.Null(await fixture.Dashboards.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task SetActive_switches_active_dashboard()
    {
        await using var fixture = new DatabaseFixture();
        var a = await fixture.Dashboards.CreateAsync("A", fixture.Clock.UtcNow);
        var b = await fixture.Dashboards.CreateAsync("B", fixture.Clock.UtcNow);

        await fixture.Dashboards.SetActiveDashboardAsync(b.DashboardId);

        Assert.Equal(b.DashboardId, await fixture.Dashboards.GetActiveDashboardIdAsync());
        Assert.False((await fixture.Dashboards.GetAsync(a.DashboardId))!.IsActive);
    }

    [Fact]
    public async Task Delete_removes_dashboard_with_layout_and_instances()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Do usunięcia", fixture.Clock.UtcNow);
        var instanceId = PanelInstanceId.New();

        await fixture.Layouts.SaveAsync(
            dashboard.DashboardId,
            new LayoutTree(new LeafNode(instanceId)));
        await fixture.Panels.UpsertAsync(new PanelInstanceRecord(
            instanceId,
            dashboard.DashboardId,
            "com.example.plugin",
            "todo",
            "Zadania",
            false,
            null,
            null,
            null,
            1,
            fixture.Clock.UtcNow,
            fixture.Clock.UtcNow));

        await fixture.Dashboards.DeleteAsync(dashboard.DashboardId);

        Assert.Null(await fixture.Dashboards.GetAsync(dashboard.DashboardId));
        Assert.Equal(0L, Count(fixture, "SELECT COUNT(*) FROM DashboardLayout WHERE DashboardId = $id;", dashboard.DashboardId));
        Assert.Equal(0L, Count(fixture, "SELECT COUNT(*) FROM PanelInstance WHERE DashboardId = $id;", dashboard.DashboardId));
    }

    [Fact]
    public async Task Create_rejects_blank_name()
    {
        await using var fixture = new DatabaseFixture();

        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.Dashboards.CreateAsync("   ", fixture.Clock.UtcNow));
    }

    private static long Count(DatabaseFixture fixture, string sql, Guid dashboardId)
    {
        using var command = fixture.Database.Connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", dashboardId.ToString("D"));
        return (long)(command.ExecuteScalar() ?? 0L);
    }
}
