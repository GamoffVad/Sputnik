using System.Windows;
using System.Windows.Media;

namespace Sputnik;

internal sealed record PetTheme(string Accent, string Surface, string Background);

internal static class ThemePalette
{
    private static readonly PetTheme Default = new("#a5e4f6", "#23292f", "#181c20");
    private static PetTheme? current;
    private const string DefaultFont = "Segoe UI";
    private static readonly Lazy<string[]> FontNames = new(() =>
        Fonts.SystemFontFamilies.Select(font => font.Source).Append(DefaultFont)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray());

    static ThemePalette() => Apply(null);

    public static string[] InstalledFonts => FontNames.Value;

    public static void ApplyFont(string? font)
    {
        font ??= DefaultFont;
        if (Application.Current?.Resources is not { } resources ||
            (resources["ThemeFont"] as FontFamily)?.Source == font) return;
        resources["ThemeFont"] = new FontFamily(font);
    }

    public static void UseTheme(this FrameworkElement element, DependencyProperty property, string role) =>
        element.SetResourceReference(property, "Theme" + role);

    public static void Apply(PetTheme? theme)
    {
        theme ??= Default;
        if (current == theme || Application.Current?.Resources is not { } resources) return;
        current = theme;
        Fill(resources, theme);
        if (!resources.Contains("ThemeFont")) ApplyFont(null);
    }

    public static void Fill(ResourceDictionary resources, PetTheme theme)
    {
        var surface = Parse(theme.Surface);
        var background = Parse(theme.Background);
        var accent = Parse(theme.Accent);
        var ink = ContrastInk(surface);
        var accentInk = ContrastInk(accent);
        void Put(string role, Color color) => resources["Theme" + role] = new SolidColorBrush(color);
        Put("Surface", surface);
        Put("Background", background);
        Put("Accent", accent);
        Put("Ink", ink);
        Put("Muted", Mix(surface, ink, .74));
        Put("Edge", Mix(surface, accent, .36));
        Put("Hover", Mix(surface, accent, .18));
        Put("AccentInk", accentInk);
        Put("AccentHover", Mix(accent, accentInk, .12));
        resources["ThemeOrb"] = new RadialGradientBrush(Mix(surface, accent, .33), surface) { GradientOrigin = new Point(.29, .24) };
        resources["ThemeOrbRim"] = new LinearGradientBrush(
            Mix(surface, accent, .45), Mix(surface, accent, .10), new Point(.1, 0), new Point(.85, 1));
        resources["ThemeOrbRimFocus"] = new LinearGradientBrush(
            Mix(surface, accent, .72), Mix(surface, accent, .30), new Point(.1, 0), new Point(.85, 1));
    }

    private static Color Parse(string color) => (Color)ColorConverter.ConvertFromString(color);

    private static Color Mix(Color first, Color second, double ratio)
    {
        byte Blend(byte a, byte b) => (byte)Math.Round(a + (b - a) * ratio);
        return Color.FromRgb(Blend(first.R, second.R), Blend(first.G, second.G), Blend(first.B, second.B));
    }

    private static Color ContrastInk(Color color)
    {
        static double Linear(byte channel)
        {
            double value = channel / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        double lightness = .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        return lightness > .179 ? Colors.Black : Colors.White;
    }
}
