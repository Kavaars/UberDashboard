using Microsoft.Extensions.DependencyInjection;
using UberDashboard.Application.Platform;
using UberDashboard.App.Hosting;
using UberDashboard.App.Settings;
using UberDashboard.App.Shell;
using UberDashboard.App.Tests.TestSupport;
using UberDashboard.Application.Abstractions;

namespace UberDashboard.App.Tests.Hosting;

public sealed class AppServiceCollectionExtensionsTests
{
    [Fact]
    public async Task Add_uber_dashboard_app_registers_window_mode_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMonitorProvider>(new FakeMonitorProvider
        {
            Monitors =
            [
                new DisplayMonitor("DISPLAY1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 96, true),
            ],
            WindowMonitor = new DisplayMonitor("DISPLAY1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 96, true),
        });
        services.AddSingleton<IAppBarNativeApi, FakeAppBarNativeApi>();
        services.AddSingleton<AppBarController>();
        services.AddSingleton<IWindowModeSettingsService>(new FakeWindowModeSettingsService());
        services.AddSingleton<IAppSettingsStore>(new FakeAppSettingsStore());
        services.AddSingleton<IClock>(new FakeClock());

        services.AddUberDashboardApp();

        await using var provider = services.BuildServiceProvider();

        Assert.IsType<MainWindowModeCoordinator>(provider.GetRequiredService<IWindowModeCoordinator>());
        Assert.IsType<DesktopWidgetWindowHost>(provider.GetRequiredService<IDesktopWidgetWindowHost>());
        Assert.IsType<ApplicationSettingsService>(provider.GetRequiredService<IApplicationSettingsService>());
        Assert.NotNull(provider.GetRequiredService<Func<SettingsWindow>>());
    }
}
