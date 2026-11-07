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
    public void Daily_reset_occurs_at_configured_local_time_and_only_once()
    {
        var now = new DateTimeOffset(2026, 1, 1, 6, 59, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 1, 1, 6, 59, 0)));
        var vm = new AffirmationPanelViewModel(() => now) { Texts = "Test", DailyResetEnabled = true };
        vm.Accept("Te");
        var state = vm.Snapshot();
        now = now.AddMinutes(1);
        vm.CheckReset();
        Assert.Equal("", vm.Typed);
        vm.Accept("T");
        vm.CheckReset();
        Assert.Equal("T", vm.Typed);
        var restored = new AffirmationPanelViewModel(() => now);
        restored.Load(state);
        Assert.Equal("", restored.Typed);
        restored.Accept("T");
        restored.Load(restored.Snapshot());
        Assert.Equal("T", restored.Typed);
    }

    [Fact]
    public void Legacy_settings_migrate_without_losing_progress_and_disabled_stays_disabled()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 1, 1, 12, 0, 0)));
        var vm = new AffirmationPanelViewModel(() => now);
        vm.Load(new AffirmationDocument { Texts = "Test", Position = 2, ResetHours = 4, ResetAt = now.AddDays(-4) });
        Assert.True(vm.DailyResetEnabled);
        Assert.Equal(7, vm.ResetHour);
        Assert.Equal("Te", vm.Typed);
        now = now.AddDays(1);
        vm.CheckReset();
        Assert.Equal("", vm.Typed);
        vm.Load(new AffirmationDocument { Texts = "Test", Position = 2, ResetHours = 0 });
        Assert.False(vm.DailyResetEnabled);
        now = now.AddDays(2);
        vm.CheckReset();
        Assert.Equal("Te", vm.Typed);
    }

    [Fact]
    public void Changing_schedule_preserves_progress_and_round_trips()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 1, 1, 12, 0, 0)));
        var vm = new AffirmationPanelViewModel(() => now) { Texts = "Test" };
        vm.Accept("Te");
        vm.DailyResetEnabled = true;
        vm.ResetHour = 13;
        vm.ResetMinute = 30;
        var restored = new AffirmationPanelViewModel(() => now);
        restored.Load(vm.Snapshot());
        Assert.Equal("Te", restored.Typed);
        Assert.Equal(13, restored.ResetHour);
        Assert.Equal(30, restored.ResetMinute);
        now = now.AddMinutes(89);
        restored.CheckReset();
        Assert.Equal("Te", restored.Typed);
        now = now.AddMinutes(1);
        restored.CheckReset();
        Assert.Equal("", restored.Typed);
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
