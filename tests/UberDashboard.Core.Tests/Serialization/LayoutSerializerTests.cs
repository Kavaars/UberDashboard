using System.Text.Json;
using System.Text.Json.Nodes;
using UberDashboard.Core.Layout;
using UberDashboard.Core.Layout.Serialization;
using UberDashboard.Core.Tests.TestSupport;

namespace UberDashboard.Core.Tests.Serialization;

public sealed class LayoutSerializerTests
{
    private static readonly LayoutLimits Limits = LayoutLimits.Default;

    [Fact]
    public void Round_trip_preserves_order_axes_and_ratios()
    {
        var split = TreeFactory.Split(SplitAxis.Horizontal, 0.35d, TreeFactory.Leaf(1), TreeFactory.Leaf(2));
        var tree = new LayoutTree(split);

        var json = LayoutSerializer.Serialize(tree, Limits);
        var result = LayoutSerializer.Deserialize(json, Limits);

        Assert.True(result.IsSuccess);
        Assert.Equal(LayoutSerializer.CurrentSchemaVersion, result.SchemaVersion);

        var restored = Assert.IsType<SplitNode>(result.Tree!.Root);
        Assert.Equal(split.SplitId, restored.SplitId);
        Assert.Equal(SplitAxis.Horizontal, restored.Axis);
        Assert.Equal(0.35d, restored.Ratio, 10);
        Assert.Equal([TreeFactory.Id(1), TreeFactory.Id(2)], LayoutWalker.Instances(result.Tree.Root));
    }

    [Fact]
    public void Round_trip_of_nested_tree_keeps_structure()
    {
        var tree = new LayoutTree(TreeFactory.Split(
            SplitAxis.Horizontal,
            0.6d,
            TreeFactory.Split(SplitAxis.Vertical, 0.4d, TreeFactory.Leaf(1), TreeFactory.Leaf(2)),
            TreeFactory.Leaf(3)));

        var json = LayoutSerializer.Serialize(tree, Limits);
        var result = LayoutSerializer.Deserialize(json, Limits);

        Assert.True(result.IsSuccess);
        Assert.Equal([TreeFactory.Id(1), TreeFactory.Id(2), TreeFactory.Id(3)], LayoutWalker.Instances(result.Tree!.Root));
        Assert.Equal(tree.LeafCount, result.Tree.LeafCount);
    }

    [Fact]
    public void Serialize_empty_tree_has_no_root_property()
    {
        var json = LayoutSerializer.Serialize(LayoutTree.Empty, Limits);

        Assert.DoesNotContain("\"root\"", json);
        Assert.Contains($"\"schemaVersion\": {LayoutSerializer.CurrentSchemaVersion}", json);
    }

    [Fact]
    public void Deserialize_empty_document_returns_empty_tree()
    {
        var json = LayoutSerializer.Serialize(LayoutTree.Empty, Limits);

        var result = LayoutSerializer.Deserialize(json, Limits);

        Assert.True(result.IsSuccess);
        Assert.True(result.Tree!.IsEmpty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    [InlineData("[1,2,3]")]
    [InlineData("{ \"root\": null }")]
    public void Deserialize_rejects_malformed_documents(string json)
    {
        var result = LayoutSerializer.Deserialize(json, Limits);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.MalformedDocument, result.Error);
        Assert.True(result.MustPreserveOriginalDocument);
    }

    [Fact]
    public void Deserialize_rejects_document_newer_than_supported()
    {
        var json = "{ \"schemaVersion\": 99, \"root\": { \"kind\": \"leaf\", \"instanceId\": \"" + Guid.NewGuid() + "\" } }";

        var result = LayoutSerializer.Deserialize(json, Limits);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.UnknownSchemaVersion, result.Error);
        Assert.True(result.MustPreserveOriginalDocument);
    }

