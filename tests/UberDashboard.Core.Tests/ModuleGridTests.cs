using UberDashboard.Core.ModularPanels;

namespace UberDashboard.Core.Tests;

public sealed class ModuleGridTests
{
    [Fact]
    public void Moving_to_occupied_cell_shifts_chain_without_losing_modules()
    {
        var input = ModuleGrid.Restore(["a", "b", "c", "d"], null, 2);
        var moved = ModuleGrid.Move(input, "d", 0, 0, 2).ToDictionary(x => x.Id);
        Assert.Equal((0, 0), (moved["d"].Column, moved["d"].Row));
        Assert.Equal((1, 0), (moved["a"].Column, moved["a"].Row));
        Assert.Equal((0, 1), (moved["b"].Column, moved["b"].Row));
        Assert.Equal((1, 1), (moved["c"].Column, moved["c"].Row));
    }

    [Fact]
    public void Restore_preserves_valid_positions_and_repairs_duplicates_and_missing_items()
    {
        var result = ModuleGrid.Restore(["a", "b", "c"], [new("a", 1, 0), new("b", 1, 0), new("unknown", 0, 0)], 2).ToDictionary(x => x.Id);
        Assert.Equal((1, 0), (result["a"].Column, result["a"].Row));
        Assert.Equal((0, 0), (result["b"].Column, result["b"].Row));
        Assert.Equal((0, 1), (result["c"].Column, result["c"].Row));
    }

    [Fact]
    public void Move_to_empty_cell_leaves_other_positions_unchanged()
    {
        var input = ModuleGrid.Restore(["a", "b"], null, 3);
        var result = ModuleGrid.Move(input, "a", 2, 1, 3).ToDictionary(x => x.Id);
        Assert.Equal((2, 1), (result["a"].Column, result["a"].Row));
        Assert.Equal((1, 0), (result["b"].Column, result["b"].Row));
    }
    [Theory]
    [InlineData(4)]
    [InlineData(3)]
    [InlineData(2)]
    [InlineData(1)]
    public void Wide_module_wraps_without_overlapping_and_preserves_order(int columns)
    {
        var placements = ModuleGrid.Arrange([(0, 1), (1, 3), (2, 1), (5, 1)], columns);
        var occupied = new HashSet<int>();
        foreach (var placement in placements)
        {
            Assert.True(placement.Start % columns + placement.Span <= columns);
            for (var cell = placement.Start; cell < placement.Start + placement.Span; cell++) Assert.True(occupied.Add(cell));
        }
        Assert.Equal(Math.Min(3, columns), placements[1].Span);
        Assert.True(placements[2].Start > placements[1].Start);
    }

    [Fact]
    public void Restoring_and_moving_preserves_sizes_and_old_documents_remain_valid()
    {
        Assert.NotNull(typeof(ModulePosition).GetConstructor([typeof(string), typeof(int), typeof(int)]));
        var old = System.Text.Json.JsonSerializer.Deserialize<ModulePosition>("{\"Id\":\"a\",\"Column\":0,\"Row\":0}")!;
        var positions = ModuleGrid.Restore(["a", "b"], [old, new("b", 1, 0) { Width = 3, Height = 2 }], 4);
        var moved = ModuleGrid.Move(positions, "b", 0, 1, 4);
        Assert.Equal((0, 1), (moved[0].Width, moved[0].Height));
        Assert.Equal((3, 2), (moved[1].Width, moved[1].Height));
        Assert.Equal(moved, System.Text.Json.JsonSerializer.Deserialize<ModulePosition[]>(System.Text.Json.JsonSerializer.Serialize(moved)));
    }

}
