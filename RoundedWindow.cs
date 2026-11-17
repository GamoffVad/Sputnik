using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
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
        // Layered UI windows can retain stale GPU pixels when resized or updated.
        // Keep this per-window so the animated pet retains hardware rendering.
        window.SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(window) is HwndSource { CompositionTarget: { } target })
                target.RenderMode = RenderMode.SoftwareOnly;
        };

        void ClipContent()
        {
            if (frame.Child is not FrameworkElement content) return;
            double radius = Radius - frame.BorderThickness.Left;
            content.Clip = new RectangleGeometry(new Rect(content.RenderSize), radius, radius);
            frame.InvalidateVisual();
        }

        frame.SizeChanged += (_, _) => ClipContent();
        frame.Loaded += (_, _) => ClipContent();
        WindowSizing.Enable(window, frame);
    }
}
