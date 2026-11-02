using UberDashboard.Core.Layout;
using UberDashboard.Core.Tests.TestSupport;

namespace UberDashboard.Core.Tests.Layout;

public sealed class LayoutWalkerTests
{
    [Fact]
    public void Instances_are_returned_in_pre_order()
    {
        var tree = new LayoutTree(TreeFactory.Split(
            SplitAxis.Horizontal,
            0.5,
            TreeFactory.Split(SplitAxis.Vertical, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2)),
            TreeFactory.Leaf(3)));

        Assert.Equal([TreeFactory.Id(1), TreeFactory.Id(2), TreeFactory.Id(3)], LayoutWalker.Instances(tree.Root));
    }

    [Fact]
    public void Empty_root_yields_no_nodes()
    {
        Assert.Empty(LayoutWalker.Traverse(null));
        Assert.Null(LayoutWalker.FirstLeaf(null));
        Assert.Null(LayoutWalker.FindLeaf(null, TreeFactory.Id(1)));
        Assert.False(LayoutWalker.Contains(null, TreeFactory.Id(1)));
        Assert.Null(LayoutWalker.Locate(null, TreeFactory.Id(1)));
    }

    [Fact]
    public void FindSplit_locates_split_by_id()
    {
        var split = TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2));
        var tree = new LayoutTree(split);

        Assert.Same(split, LayoutWalker.FindSplit(tree.Root, split.SplitId));
    }

    [Fact]
    public void Locate_reports_parent_axis_and_first_child()
    {
        var tree = new LayoutTree(TreeFactory.Split(SplitAxis.Vertical, 0.25, TreeFactory.Leaf(1), TreeFactory.Leaf(2)));

        var first = LayoutWalker.Locate(tree.Root, TreeFactory.Id(1));
        var second = LayoutWalker.Locate(tree.Root, TreeFactory.Id(2));

        Assert.NotNull(first);
        Assert.True(first.IsFirstChild);
        Assert.Equal(SplitAxis.Vertical, first.Axis);
        Assert.Equal(0.25d, first.Ratio, 10);
        Assert.Equal([TreeFactory.Id(2)], first.SiblingCandidates);

        Assert.NotNull(second);
        Assert.False(second.IsFirstChild);
        Assert.Equal([TreeFactory.Id(1)], second.SiblingCandidates);
    }

    [Fact]
    public void Locate_prefers_nearest_sibling_first()
    {
        var tree = new LayoutTree(TreeFactory.Split(
            SplitAxis.Horizontal,
            0.5,
            TreeFactory.Split(SplitAxis.Vertical, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2)),
            TreeFactory.Leaf(3)));

        var location = LayoutWalker.Locate(tree.Root, TreeFactory.Id(1));

        Assert.NotNull(location);
        Assert.Equal(TreeFactory.Id(2), location.SiblingCandidates[0]);
        Assert.Contains(TreeFactory.Id(3), location.SiblingCandidates);
    }

    [Fact]
    public void Hide_then_restore_brings_panel_back_to_original_position()
    {
        var tree = new LayoutTree(TreeFactory.Split(
            SplitAxis.Horizontal,
            0.5,
            TreeFactory.Split(SplitAxis.Vertical, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2)),
            TreeFactory.Leaf(3)));
        var target = TreeFactory.Id(2);

        var hint = RestoreHint.For(tree, target);
        var removed = LayoutOperations.Remove(tree, target);
        Assert.True(removed.IsSuccess);

        var restored = LayoutOperations.Restore(removed.Tree, target, hint);

        Assert.True(restored.IsSuccess);
        Assert.False(restored.UsedFallback);
        Assert.Equal([TreeFactory.Id(1), TreeFactory.Id(2), TreeFactory.Id(3)], LayoutWalker.Instances(restored.Tree.Root));
    }

    [Fact]
    public void RestoreHint_for_unknown_instance_is_empty()
    {
        var tree = TreeFactory.SingleLeaf(1);

        var hint = RestoreHint.For(tree, TreeFactory.Id(9));

        Assert.Null(hint.Sibling);
        Assert.Empty(hint.FallbackCandidates);
    }
}
