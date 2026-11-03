using UberDashboard.App.Settings;

namespace UberDashboard.App.Tests.Settings;

public sealed class ApplicationSettingsTests
{
    [Fact]
    public void Background_settings_are_clamped_and_old_defaults_remain_opaque()
    {
        var defaults = ApplicationSettings.Default.Normalize();
        Assert.Equal(1, defaults.WindowOpacity);
        Assert.Equal(0, defaults.WallpaperBlur);
        Assert.False(defaults.TransparentDesktopBackground);
        var settings = new ApplicationSettings { WindowOpacity = -1, WallpaperBlur = 100, BackgroundSurfaceOpacity = double.NaN }.Normalize();
        Assert.Equal(0.2, settings.WindowOpacity);
        Assert.Equal(30, settings.WallpaperBlur);
        Assert.Equal(0.7, settings.BackgroundSurfaceOpacity);
    }
    [Fact]
    public void Desktop_transparency_round_trips_and_does_not_change_text_or_window_opacity()
    {
        var settings = new ApplicationSettings { TransparentDesktopBackground = true, WindowOpacity = 1, BackgroundSurfaceOpacity = 0.4 }.Normalize();
        var restored = System.Text.Json.JsonSerializer.Deserialize<ApplicationSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!.Normalize();
        Assert.True(restored.TransparentDesktopBackground);
        Assert.Equal(1, restored.WindowOpacity);
        Assert.Equal(0.4, restored.BackgroundSurfaceOpacity);
    }
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
    [Theory]
    [InlineData("not-a-color")]
    [InlineData("#GG0011")]
    [InlineData("#12")]
    public void Invalid_colors_fall_back_without_throwing(string color)
    {
        var settings = new ApplicationSettings { AccentColor = color, FontSize = double.NaN, CornerRadius = double.PositiveInfinity, DensityScale = double.NegativeInfinity }.Normalize();
        Assert.Null(settings.AccentColor);
        Assert.Equal(13, settings.FontSize);
        Assert.Equal(12, settings.CornerRadius);
        Assert.Equal(1, settings.DensityScale);
    }
}
