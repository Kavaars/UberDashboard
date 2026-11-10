using Size = System.Windows.Size;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UberDashboard.Contracts.Panels;
using Panel = System.Windows.Controls.Panel;

namespace PanelTemplates.Plugin;

/// <summary>Displays stable logical cells while reflowing them to the available width.</summary>
public sealed class ModularItemsPanel : Panel
{
    public const int LogicalColumns = 4;
    public const double CellWidth = 180;
    public static readonly DependencyProperty CellHeightProperty = DependencyProperty.Register(nameof(CellHeight), typeof(double), typeof(ModularItemsPanel),
        new FrameworkPropertyMetadata(120d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender),
        value => value is double height && double.IsFinite(height) && height >= 24 && height <= 1000);
    public double CellHeight { get => (double)GetValue(CellHeightProperty); set => SetValue(CellHeightProperty, value); }
    public static readonly DependencyProperty MaximumColumnsProperty = DependencyProperty.Register(nameof(MaximumColumns), typeof(int), typeof(ModularItemsPanel),
        new FrameworkPropertyMetadata(LogicalColumns, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public int MaximumColumns { get => (int)GetValue(MaximumColumnsProperty); set => SetValue(MaximumColumnsProperty, value); }
    private int _columns = 1;
    private double _columnWidth = CellWidth;
    private INotifyPropertyChanged? _notifications;
    public ModularItemsPanel()
    {
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        DataContextChanged += (_, _) => { Attach(); InvalidateMeasure(); };
    }
    private void Attach()
    {
        Detach();
        _notifications = DataContext as INotifyPropertyChanged;
        if (_notifications is not null) _notifications.PropertyChanged += Changed;
    }
    private void Detach() { if (_notifications is not null) _notifications.PropertyChanged -= Changed; _notifications = null; }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IModularPanel.ModulePositions) or nameof(IModularPanel.IsModuleEditMode) or null or "") { InvalidateMeasure(); InvalidateVisual(); }
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? Math.Max(CellWidth, availableSize.Width) : CellWidth * LogicalColumns;
        _columns = Math.Clamp((int)(width / CellWidth), 1, Math.Clamp(MaximumColumns, 1, LogicalColumns));
        _columnWidth = _columns == 1 ? width : CellWidth;
        var last = -1;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(_columnWidth, CellHeight));
            last = Math.Max(last, Index(child));
        }
        var rows = Math.Max(1, last / _columns + 1) + (DataContext is IModularPanel { IsModuleEditMode: true } ? 1 : 0);
        return new Size(_columns * _columnWidth, rows * CellHeight);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            var index = Index(child);
            child.Arrange(new Rect(index % _columns * _columnWidth, index / _columns * CellHeight, _columnWidth, CellHeight));
        }
        return finalSize;
    }
    private int Index(UIElement element)
    {
        if (DataContext is IModularPanel model && element is FrameworkElement { DataContext: { } item } && model.GetModuleId(item) is { } id &&
            model.ModulePositions.FirstOrDefault(p => p.Id == id) is { } pos) return pos.Row * LogicalColumns + pos.Column;
        return InternalChildren.IndexOf(element);
    }
    public (int Column, int Row) CellAt(Point point)
    {
        var col = Math.Clamp((int)(Math.Max(0, point.X) / _columnWidth), 0, _columns - 1);
        var row = Math.Clamp((int)(Math.Max(0, point.Y) / CellHeight), 0, 4095);
        var slot = Math.Min(row * _columns + col, 4095 * LogicalColumns + LogicalColumns - 1);
        return (slot % LogicalColumns, slot / LogicalColumns);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (DataContext is not IModularPanel { IsModuleEditMode: true }) return;
        var brush = TryFindResource("Brush.Border") as Brush ?? Brushes.Gray;
        var pen = new Pen(brush, 1);
        for (var x = 0d; x <= RenderSize.Width; x += _columnWidth) dc.DrawLine(pen, new Point(x, 0), new Point(x, RenderSize.Height));
        for (var y = 0d; y <= RenderSize.Height; y += CellHeight) dc.DrawLine(pen, new Point(0, y), new Point(RenderSize.Width, y));
    }
}
