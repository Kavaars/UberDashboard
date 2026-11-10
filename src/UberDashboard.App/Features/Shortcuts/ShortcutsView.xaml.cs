using System.Windows;
using System.Windows.Input;
using Wpf = System.Windows.Controls;

namespace UberDashboard.App.Features.Shortcuts;

public partial class ShortcutsView : Wpf.UserControl
{
    public ShortcutsView() => InitializeComponent();
    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && DataContext is ShortcutsPanel { SingleClick: false, IsModuleEditMode: false } panel && SelectItem(e.OriginalSource, panel)) Open(panel);
    }
    private void OnItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Wpf.ListBoxItem { DataContext: ShortcutEntry entry } && DataContext is ShortcutsPanel panel) panel.Selected = entry;
    }
    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ShortcutsPanel { SingleClick: true, IsModuleEditMode: false } panel && e.ClickCount == 1 && SelectItem(e.OriginalSource, panel)) Open(panel);
    }
    private bool SelectItem(object source, ShortcutsPanel panel)
    {
        if (source is not DependencyObject dependency || Wpf.ItemsControl.ContainerFromElement(ShortcutList, dependency) is not Wpf.ListBoxItem { DataContext: ShortcutEntry entry }) return false;
        panel.Selected = entry;
        return true;
    }
    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ShortcutsPanel { IsModuleEditMode: false } panel && Keyboard.FocusedElement is DependencyObject focused && SelectItem(focused, panel)) { Open(panel); e.Handled = true; }
    }
    private static void Open(ShortcutsPanel panel)
    {
        if (panel.OpenCommand.CanExecute(null)) panel.OpenCommand.Execute(null);
    }
}
