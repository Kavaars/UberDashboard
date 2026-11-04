using UberDashboard.Application.Platform;
using UberDashboard.Application.Tests.TestSupport;

namespace UberDashboard.Application.Tests.Platform;

public sealed class AppBarControllerTests
{
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1080);
    private static readonly IntPtr WindowHandle = new(0x1234);

    [Fact]
    public void Resize_keeps_registration_and_requested_monitor_and_edge()
    {
        var api = new FakeAppBarNativeApi();
        var controller = new AppBarController(api);
        var monitor = new PixelRect(-1920, 0, 0, 1080);
        Assert.True(controller.Register(WindowHandle, AppBarEdge.Bottom, 280, monitor).IsSuccess);
        var result = controller.UpdateConfiguration(AppBarEdge.Bottom, 400, monitor);
        Assert.True(result.IsSuccess);
        Assert.Equal(new PixelRect(-1920, 680, 0, 1080), result.Rect);
        Assert.Equal(["register", "query", "set", "query", "set"], api.Calls);
    }

    [Fact]
    public void Register_runs_dock_cycle_in_order()
    {
        var api = new FakeAppBarNativeApi();
        var controller = new AppBarController(api);

        var result = controller.Register(WindowHandle, AppBarEdge.Top, 40, WorkArea);

        Assert.True(result.IsSuccess);
        Assert.True(controller.IsRegistered);
        Assert.Equal(["register", "query", "set"], api.Calls);
        Assert.Equal(new PixelRect(0, 0, 1920, 40), result.Rect);
    }

    [Fact]
    public void Register_applies_shell_rect_but_keeps_requested_thickness()
    {
        var api = new FakeAppBarNativeApi
        {
            QueryTransform = static (_, rect) => rect with { Bottom = rect.Top + 10 },
            SetTransform = static (_, rect) => rect with { Bottom = rect.Top + 10 },
        };
        var controller = new AppBarController(api);

        controller.Register(WindowHandle, AppBarEdge.Top, 40, WorkArea);

        Assert.Equal(new PixelRect(0, 0, 1920, 40), api.LastSet);
    }

    [Fact]
    public void Register_returns_binding_rect_from_setpos()
    {
        var api = new FakeAppBarNativeApi
        {
            SetTransform = static (_, rect) => rect with { Right = rect.Right - 100 },
        };
        var controller = new AppBarController(api);

        var result = controller.Register(WindowHandle, AppBarEdge.Top, 40, WorkArea);

        Assert.True(result.IsSuccess);
        Assert.Equal(new PixelRect(0, 0, 1820, 40), result.Rect);
    }

    [Fact]
    public void Register_fails_when_shell_declines()
    {
        var api = new FakeAppBarNativeApi { RegisterResult = false };
        var controller = new AppBarController(api);

        var result = controller.Register(WindowHandle, AppBarEdge.Top, 40, WorkArea);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Message);
        Assert.False(controller.IsRegistered);
        Assert.Equal(["register"], api.Calls);
    }

    [Fact]
    public void Register_rejects_invalid_window_handle()
    {
        var api = new FakeAppBarNativeApi();
        var controller = new AppBarController(api);

        var result = controller.Register(IntPtr.Zero, AppBarEdge.Top, 40, WorkArea);

        Assert.False(result.IsSuccess);
        Assert.Empty(api.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Register_rejects_non_positive_thickness(int thickness)
    {
        var api = new FakeAppBarNativeApi();
        var controller = new AppBarController(api);

        var result = controller.Register(WindowHandle, AppBarEdge.Top, thickness, WorkArea);

        Assert.False(result.IsSuccess);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public void Register_rejects_invalid_work_area()
    {
        var api = new FakeAppBarNativeApi();
        var controller = new AppBarController(api);

        var result = controller.Register(WindowHandle, AppBarEdge.Top, 40, PixelRect.Empty);

        Assert.False(result.IsSuccess);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public void Register_is_idempotent_when_already_registered()
    {
        var api = new FakeAppBarNativeApi();
        var controller = new AppBarController(api);
        controller.Register(WindowHandle, AppBarEdge.Top, 40, WorkArea);

        var second = controller.Register(WindowHandle, AppBarEdge.Top, 40, WorkArea);

        Assert.False(second.IsSuccess);
        Assert.Equal(["register", "query", "set"], api.Calls);
    }

    [Fact]
    public void Register_unregisters_when_shell_returns_invalid_rect()
    {
        var api = new FakeAppBarNativeApi
        {
            SetTransform = static (_, _) => PixelRect.Empty,
        };
        var controller = new AppBarController(api);

        var result = controller.Register(WindowHandle, AppBarEdge.Top, 40, WorkArea);

        Assert.False(result.IsSuccess);
        Assert.False(controller.IsRegistered);
        Assert.Equal(["register", "query", "set", "unregister"], api.Calls);
    }

    [Fact]
    public void Remove_unregisters_and_is_idempotent()
    {
        var api = new FakeAppBarNativeApi();
        var controller = new AppBarController(api);
        controller.Register(WindowHandle, AppBarEdge.Bottom, 40, WorkArea);

        controller.Remove();
        controller.Remove();

        Assert.False(controller.IsRegistered);
        Assert.Equal(["register", "query", "set", "unregister"], api.Calls);
    }

    [Fact]
    public void Reapply_before_register_fails()
    {
        var api = new FakeAppBarNativeApi();
        var controller = new AppBarController(api);

        Assert.False(controller.Reapply().IsSuccess);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public void Reapply_repeats_dock_cycle()
    {
        var api = new FakeAppBarNativeApi();
        var controller = new AppBarController(api);
        controller.Register(WindowHandle, AppBarEdge.Top, 40, WorkArea);

        var result = controller.Reapply();

        Assert.True(result.IsSuccess);
        Assert.Equal(["register", "query", "set", "query", "set"], api.Calls);
    }

    [Fact]
    public void UpdateWorkArea_recomputes_position()
    {
        var api = new FakeAppBarNativeApi();
        var controller = new AppBarController(api);
        controller.Register(WindowHandle, AppBarEdge.Top, 40, WorkArea);
        var narrower = new PixelRect(0, 0, 1280, 720);

        var result = controller.UpdateWorkArea(narrower);

        Assert.True(result.IsSuccess);
        Assert.Equal(new PixelRect(0, 0, 1280, 40), api.LastSet);
    }

    [Fact]
    public void UpdateWorkArea_rejects_invalid_area()
    {
        var api = new FakeAppBarNativeApi();
        var controller = new AppBarController(api);
        controller.Register(WindowHandle, AppBarEdge.Top, 40, WorkArea);

        Assert.False(controller.UpdateWorkArea(PixelRect.Empty).IsSuccess);
    }

    [Fact]
    public void Notifications_reach_shell_only_when_registered()
    {
        var api = new FakeAppBarNativeApi();
        var controller = new AppBarController(api);

        controller.NotifyActivated();
        controller.NotifyWindowPositionChanged();
        Assert.Empty(api.Calls);

        controller.Register(WindowHandle, AppBarEdge.Top, 40, WorkArea);
        controller.NotifyActivated();
        controller.NotifyWindowPositionChanged();

        Assert.Equal(["register", "query", "set", "activate", "poschanged"], api.Calls);
    }
    [Fact]
    public void Explorer_restart_registers_again_and_uses_new_monitor_bounds()
    {
        var native = new FakeAppBarNativeApi();
        var controller = new AppBarController(native);
        controller.Register(WindowHandle, AppBarEdge.Bottom, 200, WorkArea);
        var result = controller.Restart(new PixelRect(-1280, 0, 0, 720));
        Assert.True(result.IsSuccess);
        Assert.Equal(new PixelRect(-1280, 520, 0, 720), result.Rect);
        Assert.Equal(["register", "query", "set", "unregister", "register", "query", "set"], native.Calls);
    }

    [Fact]
    public void Failed_reregistration_does_not_leave_registration_active()
    {
        var native = new FakeAppBarNativeApi();
        var controller = new AppBarController(native);
        controller.Register(WindowHandle, AppBarEdge.Bottom, 200, WorkArea);
        native.RegisterResult = false;
        Assert.False(controller.Restart().IsSuccess);
        Assert.False(controller.IsRegistered);
    }
}
