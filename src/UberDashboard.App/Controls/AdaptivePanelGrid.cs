using System.Windows;
using System.Windows.Controls;

namespace UberDashboard.App.Controls;

/// <summary>W niskim, szerokim panelu przenosi wskazane elementy do kolumn.</summary>
public sealed class AdaptivePanelGrid : Grid
{
    public static readonly DependencyProperty WideColumnProperty = DependencyProperty.RegisterAttached(
        "WideColumn", typeof(int), typeof(AdaptivePanelGrid), new PropertyMetadata(-1));
    public static void SetWideColumn(DependencyObject element, int value) => element.SetValue(WideColumnProperty, value);
    public static int GetWideColumn(DependencyObject element) => (int)element.GetValue(WideColumnProperty);

    private readonly Dictionary<UIElement, int> _rows = [];
    private bool _wide;

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        var wide = ActualWidth >= 480 && ActualHeight < 260;
        if (wide == _wide) return;
        _wide = wide;
        foreach (UIElement child in Children)
        {
            var column = GetWideColumn(child);
            if (column < 0) continue;
            if (wide) { _rows[child] = GetRow(child); SetRow(child, 0); SetColumn(child, column); }
            else { SetRow(child, _rows.GetValueOrDefault(child)); SetColumn(child, 0); }
        }
        if (ColumnDefinitions.Count >= 3)
        {
            ColumnDefinitions[0].Width = wide ? new GridLength(180) : new GridLength(1, GridUnitType.Star);
            ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            ColumnDefinitions[2].Width = wide ? GridLength.Auto : new GridLength(0);
        }
        if (RowDefinitions.Count >= 3)
        {
            RowDefinitions[0].Height = wide ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            RowDefinitions[1].Height = wide ? new GridLength(0) : (Tag as string == "notes" ? GridLength.Auto : new GridLength(1, GridUnitType.Star));
            RowDefinitions[2].Height = wide ? new GridLength(0) : (Tag as string == "notes" ? new GridLength(1, GridUnitType.Star) : GridLength.Auto);
        }
    }
}
