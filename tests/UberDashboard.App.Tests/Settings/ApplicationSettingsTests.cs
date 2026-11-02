using UberDashboard.App.Settings;

namespace UberDashboard.App.Tests.Settings;

public sealed class ApplicationSettingsTests
{
    [Fact]
    public void Normalize_clamps_visual_and_behavioral_ranges()
    {
        var settings = new ApplicationSettings
        {
            AccentColor = "1668c1",
            WindowBackgroundColor = "  #101820  ",
            SurfaceColor = "eff1f4",
            SurfaceAltColor = "1b1d21",
            FontFamilyName = "  Segoe UI  ",
            FontSize = 40d,
            CornerRadius = 40d,
            DensityScale = 0.2d,
            WeatherRefreshMinutes = 500,
            StartWithWindows = true,
            EnableTrayIcon = true,
            CloseBehavior = CloseBehavior.MinimizeToTray,
        }.Normalize();

        Assert.Equal("#1668C1", settings.AccentColor);
        Assert.Equal("#101820", settings.WindowBackgroundColor);
        Assert.Equal("#EFF1F4", settings.SurfaceColor);
        Assert.Equal("#1B1D21", settings.SurfaceAltColor);
        Assert.Equal("Segoe UI", settings.FontFamilyName);
        Assert.Equal(24d, settings.FontSize);
        Assert.Equal(28d, settings.CornerRadius);
        Assert.Equal(0.85d, settings.DensityScale);
        Assert.Equal(180, settings.WeatherRefreshMinutes);
        Assert.True(settings.StartWithWindows);
        Assert.True(settings.EnableTrayIcon);
        Assert.Equal(CloseBehavior.MinimizeToTray, settings.CloseBehavior);
    }

    [Fact]
    public void Default_prefers_full_exit_without_tray_dependencies()
    {
        var defaults = ApplicationSettings.Default.Normalize();

        Assert.False(defaults.StartWithWindows);
        Assert.True(defaults.EnableTrayIcon);
        Assert.Equal(CloseBehavior.ExitApplication, defaults.CloseBehavior);
    }
}
