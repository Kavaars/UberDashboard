using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using UberDashboard.App.Shell;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Controls;

/// <summary>Uchwyt krawędzi panelu, ze zmianą rozmiaru i przesuwaniem klawiaturą.</summary>
public sealed class PanelResizeThumb : Thumb
{
    public static readonly DependencyProperty EdgeProperty = DependencyProperty.Register(nameof(Edge), typeof(GridEdge), typeof(PanelResizeThumb), new PropertyMetadata(GridEdge.Right));
    public GridEdge Edge { get => (GridEdge)GetValue(EdgeProperty); set => SetValue(EdgeProperty, value); }
    private GridCell _start;
    private System.Windows.Point _origin;

    public PanelResizeThumb()
    {
        Focusable = true;
        DragStarted += (_, _) => { if (Context() is { } c) { _start = c.Cell; _origin = Mouse.GetPosition(c.Canvas); } };
        DragDelta += (_, _) =>
        {
            if (Context() is not { } c) return;
            var point = Mouse.GetPosition(c.Canvas);
            var horizontal = Edge is GridEdge.Left or GridEdge.Right;
            var delta = horizontal ? (point.X - _origin.X) / Math.Max(1, c.Canvas.CellSize.Width) : (point.Y - _origin.Y) / Math.Max(1, c.Canvas.CellSize.Height);
            Apply(c, PanelGrid.ResizeEdge(_start, Edge, (int)Math.Round(delta)));
        };
        DragCompleted += (_, _) => { if (Context() is { } c) _ = c.Shell.SaveGridAsync(); };
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down) || Context() is not { } c) return;
        var (dx, dy) = e.Key switch { Key.Left => (-1, 0), Key.Right => (1, 0), Key.Up => (0, -1), _ => (0, 1) };
        e.Handled = true;
        var moved = Keyboard.Modifiers == ModifierKeys.Control
            ? c.Shell.PlacePanel(c.Leaf.InstanceId, c.Cell with { Column = c.Cell.Column + dx, Row = c.Cell.Row + dy })
            : Apply(c, PanelGrid.ResizeEdge(c.Cell, Edge, Edge is GridEdge.Left or GridEdge.Right ? dx : dy));
        if (moved) _ = c.Shell.SaveGridAsync();
    }

    private bool Apply((ShellViewModel Shell, PanelLeafViewModel Leaf, PanelGridCanvas Canvas, GridCell Cell) c, GridCell wanted)
    {
        if ((Edge is GridEdge.Left or GridEdge.Right && c.Leaf.Appearance?.LockWidth == true) ||
            (Edge is GridEdge.Top or GridEdge.Bottom && c.Leaf.Appearance?.LockHeight == true)) return false;
        return wanted != c.Cell && c.Shell.PlacePanel(c.Leaf.InstanceId, wanted);
    }

    private (ShellViewModel Shell, PanelLeafViewModel Leaf, PanelGridCanvas Canvas, GridCell Cell)? Context()
    {
        if (DataContext is not PanelLeafViewModel { IsEditMode: true } leaf || Window.GetWindow(this)?.DataContext is not ShellViewModel shell) return null;
        DependencyObject? current = this;
        while (current is not null and not PanelGridCanvas) current = VisualTreeHelper.GetParent(current);
        return current is PanelGridCanvas canvas ? (shell, leaf, canvas, leaf.Cell) : null;
    }
}
