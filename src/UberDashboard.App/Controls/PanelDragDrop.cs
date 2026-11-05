using UberDashboard.Core.Localization;
using System.Windows;
using System.Windows.Input;
using UberDashboard.App.Shell;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Controls;

/// <summary>Przeciąganie tylko za tytuł i tylko w jawnym trybie edycji.</summary>
public static class PanelDragDrop
{
    private const string Format = "UberDashboard.PanelInstance.v1";
    public static readonly DependencyProperty IsHandleProperty = DependencyProperty.RegisterAttached(
        "IsHandle", typeof(bool), typeof(PanelDragDrop), new PropertyMetadata(false, OnHandleChanged));
    public static readonly DependencyProperty IsTargetProperty = DependencyProperty.RegisterAttached(
        "IsTarget", typeof(bool), typeof(PanelDragDrop), new PropertyMetadata(false, OnTargetChanged));
    private static readonly DependencyProperty StartProperty = DependencyProperty.RegisterAttached(
        "Start", typeof(System.Windows.Point?), typeof(PanelDragDrop));
    public static bool GetIsHandle(DependencyObject value) => (bool)value.GetValue(IsHandleProperty);
    public static void SetIsHandle(DependencyObject value, bool enabled) => value.SetValue(IsHandleProperty, enabled);
    public static bool GetIsTarget(DependencyObject value) => (bool)value.GetValue(IsTargetProperty);
    public static void SetIsTarget(DependencyObject value, bool enabled) => value.SetValue(IsTargetProperty, enabled);

    private static void OnHandleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        if ((bool)e.NewValue) { element.PreviewMouseLeftButtonDown += Begin; element.PreviewMouseMove += Move; element.PreviewMouseLeftButtonUp += End; }
        else { element.PreviewMouseLeftButtonDown -= Begin; element.PreviewMouseMove -= Move; element.PreviewMouseLeftButtonUp -= End; }
    }
    private static void Begin(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PanelLeafViewModel { IsEditMode: true } } element) return;
        for (var current = e.OriginalSource as DependencyObject; current is not null && current != element;
             current = current is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.Primitives.Thumb) return;
        element.SetValue(StartProperty, e.GetPosition(element));
        element.CaptureMouse();
        e.Handled = true;
    }
    private static void End(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        element.ClearValue(StartProperty);
        if (element.IsMouseCaptured) element.ReleaseMouseCapture();
    }
    private static void Move(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PanelLeafViewModel { IsEditMode: true } leaf } element
            || e.LeftButton != MouseButtonState.Pressed || element.GetValue(StartProperty) is not System.Windows.Point start) return;
        var now = e.GetPosition(element);
        if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        element.ClearValue(StartProperty);
        element.ReleaseMouseCapture();
        System.Windows.DragDrop.DoDragDrop(element, new System.Windows.DataObject(Format, leaf.InstanceId.ToString()), System.Windows.DragDropEffects.Move);
    }
    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.AllowDrop = (bool)e.NewValue;
        if ((bool)e.NewValue) { element.PreviewDragOver += Over; element.PreviewDrop += Drop; element.DragLeave += Leave; }
        else { element.PreviewDragOver -= Over; element.PreviewDrop -= Drop; element.DragLeave -= Leave; }
    }
    private static void Over(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = System.Windows.DragDropEffects.None;
        if (sender is FrameworkElement { DataContext: PanelLeafViewModel { IsEditMode: true } leaf } element
            && e.Data.GetDataPresent(Format) && Guid.TryParse(e.Data.GetData(Format) as string, out var source) && source != leaf.InstanceId.Value)
        {
            var point = e.GetPosition(element);
            var zone = ZoneAt(point.X, point.Y, element.ActualWidth, element.ActualHeight);
            leaf.DropHint = zone switch { DropZone.Left => UiText.Get("DropOnTheLeft"), DropZone.Right => UiText.Get("DropOnTheRight"), DropZone.Top => UiText.Get("DropAbove"), _ => UiText.Get("DropBelow") };
            e.Effects = System.Windows.DragDropEffects.Move;
        }
        e.Handled = e.Data.GetDataPresent(Format);
    }
    private static void Leave(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PanelLeafViewModel leaf }) leaf.DropHint = null;
    }
    private static async void Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PanelLeafViewModel { IsEditMode: true } leaf } element
            || !e.Data.GetDataPresent(Format) || !Guid.TryParse(e.Data.GetData(Format) as string, out var id)
            || Window.GetWindow(element)?.DataContext is not ShellViewModel shell) return;
        leaf.DropHint = null;
        e.Handled = true;
        var point = e.GetPosition(element);
        await shell.MovePanelAsync(new PanelInstanceId(id), leaf.InstanceId, ZoneAt(point.X, point.Y, element.ActualWidth, element.ActualHeight)).ConfigureAwait(true);
    }

    internal static DropZone ZoneAt(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0) return DropZone.Right;
        var nx = x / width;
        var ny = y / height;
        if (Math.Min(nx, 1 - nx) < Math.Min(ny, 1 - ny)) return nx < .5 ? DropZone.Left : DropZone.Right;
        return ny < .5 ? DropZone.Top : DropZone.Bottom;
    }
}
