using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sputnik;

internal static class RoundedWindow
{
    public const double Radius = 12;

    public static void Apply(Window window, Border frame)
    {
        window.AllowsTransparency = true;
        window.Background = Brushes.Transparent;
        frame.CornerRadius = new CornerRadius(Radius);

        void ClipContent()
        {
            if (frame.Child is not FrameworkElement content) return;
            double radius = Radius - frame.BorderThickness.Left;
            content.Clip = new RectangleGeometry(new Rect(content.RenderSize), radius, radius);
        }

        frame.SizeChanged += (_, _) => ClipContent();
        frame.Loaded += (_, _) => ClipContent();
    }
}
