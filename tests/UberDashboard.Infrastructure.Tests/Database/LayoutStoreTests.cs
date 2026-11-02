using UberDashboard.Core.Layout;
using UberDashboard.Core.Layout.Serialization;
using UberDashboard.Infrastructure.Tests.TestSupport;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class LayoutStoreTests
{
    [Fact]
    public async Task Load_returns_empty_layout_when_none_saved()
    {
        await using var fixture = new DatabaseFixture();

        var result = await fixture.Layouts.LoadAsync(Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.True(result.Tree.IsEmpty);
        Assert.Equal(LayoutSerializer.CurrentSchemaVersion, result.SchemaVersion);
    }

    [Fact]
    public async Task Save_then_load_roundtrips_tree()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Główny", fixture.Clock.UtcNow);
        var tree = CreateSampleTree();

        await fixture.Layouts.SaveAsync(dashboard.DashboardId, tree);
        var result = await fixture.Layouts.LoadAsync(dashboard.DashboardId);

        Assert.True(result.IsSuccess);
        Assert.Equal(tree, result.Tree);
        Assert.Equal(LayoutSerializer.CurrentSchemaVersion, result.SchemaVersion);
    }

    [Fact]
    public async Task Save_is_upsert_and_replaces_previous_tree()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Główny", fixture.Clock.UtcNow);

        await fixture.Layouts.SaveAsync(dashboard.DashboardId, CreateSampleTree());
        var replacement = new LayoutTree(new LeafNode(PanelInstanceId.New()));
        await fixture.Layouts.SaveAsync(dashboard.DashboardId, replacement);

        var result = await fixture.Layouts.LoadAsync(dashboard.DashboardId);

        Assert.Equal(replacement, result.Tree);
        Assert.Equal(1, result.Tree.LeafCount);
    }

    [Fact]
    public async Task Load_reports_malformed_document_and_preserves_original()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Popsuty", fixture.Clock.UtcNow);
        InsertRawLayout(fixture, dashboard.DashboardId, "{ this is not json");

        var result = await fixture.Layouts.LoadAsync(dashboard.DashboardId);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.MalformedDocument, result.Error);
        Assert.True(result.MustPreserveOriginalDocument);
    }

    [Fact]
    public async Task Load_reports_newer_schema_without_overwriting()
    {
        await using var fixture = new DatabaseFixture();
        var dashboard = await fixture.Dashboards.CreateAsync("Nowszy", fixture.Clock.UtcNow);
        InsertRawLayout(fixture, dashboard.DashboardId, "{\"schemaVersion\": 999}");

        var result = await fixture.Layouts.LoadAsync(dashboard.DashboardId);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.UnknownSchemaVersion, result.Error);
        Assert.True(result.MustPreserveOriginalDocument);
    }

    private static LayoutTree CreateSampleTree()
    {
        var first = new LeafNode(PanelInstanceId.New());
        var second = new LeafNode(PanelInstanceId.New());
        return new LayoutTree(SplitNode.Create(SplitAxis.Horizontal, 0.5, first, second));
    }

    private static void InsertRawLayout(DatabaseFixture fixture, Guid dashboardId, string treeJson)
    {
        fixture.Database.EnsureInitialized();

        using var command = fixture.Database.Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DashboardLayout (DashboardId, SchemaVersion, TreeJson, DisplayMode, MonitorId, UpdatedAtUtc)
            VALUES ($id, $schemaVersion, $treeJson, 'Window', NULL, $updatedAtUtc);
            """;
        command.Parameters.AddWithValue("$id", dashboardId.ToString("D"));
        command.Parameters.AddWithValue("$schemaVersion", LayoutSerializer.CurrentSchemaVersion);
        command.Parameters.AddWithValue("$treeJson", treeJson);
        command.Parameters.AddWithValue("$updatedAtUtc", "2026-01-01T00:00:00.0000000+00:00");
        command.ExecuteNonQuery();
    }
}
