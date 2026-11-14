using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace Sputnik;

internal static class FavoriteIcons
{
    private static readonly Lazy<Dictionary<string, Geometry>> Catalog = new(Load);

    public static Geometry? Get(string id) => Catalog.Value.GetValueOrDefault(id);

    private static Dictionary<string, Geometry> Load()
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "icon-catalog.json")));
        return catalog.RootElement.EnumerateArray().SelectMany(group => group.GetProperty("icons").EnumerateArray())
            .ToDictionary(icon => icon.GetProperty("id").GetString()!, icon => Outline(icon.GetProperty("svg").GetString()!));
    }

    private static Geometry Outline(string markup)
    {
        var result = new GeometryGroup();
        foreach (var element in XElement.Parse(markup).Elements())
        {
            double N(string name) => double.TryParse((string?)element.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : 0;
            Geometry? shape = element.Name.LocalName switch
            {
                "path" => Geometry.Parse((string)element.Attribute("d")!),
                "circle" => new EllipseGeometry(new Point(N("cx"), N("cy")), N("r"), N("r")),
                "ellipse" => new EllipseGeometry(new Point(N("cx"), N("cy")), N("rx"), N("ry")),
                "rect" => new RectangleGeometry(new Rect(N("x"), N("y"), N("width"), N("height")), N("rx"), element.Attribute("ry") is null ? N("rx") : N("ry")),
                "line" => new LineGeometry(new Point(N("x1"), N("y1")), new Point(N("x2"), N("y2"))),
                "polyline" => Geometry.Parse("M " + (string)element.Attribute("points")!),
                _ => null,
            };
            if (shape is not null) result.Children.Add(shape);
        }
        result.Freeze();
        return result;
    }
}
