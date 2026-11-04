using UberDashboard.App.Features.Affirmations;

namespace UberDashboard.App.Tests.Features;

public sealed class AffirmationTests
{
    [Fact]
    public void Remaining_appearance_round_trips_and_old_documents_use_theme()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<AffirmationDocument>("{}")!;
        var vm = new AffirmationPanelViewModel();
        vm.Load(old);
        Assert.Null(vm.RemainingColor);
        Assert.Equal  (1, vm.RemainingOpacity);
        vm.RemainingColor = "#FF123456";
        vm.RemainingOpacity = 0.35;
        var restored = new AffirmationPanelViewModel();
        restored.Load(System.Text.Json.JsonSerializer.Deserialize<AffirmationDocument>(System.Text.Json.JsonSerializer.Serialize(vm.Snapshot()))!);
        Assert.Equal("#FF123456", restored.RemainingColor);
        Assert.Equal(0.35, restored.RemainingOpacity);
        restored.Reset();
        Assert.Equal(0.35, restored.RemainingOpacity);
    }

    [Fact]
    public void Accepts_only_correct_characters_and_advances_in_order()
    {
        var vm = new AffirmationPanelViewModel();
        vm.Texts = "Ala\nKot";
        vm.Accept("X");
        Assert.Equal("", vm.Typed);
        vm.Accept("Alx");
        Assert.Equal("Al", vm.Typed);
        vm.Accept("a");
        Assert.Equal("Ala", vm.Completed);
        Assert.Equal("Kot", vm.Remaining);
        vm.Accept("Kot");
        Assert.Equal("Ala\nKot", vm.Completed);
        Assert.Equal("", vm.Remaining);
        vm.ResetCommand.Execute(null);
        Assert.Equal("Ala", vm.Remaining);
    }

    [Fact]
    public void Restores_progress_and_resets_after_interval()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var vm = new AffirmationPanelViewModel(() => now);
        vm.Texts = "Test";
        vm.ResetHours = 4;
        vm.Accept("Te");
        var state = vm.Snapshot();
        var restored = new AffirmationPanelViewModel(() => now.AddHours(3));
        restored.Load(state);
        Assert.Equal("Te", restored.Typed);
        now = now.AddHours(4);
        vm.CheckReset();
        Assert.Equal("", vm.Typed);
        var expired = new AffirmationPanelViewModel(() => now);
        expired.Load(state);
        Assert.Equal("", expired.Typed);
    }
    [Fact]
    public void Completion_history_survives_reset_and_round_trip()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var vm = new AffirmationPanelViewModel(() => now) { Texts = "A\nB" };
        vm.Accept("A");
        Assert.Equal(1, vm.TotalAffirmations);
        Assert.Equal(0, vm.CurrentStreak);
        vm.Accept("B");
        vm.Reset();
        now = now.AddDays(1);
        vm.Accept("AB");
        Assert.Equal(4, vm.TotalAffirmations);
        Assert.Equal(2, vm.CurrentStreak);
        var restored = new AffirmationPanelViewModel(() => now);
        restored.Load(vm.Snapshot());
        Assert.Equal(2, restored.TotalSets);
        Assert.Equal(2, restored.CurrentStreak);
        Assert.Contains("2 dni", restored.StreakHistory);
    }
}
