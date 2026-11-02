using UberDashboard.Core.Layout;
using UberDashboard.Core.Tests.TestSupport;

namespace UberDashboard.Core.Tests.Layout;

public sealed class LayoutOperationsTests
{
    private static readonly LayoutMetrics Metrics = LayoutMetrics.Default;

    [Fact]
    public void Split_inserts_new_leaf_next_to_target()
    {
        var tree = TreeFactory.SingleLeaf(1);

        var result = LayoutOperations.Split(tree, TreeFactory.Id(1), SplitAxis.Horizontal, TreeFactory.Id(2), InsertSide.After);

        Assert.True(result.IsSuccess);
        var instances = LayoutWalker.Instances(result.Tree.Root);
        Assert.Equal([TreeFactory.Id(1), TreeFactory.Id(2)], instances);
    }

    [Fact]
    public void Split_before_puts_new_leaf_first()
    {
        var tree = TreeFactory.SingleLeaf(1);

        var result = LayoutOperations.Split(tree, TreeFactory.Id(1), SplitAxis.Vertical, TreeFactory.Id(2), InsertSide.Before);

        Assert.True(result.IsSuccess);
        Assert.Equal([TreeFactory.Id(2), TreeFactory.Id(1)], LayoutWalker.Instances(result.Tree.Root));
    }

