using Microsoft.Extensions.Logging.Abstractions;
using UberDashboard.App.Settings;
using UberDashboard.App.Tests.TestSupport;
using UberDashboard.App.Theming;

namespace UberDashboard.App.Tests.Settings;

public sealed class ToolbarThemeTests
{
    [Fact]
    public async Task Explicit_theme_selection_resets_palette_like_settings_and_preserves_other_preferences()
    {
        var store = new FakeAppSettingsStore();
        var clock = new FakeClock();
        var settings = new ApplicationSettingsService(store, clock);
        await settings.SaveAsync(ApplicationSettings.Default with
        {
            SurfaceColor = "#FFFFFFFF", SurfaceAltColor = "#FFEFF1F4",
            WindowBackgroundColor = "#FFF6F7F9", AccentColor = "#FF123456", FontSize = 19,
        }, CancellationToken.None);
        using var theme = new ThemeService(store, settings, clock, NullLogger<ThemeService>.Instance);
        await theme.ApplyAsync(ThemeKind.Dark, CancellationToken.None);
        var saved = await settings.LoadAsync();
        Assert.Null(saved.SurfaceColor);
        Assert.Null(saved.SurfaceAltColor);
        Assert.Null(saved.WindowBackgroundColor);
        Assert.Null(saved.AccentColor);
        Assert.Equal(19, saved.FontSize);
        Assert.Equal(ThemeKind.Dark, theme.Current);
    }
}
