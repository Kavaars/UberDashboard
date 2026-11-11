using ListBox = System.Windows.Controls.ListBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using Point = System.Windows.Point;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UberDashboard.Contracts.Panels;
using System.Windows.Media;

namespace UberDashboard.App.Controls;

/// <summary>Reorders elements inside one list only while that panel is in module edit mode.</summary>
public static class ModuleReorderBehavior
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(ModuleReorderBehavior), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    public static readonly DependencyProperty ManagedProperty = DependencyProperty.RegisterAttached("Managed", typeof(bool), typeof(ModuleReorderBehavior), new PropertyMetadata(false, ManagedChanged));
    public static void SetManaged(DependencyObject value, bool managed) => value.SetValue(ManagedProperty, managed);
    public static bool GetManaged(DependencyObject value) => (bool)value.GetValue(ManagedProperty);
    private static void ManagedChanged(DependencyObject value, DependencyPropertyChangedEventArgs args)
    {
        if (value is not ListBox list) return;
        if ((bool)args.NewValue) list.SelectionChanged += SelectionChanged;
        else list.SelectionChanged -= SelectionChanged;
    }
    private static void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var list = (ListBox)sender;
        if (!GetEnabled(list) && list.SelectedItems.Count > 0) list.UnselectAll();
    }
    private static readonly DependencyProperty StartProperty = DependencyProperty.RegisterAttached("Start", typeof(Point), typeof(ModuleReorderBehavior));
    private static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached("Source", typeof(object), typeof(ModuleReorderBehavior));
    private sealed record DragPayload(ListBox Owner, object Item);
    private sealed record ResizeState(string Id, Point Start, int Width, int Height, ListBoxItem Container, ModularItemsPanel Grid);
    private static readonly DependencyProperty ResizeProperty = DependencyProperty.RegisterAttached("Resize", typeof(ResizeState), typeof(ModuleReorderBehavior));

    private static void Changed(DependencyObject value, DependencyPropertyChangedEventArgs args)
    {
        if (value is not ListBox list) return;
        if ((bool)args.NewValue)
        {
            list.AllowDrop = true;
            list.PreviewMouseLeftButtonDown += Down;
            list.PreviewMouseMove += Move;
            list.PreviewMouseLeftButtonUp += Up;
            list.LostMouseCapture += LostCapture;
            list.DragOver += Over;
            list.Drop += Drop;
            list.PreviewKeyDown += KeyDown;
        }
        else
        {
            list.PreviewMouseLeftButtonDown -= Down;
            list.PreviewMouseMove -= Move;
            list.PreviewMouseLeftButtonUp -= Up;
            list.LostMouseCapture -= LostCapture;
            if (list.IsMouseCaptured) list.ReleaseMouseCapture();
            list.ClearValue(ResizeProperty);
            list.DragOver -= Over;
            list.Drop -= Drop;
            list.PreviewKeyDown -= KeyDown;
            list.ClearValue(SourceProperty);
            list.UnselectAll();
            list.ClearValue(UIElement.AllowDropProperty);
        }
    }
    private static void Down(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;
        list.SetValue(StartProperty, e.GetPosition(list));
        var item = e.OriginalSource is DependencyObject visual ? ItemsControl.ContainerFromElement(list, visual) as ListBoxItem : null;
        if (item is not null && list.DataContext is IModularPanel modular && modular.GetModuleId(item.DataContext) is { } id &&
            FindPanel(list) is { } grid)
        {
            var point = e.GetPosition(item);
            if (point.X >= item.ActualWidth - 18 && point.Y >= item.ActualHeight - 18)
            {
                var size = grid.SizeOf(item);
                list.SetValue(ResizeProperty, new ResizeState(id, e.GetPosition(grid), size.Width, size.Height, item, grid));
                list.CaptureMouse(); e.Handled = true; return;
            }
        }
        list.SetValue(SourceProperty, item?.DataContext);
    }
    private static void Move(object sender, MouseEventArgs e)
    {
        var list = (ListBox)sender;
        if (list.GetValue(ResizeProperty) is ResizeState resize)
        {
            if (e.LeftButton == MouseButtonState.Pressed && list.DataContext is IModularPanel model)
            {
                var point = e.GetPosition(resize.Grid);
                var size = resize.Grid.SizeFromDelta(resize.Container, new Point(point.X - resize.Start.X, point.Y - resize.Start.Y), resize.Width, resize.Height);
                model.ResizeModule(resize.Id, size.Width, size.Height);
            }
            e.Handled = true; return;
        }
        if (e.OriginalSource is DependencyObject visual && ItemsControl.ContainerFromElement(list, visual) is ListBoxItem hover)
        {
            var point = e.GetPosition(hover);
            list.Cursor = point.X >= hover.ActualWidth - 18 && point.Y >= hover.ActualHeight - 18 ? System.Windows.Input.Cursors.SizeNWSE : null;
        }
        else list.Cursor = null;
        if (e.LeftButton != MouseButtonState.Pressed || list.GetValue(SourceProperty) is not { } source) return;
        var start = (Point)list.GetValue(StartProperty);
        var current = e.GetPosition(list);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        list.ClearValue(SourceProperty);
        DragDrop.DoDragDrop(list, new DragPayload(list, source), DragDropEffects.Move);
        e.Handled = true;
    }
    private static void Up(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;
        if (list.GetValue(ResizeProperty) is not ResizeState) return;
        list.ClearValue(ResizeProperty); list.ReleaseMouseCapture(); e.Handled = true;
    }
    private static void LostCapture(object sender, MouseEventArgs e) => ((ListBox)sender).ClearValue(ResizeProperty);
    private static void Over(object sender, DragEventArgs e)
    {
        var list = (ListBox)sender;
        var payload = e.Data.GetData(typeof(DragPayload)) as DragPayload;
        if (payload?.Owner != list || list.DataContext is not IModularPanel { IsModuleEditMode: true } modular ||
            modular.GetModuleId(payload.Item) is not { } id || FindPanel(list) is not { } grid)
        { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        var cell = grid.CellAt(e.GetPosition(grid));
        modular.MoveModuleToCell(id, cell.Column, cell.Row);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }
    private static ModularItemsPanel? FindPanel(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ModularItemsPanel panel) return panel;
            if (FindPanel(child) is { } found) return found;
        }
        return null;
    }
    private static void Drop(object sender, DragEventArgs e) { e.Handled = true; }
    private static void KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var target = (ListBox)sender;
            if (target.SelectedItem is { } selectedItem && target.DataContext is IModularPanel model && model.GetModuleId(selectedItem) is { } moduleId &&
                target.ItemContainerGenerator.ContainerFromItem(selectedItem) is ListBoxItem container && FindPanel(target) is { } panel)
            {
                var size = panel.SizeOf(container);
                model.ResizeModule(moduleId, size.Width + (e.Key == Key.Right ? 1 : e.Key == Key.Left ? -1 : 0), size.Height + (e.Key == Key.Down ? 1 : e.Key == Key.Up ? -1 : 0));
                e.Handled = true;
            }
            return;
        }
        if (Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift) || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
        var list = (ListBox)sender;
        if (list.SelectedItem is { } selected && list.DataContext is IModularPanel modular && modular.GetModuleId(selected) is { } id &&
            modular.ModulePositions.FirstOrDefault(position => position.Id == id) is { } position)
        {
            var indexInGrid = position.Row * ModularItemsPanel.LogicalColumns + position.Column + (e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : e.Key == Key.Up ? -ModularItemsPanel.LogicalColumns : ModularItemsPanel.LogicalColumns);
            if (indexInGrid >= 0 && indexInGrid < ModularItemsPanel.LogicalColumns * 4096)
                modular.MoveModuleToCell(id, indexInGrid % ModularItemsPanel.LogicalColumns, indexInGrid / ModularItemsPanel.LogicalColumns);
            e.Handled = true; return;
        }

    }
}
