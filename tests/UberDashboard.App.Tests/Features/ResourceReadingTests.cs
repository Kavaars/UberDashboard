using UberDashboard.App.Features.Resources;
using UberDashboard.App.Panels;
using UberDashboard.App.Tests.TestSupport;
using UberDashboard.Contracts.Panels;
using UberDashboard.Core.Layout;
using UberDashboard.Core.ModularPanels;
using Microsoft.Extensions.Logging.Abstractions;

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
        var original = new ResourcePanelState { ShowTiles = true, Hidden = ["cpu|CpuUsage"], ModuleOrder = ["gpu", "cpu"] };
        var restored = System.Text.Json.JsonSerializer.Deserialize<ResourcePanelState>(System.Text.Json.JsonSerializer.Serialize(original));
        Assert.True(restored!.ShowTiles);
        Assert.Equal(original.Hidden, restored.Hidden);
        Assert.Equal(original.ModuleOrder, restored.ModuleOrder);
    }

    [Fact]
    public async Task Resource_positions_survive_missing_sensor_and_move_to_empty_cell()
    {
        var context = new PanelContext
        {
            InstanceId = PanelInstanceId.New(), PluginId = "test", DashboardId = "test", CloseToken = CancellationToken.None,
            Logger = new PanelLogger(NullLoggerFactory.Instance, "test", Guid.NewGuid(), PanelInstanceId.New()),
            Notifier = new FakePanelNotifier(), Storage = new PanelStorage(Path.GetTempPath()),
        };
        using var sampler = new HardwareSampler();
        await using var panel = new ResourcePanel(context, sampler);
        var state = new ResourcePanelState { ModuleOrder = ["missing", "cpu"], Hidden = ["missing"],
            ModulePositions = [new ModulePosition("missing", 2, 2), new ModulePosition("cpu", 0, 0)] };
        await panel.RestoreStateAsync(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(state), CancellationToken.None);
        panel.IsModuleEditMode = true;
        panel.MoveModuleToCell("cpu", 3, 3);
        var saved = System.Text.Json.JsonSerializer.Deserialize<ResourcePanelState>((await panel.CaptureStateAsync(CancellationToken.None)).Span)!;
        Assert.Contains(saved.ModulePositions, p => p.Id == "missing" && p.Column == 2 && p.Row == 2);
        Assert.Contains(saved.ModulePositions, p => p.Id == "cpu" && p.Column == 3 && p.Row == 3);
        Assert.Contains("missing", saved.Hidden);
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
