using System.Windows;
using System.Windows.Input;
using Wpf = System.Windows.Controls;

namespace UberDashboard.App.Features.Shortcuts;

public partial class ShortcutsView : Wpf.UserControl
{
    public ShortcutsView() => InitializeComponent();
    private void OnOptionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }
    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ShortcutsPanel { SingleClick: false } panel && IsItem(e.OriginalSource)) Open(panel);
    }
    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ShortcutsPanel { SingleClick: true } panel && e.ClickCount == 1 && IsItem(e.OriginalSource)) Open(panel);
    }
    private bool IsItem(object source) => source is DependencyObject dependency &&
        Wpf.ItemsControl.ContainerFromElement(ShortcutList, dependency) is Wpf.ListBoxItem;
    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ShortcutsPanel panel) { Open(panel); e.Handled = true; }
    }
    private static void Open(ShortcutsPanel panel)
    {
        if (panel.OpenCommand.CanExecute(null)) panel.OpenCommand.Execute(null);
    }
}
