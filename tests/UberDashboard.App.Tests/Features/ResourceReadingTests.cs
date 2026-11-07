using UberDashboard.App.Features.Resources;

namespace UberDashboard.App.Tests.Features;

public sealed class ResourceReadingTests
{
    [Fact]
    public void Missing_or_invalid_percent_is_distinct_from_zero()
    {
        Assert.Equal("Niedostępne", HardwareSampler.Format(null, "%"));
        Assert.Equal("Niedostępne", HardwareSampler.Format(float.NaN, "%"));
        Assert.Equal("Niedostępne", HardwareSampler.Format(101, "%"));
        Assert.Equal("0 %", HardwareSampler.Format(0, "%"));
    }

    [Fact]
    public void Vram_usage_requires_capacity_and_used_values()
    {
        Assert.Null(HardwareSampler.MemoryPercent(512, 0));
        Assert.Null(HardwareSampler.MemoryPercent(null, 1024));
        Assert.Equal(50f, HardwareSampler.MemoryPercent(512, 1024));
    }
    [Fact]
    public void Visibility_option_notifies_only_on_user_change()
    {
        var changes = 0;
        var option = new ResourceOption("cpu|usage", "CPU", true, _ => changes++);
        option.IsVisible = false;
        Assert.Equal(1, changes);
        option.SetVisible(true);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Legacy_empty_state_keeps_all_parameters_visible()
    {
        var state = System.Text.Json.JsonSerializer.Deserialize<ResourcePanelState>("{}");
        Assert.Empty(state!.Hidden);
        Assert.False(state.ShowTiles);
    }
}
