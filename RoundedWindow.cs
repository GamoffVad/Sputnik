using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;

namespace Sputnik;

internal static class RoundedWindow
{
    public const double Radius = 12;

    public static void Apply(Window window, Border frame)
    {
        // Settings and notes are opaque surfaces. Avoid layered HWNDs here:
        // their dirty-region updates can leave stale stripes over live controls.
        // Keep per-pixel transparency only for the pet and its spheres.
        window.AllowsTransparency = false;
        window.UseTheme(Window.BackgroundProperty, "Surface");
        window.ResizeMode = ResizeMode.NoResize;
        frame.CornerRadius = new CornerRadius(Radius);
        window.SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(window) is HwndSource { CompositionTarget: { } target })
                target.RenderMode = RenderMode.SoftwareOnly;
            UpdateShape(window, frame);
        };

        void ClipContent()
        {
            if (frame.Child is not FrameworkElement content) return;
            double radius = Math.Max(0, frame.CornerRadius.TopLeft - frame.BorderThickness.Left);
            content.Clip = new RectangleGeometry(new Rect(content.RenderSize), radius, radius);
            frame.InvalidateVisual();
        }

        if (frame.Child is FrameworkElement child) child.SizeChanged += (_, _) => ClipContent();
        frame.Loaded += (_, _) => { UpdateShape(window, frame); ClipContent(); };
        window.SizeChanged += (_, _) => UpdateShape(window, frame);
        window.DpiChanged += (_, _) => window.Dispatcher.BeginInvoke(new Action(() => { UpdateShape(window, frame); ClipContent(); }));
        WindowMaterial.Attach(window, frame);
    }

    internal static void UpdateShape(Window window, Border frame)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource source) return;

        // A custom HWND region disables DWM corner rounding. With Acrylic this
        // leaves a rectangular backdrop behind the rounded WPF border. Let the
        // compositor clip both layers on Windows 11, using its 8-DIP radius.
        int preference = 2; // DWMWCP_ROUND
        if (DwmSetWindowAttribute(source.Handle, 33, ref preference, sizeof(int)) == 0)
        {
            SetWindowRgn(source.Handle, IntPtr.Zero, true);
            frame.CornerRadius = new CornerRadius(8);
        }
        else
        {
            // Windows 7/10 have no DWM corner preference.
            frame.CornerRadius = new CornerRadius(Radius);
            if (!GetWindowRect(source.Handle, out var bounds)) return;
            var scale = source.CompositionTarget.TransformToDevice;
            var region = CreateRoundRectRgn(0, 0, bounds.Right - bounds.Left + 1, bounds.Bottom - bounds.Top + 1,
                (int)Math.Round(Radius * 2 * scale.M11), (int)Math.Round(Radius * 2 * scale.M22));
            // Windows owns the region after a successful SetWindowRgn.
            if (region != IntPtr.Zero && SetWindowRgn(source.Handle, region, true) == 0) DeleteObject(region);
        }
        if (frame.Child is FrameworkElement content)
        {
            double radius = Math.Max(0, frame.CornerRadius.TopLeft - frame.BorderThickness.Left);
            content.Clip = new RectangleGeometry(new Rect(content.RenderSize), radius, radius);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
