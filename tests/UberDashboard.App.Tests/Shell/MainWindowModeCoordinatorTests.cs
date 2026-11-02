using UberDashboard.Application.Platform;
using UberDashboard.App.Shell;
using UberDashboard.App.Tests.TestSupport;

namespace UberDashboard.App.Tests.Shell;

public sealed class MainWindowModeCoordinatorTests
{
    private static readonly DisplayMonitor PrimaryMonitor = new(
        "DISPLAY1",
        new PixelRect(0, 0, 1920, 1080),
        new PixelRect(0, 0, 1920, 1040),
        96,
        true);

    [Fact]
    public void Enter_docked_bar_registers_appbar_and_updates_current_mode()
    {
        var api = new FakeAppBarNativeApi();
        var appBar = new AppBarController(api);
        var coordinator = CreateCoordinator(appBar: appBar);

        var result = coordinator.EnterDockedBar(new IntPtr(0x1234), "DISPLAY1", AppBarEdge.Bottom, 56, PrimaryMonitor.WorkArea);

        Assert.True(result.IsSuccess);
        Assert.Equal(WindowMode.DockedBar, coordinator.CurrentMode);
    }

    [Fact]
    public async Task Switch_async_falls_back_to_normal_when_desktop_widget_attach_fails()
    {
        var coordinator = CreateCoordinator(
            settings: new WindowModeSettings
            {
                LaunchMode = WindowMode.DesktopWidget,
                DesktopWidgetFallbackToNormal = true,
            },
            desktopWidgetHost: new FakeDesktopWidgetWindowHost { TryAttachResult = false });
        coordinator.AttachWindow(new IntPtr(0x1234));

        var result = await coordinator.SwitchAsync(WindowMode.DesktopWidget);

        Assert.False(result.IsSuccess);
        Assert.Equal(WindowMode.Normal, result.ActiveMode);
        Assert.Equal(WindowMode.Normal, coordinator.CurrentMode);
    }

    [Fact]
    public async Task Initialize_async_applies_launch_mode_from_settings()
    {
        var api = new FakeAppBarNativeApi();
        var appBar = new AppBarController(api);
        var desktopWidgetHost = new FakeDesktopWidgetWindowHost();
        var coordinator = CreateCoordinator(
            appBar: appBar,
            desktopWidgetHost: desktopWidgetHost,
            settings: new WindowModeSettings
            {
                LaunchMode = WindowMode.DockedBar,
                MonitorId = "DISPLAY1",
                DockEdge = AppBarEdge.Bottom,
                DockThickness = 56,
            });
        coordinator.AttachWindow(new IntPtr(0x1234));

        var result = await coordinator.InitializeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(WindowMode.DockedBar, result.ActiveMode);
        Assert.Equal(["detach"], desktopWidgetHost.Calls);
        Assert.Equal(["register", "query", "set"], api.Calls);
    }

    private static MainWindowModeCoordinator CreateCoordinator(
        AppBarController? appBar = null,
        WindowModeSettings? settings = null,
        FakeDesktopWidgetWindowHost? desktopWidgetHost = null)
    {
        return new MainWindowModeCoordinator(
            appBar ?? new AppBarController(new FakeAppBarNativeApi()),
            new FakeMonitorProvider
            {
                Monitors = [PrimaryMonitor],
                WindowMonitor = PrimaryMonitor,
            },
            new FakeWindowModeSettingsService
            {
                Settings = settings ?? WindowModeSettings.Default,
            },
            desktopWidgetHost ?? new FakeDesktopWidgetWindowHost());
    }
}