    [Fact]
    public void Deserialize_rejects_invalid_ratio()
    {
        var json = BuildSplitDocument(ratio: 1.5d);

        var result = LayoutSerializer.Deserialize(json, Limits);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.InvalidRatio, result.Error);
    }

    [Fact]
    public void Deserialize_rejects_unknown_axis()
    {
        var json = BuildSplitDocument(axis: "diagonal");

        var result = LayoutSerializer.Deserialize(json, Limits);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.MalformedDocument, result.Error);
    }

    [Fact]
    public void Deserialize_rejects_leaf_without_instance()
    {
        var json = "{ \"schemaVersion\": 1, \"root\": { \"kind\": \"leaf\" } }";

        var result = LayoutSerializer.Deserialize(json, Limits);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.MalformedDocument, result.Error);
    }

    [Fact]
    public void Deserialize_rejects_document_exceeding_node_limit()
    {
        var tree = new LayoutTree(TreeFactory.Split(
            SplitAxis.Horizontal,
            0.5d,
            TreeFactory.Split(SplitAxis.Vertical, 0.5d, TreeFactory.Leaf(1), TreeFactory.Leaf(2)),
            TreeFactory.Leaf(3)));
        var json = LayoutSerializer.Serialize(tree, Limits);

        var result = LayoutSerializer.Deserialize(json, new LayoutLimits(2, 64));

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.NodeLimitExceeded, result.Error);
    }

    [Fact]
    public void Deserialize_without_migration_fails_for_older_document()
    {
        var json = BuildSingleLeafDocument(schemaVersion: 1);

        var result = LayoutSerializer.Deserialize(json, Limits, LayoutSchemaMigrations.Empty, targetSchemaVersion: 3);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.UnknownSchemaVersion, result.Error);
    }

    [Fact]
    public void Deserialize_applies_migration_chain_up_to_target_version()
    {
        var json = BuildSingleLeafDocument(schemaVersion: 1);
        var migrations = new LayoutSchemaMigrations([new MarkerMigration(1), new MarkerMigration(2)]);

        var result = LayoutSerializer.Deserialize(json, Limits, migrations, targetSchemaVersion: 3);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.SchemaVersion);
        Assert.Equal(1, result.Tree!.LeafCount);
    }

    [Fact]
    public void Deserialize_reports_failure_when_migration_throws()
    {
        var json = BuildSingleLeafDocument(schemaVersion: 1);
        var migrations = new LayoutSchemaMigrations([new ThrowingMigration()]);

        var result = LayoutSerializer.Deserialize(json, Limits, migrations, targetSchemaVersion: 2);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.MalformedDocument, result.Error);
    }

    [Fact]
    public void Schema_migrations_reject_non_continuous_chain()
    {
        var exception = Assert.Throws<ArgumentException>(() => new LayoutSchemaMigrations([new GapMigration()]));

        Assert.Contains("dokładnie o jeden", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_migrations_reject_duplicate_source_version()
    {
        var exception = Assert.Throws<ArgumentException>(() => new LayoutSchemaMigrations([new MarkerMigration(1), new MarkerMigration(1)]));

        Assert.Contains("Zduplikowana migracja", exception.Message, StringComparison.Ordinal);
    }

    private static string BuildSingleLeafDocument(int schemaVersion) =>
        $"{{ \"schemaVersion\": {schemaVersion}, \"root\": {{ \"kind\": \"leaf\", \"instanceId\": \"{Guid.NewGuid()}\" }} }}";

    private static string BuildSplitDocument(double ratio = 0.5d, string axis = "horizontal")
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["root"] = new JsonObject
            {
                ["kind"] = "split",
                ["splitId"] = Guid.NewGuid().ToString("D"),
                ["axis"] = axis,
                ["ratio"] = ratio,
                ["first"] = new JsonObject { ["kind"] = "leaf", ["instanceId"] = first.ToString() },
                ["second"] = new JsonObject { ["kind"] = "leaf", ["instanceId"] = second.ToString() },
            },
        }.ToJsonString();
    }

    private sealed class ThrowingMigration : ILayoutSchemaMigration
    {
        public int FromVersion => 1;

        public int ToVersion => 2;

        public JsonObject Apply(JsonObject document) => throw new JsonException("testowa awaria migracji");
    }

    private sealed class GapMigration : ILayoutSchemaMigration
    {
        public int FromVersion => 1;

        public int ToVersion => 3;

        public JsonObject Apply(JsonObject document) => document;
    }
}
