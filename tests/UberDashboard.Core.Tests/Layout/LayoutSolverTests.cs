using UberDashboard.Core.Layout;
using UberDashboard.Core.Tests.TestSupport;

namespace UberDashboard.Core.Tests.Layout;

public sealed class LayoutSolverTests
{
    [Fact]
    public void Solve_returns_empty_solution_for_empty_tree()
    {
        var solution = LayoutSolver.Solve(LayoutTree.Empty, new LayoutRect(0d, 0d, 400d, 200d), TreeFactory.Uniform(MinSize.Zero), LayoutMetrics.Default);

        Assert.Empty(solution.Placements);
        Assert.False(solution.IsOverflowing);
    }

    [Fact]
    public void Solve_places_single_leaf_across_whole_area()
    {
        var tree = TreeFactory.SingleLeaf(1);
        var area = new LayoutRect(10d, 20d, 300d, 150d);

        var solution = LayoutSolver.Solve(tree, area, TreeFactory.Uniform(MinSize.Zero), LayoutMetrics.Default);

        var placement = Assert.Single(solution.Placements);
        Assert.Equal(area, placement.Bounds);
        Assert.False(solution.IsOverflowing);
    }

    [Fact]
    public void Solve_splits_area_horizontally_with_splitter_gap()
    {
        var tree = new LayoutTree(TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2)));

        var solution = LayoutSolver.Solve(tree, new LayoutRect(0d, 0d, 400d, 200d), TreeFactory.Uniform(MinSize.Zero), LayoutMetrics.Default);

        Assert.Equal(new LayoutRect(0d, 0d, 197d, 200d), solution.BoundsOf(TreeFactory.Id(1)));
        Assert.Equal(new LayoutRect(203d, 0d, 197d, 200d), solution.BoundsOf(TreeFactory.Id(2)));
        Assert.False(solution.IsOverflowing);
    }

    [Fact]
    public void Solve_reports_overflow_when_area_is_below_sum_of_minima()
    {
        var tree = new LayoutTree(TreeFactory.Split(SplitAxis.Horizontal, 0.5, TreeFactory.Leaf(1), TreeFactory.Leaf(2)));
        var minSizes = TreeFactory.Registry(
            (1, new MinSize(100d, 0d)),
            (2, new MinSize(100d, 0d)));

        var solution = LayoutSolver.Solve(tree, new LayoutRect(0d, 0d, 50d, 200d), minSizes, LayoutMetrics.Default);

        Assert.True(solution.IsOverflowing);
        Assert.Equal(2, solution.Placements.Count);
    }

    [Fact]
    public void Solve_reports_overflow_when_leaf_is_smaller_than_its_minimum()
    {
        var tree = TreeFactory.SingleLeaf(1);
        var minSizes = TreeFactory.Registry((1, new MinSize(500d, 400d)));

        var solution = LayoutSolver.Solve(tree, new LayoutRect(0d, 0d, 100d, 100d), minSizes, LayoutMetrics.Default);

        Assert.True(solution.IsOverflowing);
    }

    [Theory]
    [InlineData(0.5d, 100d, 100d, 394d, 0.5d)]
    [InlineData(0.1d, 100d, 100d, 394d, 100d / 394d)]
    [InlineData(0.99d, 100d, 100d, 394d, 294d / 394d)]
    public void TryClampRatio_respects_minimum_bounds(double ratio, double minFirst, double minSecond, double available, double expected)
    {
        var clamped = LayoutSolver.TryClampRatio(ratio, minFirst, minSecond, available);

        Assert.NotNull(clamped);
        Assert.Equal(expected, clamped.Value, 10);
    }

    [Theory]
    [InlineData(0.5d, 100d, 100d, 100d)]
    [InlineData(0.5d, 0d, 0d, 0d)]
    [InlineData(0.5d, 0d, 0d, -5d)]
    [InlineData(double.NaN, 0d, 0d, 394d)]
    [InlineData(double.PositiveInfinity, 0d, 0d, 394d)]
    public void TryClampRatio_returns_null_when_area_cannot_fit_minima(double ratio, double minFirst, double minSecond, double available)
    {
        Assert.Null(LayoutSolver.TryClampRatio(ratio, minFirst, minSecond, available));
    }
}
