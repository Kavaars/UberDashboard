using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using UberDashboard.App.Shell;

namespace UberDashboard.App.Controls;

/// <summary>W trybie edycji wydziela wolny obszar przez przeciągnięcie krawędzi.</summary>
public sealed class PanelEdgeThumb : Thumb
{
    public bool IsTop { get; set; }
    private double _delta;
    public PanelEdgeThumb()
    {
        Cursor = System.Windows.Input.Cursors.SizeNS;
        Focusable = true;
        DragStarted += (_, _) => _delta = 0;
        DragDelta += (_, e) => { _delta += e.VerticalChange; RenderTransform = new System.Windows.Media.TranslateTransform(0, _delta); };
        DragCompleted += async (_, e) =>
        {
            RenderTransform = System.Windows.Media.Transform.Identity;
            if (!e.Canceled) await CommitAsync(IsTop ? _delta : -_delta).ConfigureAwait(true);
        };
    }
    protected override async void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Up or Key.Down)) return;
        e.Handled = true;
        if ((IsTop && e.Key == Key.Down) || (!IsTop && e.Key == Key.Up)) await CommitAsync(80).ConfigureAwait(true);
    }
    private Task CommitAsync(double space)
    {
        if (DataContext is not PanelLeafViewModel { IsEditMode: true } leaf || space < 12
            || Window.GetWindow(this)?.DataContext is not ShellViewModel shell) return Task.CompletedTask;
        var element = this as FrameworkElement;
        while (element is not null && element is not PanelAppearanceBorder) element = element.Parent as FrameworkElement ?? System.Windows.Media.VisualTreeHelper.GetParent(element) as FrameworkElement;
        return shell.ReserveSpaceAsync(leaf.InstanceId, IsTop, space, element?.ActualHeight ?? 600);
    }
}
