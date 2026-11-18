using UberDashboard.Core.Localization;
using System.Collections.Immutable;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using UberDashboard.App.Panels;
using UberDashboard.App.Settings;
using UberDashboard.Core.Layout;
using UberDashboard.Core.Panels;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Control = System.Windows.Controls.Control;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Binding = System.Windows.Data.Binding;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using MessageBox = UberDashboard.App.Controls.ThemedMessageBox;

namespace UberDashboard.App.Controls;

/// <summary>Wspólny pasek zakładek dla instancji wszystkich typów paneli.</summary>
public sealed class PanelGroupTabStrip : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(nameof(Session), typeof(PanelGroupSession), typeof(PanelGroupTabStrip), new PropertyMetadata(null, OnSessionChanged));
    public PanelGroupSession? Session { get => (PanelGroupSession?)GetValue(SessionProperty); set => SetValue(SessionProperty, value); }
    public static readonly DependencyProperty OverflowProperty = DependencyProperty.Register(nameof(Overflow), typeof(PanelTabOverflow), typeof(PanelGroupTabStrip),
        new PropertyMetadata(PanelTabOverflow.Scroll, (d, _) => ((PanelGroupTabStrip)d).UpdateOverflow()));
    public PanelTabOverflow Overflow { get => (PanelTabOverflow)GetValue(OverflowProperty); set => SetValue(OverflowProperty, value); }
    private readonly TabItemsPanel _items = new();
    private readonly ScrollViewer _viewport = new() { Focusable = false };
    private readonly Button _left;
    private readonly Button _right;
    private Point _start;
    private ImmutableArray<PanelTab> _rendered = [];
    private PanelInstanceId? _lastActive;
    internal Func<PanelTab, bool> ConfirmDeletion { get; set; } = tab => ThemedMessageBox.Show(UiText.Format("DeleteTheTabAndItsData", tab.Name), UiText.Get("DeleteTab"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    public PanelGroupTabStrip()
    {
        Focusable = false;
        HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
        VerticalContentAlignment = System.Windows.VerticalAlignment.Stretch;
        _viewport.Content = _items;
        _left = CreateArrow(Dock.Left, "\uE76B", "PanelTabScrollLeft", "PanelTabScrollLeftHelp", -1);
        _right = CreateArrow(Dock.Right, "\uE76C", "PanelTabScrollRight", "PanelTabScrollRightHelp", 1);
        var layout = new DockPanel { ClipToBounds = true };
        layout.Children.Add(_left);
        layout.Children.Add(_right);
        layout.Children.Add(_viewport);
        Content = layout;
        _viewport.ScrollChanged += (_, _) => UpdateArrows();
        SizeChanged += (_, _) => UpdateArrows();
        PreviewMouseWheel += (_, e) =>
        {
            if (Overflow != PanelTabOverflow.Scroll || _viewport.ScrollableWidth <= 0) return;
            _viewport.ScrollToHorizontalOffset(_viewport.HorizontalOffset - e.Delta);
            e.Handled = true;
        };
        UpdateOverflow();
        Loaded += (_, _) =>
        {
            if (Session is not { } session) return;
            session.Changed -= Changed;
            session.Changed += Changed;
            Changed(session, EventArgs.Empty);
            RevealActiveTab();
        };
        Unloaded += (_, _) => { if (Session is { } session) session.Changed -= Changed; };
    }

    private Button CreateArrow(Dock dock, string glyph, string label, string help, int direction)
    {
        var button = new Button { Content = glyph, Width = 22, MinWidth = 0, Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, ToolTip = UiText.Get(help), Visibility = Visibility.Collapsed };
        button.SetResourceReference(StyleProperty, "PanelActionGlyphButton");
        AutomationProperties.SetName(button, UiText.Get(label));
        DockPanel.SetDock(button, dock);
        button.Click += (_, _) => _viewport.ScrollToHorizontalOffset(_viewport.HorizontalOffset + direction * Math.Max(16, _viewport.ViewportWidth * .75));
        return button;
    }

    private void UpdateOverflow()
    {
        _items.Shrink = Overflow == PanelTabOverflow.Shrink;
        _items.InvalidateMeasure();
        _viewport.HorizontalScrollBarVisibility = Overflow == PanelTabOverflow.Scroll ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
        _viewport.VerticalScrollBarVisibility = Overflow == PanelTabOverflow.Wrap ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        _viewport.ScrollToHorizontalOffset(0);
        _viewport.ScrollToVerticalOffset(0);
        UpdateArrows();
        RevealActiveTab();
    }

    private void UpdateArrows()
    {
        // Compare with the strip's full width so the arrows' own space cannot keep them visible.
        var visibility = Overflow == PanelTabOverflow.Scroll && _viewport.ExtentWidth > ActualWidth + .5
            ? Visibility.Visible : Visibility.Collapsed;
        _left.Visibility = _right.Visibility = visibility;
        _left.IsEnabled = _viewport.HorizontalOffset > .5;
        _right.IsEnabled = _viewport.ScrollableWidth - _viewport.HorizontalOffset > .5;
    }

    private void RevealActiveTab()
    {
        if (!IsLoaded) return;
        _ = Dispatcher.InvokeAsync(() => _items.Children.OfType<Button>()
            .FirstOrDefault(button => button.Tag is PanelTab tab && tab.InstanceId == Session?.Group.ActiveId)?.BringIntoView(),
            DispatcherPriority.Loaded);
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
                ApplyMicrosoftTodoBorder(button, session.IsMicrosoftTodoTab(tab.InstanceId), session.HasNewTodoItems(tab.InstanceId));
                var selected = tab.InstanceId == session.Group.ActiveId;
                ApplyTabAppearance(button, tab, selected);
                System.Windows.Automation.AutomationProperties.SetItemStatus(button, session.HasNewTodoItems(tab.InstanceId) ? UiText.Get("TodoTabNewItems") : selected ? UiText.Get("Active") : UiText.Get("Inactive"));
            }
        if (_lastActive == session.Group.ActiveId) return;
        _lastActive = session.Group.ActiveId;
        RevealActiveTab();
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
        _lastActive = Session?.Group.ActiveId;
        if (Session is not { } session) return;
        foreach (var tab in session.Group.Tabs)
        {
            var button = new Button { Tag = tab, Content = TabCaption(tab), Padding = new Thickness(5, 5, 5, 5), Margin = new Thickness(0, 0, 2, 0),
                MinWidth = 0, ClipToBounds = true, ToolTip = tab.Name, AllowDrop = true };
            button.SetResourceReference(StyleProperty, "PanelActionButton");
            ApplyMicrosoftTodoBorder(button, session.IsMicrosoftTodoTab(tab.InstanceId), session.HasNewTodoItems(tab.InstanceId));
            button.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
            ApplyTabAppearance(button, tab, tab.InstanceId == session.Group.ActiveId);
            System.Windows.Automation.AutomationProperties.SetName(button, UiText.Format("Tab2", tab.Name));
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, session.HasNewTodoItems(tab.InstanceId) ? UiText.Get("TodoTabNewItems") : tab.InstanceId == session.Group.ActiveId ? UiText.Get("Active") : UiText.Get("Inactive"));
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
            var remove = new MenuItem { Header = UiText.Get("DeleteTab2"), IsEnabled = session.Group.Tabs.Length > 1 };
            if (session.IsMicrosoftTodoTab(tab.InstanceId))
            {
                var sync = new MenuItem { Header = UiText.Get("SyncEditorTitle"), ToolTip = UiText.Get("SyncEnableHelp") };
                sync.Click += async (_, _) => await Run(async () =>
                {
                    await session.SelectAsync(tab.InstanceId);
                    if (session.ActiveViewModel is Features.Todo.MicrosoftTodoListViewModel list)
                        await list.OpenSyncEditorCommand.ExecuteAsync(null);
                });
                menu.Items.Add(sync);
                var local = new MenuItem { Header = UiText.Get("TodoDeleteTabOnly") };
                local.Click += async (_, _) => await Run(async () => { if (ConfirmDeletion(tab)) await session.RemoveAsync(tab.InstanceId); });
                var remote = new MenuItem { Header = UiText.Get("TodoDeleteTabAndRemote") };
                remote.Click += async (_, _) => await Run(async () =>
                {
                    if (ThemedMessageBox.Show(UiText.Format("TodoDeleteRemoteConfirm", tab.Name), UiText.Get("DeleteTab"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
                        await session.RemoveMicrosoftTodoListAsync(tab.InstanceId);
                });
                remove.Items.Add(local); remove.Items.Add(remote);
            }
            else remove.Click += async (_, _) => { await Run(async () => { if (!await session.NeedsDeletionConfirmationAsync(tab.InstanceId) || ConfirmDeletion(tab)) await session.RemoveAsync(tab.InstanceId); }); };
            menu.Items.Add(remove);
            if (session.SupportsFileImport)
            {
                var import = new MenuItem { Header = UiText.Get("ImportFilesIntoNewTabs") };
                import.Click += async (_, _) => await Run(() => PanelFileImport.RunAsync(session, Window.GetWindow(this)));
                menu.Items.Add(import);
            }
            var color = new MenuItem { Header = UiText.Get("TabColor") };
            foreach (var (key, hex) in TabColors)
            {
                var swatch = new MenuItem { Header = UiText.Get(key), Icon = new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = ColorBrush(hex) } };
                swatch.Click += async (_, _) => await Run(() => session.UpdateAsync(group => group.Recolor(tab.InstanceId, hex)));
                color.Items.Add(swatch);
            }
            var reset = new MenuItem { Header = UiText.Get("TabColorDefault") };
            reset.Click += async (_, _) => await Run(() => session.UpdateAsync(group => group.Recolor(tab.InstanceId, null)));
            color.Items.Add(new Separator());
            color.Items.Add(reset);
            menu.Items.Add(color);
            button.ContextMenu = menu;
            _items.Children.Add(button);
        }
        var add = new Button { Content = "+", Padding = new Thickness(6, 2, 6, 2), ToolTip = UiText.Get("AddsAnIndependentTabToThisPanel") };
        add.SetResourceReference(StyleProperty, "PanelActionButton");
        add.MinWidth = 26;
        add.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        System.Windows.Automation.AutomationProperties.SetName(add, UiText.Get("AddTab"));
        add.Click += async (_, _) => await Run(() => session.AddAsync());
        _items.Children.Add(add);
        RevealActiveTab();
    }

    private static TextBlock TabCaption(PanelTab tab)
    {
        var caption = new TextBlock { Text = tab.Name, TextTrimming = TextTrimming.CharacterEllipsis };
        caption.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1),
        });
        return caption;
    }

    private sealed class TabItemsPanel : WrapPanel
    {
        public bool Shrink { get; set; }
        private double[] _widths = [];

        protected override Size MeasureOverride(Size availableSize)
        {
            if (!Shrink || InternalChildren.Count == 0) return base.MeasureOverride(availableSize);

            var naturalSize = base.MeasureOverride(new Size(double.PositiveInfinity, availableSize.Height));
            if (_widths.Length != InternalChildren.Count) _widths = new double[InternalChildren.Count];
            var addWidth = InternalChildren[^1].DesiredSize.Width;
            var tabWidth = naturalSize.Width - addWidth;
            var availableTabs = Math.Max(0, availableSize.Width - addWidth);
            var scale = tabWidth > availableTabs ? availableTabs / tabWidth : 1;
            for (var index = 0; index < _widths.Length; index++)
            {
                var child = InternalChildren[index];
                var width = index == _widths.Length - 1 ? Math.Min(addWidth, availableSize.Width) : child.DesiredSize.Width * scale;
                _widths[index] = width;
                child.Measure(new Size(width, availableSize.Height));
            }
            return new Size(Math.Min(availableSize.Width, naturalSize.Width), naturalSize.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (!Shrink || InternalChildren.Count == 0) return base.ArrangeOverride(finalSize);

            var left = 0d;
            for (var index = 0; index < _widths.Length; index++)
            {
                InternalChildren[index].Arrange(new Rect(left, 0, _widths[index], finalSize.Height));
                left += _widths[index];
            }
            return finalSize;
        }
    }
    /// <summary>Kolor tła zakładki; domyślny to tło zależne od tego, czy zakładka jest aktywna.</summary>
    private static void ApplyTabAppearance(Button button, PanelTab tab, bool selected)
    {
        button.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
        if (tab.Color is { } hex) button.Background = ColorBrush(hex);
        else
        {
            button.SetResourceReference(Control.BackgroundProperty, selected ? "Brush.Accent" : "Brush.Surface");
            button.SetResourceReference(Control.ForegroundProperty, selected ? "Brush.Accent.Text" : "Brush.Text");
        }
        // Aktywna zakładka dostaje wyraźną ramkę akcentową (nadpisuje ramkę Microsoft To Do).
        if (selected)
        {
            button.SetResourceReference(Control.BorderBrushProperty, "Brush.Accent");
            button.BorderThickness = new Thickness(2);
        }
    }

    private static System.Windows.Media.Brush ColorBrush(string hex)
    {
        var brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static readonly (string Key, string Hex)[] TabColors =
    [
        ("TabColorRed", "#E5484D"), ("TabColorOrange", "#F76B15"), ("TabColorYellow", "#F5C518"), ("TabColorGreen", "#30A46C"),
        ("TabColorBlue", "#3E63DD"), ("TabColorPurple", "#8E4EC6"), ("TabColorPink", "#D6409F"), ("TabColorGray", "#8B8D98"),
    ];

    private static void ApplyMicrosoftTodoBorder(Button button, bool linked, bool unread)
    {
        if (linked || unread)
        {
            button.SetResourceReference(Control.BorderBrushProperty, unread ? "Brush.Warning" : "Brush.MicrosoftTodo.TabBorder");
            button.BorderThickness = new Thickness(1);
        }
        else
        {
            button.ClearValue(Control.BorderBrushProperty);
            button.ClearValue(Control.BorderThicknessProperty);
        }
    }

    private void Rename(Button button, PanelTab tab, PanelGroupSession session)
    {
        if (button.Content is TextBox) return;
        var caption = button.Content;
        var input = new TextBox { Text = tab.Name, MinWidth = Overflow == PanelTabOverflow.Shrink ? 0 : 70 };
        button.Content = input;
        var finished = false;
        async Task Finish(bool save)
        {
            if (finished) return;
            finished = true;
            if (save) await Run(() => session.UpdateAsync(group => group.Rename(tab.InstanceId, input.Text)));
            button.Content = caption; button.Focus();
        }
        input.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Finish(true); } else if (e.Key == Key.Escape) { e.Handled = true; await Finish(false); } };
        input.LostKeyboardFocus += async (_, _) => await Finish(true);
        input.Focus(); input.SelectAll();
    }
}
