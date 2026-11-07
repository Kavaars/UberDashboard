using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace UberDashboard.App.Features.Shortcuts;

/// <summary>Bundled Lucide 0.468.0 artwork; only known embedded resources are accepted.</summary>
internal static class ShortcutIconCatalog
{
    private const string Prefix = "UberDashboard.App.Features.Shortcuts.Assets.Lucide.";
    internal static IReadOnlyList<string> Names { get; } = Assembly.GetExecutingAssembly().GetManifestResourceNames()
        .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".svg", StringComparison.Ordinal))
        .Select(n => n[Prefix.Length..^4]).Order(StringComparer.Ordinal).ToArray();

    internal static ImageSource? Render(string name, Color color)
    {
        if (!Names.Contains(name, StringComparer.Ordinal)) return null;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefix + name + ".svg");
        if (stream is null) return null;
        var root = XDocument.Load(stream).Root ?? throw new InvalidDataException("Invalid bundled icon.");
        var pen = new Pen(new SolidColorBrush(color), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            foreach (var element in root.Elements())
            {
                double N(string attribute, double fallback = 0) => double.Parse((string?)element.Attribute(attribute) ?? fallback.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                Geometry shape = element.Name.LocalName switch
                {
                    "path" => Geometry.Parse((string?)element.Attribute("d") ?? ""),
                    "circle" => new EllipseGeometry(new Point(N("cx"), N("cy")), N("r"), N("r")),
                    "rect" => new RectangleGeometry(new Rect(N("x"), N("y"), N("width"), N("height")), N("rx"), N("ry", N("rx"))),
                    "line" => new LineGeometry(new Point(N("x1"), N("y1")), new Point(N("x2"), N("y2"))),
                    "polyline" or "polygon" => Geometry.Parse("M " + ((string?)element.Attribute("points") ?? "") + (element.Name.LocalName == "polygon" ? " Z" : "")),
                    _ => throw new InvalidDataException("Unsupported bundled icon shape."),
                };
                context.DrawGeometry(null, pen, shape);
            }
        }
        var image = new DrawingImage(drawing); image.Freeze(); return image;
    }
}
