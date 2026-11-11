using UberDashboard.Core.Layout;
using UberDashboard.Core.Tests.TestSupport;

namespace UberDashboard.Core.Tests.Layout;

public sealed class PanelGridTests
{
    [Theory]
    [InlineData(GridEdge.Left, 2, 5, 4, 3, 6)]
    [InlineData(GridEdge.Top, -2, 3, 2, 5, 8)]
    [InlineData(GridEdge.Right, -9, 3, 4, 1, 6)]
    [InlineData(GridEdge.Bottom, 2, 3, 4, 5, 8)]
    public void ResizeEdge_keeps_opposite_edge(GridEdge edge, int delta, int x, int y, int width, int height)
    {
        Assert.Equal(new GridCell(x, y, width, height), PanelGrid.ResizeEdge(new GridCell(3, 4, 5, 6), edge, delta));
    }

    [Fact]
    public void Fits_rejects_overlap_and_out_of_bounds_but_ignores_self()
    {
        var placed = new Dictionary<PanelInstanceId, GridCell> { [TreeFactory.Id(1)] = new(0, 0, 4, 4), [TreeFactory.Id(2)] = new(6, 0, 2, 2) };
        Assert.True(PanelGrid.Fits(placed, TreeFactory.Id(1), new(1, 1, 4, 4), 12, 8));
        Assert.False(PanelGrid.Fits(placed, TreeFactory.Id(1), new(5, 0, 2, 2), 12, 8));
        Assert.False(PanelGrid.Fits(placed, TreeFactory.Id(3), new(11, 0, 2, 1), 12, 8));
        Assert.True(PanelGrid.Fits(placed, TreeFactory.Id(3), new(4, 0, 2, 8), 12, 8));
    }

    [Fact]
    public void FindFree_scans_rows_and_shrinks_when_full()
    {
        var placed = new Dictionary<PanelInstanceId, GridCell> { [TreeFactory.Id(1)] = new(0, 0, 3, 4) };
        Assert.Equal(new GridCell(3, 0, 1, 4), PanelGrid.FindFree(placed, 1, 4, 4, 4));
        Assert.Equal(new GridCell(3, 0, 1, 1), PanelGrid.FindFree(placed, 2, 2, 4, 4));
        placed[TreeFactory.Id(2)] = new(3, 0, 1, 4);
        Assert.Null(PanelGrid.FindFree(placed, 1, 1, 4, 4));
    }

    [Fact]
    public void FromTree_keeps_split_proportions_without_gaps()
    {
        var root = TreeFactory.Split(SplitAxis.Horizontal, .25, TreeFactory.Leaf(1),
            TreeFactory.Split(SplitAxis.Vertical, .5, TreeFactory.Leaf(2), TreeFactory.Leaf(3)));
        var cells = PanelGrid.FromTree(root, 24, 16);
        Assert.Equal(new GridCell(0, 0, 6, 16), cells[TreeFactory.Id(1)]);
        Assert.Equal(new GridCell(6, 0, 18, 8), cells[TreeFactory.Id(2)]);
        Assert.Equal(new GridCell(6, 8, 18, 8), cells[TreeFactory.Id(3)]);
    }

    [Fact]
    public void Rescale_follows_density_and_stays_inside()
    {
        Assert.Equal(new GridCell(6, 4, 12, 8), PanelGrid.Rescale(new(3, 2, 6, 4), 12, 8, 24, 16));
        Assert.Equal(new GridCell(3, 2, 1, 1), PanelGrid.Rescale(new(23, 15, 1, 1), 24, 16, 4, 3));
    }
}
