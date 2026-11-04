using UberDashboard.App.Settings;
using UberDashboard.App.Theming;

namespace UberDashboard.App.Tests.Settings;

public sealed class AppearanceRegressionTests
{
    [Fact]
    public void Panel_text_override_round_trips_and_old_settings_default_to_theme()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<PanelAppearanceSettings>("{}");
        Assert.NotNull(old);
        Assert.False(old.OverrideTextColor);
        var settings = new PanelAppearanceSettings { OverrideTextColor = true, TextColor = "#FF2384A1" }.Normalize();
        var restored = System.Text.Json.JsonSerializer.Deserialize<PanelAppearanceSettings>(System.Text.Json.JsonSerializer.Serialize(settings));
        Assert.NotNull(restored);
        Assert.True(restored.OverrideTextColor);
        Assert.Equal("#FF2384A1", restored.TextColor);
    }

    [Theory]
    [InlineData("#FFFFFFFF", "#FF0F172A")]
    [InlineData("#FF202124", "#FFF2F3F5")]
    public void Custom_surface_chooses_readable_text(string background, string expected)
        => Assert.Equal(expected, ThemeService.ContrastingText(background));

    [Fact]
    public void Panel_appearance_normalizes_invalid_values()
    {
        var value = new PanelAppearanceSettings { BackgroundColor = "invalid", FontSize = double.NaN, WallpaperOpacity = 5 }.Normalize();
        Assert.Null(value.BackgroundColor);
        Assert.Equal(13, value.FontSize);
        Assert.Equal(1, value.WallpaperOpacity);
    }
    [Fact]
    public void Old_saved_light_defaults_do_not_cover_dark_theme_and_custom_colors_survive()
    {
        var legacy = new ApplicationSettings { SurfaceColor = "#FFFFFFFF", WindowBackgroundColor = "#FFF6F7F9", SurfaceAltColor = "#FFEFF1F4" };
        var migrated = ThemeService.MigratePalette(legacy, true);
        Assert.Null(migrated.SurfaceColor); Assert.Equal(1, migrated.PaletteVersion);
        Assert.Equal("#123456", ThemeService.MigratePalette(legacy with { SurfaceColor = "#123456" }, true).SurfaceColor);
        Assert.Equal(legacy with { PaletteVersion = 1 }, ThemeService.MigratePalette(legacy with { PaletteVersion = 1 }, true));
    }
}
