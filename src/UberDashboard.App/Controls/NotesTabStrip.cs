using Orientation = System.Windows.Controls.Orientation;
using Binding = System.Windows.Data.Binding;
using DragDropEffects = System.Windows.DragDropEffects;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;
using TextBox = System.Windows.Controls.TextBox;
using Control = System.Windows.Controls.Control;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = UberDashboard.App.Controls.ThemedMessageBox;
using DragDrop = System.Windows.DragDrop;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using UberDashboard.App.Features.Notes;

namespace UberDashboard.App.Controls;

public sealed class NotesTabStrip : ScrollViewer
{
    private readonly StackPanel _items = new() { Orientation = Orientation.Horizontal };
    private NotesPanelViewModel? _model;
    private Point _start;
    public NotesTabStrip()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Content = _items;
        DataContextChanged += (_, _) => Attach(DataContext as NotesPanelViewModel);
    }
    private void Attach(NotesPanelViewModel? model)
    {
        if (_model is not null) { _model.Tabs.CollectionChanged -= Changed; _model.PropertyChanged -= ModelChanged; }
        _model = model;
        if (_model is not null) { _model.Tabs.CollectionChanged += Changed; _model.PropertyChanged += ModelChanged; }
        RenderTabs();
    }
    private void Changed(object? sender, NotifyCollectionChangedEventArgs e) => RenderTabs();
    private void ModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NotesPanelViewModel.SelectedTab)) return;
        foreach (var button in _items.Children.OfType<Button>())
            if (button.DataContext is NotesTab tab) button.SetResourceReference(Control.BackgroundProperty, ReferenceEquals(tab, _model?.SelectedTab) ? "Brush.Surface.Alt" : "Brush.Surface");
    }
    private void RenderTabs()
    {
        _items.Children.Clear();
        if (_model is not { } model) return;
        foreach (var tab in model.Tabs)
        {
            var button = new Button { Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 3, 0), DataContext = tab, AllowDrop = true, ToolTip = "Kliknij, aby wybrać notatkę. Dwuklik lub F2 zmienia nazwę; przeciągnij, aby zmienić kolejność." };
            button.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
            button.SetResourceReference(Control.BackgroundProperty, ReferenceEquals(tab, model.SelectedTab) ? "Brush.Surface.Alt" : "Brush.Surface");
            var title = new TextBlock(); title.SetBinding(TextBlock.TextProperty, new Binding(nameof(NotesTab.Title)));
            button.Content = title;
            System.Windows.Automation.AutomationProperties.SetName(button, "Zakładka notatki");
            button.Click += (_, _) => model.SelectedTab = tab;
            button.MouseDoubleClick += (_, e) => { Rename(button, tab, model); e.Handled = true; };
            button.KeyDown += (_, e) => { if (e.Key == Key.F2) { Rename(button, tab, model); e.Handled = true; } };
            button.PreviewMouseLeftButtonDown += (_, e) => _start = e.GetPosition(button);
            button.PreviewMouseMove += (_, e) => { var p = e.GetPosition(button); if (e.LeftButton == MouseButtonState.Pressed && (Math.Abs(p.X - _start.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(p.Y - _start.Y) >= SystemParameters.MinimumVerticalDragDistance)) DragDrop.DoDragDrop(button, tab, DragDropEffects.Move); };
            button.Drop += (_, e) => { if (e.Data.GetData(typeof(NotesTab)) is NotesTab moving) model.MoveTab(moving, model.Tabs.IndexOf(tab)); e.Handled = true; };
            var menu = new ContextMenu();
            var remove = new MenuItem { Header = "Usuń notatkę", IsEnabled = model.Tabs.Count > 1 };
            remove.Click += (_, _) => model.RemoveTab(tab, !model.TabHasContent(tab) || MessageBox.Show($"Usunąć notatkę „{tab.Title}” wraz z jej treścią?", "Usuwanie notatki", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
            menu.Items.Add(remove); button.ContextMenu = menu;
            _items.Children.Add(button);
        }
        var add = new Button { Content = "+", Padding = new Thickness(6, 2, 6, 2), Command = model.AddTabCommand, ToolTip = "Dodaje nową notatkę." };
        System.Windows.Automation.AutomationProperties.SetName(add, "Dodaj notatkę");
        _items.Children.Add(add);
    }
    private static void Rename(Button button, NotesTab tab, NotesPanelViewModel model)
    {
        if (button.Content is TextBox) return;
        var input = new TextBox { Text = tab.Title, MinWidth = 70 };
        button.Content = input;
        var finished = false;
        void Finish(bool save)
        {
            if (finished) return;
            finished = true;
            if (save) model.RenameTab(tab, input.Text);
            var text = new TextBlock(); text.SetBinding(TextBlock.TextProperty, new Binding(nameof(NotesTab.Title)));
            button.Content = text;
            button.Focus();
        }
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Finish(true); e.Handled = true; } else if (e.Key == Key.Escape) { Finish(false); e.Handled = true; } };
        input.LostKeyboardFocus += (_, _) => Finish(true);
        input.Focus(); input.SelectAll();
    }
}
