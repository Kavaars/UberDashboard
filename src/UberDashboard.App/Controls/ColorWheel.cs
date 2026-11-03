using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;

namespace UberDashboard.App.Controls;

/// <summary>Wspólny wybór HSV z kołem, jasnością, alfą i schowanym polem HEX.</summary>
public sealed class ColorWheel : UserControl
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(string), typeof(ColorWheel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, Changed));
    public string? SelectedColor { get => (string?)GetValue(SelectedColorProperty); set => SetValue(SelectedColorProperty, value); }
    private readonly Wheel _wheel;
    private readonly Slider _brightness = new() { Minimum = 0, Maximum = 1, Value = 1 };
    private readonly Slider _alpha = new() { Minimum = 0, Maximum = 1, Value = 1 };
    private readonly Border _sample = new() { Height = 18, Margin = new Thickness(0, 4, 0, 4) };
    private bool _updating;
    private double _hue, _saturation;

    public ColorWheel()
    {
        var stack = new StackPanel();
        _wheel = new Wheel(Choose) { Width = 150, Height = 150, Focusable = true, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(_wheel, "Koło kolorów — strzałki zmieniają odcień i nasycenie");
        stack.Children.Add(_wheel);
        stack.Children.Add(_sample);
        AddSlider(stack, "Jasność", _brightness);
        AddSlider(stack, "Krycie", _alpha);
        var hex = new TextBox();
        hex.SetResourceReference(StyleProperty, "ThemedTextBox");
        hex.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(SelectedColor)) { Source = this, Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus });
        System.Windows.Automation.AutomationProperties.SetName(hex, "Wartość koloru HEX RGB lub ARGB");
        stack.Children.Add(new Expander { Header = "Wpisz wartość koloru", Content = hex, IsExpanded = false });
        var reset = new Button { Content = "Kolor z motywu", ToolTip = "Usuwa własny kolor i przywraca kolor bieżącego motywu." };
        reset.SetResourceReference(StyleProperty, "PanelActionButton");
        reset.Click += (_, _) => SetCurrentValue(SelectedColorProperty, null);
        stack.Children.Add(reset);
        Content = stack;
        SetResourceReference(ForegroundProperty, "Brush.Text");
        _brightness.ValueChanged += (_, _) => Publish();
        _alpha.ValueChanged += (_, _) => Publish();
    }
    private static void AddSlider(StackPanel stack, string label, Slider slider)
    {
        stack.Children.Add(new TextBlock { Text = label });
        System.Windows.Automation.AutomationProperties.SetName(slider, label + " koloru");
        stack.Children.Add(slider);
    }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ColorWheel)d).ReadColor();
    private void ReadColor()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var value = new UberDashboard.App.Settings.ApplicationSettings { SurfaceColor = SelectedColor }.Normalize().SurfaceColor;
            var color = value is null ? Colors.White : (Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
            var max = Math.Max(color.R, Math.Max(color.G, color.B)) / 255d;
            var min = Math.Min(color.R, Math.Min(color.G, color.B)) / 255d;
            var delta = max - min;
            _saturation = max == 0 ? 0 : delta / max;
            if (delta > 0)
            {
                var r = color.R / 255d; var g = color.G / 255d; var b = color.B / 255d;
                _hue = max == r ? 60 * ((g - b) / delta % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
                _hue = (_hue + 360) % 360;
            }
            _brightness.Value = max; _alpha.Value = color.A / 255d;
            _sample.Background = new SolidColorBrush(color);
            _wheel.SetMarker(_hue, _saturation);
        }
        finally { _updating = false; }
    }
    private void Choose(double hue, double saturation) { _hue = hue; _saturation = saturation; Publish(); }
    private void Publish()
    {
        if (_updating) return;
        var color = FromHsv(_hue, _saturation, _brightness.Value, _alpha.Value);
        SetCurrentValue(SelectedColorProperty, color.ToString());
    }
    internal static Color FromHsv(double hue, double saturation, double value, double alpha = 1)
    {
        var h = ((hue % 360) + 360) % 360 / 60;
        var c = value * saturation; var x = c * (1 - Math.Abs(h % 2 - 1)); var m = value - c;
        var (r, g, b) = (int)h switch { 0 => (c, x, 0d), 1 => (x, c, 0d), 2 => (0d, c, x), 3 => (0d, x, c), 4 => (x, 0d, c), _ => (c, 0d, x) };
        static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return Color.FromArgb(Byte(alpha), Byte(r + m), Byte(g + m), Byte(b + m));
    }
    private sealed class Wheel(Action<double, double> choose) : FrameworkElement
    {
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new WheelPeer(this);
        private static readonly BitmapSource Bitmap = CreateBitmap();
        private double _hue, _saturation;
        public void SetMarker(double hue, double saturation) { _hue = hue; _saturation = saturation; InvalidateVisual(); }
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawImage(Bitmap, new Rect(RenderSize));
            var radius = Math.Min(ActualWidth, ActualHeight) / 2;
            var angle = _hue * Math.PI / 180;
            var point = new Point(ActualWidth / 2 + Math.Cos(angle) * radius * _saturation, ActualHeight / 2 + Math.Sin(angle) * radius * _saturation);
            dc.DrawEllipse(null, new Pen(Brushes.Black, 3), point, 5, 5);
            dc.DrawEllipse(null, new Pen(Brushes.White, 1), point, 5, 5);
        }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { base.OnMouseLeftButtonDown(e); Focus(); CaptureMouse(); Pick(e.GetPosition(this)); e.Handled = true; }
        protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e) { base.OnMouseMove(e); if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) Pick(e.GetPosition(this)); }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { ReleaseMouseCapture(); base.OnMouseLeftButtonUp(e); }
        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) { base.OnKeyDown(e); return; }
            choose((_hue + (e.Key == Key.Left ? -3 : e.Key == Key.Right ? 3 : 0) + 360) % 360,
                Math.Clamp(_saturation + (e.Key == Key.Up ? .02 : e.Key == Key.Down ? -.02 : 0), 0, 1));
            e.Handled = true;
        }
        private void Pick(Point point)
        {
            var x = point.X - ActualWidth / 2; var y = point.Y - ActualHeight / 2;
            choose((Math.Atan2(y, x) * 180 / Math.PI + 360) % 360, Math.Clamp(Math.Sqrt(x * x + y * y) / (Math.Min(ActualWidth, ActualHeight) / 2), 0, 1));
        }
        private static BitmapSource CreateBitmap()
        {
            const int size = 180; var pixels = new byte[size * size * 4];
            for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
            {
                var dx = (x - size / 2d) / (size / 2d); var dy = (y - size / 2d) / (size / 2d);
                var s = Math.Sqrt(dx * dx + dy * dy); if (s > 1) continue;
                var c = FromHsv(Math.Atan2(dy, dx) * 180 / Math.PI, s, 1); var i = (y * size + x) * 4;
                pixels[i] = c.B; pixels[i + 1] = c.G; pixels[i + 2] = c.R; pixels[i + 3] = 255;
            }
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4); bitmap.Freeze(); return bitmap;
        }
        private sealed class WheelPeer(Wheel owner) : System.Windows.Automation.Peers.FrameworkElementAutomationPeer(owner)
        {
            protected override string GetClassNameCore() => "Koło kolorów";
            protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() => System.Windows.Automation.Peers.AutomationControlType.Custom;
        }
    }
}
