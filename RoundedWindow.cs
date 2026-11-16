using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Interop;

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

    public static void ApplyNativeRegion(Window window, Border frame)
    {
        frame.CornerRadius = new CornerRadius(Radius);
        window.SetResourceReference(Window.BackgroundProperty, "ThemeSurface");
        void UpdateRegion()
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds)) return;
            var dpi = VisualTreeHelper.GetDpi(window);
            var region = CreateRoundRectRgn(0, 0, bounds.Right - bounds.Left + 1, bounds.Bottom - bounds.Top + 1,
                (int)Math.Round(Radius * 2 * dpi.DpiScaleX), (int)Math.Round(Radius * 2 * dpi.DpiScaleY));
            if (region != IntPtr.Zero && SetWindowRgn(handle, region, true) == 0) DeleteObject(region);
        }

        window.SourceInitialized += (_, _) => UpdateRegion();
        window.SizeChanged += (_, _) => UpdateRegion();
        window.DpiChanged += (_, _) => UpdateRegion();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr region);
}