    [Fact]
    public void Split_on_empty_tree_fails_with_missing_root()
    {
        var result = LayoutOperations.Split(LayoutTree.Empty, TreeFactory.Id(1), SplitAxis.Horizontal, TreeFactory.Id(2), InsertSide.After);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.MissingRoot, result.Error);
    }

    [Fact]
    public void Split_unknown_target_fails_with_leaf_not_found()
    {
        var tree = TreeFactory.SingleLeaf(1);

        var result = LayoutOperations.Split(tree, TreeFactory.Id(9), SplitAxis.Horizontal, TreeFactory.Id(2), InsertSide.After);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.LeafNotFound, result.Error);
    }

    [Fact]
    public void Split_rejects_instance_already_present()
    {
        var tree = new LayoutTree(TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2)));

        var result = LayoutOperations.Split(tree, TreeFactory.Id(1), SplitAxis.Horizontal, TreeFactory.Id(2), InsertSide.After);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.DuplicateInstance, result.Error);
    }

    [Fact]
    public void Move_rebuilds_split_in_requested_orientation()
    {
        var tree = new LayoutTree(TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2)));

        var result = LayoutOperations.Move(tree, TreeFactory.Id(1), TreeFactory.Id(2), DropZone.Top);

        Assert.True(result.IsSuccess);
        var root = Assert.IsType<SplitNode>(result.Tree.Root);
        Assert.Equal(SplitAxis.Vertical, root.Axis);
        Assert.Equal([TreeFactory.Id(1), TreeFactory.Id(2)], LayoutWalker.Instances(result.Tree.Root));
    }

    [Fact]
    public void Move_onto_self_is_rejected()
    {
        var tree = TreeFactory.SingleLeaf(1);

        var result = LayoutOperations.Move(tree, TreeFactory.Id(1), TreeFactory.Id(1), DropZone.Right);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.SelfMove, result.Error);
    }

    [Fact]
    public void Move_unknown_instance_is_rejected()
    {
        var tree = new LayoutTree(TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2)));

        var result = LayoutOperations.Move(tree, TreeFactory.Id(7), TreeFactory.Id(2), DropZone.Right);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.LeafNotFound, result.Error);
    }

    [Fact]
    public void Swap_exchanges_positions_without_moving_instance_data()
    {
        var tree = new LayoutTree(TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2)));

        var result = LayoutOperations.Swap(tree, TreeFactory.Id(1), TreeFactory.Id(2));

        Assert.True(result.IsSuccess);
        Assert.Equal([TreeFactory.Id(2), TreeFactory.Id(1)], LayoutWalker.Instances(result.Tree.Root));
    }

    [Fact]
    public void Swap_requires_two_distinct_instances()
    {
        var tree = TreeFactory.SingleLeaf(1);

        var result = LayoutOperations.Swap(tree, TreeFactory.Id(1), TreeFactory.Id(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.SelfMove, result.Error);
    }

    [Fact]
    public void Swap_fails_when_one_instance_is_absent()
    {
        var tree = new LayoutTree(TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2)));

        var result = LayoutOperations.Swap(tree, TreeFactory.Id(1), TreeFactory.Id(5));

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.LeafNotFound, result.Error);
    }

    [Fact]
    public void Replace_substitutes_leaf_in_place()
    {
        var tree = new LayoutTree(TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2)));

        var result = LayoutOperations.Replace(tree, TreeFactory.Id(1), TreeFactory.Id(3));

        Assert.True(result.IsSuccess);
        Assert.Equal([TreeFactory.Id(3), TreeFactory.Id(2)], LayoutWalker.Instances(result.Tree.Root));
    }

    [Fact]
    public void Replace_rejects_replacement_already_in_tree()
    {
        var tree = new LayoutTree(TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2)));

        var result = LayoutOperations.Replace(tree, TreeFactory.Id(1), TreeFactory.Id(2));

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.DuplicateInstance, result.Error);
    }

    [Fact]
    public void Remove_compacts_single_child_split()
    {
        var tree = new LayoutTree(TreeFactory.Split(
            SplitAxis.Horizontal,
            0.5,
            TreeFactory.Split(SplitAxis.Vertical, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2)),
            TreeFactory.Leaf(3)));

        var result = LayoutOperations.Remove(tree, TreeFactory.Id(1));

        Assert.True(result.IsSuccess);
        var root = Assert.IsType<SplitNode>(result.Tree.Root);
        Assert.Equal(SplitAxis.Horizontal, root.Axis);
        Assert.Equal([TreeFactory.Id(2), TreeFactory.Id(3)], LayoutWalker.Instances(result.Tree.Root));
    }

    [Fact]
    public void Remove_last_leaf_leaves_empty_tree()
    {
        var tree = TreeFactory.SingleLeaf(1);

        var result = LayoutOperations.Remove(tree, TreeFactory.Id(1));

        Assert.True(result.IsSuccess);
        Assert.True(result.Tree.IsEmpty);
    }

    [Fact]
    public void Remove_unknown_instance_is_rejected()
    {
        var tree = TreeFactory.SingleLeaf(1);

        var result = LayoutOperations.Remove(tree, TreeFactory.Id(4));

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.LeafNotFound, result.Error);
    }

    [Fact]
    public void ToggleAxis_flips_axis_and_keeps_ratio()
    {
        var split = TreeFactory.Split(SplitAxis.Horizontal, 0.3, TreeFactory.Leaf(1), TreeFactory.Leaf(2));
        var tree = new LayoutTree(split);

        var result = LayoutOperations.ToggleAxis(tree, split.SplitId);

        Assert.True(result.IsSuccess);
        var toggled = Assert.IsType<SplitNode>(result.Tree.Root);
        Assert.Equal(SplitAxis.Vertical, toggled.Axis);
        Assert.Equal(0.3, toggled.Ratio, 10);
        Assert.Equal([TreeFactory.Id(1), TreeFactory.Id(2)], LayoutWalker.Instances(result.Tree.Root));
    }

    [Fact]
    public void ToggleAxis_unknown_split_is_rejected()
    {
        var tree = TreeFactory.SingleLeaf(1);

        var result = LayoutOperations.ToggleAxis(tree, Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.SplitNotFound, result.Error);
    }

    [Fact]
    public void Resize_clamps_ratio_to_minimum_of_first_child()
    {
        var split = TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2));
        var tree = new LayoutTree(split);
        var minSizes = TreeFactory.Registry(
            (1, new MinSize(100d, 0d)),
            (2, new MinSize(100d, 0d)));

        var result = LayoutOperations.Resize(tree, split.SplitId, 0.1d, 400d, minSizes, Metrics);

        Assert.True(result.IsSuccess);
        var resized = Assert.IsType<SplitNode>(result.Tree.Root);
        Assert.Equal(100d / 394d, resized.Ratio, 10);
    }

    [Fact]
    public void Resize_is_rejected_when_area_is_smaller_than_sum_of_minima()
    {
        var split = TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2));
        var tree = new LayoutTree(split);
        var minSizes = TreeFactory.Registry(
            (1, new MinSize(100d, 0d)),
            (2, new MinSize(100d, 0d)));

        var result = LayoutOperations.Resize(tree, split.SplitId, 0.5d, 200d, minSizes, Metrics);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.InvalidRatio, result.Error);
        Assert.Same(tree, result.Tree);
    }

    [Fact]
    public void Resize_reports_insufficient_space_when_region_is_smaller_than_splitter()
    {
        var split = TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2));
        var tree = new LayoutTree(split);

        var result = LayoutOperations.Resize(tree, split.SplitId, 0.5d, 4d, TreeFactory.Uniform(MinSize.Zero), Metrics);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.InsufficientSpace, result.Error);
    }

    [Fact]
    public void Resize_rejects_non_finite_ratio()
    {
        var split = TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2));
        var tree = new LayoutTree(split);

        var result = LayoutOperations.Resize(tree, split.SplitId, double.NaN, 400d, TreeFactory.Uniform(MinSize.Zero), Metrics);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.InvalidRatio, result.Error);
    }

    [Fact]
    public void Restore_into_empty_tree_creates_root_leaf()
    {
        var result = LayoutOperations.Restore(LayoutTree.Empty, TreeFactory.Id(1), RestoreHint.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([TreeFactory.Id(1)], LayoutWalker.Instances(result.Tree.Root));
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public void Restore_places_panel_next_to_remembered_sibling()
    {
        var tree = new LayoutTree(TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2)));
        var hint = new RestoreHint(TreeFactory.Id(2), SplitAxis.Horizontal, false, 0.5d, []);

        var result = LayoutOperations.Restore(tree, TreeFactory.Id(3), hint);

        Assert.True(result.IsSuccess);
        Assert.False(result.UsedFallback);
        Assert.Equal([TreeFactory.Id(1), TreeFactory.Id(2), TreeFactory.Id(3)], LayoutWalker.Instances(result.Tree.Root));
    }

    [Fact]
    public void Restore_uses_fallback_candidate_when_sibling_is_gone()
    {
        var tree = new LayoutTree(TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2)));
        var hint = new RestoreHint(null, SplitAxis.Horizontal, true, 0.5d, [TreeFactory.Id(1)]);

        var result = LayoutOperations.Restore(tree, TreeFactory.Id(3), hint);

        Assert.True(result.IsSuccess);
        Assert.True(result.UsedFallback);
        Assert.Contains(TreeFactory.Id(3), LayoutWalker.Instances(result.Tree.Root));
    }

    [Fact]
    public void Restore_appends_to_first_leaf_when_no_hint_matches()
    {
        var tree = new LayoutTree(TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2)));

        var result = LayoutOperations.Restore(tree, TreeFactory.Id(3), RestoreHint.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.UsedFallback);
        Assert.Equal(3, result.Tree.LeafCount);
    }

    [Fact]
    public void Restore_rejects_instance_already_visible()
    {
        var tree = TreeFactory.SingleLeaf(1);

        var result = LayoutOperations.Restore(tree, TreeFactory.Id(1), RestoreHint.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.DuplicateInstance, result.Error);
    }

    [Fact]
    public void Failed_operation_returns_unchanged_tree_instance()
    {
        var tree = TreeFactory.SingleLeaf(1);

        var result = LayoutOperations.Remove(tree, TreeFactory.Id(9));

        Assert.Same(tree, result.Tree);
    }
}
