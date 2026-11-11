using Size = System.Windows.Size;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UberDashboard.App.Controls;

/// <summary>Lays the child out at a fixed logical width and scales it so the width fills 95% of the box; height follows proportionally. <see cref="Scale"/> multiplies the result.</summary>
public sealed class FitColumnsBox : Decorator
{
    public const double LogicalWidth = 750;
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(nameof(Scale), typeof(double), typeof(FitColumnsBox),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsMeasure), value => value is double v && double.IsFinite(v) && v > 0);
    public double Scale { get => (double)GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _shift = new();
    private double _factor = 1;

    public FitColumnsBox()
    {
        ClipToBounds = true;
        var group = new TransformGroup();
        group.Children.Add(_scale);
        group.Children.Add(_shift);
        RenderTransform = group;
    }

    protected override Size MeasureOverride(Size available)
    {
        if (Child is not FrameworkElement child) return default;
        child.Width = LogicalWidth;
        child.Measure(new Size(LogicalWidth, double.PositiveInfinity));
        _factor = (double.IsFinite(available.Width) ? available.Width * 0.95 / LogicalWidth : 1) * Scale;
        return new Size(double.IsFinite(available.Width) ? available.Width : LogicalWidth * _factor, child.DesiredSize.Height * _factor);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is not FrameworkElement child) return finalSize;
        child.Arrange(new Rect(0, 0, LogicalWidth, child.DesiredSize.Height));
        _scale.ScaleX = _scale.ScaleY = _factor;
        _shift.X = Math.Max(0, (finalSize.Width - LogicalWidth * _factor) / 2);
        return finalSize;
    }
}
