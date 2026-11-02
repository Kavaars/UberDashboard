using UberDashboard.Core.Layout;
using UberDashboard.Core.Tests.TestSupport;

namespace UberDashboard.Core.Tests.Layout;

public sealed class MinimumSizesTests
{
    private static readonly LayoutMetrics Metrics = LayoutMetrics.Default;

    [Fact]
    public void Leaf_minimum_comes_from_source()
    {
        var minSizes = TreeFactory.Registry((1, new MinSize(120d, 80d)));

        var minimum = MinimumSizes.Of(TreeFactory.Leaf(1), minSizes, Metrics);

        Assert.Equal(new MinSize(120d, 80d), minimum);
    }

    [Fact]
    public void Horizontal_split_sums_widths_and_takes_max_height()
    {
        var minSizes = TreeFactory.Registry(
            (1, new MinSize(100d, 80d)),
            (2, new MinSize(150d, 60d)));
        var node = TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2));

        var minimum = MinimumSizes.Of(node, minSizes, Metrics);

        Assert.Equal(new MinSize(100d + Metrics.SplitterThickness + 150d, 80d), minimum);
    }

    [Fact]
    public void Vertical_split_sums_heights_and_takes_max_width()
    {
        var minSizes = TreeFactory.Registry(
            (1, new MinSize(100d, 80d)),
            (2, new MinSize(150d, 60d)));
        var node = TreeFactory.Split(SplitAxis.Vertical, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2));

        var minimum = MinimumSizes.Of(node, minSizes, Metrics);

        Assert.Equal(new MinSize(150d, 80d + Metrics.SplitterThickness + 60d), minimum);
    }

    [Fact]
    public void Empty_tree_has_zero_minimum()
    {
        Assert.Equal(MinSize.Zero, MinimumSizes.Of(LayoutTree.Empty, TreeFactory.Uniform(new MinSize(10d, 10d)), Metrics));
    }
}
