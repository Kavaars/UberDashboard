using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using UberDashboard.App.Shell;

namespace UberDashboard.App.Controls;

/// <summary>Łączy dzielnik WPF z trwałym dokumentem układu bez zapisu przy każdym pikselu ruchu.</summary>
public sealed class PersistedGridSplitter : GridSplitter
{
    public PersistedGridSplitter()
    {
        Focusable = true;
        KeyboardIncrement = 10;
        DragCompleted += (_, e) => { if (!e.Canceled) QueueCommit(); };
        AutomationProperties.SetName(this, "Zmień rozmiar paneli");
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) QueueCommit();
    }

    private void QueueCommit() => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
    {
        if (DataContext is not PanelSplitViewModel { PersistResize: { } persist } split || Parent is not Grid grid) return;
        grid.UpdateLayout();
        var horizontal = ResizeDirection == GridResizeDirection.Columns;
        var first = horizontal ? grid.ColumnDefinitions[0].ActualWidth : grid.RowDefinitions[0].ActualHeight;
        var second = horizontal ? grid.ColumnDefinitions[2].ActualWidth : grid.RowDefinitions[2].ActualHeight;
        var total = horizontal ? grid.ActualWidth : grid.ActualHeight;
        if (first + second <= 0 || !double.IsFinite(total)) return;
        await persist(split.SplitId, first / (first + second), total).ConfigureAwait(true);
    }));
}
