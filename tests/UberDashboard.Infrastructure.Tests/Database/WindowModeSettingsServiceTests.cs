using UberDashboard.Application.Platform;
using UberDashboard.Infrastructure.Settings;
using UberDashboard.Infrastructure.Tests.TestSupport;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class WindowModeSettingsServiceTests
{
    [Fact]
    public async Task Persists_and_restores_window_mode_settings()
    {
        await using var fixture = new DatabaseFixture();
        var service = new WindowModeSettingsService(fixture.Settings, fixture.Clock);
        var input = new WindowModeSettings
        {
            LaunchMode = WindowMode.DockedBar,
            MonitorId = "DISPLAY1",
            DockEdge = AppBarEdge.Left,
            DockThickness = 280,
            WidgetOpacity = 0.75d,
            DesktopWidgetFallbackToNormal = true,
        };

        await service.SetAsync(input);
        var restored = await service.GetAsync();

        Assert.Equal(input, restored);
    }

    [Fact]
    public async Task Returns_defaults_when_payload_is_invalid_json()
    {
        await using var fixture = new DatabaseFixture();
        await fixture.Settings.SetAsync("app.windowMode", "{bad-json", fixture.Clock.UtcNow);

        var service = new WindowModeSettingsService(fixture.Settings, fixture.Clock);
        var restored = await service.GetAsync();

        Assert.Equal(WindowModeSettings.Default, restored);
    }
}
