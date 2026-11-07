using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace UberDashboard.App.Controls;

/// <summary>Drag handle for a floating popup or context menu.</summary>
public sealed class PopupDragHandle : Thumb
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(nameof(Target), typeof(object), typeof(PopupDragHandle));
    public object? Target { get => GetValue(TargetProperty); set => SetValue(TargetProperty, value); }

    public PopupDragHandle() => DragDelta += OnDragDelta;

    private void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        switch (Target)
        {
            case Popup popup:
                popup.HorizontalOffset += e.HorizontalChange;
                popup.VerticalOffset += e.VerticalChange;
                break;
            case System.Windows.Controls.ContextMenu menu:
                menu.HorizontalOffset += e.HorizontalChange;
                menu.VerticalOffset += e.VerticalChange;
                break;
        }
    }
}
