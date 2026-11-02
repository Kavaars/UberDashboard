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

    private sealed class RecordingNotifier : IPanelNotifier
    {
        public void Publish(string message, PanelNotificationSeverity severity = PanelNotificationSeverity.Information)
        {
        }
    }
}
