using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Sputnik;

// Native DWM glass keeps the WPF window out of the layered-window rendering path.
internal static class WindowMaterial
{
    private sealed record Surface(Window Window, Border Frame);
    private static readonly List<Surface> surfaces = [];
    private static bool enabled;
    private static double transparency = 25;
    private static readonly Version WindowsVersion = ReadWindowsVersion();

    internal static void Configure(JsonObject? state) => Preview(
        state?["windowTransparency"] is JsonValue value && value.TryGetValue<double>(out var percent) ? percent : 25,
        state?["windowGlass"] is JsonValue glass && glass.TryGetValue<bool>(out var on) && on);

    internal static void Preview(double percent, bool glass)
    {
        transparency = Compatibility.Clamp(percent, 0, 70);
        enabled = glass;
        Refresh();
    }

    internal static void Attach(Window window, Border frame)
    {
        var surface = new Surface(window, frame);
        surfaces.Add(surface);
        frame.UseTheme(Border.BackgroundProperty, "WindowSurface");
        window.SourceInitialized += (_, _) => Apply(surface);
        window.IsVisibleChanged += (_, _) => { if (window.IsVisible) Apply(surface); };
        window.Closed += (_, _) => surfaces.Remove(surface);
        Apply(surface);
    }

    internal static void Refresh()
    {
        foreach (var surface in surfaces.ToArray()) Apply(surface);
    }

    private static void Apply(Surface surface)
    {
        var window = surface.Window;
        var color = (window.TryFindResource("ThemeSurface") as SolidColorBrush)?.Color ?? Color.FromRgb(35, 41, 47);
        var source = PresentationSource.FromVisual(window) as HwndSource;
        bool paper = window is StickyNoteWindow;
        // Notes are overlapping paper surfaces. Native blur/Acrylic paints a
        // rectangular backdrop outside their custom region; keep paper opaque.
        // Other panels retain the user's global glass setting.
        bool glass = !paper && source is not null && enabled && transparency > 0 && !SystemParameters.HighContrast && SetBackdrop(source, true, color);
        if (!glass && source is not null) SetBackdrop(source, false, color);
        var fill = new SolidColorBrush(color) { Opacity = glass ? 1 - transparency / 100 : 1 };
        fill.Freeze();
        window.Resources["ThemeWindowSurface"] = fill;
        window.Resources["ThemePageSurface"] = glass ? Brushes.Transparent : new SolidColorBrush(color);
        window.Background = glass ? Brushes.Transparent : new SolidColorBrush(color);
        RoundedWindow.UpdateShape(window, surface.Frame);
        surface.Frame.InvalidateVisual();
    }

    private static bool SetBackdrop(HwndSource source, bool glass, Color color)
    {
        try
        {
            if (DwmIsCompositionEnabled(out bool composition) < 0 || !composition) return false;
            bool applied = false;
            if (WindowsVersion.Major >= 10 && WindowsVersion.Build >= 22621)
            {
                int dark = color.R * .2126 + color.G * .7152 + color.B * .0722 < 128 ? 1 : 0;
                DwmSetWindowAttribute(source.Handle, 20, ref dark, 4);
                int backdrop = glass ? 3 : 1; // Desktop Acrylic / None.
                applied = DwmSetWindowAttribute(source.Handle, 38, ref backdrop, 4) == 0;
            }
            if (!applied && WindowsVersion.Major >= 10)
            {
                var accent = new AccentPolicy { State = glass ? 3 : 0 }; // Native blur behind on Windows 10.
                IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
                try
                {
                    Marshal.StructureToPtr(accent, buffer, false);
                    var data = new CompositionData { Attribute = 19, Data = buffer, Size = new IntPtr(Marshal.SizeOf<AccentPolicy>()) };
                    applied = SetWindowCompositionAttribute(source.Handle, ref data) != 0;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            else if (WindowsVersion.Major < 10)
            {
                var blur = new BlurBehind { Flags = 1, Enabled = glass };
                applied = DwmEnableBlurBehindWindow(source.Handle, ref blur) == 0;
            }
            var margins = new Margins { Left = glass && applied ? -1 : 0 };
            if (DwmExtendFrameIntoClientArea(source.Handle, ref margins) < 0) return false;
            source.CompositionTarget.BackgroundColor = glass && applied ? Colors.Transparent : color;
            return glass && applied;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    private static Version ReadWindowsVersion()
    {
        var version = new OsVersion { Size = Marshal.SizeOf<OsVersion>(), ServicePack = "" };
        return RtlGetVersion(ref version) == 0 ? new Version(version.Major, version.Minor, version.Build) : Environment.OSVersion.Version;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int State, Flags, GradientColor, Animation; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionData { public int Attribute; public IntPtr Data, Size; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BlurBehind
    {
        public uint Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Enabled;
        public IntPtr Region;
        [MarshalAs(UnmanagedType.Bool)] public bool Transition;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OsVersion
    {
        public int Size, Major, Minor, Build, Platform;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string ServicePack;
    }
    [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
    private static extern int RtlGetVersion(ref OsVersion version);
    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr window, ref BlurBehind blur);
    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr window, ref CompositionData data);
}
