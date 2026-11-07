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
    [Fact]
    public void Tile_view_round_trips_with_hidden_parameters()
    {
        var original = new ResourcePanelState { ShowTiles = true, Hidden = ["cpu|CpuUsage"] };
        var restored = System.Text.Json.JsonSerializer.Deserialize<ResourcePanelState>(System.Text.Json.JsonSerializer.Serialize(original));
        Assert.True(restored!.ShowTiles);
        Assert.Equal(original.Hidden, restored.Hidden);
    }

    [Theory]
    [InlineData("device|CpuTemperature", "°C")]
    [InlineData("device|GpuUsage", "GPU")]
    [InlineData("Memory|RamUsage", "RAM")]
    [InlineData("device|VramUsed", "VRAM")]
    public void Tile_symbol_follows_reading_type(string key, string expected)
    {
        Assert.Equal(expected, new ResourceReading("device", "reading", "value", key).Symbol);
    }
    [Theory]
    [InlineData("device|CpuTemperature", "52.5 °C", "CPU °", "52.5")]
    [InlineData("device|GpuTemperature", "Niedostępne", "GPU °", "Niedostępne")]
    [InlineData("device|GpuUsage", "20 %", "GPU", "20 %")]
    public void Compact_tiles_keep_context_and_unavailable_values(string key, string value, string label, string displayed)
    {
        var reading = new ResourceReading("device", "reading", value, key);
        Assert.Equal(label, reading.TileLabel);
        Assert.Equal(displayed, reading.TileValue);
    }
}
