using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UberDashboard.App.Settings;
using UberDashboard.App.Theming;

namespace UberDashboard.App.Controls;

/// <summary>Tokeny wyglądu ograniczone do pojedynczej karty.</summary>
public sealed class PanelAppearanceBorder : Border
{
    private static readonly DependencyProperty ThemeRevisionProperty = DependencyProperty.Register("ThemeRevision", typeof(object), typeof(PanelAppearanceBorder), new PropertyMetadata(null, (d, _) => ((PanelAppearanceBorder)d).Refresh()));
    private static readonly DependencyProperty SurfaceOpacityProperty = DependencyProperty.Register("SurfaceOpacity", typeof(double), typeof(PanelAppearanceBorder), new PropertyMetadata(1d, (d, _) => ((PanelAppearanceBorder)d).Refresh()));
    private static readonly DependencyProperty PanelTextProperty = DependencyProperty.Register("PanelText", typeof(Brush), typeof(PanelAppearanceBorder), new PropertyMetadata(null, (d, _) => ((PanelAppearanceBorder)d).Refresh()));
    public PanelAppearanceBorder() { SetResourceReference(PanelTextProperty, "Brush.Panel.DefaultText"); SetResourceReference(ThemeRevisionProperty, "Token.ThemeRevision"); SetResourceReference(SurfaceOpacityProperty, "Token.BackgroundSurfaceOpacity"); }
    public static readonly DependencyProperty AppearanceProperty = DependencyProperty.Register(nameof(Appearance), typeof(PanelAppearanceViewModel), typeof(PanelAppearanceBorder), new PropertyMetadata(null, Changed));
    public PanelAppearanceViewModel? Appearance { get => (PanelAppearanceViewModel?)GetValue(AppearanceProperty); set => SetValue(AppearanceProperty, value); }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var border = (PanelAppearanceBorder)d;
        if (e.OldValue is PanelAppearanceViewModel oldValue) PropertyChangedEventManager.RemoveHandler(oldValue, border.Update, "");
        if (e.NewValue is PanelAppearanceViewModel newValue) PropertyChangedEventManager.AddHandler(newValue, border.Update, "");
        border.Refresh();
    }
    private void Update(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(PanelAppearanceViewModel.Snapshot)) Refresh(); }
    private void Refresh()
    {
        if (Appearance is not { } appearance) return;
        var settings = appearance.Snapshot;
        foreach (var key in new[] { "Brush.Surface", "Brush.Surface.Alt", "Brush.Text", "Brush.Text.Muted", "Token.FontFamily", "Token.FontSize.Body", "Token.FontSize.Caption" }) Resources.Remove(key);
        var color = settings.BackgroundColor;
        if (color is null && !string.IsNullOrWhiteSpace(settings.WallpaperPath)
            && System.Windows.Application.Current?.TryFindResource("Brush.Surface") is SolidColorBrush themeSurface) color = themeSurface.Color.ToString();
        if (color is not null)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Opacity = (double)GetValue(SurfaceOpacityProperty);
            if (!string.IsNullOrWhiteSpace(settings.WallpaperPath)) brush.Opacity *= 1 - settings.WallpaperOpacity * .65;
            Resources["Brush.Surface"] = brush; Resources["Brush.Surface.Alt"] = brush;
        }
        var text = settings.OverrideTextColor && settings.TextColor is { } custom
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(custom))
            : GetValue(PanelTextProperty) as Brush;
        if (text is not null) { Resources["Brush.Text"] = text; Resources["Brush.Text.Muted"] = text; }
        if (!settings.CustomizeText) return;
        if (settings.FontFamily is { } font) Resources["Token.FontFamily"] = new FontFamily(font);
        Resources["Token.FontSize.Body"] = settings.FontSize;
        Resources["Token.FontSize.Caption"] = Math.Max(10, settings.FontSize - 1);
    }
}
