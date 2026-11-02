using UberDashboard.Core.Layout;
using UberDashboard.Core.Tests.TestSupport;

namespace UberDashboard.Core.Tests.Layout;

public sealed class LayoutValidatorTests
{
    private static readonly LayoutLimits Limits = LayoutLimits.Default;

    [Fact]
    public void Valid_tree_with_matching_visible_instances_has_no_issues()
    {
        var tree = new LayoutTree(TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2)));
        var visible = new HashSet<PanelInstanceId> { TreeFactory.Id(1), TreeFactory.Id(2) };

        var result = LayoutValidator.Validate(tree, visible, Limits);

        Assert.True(result.IsValid);
        Assert.Equal("układ poprawny", result.Describe());
    }

    [Fact]
    public void Empty_tree_without_visible_instances_is_valid()
    {
        var result = LayoutValidator.Validate(LayoutTree.Empty, new HashSet<PanelInstanceId>(), Limits);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Empty_tree_with_visible_instances_is_inconsistent()
    {
        var visible = new HashSet<PanelInstanceId> { TreeFactory.Id(1) };

        var result = LayoutValidator.Validate(LayoutTree.Empty, visible, Limits);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == LayoutErrorCode.MissingRoot);
    }

    [Fact]
    public void Duplicate_leaf_is_reported()
    {
        var duplicate = TreeFactory.Id(1);
        var tree = new LayoutTree(TreeFactory.Split(SplitAxis.Horizontal, 0.5, new LeafNode(duplicate), new LeafNode(duplicate)));
        var visible = new HashSet<PanelInstanceId> { duplicate };

        var result = LayoutValidator.Validate(tree, visible, Limits);

        Assert.Contains(result.Issues, issue => issue.Code == LayoutErrorCode.DuplicateInstance);
    }

    [Fact]
    public void Leaf_pointing_to_unknown_instance_is_reported()
    {
        var tree = TreeFactory.SingleLeaf(1);
        var visible = new HashSet<PanelInstanceId> { TreeFactory.Id(2) };

        var result = LayoutValidator.Validate(tree, visible, Limits);

        Assert.Contains(result.Issues, issue => issue.Code == LayoutErrorCode.UnknownInstance);
    }

    [Fact]
    public void Visible_instance_missing_from_tree_is_reported()
    {
        var tree = TreeFactory.SingleLeaf(1);
        var visible = new HashSet<PanelInstanceId> { TreeFactory.Id(1), TreeFactory.Id(2) };

        var result = LayoutValidator.Validate(tree, visible, Limits);

        Assert.Contains(result.Issues, issue => issue.Code == LayoutErrorCode.UnknownInstance && issue.InstanceId == TreeFactory.Id(2));
    }

    [Fact]
    public void Invalid_ratio_is_reported()
    {
        var split = TreeFactory.Split(SplitAxis.Horizontal, 1.5d, TreeFactory.Leaf(1), TreeFactory.Leaf(2));
        var tree = new LayoutTree(split);
        var visible = new HashSet<PanelInstanceId> { TreeFactory.Id(1), TreeFactory.Id(2) };

        var result = LayoutValidator.Validate(tree, visible, Limits);

        Assert.Contains(result.Issues, issue => issue.Code == LayoutErrorCode.InvalidRatio);
    }

    [Fact]
    public void Duplicate_split_id_is_reported()
    {
        var splitId = Guid.NewGuid();
        var tree = new LayoutTree(new SplitNode(
            splitId,
            SplitAxis.Horizontal,
            0.5d,
            new SplitNode(splitId, SplitAxis.Vertical, 0.5d, TreeFactory.Leaf(1), TreeFactory.Leaf(2)),
            TreeFactory.Leaf(3)));
        var visible = new HashSet<PanelInstanceId> { TreeFactory.Id(1), TreeFactory.Id(2), TreeFactory.Id(3) };

        var result = LayoutValidator.Validate(tree, visible, Limits);

        Assert.Contains(result.Issues, issue => issue.Code == LayoutErrorCode.InvalidSplitId);
    }

    [Fact]
    public void Depth_limit_is_enforced()
    {
        var tree = DeepTree(80);
        var visible = new HashSet<PanelInstanceId>(LayoutWalker.Instances(tree.Root));

        var result = LayoutValidator.Validate(tree, visible, Limits);

        Assert.Contains(result.Issues, issue => issue.Code == LayoutErrorCode.DepthLimitExceeded);
    }

    [Fact]
    public void Node_limit_is_enforced()
    {
        var tree = DeepTree(6);
        var visible = new HashSet<PanelInstanceId>(LayoutWalker.Instances(tree.Root));

        var result = LayoutValidator.Validate(tree, visible, new LayoutLimits(4, 64));

        Assert.Contains(result.Issues, issue => issue.Code == LayoutErrorCode.NodeLimitExceeded);
    }

    private static LayoutTree DeepTree(int leafCount)
    {
        LayoutNode node = TreeFactory.Leaf(1);
        for (var i = 2; i <= leafCount; i++)
        {
            node = TreeFactory.Split(SplitAxis.Horizontal, 0.5d, node, TreeFactory.Leaf(i));
        }

        return new LayoutTree(node);
    }
}
