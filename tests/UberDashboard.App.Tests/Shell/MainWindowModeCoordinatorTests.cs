using UberDashboard.Application.Platform;
using UberDashboard.App.Shell;
using UberDashboard.App.Tests.TestSupport;

namespace UberDashboard.App.Tests.Shell;

public sealed class MainWindowModeCoordinatorTests
{
    [Fact]
    public async Task Successful_switch_remembers_mode_and_automatic_second_monitor_for_next_start()
    {
        var second = PrimaryMonitor with { Id = "DISPLAY2", Bounds = new PixelRect(-1920, 0, 0, 1080), IsPrimary = false };
        var settings = new FakeWindowModeSettingsService { Settings = new WindowModeSettings { LaunchMode = WindowMode.Normal } };
        var monitors = new FakeMonitorProvider { Monitors = [PrimaryMonitor, second], WindowMonitor = second };
        var coordinator = new MainWindowModeCoordinator(new AppBarController(new FakeAppBarNativeApi()), monitors,
            settings, new FakeDesktopWidgetWindowHost(), new FakeDockedBarWindowHost());
        coordinator.AttachWindow(new IntPtr(42));

        Assert.True((await coordinator.SwitchAsync(WindowMode.DockedBar)).IsSuccess);
        Assert.Equal(WindowMode.DockedBar, settings.Settings.LaunchMode);
        Assert.Equal("DISPLAY2", settings.Settings.MonitorId);

        var restoredHost = new FakeDockedBarWindowHost();
        var restarted = new MainWindowModeCoordinator(new AppBarController(new FakeAppBarNativeApi()),
            new FakeMonitorProvider { Monitors = [PrimaryMonitor, second], WindowMonitor = PrimaryMonitor },
            settings, new FakeDesktopWidgetWindowHost(), restoredHost);
        restarted.AttachWindow(new IntPtr(43));
        Assert.True((await restarted.InitializeAsync()).IsSuccess);
        Assert.Equal(-1920, restoredHost.AppliedBounds?.Left);
    }

    private static readonly DisplayMonitor PrimaryMonitor = new(
        "DISPLAY1",
        new PixelRect(0, 0, 1920, 1080),
        new PixelRect(0, 0, 1920, 1040),
        96,
        true);

    [Fact]
    public async Task Temporary_normal_mode_for_tray_preserves_saved_bar()
    {
        var settings = new FakeWindowModeSettingsService { Settings = new WindowModeSettings { MonitorId = "DISPLAY2" } };
        var coordinator = new MainWindowModeCoordinator(new AppBarController(new FakeAppBarNativeApi()),
            new FakeMonitorProvider { Monitors = [PrimaryMonitor], WindowMonitor = PrimaryMonitor }, settings,
            new FakeDesktopWidgetWindowHost(), new FakeDockedBarWindowHost());
        coordinator.AttachWindow(new IntPtr(42));
        Assert.True((await coordinator.SwitchTemporarilyAsync(WindowMode.Normal)).IsSuccess);
        Assert.Equal(WindowMode.DockedBar, settings.Settings.LaunchMode);
        Assert.Equal("DISPLAY2", settings.Settings.MonitorId);
    }

    [Fact]
    public async Task Startup_on_missing_monitor_uses_available_display_without_losing_preference()
    {
        var settings = new FakeWindowModeSettingsService { Settings = new WindowModeSettings { MonitorId = "DISPLAY2" } };
        var host = new FakeDockedBarWindowHost();
        var coordinator = new MainWindowModeCoordinator(new AppBarController(new FakeAppBarNativeApi()),
            new FakeMonitorProvider { Monitors = [PrimaryMonitor], WindowMonitor = PrimaryMonitor }, settings,
            new FakeDesktopWidgetWindowHost(), host);
        coordinator.AttachWindow(new IntPtr(42));
        Assert.True((await coordinator.InitializeAsync()).IsSuccess);
        Assert.Equal(0, host.AppliedBounds?.Left);
        Assert.Equal("DISPLAY2", settings.Settings.MonitorId);
    }

    [Fact]
    public async Task Failed_startup_preserves_saved_bar_for_next_start()
    {
        var settings = new FakeWindowModeSettingsService { Settings = new WindowModeSettings { MonitorId = "DISPLAY2" } };
        var coordinator = new MainWindowModeCoordinator(new AppBarController(new FakeAppBarNativeApi()),
            new FakeMonitorProvider { Monitors = [PrimaryMonitor], WindowMonitor = PrimaryMonitor }, settings,
            new FakeDesktopWidgetWindowHost(), new FakeDockedBarWindowHost { ApplyResult = false });
        coordinator.AttachWindow(new IntPtr(42));
        Assert.False((await coordinator.InitializeAsync()).IsSuccess);
        Assert.Equal(WindowMode.DockedBar, settings.Settings.LaunchMode);
        Assert.Equal("DISPLAY2", settings.Settings.MonitorId);
    }

    [Fact]
    public async Task Explicit_normal_mode_is_remembered()
    {
        var settings = new FakeWindowModeSettingsService();
        var coordinator = new MainWindowModeCoordinator(new AppBarController(new FakeAppBarNativeApi()),
            new FakeMonitorProvider { Monitors = [PrimaryMonitor], WindowMonitor = PrimaryMonitor }, settings,
            new FakeDesktopWidgetWindowHost(), new FakeDockedBarWindowHost());
        Assert.True((await coordinator.SwitchAsync(WindowMode.Normal)).IsSuccess);
        Assert.Equal(WindowMode.Normal, settings.Settings.LaunchMode);
    }

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

    [Fact]
    public async Task Docking_applies_exact_rect_returned_by_shell()
    {
        var native = new FakeAppBarNativeApi { SetTransform = (_, _) => new PixelRect(-1280, 500, 0, 740) };
        var window = new FakeDockedBarWindowHost();
        var coordinator = CreateCoordinator(appBar: new AppBarController(native), dockedWindow: window);
        coordinator.AttachWindow(new IntPtr(42));
        var result = await coordinator.SwitchAsync(WindowMode.DockedBar);
        Assert.True(result.IsSuccess);
        Assert.Equal(new PixelRect(-1280, 500, 0, 740), window.AppliedBounds);
    }

    [Fact]
    public async Task Failed_window_position_releases_reservation_and_falls_back()
    {
        var native = new FakeAppBarNativeApi();
        var appBar = new AppBarController(native);
        var coordinator = CreateCoordinator(appBar: appBar, dockedWindow: new FakeDockedBarWindowHost { ApplyResult = false });
        coordinator.AttachWindow(new IntPtr(42));
        var result = await coordinator.SwitchAsync(WindowMode.DockedBar);
        Assert.False(result.IsSuccess);
        Assert.Equal(WindowMode.Normal, coordinator.CurrentMode);
        Assert.False(appBar.IsRegistered);
        Assert.Contains("unregister", native.Calls);
    }

    [Fact]
    public async Task Desktop_host_does_not_claim_a_fullscreen_window_is_a_desktop_layer()
    {
        Assert.False(await new DesktopWidgetWindowHost().TryAttachAsync(new IntPtr(42), 1, CancellationToken.None));
    }
    private static MainWindowModeCoordinator CreateCoordinator(
        AppBarController? appBar = null,
        WindowModeSettings? settings = null,
        FakeDesktopWidgetWindowHost? desktopWidgetHost = null, FakeDockedBarWindowHost? dockedWindow = null)
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
            desktopWidgetHost ?? new FakeDesktopWidgetWindowHost(),
            dockedWindow ?? new FakeDockedBarWindowHost());
    }
}
