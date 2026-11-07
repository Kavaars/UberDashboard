using UberDashboard.App.Features.Todo;
using UberDashboard.Contracts.Host;

namespace UberDashboard.App.Tests.Features.Todo;

public sealed class TodoPanelViewModelTests
{
    [Fact]
    public void Changing_show_summary_marks_panel_dirty_and_raises_change()
    {
        var notifier = new RecordingNotifier();
        var viewModel = new TodoPanelViewModel(notifier);
        var changedCount = 0;
        viewModel.Changed += (_, _) => changedCount++;

        viewModel.ShowSummary = false;

        Assert.False(viewModel.ShowSummary);
        Assert.True(viewModel.HasUnsavedChanges);
        Assert.Equal(1, changedCount);
        Assert.False(viewModel.Snapshot().ShowSummary);
    }

    [Fact]
    public void Filters_sorts_and_keeps_recent_completed_tasks_after_confirmation()
    {
        var vm = new TodoPanelViewModel(new RecordingNotifier(), _ => true);
        var older = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        vm.Load(new TodoDocument { Items = [new("first", false), new("old", true, CompletedAt: older),
            new("newest", false), new("recent", true, CompletedAt: older.AddDays(1))] });
        Assert.Equal(new[] { "newest", "first", "recent", "old" }, vm.VisibleItems.Select(x => x.Text));
        vm.CompletedDisplayLimit = 1;
        Assert.Equal(new[] { "newest", "first", "recent" }, vm.VisibleItems.Select(x => x.Text));
        vm.ShowOnlyIncomplete = true;
        Assert.Equal(new[] { "newest", "first" }, vm.VisibleItems.Select(x => x.Text));
        vm.CompletedKeepLimit = 1;
        vm.KeepCompletedCommand.Execute(null);
        Assert.Equal(new[] { "first", "newest", "recent" }, vm.Items.Select(x => x.Text));
        Assert.Equal(1, vm.Snapshot().CompletedKeepLimit);
    }

    [Fact]
    public void Cancelled_deletion_preserves_completed_tasks_and_legacy_order()
    {
        var vm = new TodoPanelViewModel(new RecordingNotifier(), _ => false);
        vm.Load(new TodoDocument { Items = [new("old", true), new("new", true)] });
        Assert.Equal(new[] { "new", "old" }, vm.VisibleItems.Select(x => x.Text));
        vm.ClearCompletedCommand.Execute(null);
        Assert.Equal(2, vm.Items.Count);
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void Spacing_is_saved_restored_and_clamped_without_changing_tasks()
    {
        var vm = new TodoPanelViewModel(new RecordingNotifier());
        vm.Load(new TodoDocument { Items = [new("task", false)] });
        vm.EdgeSpacing = 0;
        vm.EntrySpacing = 20;
        var restored = new TodoPanelViewModel(new RecordingNotifier());
        restored.Load(vm.Snapshot());
        Assert.Equal(0, restored.EdgeSpacing);
        Assert.Equal(20, restored.EntrySpacing);
        Assert.Equal(new System.Windows.Thickness(0), restored.EdgePadding);
        Assert.Equal(new System.Windows.Thickness(0, 20, 0, 20), restored.EntryPadding);
        Assert.Single(restored.Items);
        restored.Load(new TodoDocument { EdgeSpacing = -10, EntrySpacing = 999 });
        Assert.Equal(0, restored.EdgeSpacing);
        Assert.Equal(24, restored.EntrySpacing);
        restored.Load(new TodoDocument());
        Assert.Equal(12, restored.EdgeSpacing);
        Assert.Equal(6, restored.EntrySpacing);
    }

    private sealed class RecordingNotifier : IPanelNotifier
    {
        public void Publish(string message, PanelNotificationSeverity severity = PanelNotificationSeverity.Information)
        {
        }
    }
}
