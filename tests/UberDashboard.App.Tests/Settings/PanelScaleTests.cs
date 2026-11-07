using UberDashboard.App.Controls;
using UberDashboard.App.Settings;

namespace UberDashboard.App.Tests.Settings;

public sealed class PanelScaleTests
{
    [Fact]
    public void Scale_uses_area_and_respects_limits()
    {
        Assert.Equal(1, PanelScaleConverter.Calculate(400, 300, 15, 200), 3);
        Assert.Equal(.5, PanelScaleConverter.Calculate(200, 150, 15, 200), 3);
        Assert.Equal(.4, PanelScaleConverter.Calculate(10, 10, 40, 150), 3);
        Assert.Equal(1.5, PanelScaleConverter.Calculate(2000, 1000, 40, 150), 3);
        Assert.Equal(1, PanelScaleConverter.Calculate(0, 0, 15, 200), 3);
    }

    [Fact]
    public void Settings_normalize_invalid_and_crossed_limits()
    {
        var value = new PanelAppearanceSettings { MinScale = 250, MaxScale = 10 }.Normalize();
        Assert.Equal(200, value.MinScale);
        Assert.Equal(200, value.MaxScale);
        Assert.Equal(15, new PanelAppearanceSettings { MinScale = double.NaN }.Normalize().MinScale);
    }
}
