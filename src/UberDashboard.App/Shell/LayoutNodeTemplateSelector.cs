using System.Windows;
using System.Windows.Controls;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Shell;

/// <summary>
/// Selektor szablonu węzła układu. Rozróżnia liść panelu oraz podział poziomy
/// i pionowy, dzięki czemu drzewo jest renderowane rekurencyjnie bez duplikowania
/// zawartości paneli w dwóch wariantach układu.
/// </summary>
public sealed class LayoutNodeTemplateSelector : DataTemplateSelector
{
    /// <summary>Szablon liścia — karta panelu.</summary>
    public DataTemplate? Leaf { get; set; }

    /// <summary>Szablon podziału poziomego (kolumny).</summary>
    public DataTemplate? HorizontalSplit { get; set; }

    /// <summary>Szablon podziału pionowego (wiersze).</summary>
    public DataTemplate? VerticalSplit { get; set; }

    /// <inheritdoc />
    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item switch
    {
        PanelLeafViewModel => Leaf,
        PanelSplitViewModel split => split.Axis == SplitAxis.Vertical ? VerticalSplit : HorizontalSplit,
        _ => base.SelectTemplate(item, container),
    };
}
