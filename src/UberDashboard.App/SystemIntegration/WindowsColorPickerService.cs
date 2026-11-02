using System.Drawing;
using System.Windows.Forms;

namespace UberDashboard.App.SystemIntegration;

/// <summary>Systemowy picker koloru Windows z fallbackiem do ręcznego hex.</summary>
public sealed class WindowsColorPickerService : IColorPickerService
{
    public bool TryPickColor(string? initialColor, out string? selectedColor)
    {
        using var dialog = new ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            SolidColorOnly = false,
        };

        if (!string.IsNullOrWhiteSpace(initialColor) && TryParse(initialColor, out var color))
        {
            dialog.Color = color;
        }

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            selectedColor = null;
            return false;
        }

        selectedColor = $"#{dialog.Color.A:X2}{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        return true;
    }

    private static bool TryParse(string value, out Color color)
    {
        try
        {
            color = ColorTranslator.FromHtml(value);
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }
}
