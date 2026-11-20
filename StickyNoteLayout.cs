using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Sputnik;

internal static class StickyNoteLayout
{
    internal const double Width = 340, Gap = 12, Margin = 16;

    internal static bool TryArrange(Rect workArea, double[] heights, out Rect[] placements)
    {
        placements = [];
        if (heights.Length == 0) return true;
        double width = Math.Min(Width, workArea.Width - Margin * 2);
        double availableHeight = workArea.Height - Margin * 2;
        if (width < StickyNoteWindow.MinimumWidth || availableHeight < StickyNoteWindow.MinimumHeight) return false;
        int columns = (int)Math.Floor((workArea.Width - Margin * 2 + Gap) / (width + Gap));

        Rect[]? Pack(double cap)
        {
            var result = new Rect[heights.Length];
            int column = 0;
            double y = workArea.Top + Margin;
            for (int i = 0; i < heights.Length; i++)
            {
                double height = Math.Max(StickyNoteWindow.MinimumHeight, Math.Min(heights[i], cap));
                if (y + height > workArea.Bottom - Margin + .001)
                {
                    column++;
                    y = workArea.Top + Margin;
                }
                if (column >= columns) return null;
                result[i] = new Rect(workArea.Right - Margin - width - column * (width + Gap), y, width, height);
                y += height + Gap;
            }
            return result;
        }

        if (Pack(availableHeight) is { } original) { placements = original; return true; }
        if (Pack(StickyNoteWindow.MinimumHeight) is not { } compact) return false;
        // Only shorten tall notes when needed to keep every note inside the work area.
        double low = StickyNoteWindow.MinimumHeight, high = availableHeight;
        placements = compact;
        while (high - low > .1)
        {
            double cap = (low + high) / 2;
            if (Pack(cap) is { } candidate) { low = cap; placements = candidate; }
            else high = cap;
        }
        return true;
    }

    internal static IntPtr Monitor(Window window) => MonitorFromWindow(new WindowInteropHelper(window).Handle, 2);

    internal static Rect WorkArea(Window window)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        IntPtr monitor = Monitor(window);
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;
        Point top = window.PointFromScreen(new Point(info.Work.Left, info.Work.Top));
        Point bottom = window.PointFromScreen(new Point(info.Work.Right, info.Work.Bottom));
        return new Rect(Math.Round(window.Left + top.X, 3), Math.Round(window.Top + top.Y, 3),
            Math.Round(bottom.X - top.X, 3), Math.Round(bottom.Y - top.Y, 3));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
