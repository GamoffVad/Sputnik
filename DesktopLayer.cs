using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Sputnik;

// Keep the desktop companion above normal windows and Explorer's taskbar
// without activating it or taking keyboard focus from the user's application.
internal sealed class DesktopLayer : IDisposable
{
    private readonly Window window;
    private readonly WinEvent callback;
    private IntPtr foregroundHook;
    private IntPtr shellHook;
    private bool pending;
    private bool disposed;

    internal DesktopLayer(Window window)
    {
        this.window = window;
        callback = OnWindowEvent;
        window.SourceInitialized += (_, _) => Apply(window.Topmost);
        window.IsVisibleChanged += (_, _) => { if (window.IsVisible) Raise(); };
    }

    internal void Apply(bool enabled)
    {
        if (disposed) return;
        window.Topmost = enabled;
        foreach (Window child in window.OwnedWindows) child.Topmost = enabled;
        if (enabled && new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            // OUTOFCONTEXT | SKIPOWNPROCESS: own menus and dialogs keep their order.
            if (foregroundHook == IntPtr.Zero) foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 2);
            if (shellHook == IntPtr.Zero) shellHook = SetWinEventHook(0x8002, 0x8004, IntPtr.Zero, callback, 0, 0, 2);
            Raise();
        }
        else StopWatching();
    }

    private void OnWindowEvent(IntPtr hook, uint eventId, IntPtr handle, int objectId, int childId, uint thread, uint time)
    {
        if (disposed || handle == IntPtr.Zero) return;
        if (eventId != 3)
        {
            if (objectId != 0 || childId != 0) return;
            var name = new StringBuilder(80);
            GetClassName(handle, name, name.Capacity);
            if (name.ToString() is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd")) return;
        }
        if (pending) return;
        pending = true;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { pending = false; Raise(); }));
    }

    internal void Raise()
    {
        if (disposed || !window.Topmost || !window.IsVisible) return;
        const uint noMove = 2, noSize = 1, noActivate = 0x10, noOwnerZOrder = 0x200;
        void RaiseWindow(Window target)
        {
            if (target.IsVisible) SetWindowPos(new WindowInteropHelper(target).Handle, new IntPtr(-1), 0, 0, 0, 0,
                noMove | noSize | noActivate | noOwnerZOrder);
        }
        RaiseWindow(window);
        foreach (Window child in window.OwnedWindows) RaiseWindow(child);
    }

    private void StopWatching()
    {
        if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
        if (shellHook != IntPtr.Zero) UnhookWinEvent(shellHook);
        foregroundHook = shellHook = IntPtr.Zero;
    }

    public void Dispose() { disposed = true; StopWatching(); }

    private delegate void WinEvent(IntPtr hook, uint eventId, IntPtr handle, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder name, int length);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
