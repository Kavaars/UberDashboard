using UberDashboard.Application.Platform;

namespace UberDashboard.Application.Tests.Platform;

public sealed class WindowModeSettingsContractTests
{
    [Fact]
    public void Defaults_use_normal_mode_and_safe_values()
    {
        var settings = WindowModeSettings.Default;

        Assert.Equal(WindowMode.Normal, settings.LaunchMode);
        Assert.Equal(AppBarEdge.Bottom, settings.DockEdge);
        Assert.InRange(settings.DockThickness, 200, 1200);
        Assert.InRange(settings.WidgetOpacity, 0.2d, 1.0d);
        Assert.True(settings.DesktopWidgetFallbackToNormal);
    }

    [Fact]
    public void Normalize_clamps_dock_thickness_to_user_range()
    {
        var tooSmall = new WindowModeSettings { DockThickness = 80 }.Normalize();
        var tooLarge = new WindowModeSettings { DockThickness = 2400 }.Normalize();

        Assert.Equal(200, tooSmall.DockThickness);
        Assert.Equal(1200, tooLarge.DockThickness);
    }
}
