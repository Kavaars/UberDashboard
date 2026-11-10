using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using UberDashboard.Contracts.Panels;
using ListBox = System.Windows.Controls.ListBox;
using Point = System.Windows.Point;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace PanelTemplates.Plugin;

internal sealed class ModularView : ListBox
{
    private Point _start;
    private object? _source;
    private sealed record DragItem(ModularView Owner, object Item);
    public ModularView()
    {
        System.Windows.Automation.AutomationProperties.SetName(this, TemplateText.Get("Elementy panelu", "Panel items"));
        System.Windows.Automation.AutomationProperties.SetHelpText(this, TemplateText.Get("W trybie edycji użyj Ctrl+Shift i strzałki, aby przesunąć element.", "In edit mode use Ctrl+Shift and an arrow key to move an item."));
        Background = System.Windows.Media.Brushes.Transparent;
        BorderThickness = new Thickness(0);
        SetResourceReference(ForegroundProperty, "Brush.Text");
        ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Auto);
        SetBinding(ItemsSourceProperty, new Binding(nameof(TemplateModel.Modules)));
        DisplayMemberPath = nameof(TemplateItem.Name);
        ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(ModularItemsPanel)));
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            _start = e.GetPosition(this);
            _source = e.OriginalSource is DependencyObject visual ? (ItemsControl.ContainerFromElement(this, visual) as ListBoxItem)?.DataContext : null;
        };
        PreviewMouseMove += OnMove;
        DragOver += OnOver;
        Drop += (_, e) => e.Handled = true;
        PreviewKeyDown += OnKey;
        SetBinding(AllowDropProperty, new Binding(nameof(TemplateModel.IsModuleEditMode)));
    }
    private void OnMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not IModularPanel { IsModuleEditMode: true } || e.LeftButton != MouseButtonState.Pressed || _source is null) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var source = _source; _source = null;
        DragDrop.DoDragDrop(this, new DragItem(this, source), DragDropEffects.Move);
        e.Handled = true;
    }
    private void OnOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(DragItem)) is not DragItem drag || drag.Owner != this || DataContext is not IModularPanel { IsModuleEditMode: true } model ||
            model.GetModuleId(drag.Item) is not { } id || FindGrid(this) is not { } panel)
        { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        var (column, row) = panel.CellAt(e.GetPosition(panel));
        model.MoveModuleToCell(id, column, row);
        e.Effects = DragDropEffects.Move; e.Handled = true;
    }
    private void OnKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift) || e.Key is not (Key.Left or Key.Right) ||
            DataContext is not IModularPanel { IsModuleEditMode: true } model || SelectedItem is not { } selected || model.GetModuleId(selected) is not { } id ||
            model.ModulePositions.FirstOrDefault(p => p.Id == id) is not { } position) return;
        var index = position.Row * 4 + position.Column + (e.Key == Key.Left ? -1 : 1);
        if (index >= 0 && index < 4 * 4096) model.MoveModuleToCell(id, index % 4, index / 4);
        e.Handled = true;
    }
    private static ModularItemsPanel? FindGrid(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ModularItemsPanel panel) return panel;
            if (FindGrid(child) is { } found) return found;
        }
        return null;
    }
}
