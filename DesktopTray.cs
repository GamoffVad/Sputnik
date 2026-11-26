using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Sputnik;

internal sealed class DesktopTray : IDisposable
{
    private const int Callback = 0x8000 + 55;
    private readonly HwndSource source;
    private readonly Action restore;
    private readonly Action quick;
    private readonly Action hide;
    private readonly Action exit;
    private readonly Func<bool> hidden;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly IntPtr icon;
    private readonly System.Windows.Threading.DispatcherTimer retry = new() { Interval = TimeSpan.FromSeconds(2) };
    private ContextMenu? menu;
    private bool disposed;
    internal bool Available { get; private set; }

    internal DesktopTray(Action restore, Action quick, Action hide, Action exit, Func<bool> hidden)
    {
        this.restore = restore; this.quick = quick; this.hide = hide; this.exit = exit; this.hidden = hidden;
        source = new HwndSource(new HwndSourceParameters("Sputnik.Tray") { Width = 0, Height = 0, WindowStyle = 0 });
        source.AddHook(Message);
        ExtractIconEx(System.IO.Path.Combine(AppContext.BaseDirectory, "Sputnik.exe"), 0, out var large, out var small, 1);
        if (large != IntPtr.Zero) DestroyIcon(large);
        icon = small != IntPtr.Zero ? small : CopyIcon(LoadIcon(IntPtr.Zero, new IntPtr(32512)));
        retry.Tick += (_, _) => EnsureVisible();
        if (!EnsureVisible()) retry.Start();
    }

    private NotifyData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyData>(), Window = source.Handle, Id = 1,
        Flags = 1 | 2 | 4, Message = Callback, Icon = icon,
        Tip = "Спутник — нажмите, чтобы показать", Info = "", InfoTitle = ""
    };

    internal bool EnsureVisible()
    {
        if (disposed) return false;
        if (Available) return true;
        var data = Data();
        Available = ShellNotifyIcon(0, ref data) || ShellNotifyIcon(1, ref data);
        if (Available) { retry.Stop(); data.Version = 3; ShellNotifyIcon(4, ref data); }
        return Available;
    }

    private IntPtr Message(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (disposed) return IntPtr.Zero;
        if ((uint)message == taskbarCreated) { Available = false; if (!EnsureVisible()) retry.Start(); }
        if (message != Callback) return IntPtr.Zero;
        switch ((int)lParam.ToInt64())
        {
            case 0x202: case 0x203: case 0x400: case 0x401: restore(); break;
            case 0x205: case 0x7B: ShowMenu(); break;
        }
        handled = true;
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        if (menu is not null) menu.IsOpen = false;
        menu = new ContextMenu();
        void Item(string label, Action action)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Item(hidden() ? "Показать Спутник" : "Скрыть в трей", hidden() ? restore : hide);
        Item("Быстрый чат", quick);
        menu.Items.Add(new Separator());
        Item("Выход", exit);
        SetForegroundWindow(source.Handle);
        menu.IsOpen = true;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        retry.Stop();
        if (menu is not null) menu.IsOpen = false;
        var data = Data(); ShellNotifyIcon(2, ref data);
        Available = false;
        source.Dispose();
        if (icon != IntPtr.Zero) DestroyIcon(icon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Message; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint operation, ref NotifyData data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr CopyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
}
