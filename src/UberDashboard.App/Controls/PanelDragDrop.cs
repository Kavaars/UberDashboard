using System.Windows;
using System.Windows.Input;
using UberDashboard.App.Shell;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Controls;

/// <summary>Przeciąganie panelu w jawnym trybie edycji; cel to <see cref="PanelGridCanvas"/>.</summary>
public static class PanelDragDrop
{
    private const string Format = "UberDashboard.PanelInstance.v1";
    public static readonly DependencyProperty IsHandleProperty = DependencyProperty.RegisterAttached(
        "IsHandle", typeof(bool), typeof(PanelDragDrop), new PropertyMetadata(false, OnHandleChanged));
    private static readonly DependencyProperty StartProperty = DependencyProperty.RegisterAttached(
        "Start", typeof(System.Windows.Point?), typeof(PanelDragDrop));
    public static bool GetIsHandle(DependencyObject value) => (bool)value.GetValue(IsHandleProperty);
    public static void SetIsHandle(DependencyObject value, bool enabled) => value.SetValue(IsHandleProperty, enabled);

    /// <summary>Punkt chwycenia względem lewego górnego rogu panelu; utrzymuje panel pod kursorem.</summary>
    internal static System.Windows.Point GrabOffset { get; private set; }

    internal static bool TryGetDragged(System.Windows.IDataObject data, out PanelInstanceId id)
    {
        id = default;
        if (!data.GetDataPresent(Format) || !Guid.TryParse(data.GetData(Format) as string, out var value)) return false;
        id = new PanelInstanceId(value);
        return true;
    }

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
        DependencyObject? parent = element;
        while (parent is not null and not PanelAppearanceBorder) parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        GrabOffset = parent is UIElement panel ? element.TranslatePoint(start, panel) : start;
        System.Windows.DragDrop.DoDragDrop(element, new System.Windows.DataObject(Format, leaf.InstanceId.ToString()), System.Windows.DragDropEffects.Move);
        // Panel przesuwa się na żywo podczas przeciągania; zapisz pozycję po upuszczeniu lub anulowaniu.
        if (Window.GetWindow(element)?.DataContext is ShellViewModel shell) _ = shell.SaveGridAsync();
    }
}
