using CommunityToolkit.Mvvm.ComponentModel;

namespace UberDashboard.App.Shell;

/// <summary>
/// Bazowy model widoku węzła drzewa układu. Warstwa WPF renderuje drzewo
/// rekurencyjnie: <see cref="PanelSplitViewModel"/> jako siatkę z dzielnikiem,
/// <see cref="PanelLeafViewModel"/> jako kartę panelu. Typ bazowy służy wyłącznie
/// do rozróżniania węzłów przez selektor szablonu.
/// </summary>
public abstract class LayoutNodeViewModel : ObservableObject
{
    /// <summary>Minimum poddrzewa w DIP, używane również przez przewijany widok WPF.</summary>
    public UberDashboard.Core.Layout.MinSize Minimum { get; init; }
}
