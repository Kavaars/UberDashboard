using Size = System.Windows.Size;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using System.Windows;
using System.Windows.Media;
using UberDashboard.App.Shell;
using UberDashboard.Core.Layout;
using Panel = System.Windows.Controls.Panel;

namespace UberDashboard.App.Controls;

/// <summary>Dzieli cały obszar pulpitu na siatkę i układa panele w ich komórkach; w trybie edycji przyjmuje przeciągane panele.</summary>
public sealed class PanelGridCanvas : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(nameof(Columns), typeof(int), typeof(PanelGridCanvas),
        new FrameworkPropertyMetadata(24, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender), value => value is int v && v >= 1);
    public int Columns { get => (int)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(nameof(Rows), typeof(int), typeof(PanelGridCanvas),
        new FrameworkPropertyMetadata(16, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender), value => value is int v && v >= 1);
    public int Rows { get => (int)GetValue(RowsProperty); set => SetValue(RowsProperty, value); }
    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(nameof(ShowGrid), typeof(bool), typeof(PanelGridCanvas),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool ShowGrid { get => (bool)GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }
    public static readonly DependencyProperty CellProperty = DependencyProperty.RegisterAttached("Cell", typeof(GridCell), typeof(PanelGridCanvas),
        new FrameworkPropertyMetadata(new GridCell(0, 0, 1, 1), FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));
    public static GridCell GetCell(DependencyObject value) => (GridCell)value.GetValue(CellProperty);
    public static void SetCell(DependencyObject value, GridCell cell) => value.SetValue(CellProperty, cell);

    public PanelGridCanvas() => AllowDrop = true;

    public Size CellSize => new(RenderSize.Width / Columns, RenderSize.Height / Rows);

    public (int Column, int Row) CellAt(Point point) =>
        (Math.Clamp((int)Math.Floor(point.X / Math.Max(1, CellSize.Width)), 0, Columns - 1), Math.Clamp((int)Math.Floor(point.Y / Math.Max(1, CellSize.Height)), 0, Rows - 1));

    private static Rect RectOf(GridCell cell, Size step) => new(cell.Column * step.Width, cell.Row * step.Height, cell.ColumnSpan * step.Width, cell.RowSpan * step.Height);

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 0, double.IsFinite(availableSize.Height) ? availableSize.Height : 0);
        var step = new Size(size.Width / Columns, size.Height / Rows);
        foreach (UIElement child in InternalChildren) child.Measure(RectOf(GetCell(child), step).Size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var step = new Size(finalSize.Width / Columns, finalSize.Height / Rows);
        foreach (UIElement child in InternalChildren) child.Arrange(RectOf(GetCell(child), step));
        return finalSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Przezroczyste tło przyjmuje upuszczenie także na pustych komórkach.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (!ShowGrid) return;
        var pen = new Pen(TryFindResource("Brush.Border") as Brush ?? Brushes.Gray, 1);
        for (var column = 0; column <= Columns; column++) dc.DrawLine(pen, new Point(column * CellSize.Width, 0), new Point(column * CellSize.Width, RenderSize.Height));
        for (var row = 0; row <= Rows; row++) dc.DrawLine(pen, new Point(0, row * CellSize.Height), new Point(RenderSize.Width, row * CellSize.Height));
    }

    // Tunelowanie: pola tekstowe w panelach nie przechwycą przeciąganego panelu.
    protected override void OnPreviewDragOver(System.Windows.DragEventArgs e)
    {
        base.OnPreviewDragOver(e);
        if (!PanelDragDrop.TryGetDragged(e.Data, out var id)) return;
        e.Handled = true;
        e.Effects = System.Windows.DragDropEffects.None;
        if (DataContext is not ShellViewModel { IsEditMode: true } shell || shell.CellOf(id) is not { } current) return;
        var point = e.GetPosition(this);
        var (column, row) = CellAt(new Point(point.X - PanelDragDrop.GrabOffset.X + CellSize.Width / 2, point.Y - PanelDragDrop.GrabOffset.Y + CellSize.Height / 2));
        shell.PlacePanel(id, (current with { Column = column, Row = row }).Clamp(Columns, Rows));
        e.Effects = System.Windows.DragDropEffects.Move;
    }

    protected override void OnPreviewDrop(System.Windows.DragEventArgs e)
    {
        base.OnPreviewDrop(e);
        if (PanelDragDrop.TryGetDragged(e.Data, out _)) e.Handled = true;
    }
}
