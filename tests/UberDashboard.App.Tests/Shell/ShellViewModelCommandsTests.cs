using UberDashboard.Application.Platform;

namespace UberDashboard.App.Tests.Shell;

public sealed class ShellViewModelCommandsTests
{
    [Fact]
    public void Window_mode_enum_keeps_expected_order()
    {
        Assert.Equal(WindowMode.Normal, (WindowMode)0);
        Assert.Equal(WindowMode.DockedBar, (WindowMode)1);
        Assert.Equal(WindowMode.DesktopWidget, (WindowMode)2);
    }
}
