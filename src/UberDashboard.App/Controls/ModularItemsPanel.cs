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
using UberDashboard.Core.ModularPanels;
using Panel = System.Windows.Controls.Panel;

namespace UberDashboard.App.Controls;

/// <summary>Displays stable logical cells while reflowing them to the available width.</summary>
public sealed class ModularItemsPanel : Panel
{
    public const int LogicalColumns = 4;
    public const double DefaultCellWidth = 180;
    public static readonly DependencyProperty CellWidthProperty = DependencyProperty.Register(nameof(CellWidth), typeof(double), typeof(ModularItemsPanel),
        new FrameworkPropertyMetadata(DefaultCellWidth, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender),
        value => value is double width && double.IsFinite(width) && width >= 24 && width <= 1000);
    public double CellWidth { get => (double)GetValue(CellWidthProperty); set => SetValue(CellWidthProperty, value); }
    public static readonly DependencyProperty AutoCellHeightProperty = DependencyProperty.Register(nameof(AutoCellHeight), typeof(bool), typeof(ModularItemsPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public bool AutoCellHeight { get => (bool)GetValue(AutoCellHeightProperty); set => SetValue(AutoCellHeightProperty, value); }
    public static readonly DependencyProperty CellHeightProperty = DependencyProperty.Register(nameof(CellHeight), typeof(double), typeof(ModularItemsPanel),
        new FrameworkPropertyMetadata(120d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender),
        value => value is double height && double.IsFinite(height) && height >= 24 && height <= 1000);
    public double CellHeight { get => (double)GetValue(CellHeightProperty); set => SetValue(CellHeightProperty, value); }
    public static readonly DependencyProperty MaximumColumnsProperty = DependencyProperty.Register(nameof(MaximumColumns), typeof(int), typeof(ModularItemsPanel),
        new FrameworkPropertyMetadata(1024, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public int MaximumColumns { get => (int)GetValue(MaximumColumnsProperty); set => SetValue(MaximumColumnsProperty, value); }
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(ModularItemsPanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure),
        value => value is double spacing && double.IsFinite(spacing) && spacing >= -8 && spacing <= 24);
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }
    public static readonly DependencyProperty ColumnSpanProperty = DependencyProperty.RegisterAttached("ColumnSpan", typeof(int), typeof(ModularItemsPanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure), value => value is int span && span >= 1 && span <= LogicalColumns);
    public static int GetColumnSpan(DependencyObject value) => (int)value.GetValue(ColumnSpanProperty);
    public static void SetColumnSpan(DependencyObject value, int span) => value.SetValue(ColumnSpanProperty, span);
    private readonly Dictionary<UIElement, (int Start, int Span, int Logical)> _placements = [];
    private int _columns = 1;
    private double _columnWidth = DefaultCellWidth;
    private double _cellHeight = 120;
    private double Gap => 8 + Spacing;
    private double ColumnStep => _columnWidth + Gap;
    private double RowStep => _cellHeight + Gap;
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
        _columns = Math.Clamp((int)((width + Gap) / (CellWidth + Gap)), 1, Math.Clamp(MaximumColumns, 1, 1024));
        _columnWidth = _columns == 1 ? width : CellWidth;
        var ordered = InternalChildren.Cast<UIElement>().Select(child => (Child: child, Slot: Index(child), Span: GetColumnSpan(child)))
            .OrderBy(item => item.Slot).ToArray();
        var arranged = ModuleGrid.Arrange(ordered.Select(item => (item.Slot, item.Span)).ToArray(), _columns);
        _placements.Clear();
        _cellHeight = CellHeight;
        if (AutoCellHeight)
        {
            foreach (var item in ordered)
            {
                item.Child.Measure(new Size(_columnWidth * Math.Min(item.Span, _columns) + Gap * (Math.Min(item.Span, _columns) - 1), double.PositiveInfinity));
                _cellHeight = Math.Max(_cellHeight, item.Child.DesiredSize.Height);
            }
        }
        var last = -1;
        for (var i = 0; i < ordered.Length; i++)
        {
            var item = ordered[i]; var placement = arranged[i];
            _placements[item.Child] = (placement.Start, placement.Span, item.Slot);
            item.Child.Measure(new Size(_columnWidth * placement.Span + Gap * (placement.Span - 1), _cellHeight));
            last = Math.Max(last, placement.Start + placement.Span - 1);
        }
        var rows = Math.Max(1, last / _columns + 1) + (DataContext is IModularPanel { IsModuleEditMode: true } ? 1 : 0);
        return new Size(_columns * _columnWidth + (_columns - 1) * Gap, rows * _cellHeight + (rows - 1) * Gap);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            if (!_placements.TryGetValue(child, out var placement)) continue;
            child.Arrange(new Rect(placement.Start % _columns * ColumnStep, placement.Start / _columns * RowStep,
                _columnWidth * placement.Span + Gap * (placement.Span - 1), _cellHeight));
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
        var col = Math.Clamp((int)(Math.Max(0, point.X) / ColumnStep), 0, _columns - 1);
        var row = Math.Clamp((int)(Math.Max(0, point.Y) / RowStep), 0, 4095);
        var slot = Math.Min(row * _columns + col, 4095 * LogicalColumns + LogicalColumns - 1);
        var hit = _placements.Values.FirstOrDefault(p => slot >= p.Start && slot < p.Start + p.Span);
        if (hit.Span > 0) slot = hit.Logical;
        else
        {
            var previous = _placements.Values.Where(p => p.Start < slot).OrderByDescending(p => p.Start).FirstOrDefault();
            if (previous.Span > 0) slot = Math.Min(previous.Logical + Math.Max(1, slot - previous.Start - previous.Span + 1), 4095 * LogicalColumns + LogicalColumns - 1);
        }
        return (slot % LogicalColumns, slot / LogicalColumns);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (DataContext is not IModularPanel { IsModuleEditMode: true }) return;
        var brush = TryFindResource("Brush.Border") as Brush ?? Brushes.Gray;
        var pen = new Pen(brush, 1);
        for (var x = 0d; x <= RenderSize.Width; x += ColumnStep) dc.DrawLine(pen, new Point(x, 0), new Point(x, RenderSize.Height));
        for (var y = 0d; y <= RenderSize.Height; y += RowStep) dc.DrawLine(pen, new Point(0, y), new Point(RenderSize.Width, y));
    }
}
