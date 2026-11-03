using UberDashboard.App.Controls;
using UberDashboard.Core.Layout;
namespace UberDashboard.App.Tests.Shell;

public sealed class PanelDropZoneTests
{
    [Theory]
    [InlineData(1, 100, DropZone.Left)]
    [InlineData(399, 100, DropZone.Right)]
    [InlineData(200, 1, DropZone.Top)]
    [InlineData(200, 199, DropZone.Bottom)]
    public void Nearest_edge_selects_expected_axis(double x, double y, DropZone expected) =>
        Assert.Equal(expected, PanelDragDrop.ZoneAt(x, y, 400, 200));
}
