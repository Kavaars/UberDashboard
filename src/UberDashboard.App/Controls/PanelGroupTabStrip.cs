using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using UberDashboard.App.Panels;
using UberDashboard.Core.Panels;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Control = System.Windows.Controls.Control;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Binding = System.Windows.Data.Binding;
using Point = System.Windows.Point;
using Orientation = System.Windows.Controls.Orientation;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using MessageBox = UberDashboard.App.Controls.ThemedMessageBox;

namespace UberDashboard.App.Controls;

/// <summary>Wspólny pasek zakładek dla instancji wszystkich typów paneli.</summary>
public sealed class PanelGroupTabStrip : ScrollViewer
{
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(nameof(Session), typeof(PanelGroupSession), typeof(PanelGroupTabStrip), new PropertyMetadata(null, OnSessionChanged));
    public PanelGroupSession? Session { get => (PanelGroupSession?)GetValue(SessionProperty); set => SetValue(SessionProperty, value); }
    private readonly StackPanel _items = new() { Orientation = Orientation.Horizontal };
    private Point _start;
    private ImmutableArray<PanelTab> _rendered = [];
    internal Func<PanelTab, bool> ConfirmDeletion { get; set; } = tab => ThemedMessageBox.Show($"Usunąć zakładkę „{tab.Name}” wraz z jej danymi?", "Usuwanie zakładki", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    public PanelGroupTabStrip()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        PreviewMouseWheel += (_, e) => { if (ScrollableWidth <= 0) return; ScrollToHorizontalOffset(HorizontalOffset - e.Delta); e.Handled = true; };
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Content = _items;
        Loaded += (_, _) => { if (Session is { } session) { session.Changed -= Changed; session.Changed += Changed; } };
        Unloaded += (_, _) => { if (Session is { } session) session.Changed -= Changed; };
    }
    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var strip = (PanelGroupTabStrip)d;
        if (e.OldValue is PanelGroupSession old) old.Changed -= strip.Changed;
        if (strip.IsLoaded && e.NewValue is PanelGroupSession current) current.Changed += strip.Changed;
        strip.Render();
    }
    private void Changed(object? sender, EventArgs e)
    {
        if (Session is not { } session || !_rendered.SequenceEqual(session.Group.Tabs)) { Render(); return; }
        foreach (var button in _items.Children.OfType<Button>())
            if (button.Tag is PanelTab tab)
            {
                var selected = tab.InstanceId == session.Group.ActiveId;
                button.SetResourceReference(Control.BackgroundProperty, selected ? "Brush.Surface.Alt" : "Brush.Surface");
                System.Windows.Automation.AutomationProperties.SetItemStatus(button, selected ? "Aktywna" : "Nieaktywna");
            }
    }
    private async Task Run(Func<Task> action)
    {
        if (Session is not { } session) return;
        try { await action().ConfigureAwait(true); }
        catch (Exception error) { session.ReportError(error); }
    }
    private void Render()
    {
        _items.Children.Clear();
        _rendered = Session?.Group.Tabs ?? [];
        if (Session is not { } session) return;
        foreach (var tab in session.Group.Tabs)
        {
            var button = new Button { Tag = tab, Content = tab.Name, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 3, 0), AllowDrop = true, ToolTip = "Wybiera zakładkę. Dwuklik lub F2 zmienia nazwę; przeciągnięcie albo Ctrl+Shift+strzałka zmienia kolejność." };
            button.SetResourceReference(StyleProperty, "PanelActionButton");
            button.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
            button.SetResourceReference(Control.BackgroundProperty, tab.InstanceId == session.Group.ActiveId ? "Brush.Surface.Alt" : "Brush.Surface");
            System.Windows.Automation.AutomationProperties.SetName(button, $"Zakładka {tab.Name}");
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, tab.InstanceId == session.Group.ActiveId ? "Aktywna" : "Nieaktywna");
            button.Click += async (_, _) => await Run(() => session.SelectAsync(tab.InstanceId));
            button.MouseDoubleClick += (_, e) => { Rename(button, tab, session); e.Handled = true; };
            button.KeyDown += async (_, e) =>
            {
                if (e.Key == Key.F2) { Rename(button, tab, session); e.Handled = true; return; }
                if (e.Key is not (Key.Left or Key.Right)) return;
                var direction = e.Key == Key.Left ? -1 : 1;
                var index = session.Group.Tabs.IndexOf(tab);
                var target = index + direction;
                if (index < 0 || target < 0 || target >= session.Group.Tabs.Length) return;
                var moving = Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
                if (Keyboard.Modifiers != ModifierKeys.None && !moving) return;
                e.Handled = true;
                var focusId = moving ? tab.InstanceId : session.Group.Tabs[target].InstanceId;
                if (moving) await Run(() => session.UpdateAsync(group => group.Move(tab.InstanceId, target)));
                else await Run(() => session.SelectAsync(focusId));
                _items.Children.OfType<Button>().FirstOrDefault(b => b.Tag is PanelTab item && item.InstanceId == focusId)?.Focus();
            };
            button.PreviewMouseLeftButtonDown += (_, e) => _start = e.GetPosition(button);
            button.PreviewMouseMove += (_, e) => { var p = e.GetPosition(button); if (e.LeftButton == MouseButtonState.Pressed && (Math.Abs(p.X - _start.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(p.Y - _start.Y) >= SystemParameters.MinimumVerticalDragDistance)) DragDrop.DoDragDrop(button, tab, DragDropEffects.Move); };
            button.PreviewDragOver += (_, e) =>
            {
                var valid = e.Data.GetData(typeof(PanelTab)) is PanelTab moving && moving.InstanceId != tab.InstanceId && session.Group.Tabs.Any(item => item.InstanceId == moving.InstanceId);
                e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
            };
            button.Drop += async (_, e) => { if (e.Data.GetData(typeof(PanelTab)) is PanelTab moving) await Run(() => session.UpdateAsync(group => group.Move(moving.InstanceId, group.Tabs.IndexOf(tab)))); e.Handled = true; };
            var menu = new ContextMenu();
            var remove = new MenuItem { Header = "Usuń zakładkę", IsEnabled = session.Group.Tabs.Length > 1 };
            remove.Click += async (_, _) => { await Run(async () => { if (!await session.NeedsDeletionConfirmationAsync(tab.InstanceId) || ConfirmDeletion(tab)) await session.RemoveAsync(tab.InstanceId); }); };
            menu.Items.Add(remove);
            if (session.SupportsFileImport)
            {
                var import = new MenuItem { Header = "Importuj pliki do nowych zakładek…" };
                import.Click += async (_, _) => await Run(() => PanelFileImport.RunAsync(session, Window.GetWindow(this)));
                menu.Items.Add(import);
            }
            button.ContextMenu = menu;
            _items.Children.Add(button);
        }
        var add = new Button { Content = "+", Padding = new Thickness(6, 2, 6, 2), ToolTip = "Dodaje niezależną zakładkę tego panelu." };
        add.SetResourceReference(StyleProperty, "PanelActionButton");
        add.MinWidth = 26;
        add.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        System.Windows.Automation.AutomationProperties.SetName(add, "Dodaj zakładkę");
        add.Click += async (_, _) => await Run(() => session.AddAsync());
        _items.Children.Add(add);
    }
    private void Rename(Button button, PanelTab tab, PanelGroupSession session)
    {
        if (button.Content is TextBox) return;
        var input = new TextBox { Text = tab.Name, MinWidth = 70 };
        button.Content = input;
        var finished = false;
        async Task Finish(bool save)
        {
            if (finished) return;
            finished = true;
            if (save) await Run(() => session.UpdateAsync(group => group.Rename(tab.InstanceId, input.Text)));
            button.Content = tab.Name; button.Focus();
        }
        input.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Finish(true); } else if (e.Key == Key.Escape) { e.Handled = true; await Finish(false); } };
        input.LostKeyboardFocus += async (_, _) => await Finish(true);
        input.Focus(); input.SelectAll();
    }
}
