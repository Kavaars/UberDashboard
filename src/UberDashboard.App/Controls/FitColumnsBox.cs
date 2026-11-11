using Size = System.Windows.Size;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UberDashboard.App.Controls;

/// <summary>Scales the child while keeping its layout as wide as the available box. <see cref="Scale"/> multiplies the result.</summary>
public sealed class FitColumnsBox : Decorator
{
    public const double LogicalWidth = 750;
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(nameof(Scale), typeof(double), typeof(FitColumnsBox),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsMeasure), value => value is double v && double.IsFinite(v) && v > 0);
    public double Scale { get => (double)GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
    private readonly ScaleTransform _scale = new(1, 1);
    private double _factor = 1;

    public FitColumnsBox()
    {
        ClipToBounds = true;
    }

    protected override Size MeasureOverride(Size available)
    {
        if (Child is not FrameworkElement child) return default;
        _factor = (double.IsFinite(available.Width) && available.Width > 0 ? available.Width * 0.95 / LogicalWidth : 1) * Scale;
        child.Width = double.IsFinite(available.Width) ? available.Width / _factor : LogicalWidth;
        child.RenderTransform = _scale;
        child.Measure(new Size(child.Width, double.PositiveInfinity));
        return new Size(double.IsFinite(available.Width) ? available.Width : LogicalWidth * _factor, child.DesiredSize.Height * _factor);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is not FrameworkElement child) return finalSize;
        child.Arrange(new Rect(0, 0, finalSize.Width / _factor, child.DesiredSize.Height));
        _scale.ScaleX = _scale.ScaleY = _factor;
        return finalSize;
    }
}
