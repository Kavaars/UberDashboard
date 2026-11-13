using System.Windows;
using Control = System.Windows.Controls.Control;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using System.Windows.Media;

namespace UberDashboard.App.Features.Weather;

public sealed class WeatherIcon : Control
{
    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(nameof(Code), typeof(int), typeof(WeatherIcon), new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsDayProperty = DependencyProperty.Register(nameof(IsDay), typeof(bool), typeof(WeatherIcon), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IconSetProperty = DependencyProperty.Register(nameof(IconSet), typeof(string), typeof(WeatherIcon), new FrameworkPropertyMetadata("line", FrameworkPropertyMetadataOptions.AffectsRender));
    public int Code { get => (int)GetValue(CodeProperty); set => SetValue(CodeProperty, value); }
    public bool IsDay { get => (bool)GetValue(IsDayProperty); set => SetValue(IsDayProperty, value); }
    public string IconSet { get => (string)GetValue(IconSetProperty); set => SetValue(IconSetProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(size / 48, size / 48));
        var foreground = Foreground ?? Brushes.Gray;
        var colored = IconSet == "color";
        var fill = IconSet != "line";
        var sun = colored ? Brushes.Goldenrod : foreground;
        var cloud = colored ? Brushes.SlateGray : foreground;
        var rain = colored ? Brushes.DodgerBlue : foreground;
        var pen = new Pen(foreground, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var kind = WeatherConditionCatalog.IconKind(Code);
        void Line(double x1, double y1, double x2, double y2, Brush brush) => dc.DrawLine(new Pen(brush, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new Point(x1, y1), new Point(x2, y2));
        if (kind is "clear" or "partly")
        {
            var center = kind == "clear" ? new Point(24, 21) : new Point(18, 16);
            var radius = kind == "clear" ? 8 : 6;
            if (IsDay)
            {
                dc.DrawEllipse(fill ? sun : null, new Pen(sun, 2.2), center, radius, radius);
                for (var i = 0; i < 8; i++) { var a = i * Math.PI / 4; Line(center.X + Math.Cos(a) * (radius + 3), center.Y + Math.Sin(a) * (radius + 3), center.X + Math.Cos(a) * (radius + 6), center.Y + Math.Sin(a) * (radius + 6), sun); }
            }
            else
            {
                // A crescent stays legible without relying on a system glyph.
                dc.DrawGeometry(fill ? sun : null, new Pen(sun, 2.2), Geometry.Parse("M 24,7 A 12,12 0 1 0 34,28 A 10,10 0 0 1 24,7 Z"));
            }
        }
        if (kind is "cloud" or "partly" or "rain" or "snow" or "storm")
        {
            var geometry = Geometry.Parse("M 10,31 C 5,31 5,23 11,22 C 11,13 24,10 29,18 C 37,16 42,21 41,26 C 41,30 38,32 34,32 Z");
            dc.DrawGeometry(fill ? cloud : null, new Pen(cloud, 2.2) { LineJoin = PenLineJoin.Round }, geometry);
        }
        if (kind == "rain") { Line(16, 35, 13, 41, rain); Line(25, 35, 22, 41, rain); Line(34, 35, 31, 41, rain); }
        if (kind == "snow") { Line(15, 36, 15, 42, rain); Line(12, 39, 18, 39, rain); Line(30, 36, 30, 42, rain); Line(27, 39, 33, 39, rain); }
        if (kind == "storm") dc.DrawGeometry(colored ? Brushes.Goldenrod : foreground, null, Geometry.Parse("M 25,32 L 19,40 L 24,40 L 22,46 L 31,37 L 26,37 Z"));
        if (kind == "fog") { Line(9, 18, 39, 18, foreground); Line(13, 25, 43, 25, foreground); Line(8, 32, 37, 32, foreground); }
        if (kind == "unknown") dc.DrawEllipse(foreground, pen, new Point(24, 24), 2, 2);
        dc.Pop(); dc.Pop();
    }
}
