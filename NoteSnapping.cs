using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Sputnik;

internal static class NoteSnapping
{
    internal const double Distance = 10, Gap = 12, Reach = 48;

    // All rectangles use the same coordinates: physical pixels for HWNDs,
    // logical points in geometry tests. Never mix WPF Left/Top with HWND bounds.
    internal static Rect Snap(Rect moving, IEnumerable<Rect> neighbours, double scaleX = 1, double scaleY = 1)
    {
        double dx = 0, dy = 0, bestX = Distance * scaleX + .001, bestY = Distance * scaleY + .001;
        void X(double delta) { if (Math.Abs(delta) < bestX) { bestX = Math.Abs(delta); dx = delta; } }
        void Y(double delta) { if (Math.Abs(delta) < bestY) { bestY = Math.Abs(delta); dy = delta; } }
        foreach (var other in neighbours)
        {
            double verticalGap = Math.Max(0, Math.Max(other.Top - moving.Bottom, moving.Top - other.Bottom));
            double horizontalGap = Math.Max(0, Math.Max(other.Left - moving.Right, moving.Left - other.Right));
            if (verticalGap <= Reach * scaleY)
            {
                X(other.Left - moving.Left);
                X(other.Right - moving.Right);
                X((other.Left + other.Right - moving.Left - moving.Right) / 2);
            }
            if (horizontalGap <= Reach * scaleX)
            {
                Y(other.Top - moving.Top);
                Y(other.Bottom - moving.Bottom);
                Y((other.Top + other.Bottom - moving.Top - moving.Bottom) / 2);
            }
            // Dock side by side only while the cards overlap on the other axis.
            if (verticalGap == 0)
            {
                X(other.Right + Gap * scaleX - moving.Left);
                X(other.Left - Gap * scaleX - moving.Right);
            }
            if (horizontalGap == 0)
            {
                Y(other.Bottom + Gap * scaleY - moving.Top);
                Y(other.Top - Gap * scaleY - moving.Bottom);
            }
        }
        moving.Offset(dx, dy);
        return moving;
    }

    internal static void Attach(StickyNoteWindow window, Func<IEnumerable<StickyNoteWindow>> neighbours)
    {
        HwndSource? source = null;
        IntPtr Moving(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WmMoving = 0x0216;
            if (message != WmMoving || lParam == IntPtr.Zero || (Keyboard.Modifiers & ModifierKeys.Alt) != 0) return IntPtr.Zero;
            var native = Marshal.PtrToStructure<NativeRect>(lParam);
            var proposed = native.Rect;
            var targets = new List<Rect>();
            foreach (var other in neighbours().ToArray())
                if (other != window && other.IsVisible && other.WindowState != WindowState.Minimized &&
                    PresentationSource.FromVisual(other) is HwndSource target && GetWindowRect(target.Handle, out var bounds))
                    targets.Add(bounds.Rect);
            var scale = source!.CompositionTarget.TransformToDevice;
            var snapped = Snap(proposed, targets, scale.M11, scale.M22);
            int dx = (int)Math.Round(snapped.Left - proposed.Left), dy = (int)Math.Round(snapped.Top - proposed.Top);
            if (dx == 0 && dy == 0) return IntPtr.Zero;
            native.Left += dx; native.Right += dx; native.Top += dy; native.Bottom += dy;
            Marshal.StructureToPtr(native, lParam, false);
            handled = true;
            return new IntPtr(1);
        }
        window.SourceInitialized += (_, _) =>
        {
            source = (HwndSource)PresentationSource.FromVisual(window);
            source.AddHook(Moving);
        };
        window.Closed += (_, _) => { source?.RemoveHook(Moving); source = null; };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        internal readonly Rect Rect => new(Left, Top, Right - Left, Bottom - Top);
    }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect bounds);
}
