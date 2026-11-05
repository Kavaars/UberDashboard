using UberDashboard.Application.Platform;

namespace UberDashboard.Application.Tests.Platform;

public sealed class WindowPlacementTests
{
    [Theory]
    [InlineData(-1800, 100, -800, 800, -1800, 100, -800, 800)]
    [InlineData(500, 100, 1500, 800, -1000, 100, 0, 800)]
    [InlineData(-3000, -500, 1000, 2000, -1920, 0, 0, 1040)]
    public void Restores_negative_coordinates_and_keeps_offscreen_windows_visible(
        int left, int top, int right, int bottom, int expectedLeft, int expectedTop, int expectedRight, int expectedBottom)
    {
        var monitor = new DisplayMonitor("second", new PixelRect(-1920, 0, 0, 1080), new PixelRect(-1920, 0, 0, 1040), 144, false);
        var placement = new WindowPlacement("second", new PixelRect(left, top, right, bottom), false);
        Assert.Equal(new PixelRect(expectedLeft, expectedTop, expectedRight, expectedBottom), placement.FitTo(monitor));
    }
}
