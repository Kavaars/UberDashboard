using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Binding = System.Windows.Data.Binding;
using UserControl = System.Windows.Controls.UserControl;

namespace UberDashboard.App.Controls;

/// <summary>Próbka koloru z pełnym edytorem HSV tworzonym dopiero po otwarciu.</summary>
public sealed class CompactColorPicker : UserControl
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(string), typeof(CompactColorPicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            static (d, _) => ((CompactColorPicker)d).Refresh()));

    public static readonly DependencyProperty DefaultBrushKeyProperty = DependencyProperty.Register(
        nameof(DefaultBrushKey), typeof(string), typeof(CompactColorPicker),
        new PropertyMetadata("Brush.Surface", static (d, _) => ((CompactColorPicker)d).Refresh()));

    private readonly Border _sample = new() { Width = 24, Height = 18, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 8, 0), BorderThickness = new Thickness(1) };
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _button;
    private readonly Popup _popup;

    public string? SelectedColor { get => (string?)GetValue(SelectedColorProperty); set => SetValue(SelectedColorProperty, value); }
    public string DefaultBrushKey { get => (string)GetValue(DefaultBrushKeyProperty); set => SetValue(DefaultBrushKeyProperty, value); }

    public CompactColorPicker()
    {
        HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        content.Children.Add(_sample);
        content.Children.Add(_label);
        _sample.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        _button = new Button { Content = content, Margin = new Thickness(0), ToolTip = "Otwiera koło kolorów z jasnością, kryciem, wartością HEX i powrotem do koloru motywu." };
        _button.SetResourceReference(StyleProperty, "ToolbarButton");
        _button.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty,
            new Binding { Path = new PropertyPath(System.Windows.Automation.AutomationProperties.NameProperty), Source = this });
        _popup = new Popup { PlacementTarget = _button, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        _button.Click += (_, _) => OpenEditor();
        _popup.Closed += (_, _) => _button.Focus();
        Unloaded += (_, _) => _popup.IsOpen = false;
        Content = _button;
        Refresh();
    }

    public bool IsEditorCreated => _popup.Child is not null;

    public void OpenEditor()
    {
        if (_popup.IsOpen) { _popup.IsOpen = false; return; }
        if (_popup.Child is null)
        {
            var picker = new ColorWheel();
            picker.SetBinding(ColorWheel.SelectedColorProperty, new Binding(nameof(SelectedColor)) { Source = this, Mode = BindingMode.TwoWay });
            var stack = new StackPanel();
            stack.Children.Add(picker);
            var close = new Button { Content = "Gotowe", Margin = new Thickness(0, 8, 0, 0), ToolTip = "Zamyka edytor; wybrany kolor jest już widoczny i zapisywany automatycznie." };
            close.SetResourceReference(StyleProperty, "ToolbarButton");
            close.Click += (_, _) => _popup.IsOpen = false;
            stack.Children.Add(close);
            var border = new Border { Width = 260, Child = new ScrollViewer { Content = stack, MaxHeight = Math.Min(460, SystemParameters.WorkArea.Height - 80), VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            border.SetResourceReference(StyleProperty, "PanelPopupCard");
            border.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { _popup.IsOpen = false; e.Handled = true; } };
            _popup.Child = border;
        }
        _popup.IsOpen = true;
        _popup.Child!.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void Refresh()
    {
        if (_sample is null) return;
        var color = new Settings.ApplicationSettings { SurfaceColor = SelectedColor }.Normalize().SurfaceColor;
        if (color is null) _sample.SetResourceReference(Border.BackgroundProperty, DefaultBrushKey);
        else _sample.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        _label.Text = color is null ? "Z motywu" : "Własny kolor";
    }
}
