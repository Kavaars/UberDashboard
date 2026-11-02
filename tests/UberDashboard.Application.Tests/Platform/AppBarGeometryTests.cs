using UberDashboard.Application.Platform;

namespace UberDashboard.Application.Tests.Platform;

public sealed class AppBarGeometryTests
{
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(AppBarEdge.Left, 0, 0, 40, 1080)]
    [InlineData(AppBarEdge.Right, 1880, 0, 1920, 1080)]
    [InlineData(AppBarEdge.Top, 0, 0, 1920, 40)]
    [InlineData(AppBarEdge.Bottom, 0, 1040, 1920, 1080)]
    public void RequestedRect_places_bar_on_chosen_edge(AppBarEdge edge, int left, int top, int right, int bottom)
    {
        var rect = AppBarGeometry.RequestedRect(WorkArea, edge, 40);

        Assert.Equal(new PixelRect(left, top, right, bottom), rect);
    }

    [Fact]
    public void RequestedRect_respects_negative_virtual_screen_coordinates()
    {
        var workArea = new PixelRect(-1920, 0, 0, 1080);

        var rect = AppBarGeometry.RequestedRect(workArea, AppBarEdge.Top, 40);

        Assert.Equal(new PixelRect(-1920, 0, 0, 40), rect);
    }

    [Fact]
    public void RequestedRect_clamps_thickness_to_work_area()
    {
        var rect = AppBarGeometry.RequestedRect(WorkArea, AppBarEdge.Left, 5000);

        Assert.Equal(new PixelRect(0, 0, 1920, 1080), rect);
    }

    [Fact]
    public void RequestedRect_zero_thickness_yields_empty_rect()
    {
        var rect = AppBarGeometry.RequestedRect(WorkArea, AppBarEdge.Top, 0);

        Assert.False(rect.IsValid);
    }

    [Theory]
    [InlineData(AppBarEdge.Left, 10, 0, 1920, 1080, 10, 0, 50, 1080)]
    [InlineData(AppBarEdge.Right, 1900, 0, 1920, 1080, 1880, 0, 1920, 1080)]
    [InlineData(AppBarEdge.Top, 0, 0, 1920, 10, 0, 0, 1920, 40)]
    [InlineData(AppBarEdge.Bottom, 0, 1070, 1920, 1080, 0, 1040, 1920, 1080)]
    public void RefitThickness_restores_requested_thickness_at_queried_position(
        AppBarEdge edge, int ql, int qt, int qr, int qb, int el, int et, int er, int eb)
    {
        var queried = new PixelRect(ql, qt, qr, qb);

        var rect = AppBarGeometry.RefitThickness(queried, edge, 40);

        Assert.Equal(new PixelRect(el, et, er, eb), rect);
    }

    [Fact]
    public void IsUsable_rejects_zero_sized_rect()
    {
        Assert.False(AppBarGeometry.IsUsable(PixelRect.Empty));
        Assert.True(AppBarGeometry.IsUsable(WorkArea));
    }
}
