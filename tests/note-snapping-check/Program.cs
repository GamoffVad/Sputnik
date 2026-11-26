using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

sealed class TestApp : Sputnik.App { protected override void OnStartup(StartupEventArgs e) { } }
static class Program
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly Assembly = typeof(Sputnik.App).Assembly;
    static object? Call(object target, string name, params object?[] args) =>
        (target as Type ?? target.GetType()).GetMethod(name, Flags)!.Invoke(target is Type ? null : target, args);
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Equal(Rect actual, Rect expected, string message) => Assert(actual == expected, $"{message}: {actual} != {expected}");
    static void Pump() => Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    static Rect Snap(Rect moving, Rect[] targets, double scale = 1) => (Rect)Call(Assembly.GetType("Sputnik.NoteSnapping")!, "Snap", moving, targets, scale, scale)!;
    static Rect Scale(Rect r, double s) => new(r.X * s, r.Y * s, r.Width * s, r.Height * s);
    [STAThread]
    static void Main(string[] args)
    {
        var target = new Rect(100, 100, 340, 320);
        var cases = new (Rect Input, Rect Expected)[] {
            (new(457,106,340,320),new(452,100,340,320)),
            (new(-257,94,340,320),new(-252,100,340,320)),
            (new(106,437,340,320),new(100,432,340,320)),
            (new(94,-237,340,320),new(100,-232,340,320)),
            (new(137,450,300,200),new(140,450,300,200)),
            (new(167,450,200,200),new(170,450,200,200)),
            (new(470,217,200,200),new(470,220,200,200)),
            (new(470,157,200,200),new(470,160,200,200)),
            (new(105,900,340,320),new(105,900,340,320)),
            (new(463,100,340,320),new(463,100,340,320)),
        };
        foreach (double scale in new[] { 1d, 1.25, 1.5, 2 })
            foreach (var test in cases) Equal(Snap(Scale(test.Input,scale),[Scale(target,scale)],scale),Scale(test.Expected,scale),"Axis/gap/DPI snapping");
        var negative = target; negative.Offset(-2000,-1000);
        var candidate = cases[0].Input; candidate.Offset(-2000,-1000);
        var expected = cases[0].Expected; expected.Offset(-2000,-1000);
        Equal(Snap(candidate,[negative]),expected,"Negative monitor coordinates");
        Equal(Snap(candidate,[]),candidate,"No visible targets");
        var competing = new Rect(101,100,340,320);
        Equal(Snap(new(454,102,340,320),[target,competing]),new(453,100,340,320),"Nearest neighbour wins");

        var app = new TestApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Sputnik;component/Components/Controls.xaml") });
        var theme = Activator.CreateInstance(Assembly.GetType("Sputnik.PetTheme")!,"#C18D6B","#221B17","#352923");
        Call(Assembly.GetType("Sputnik.ThemePalette")!,"Fill",app.Resources,theme);
        string data = Path.GetFullPath(args.Length == 0 ? "artifacts/note-snapping-check" : args[0]);
        Directory.CreateDirectory(data);
        Assert(!File.Exists(Path.Combine(data,"notes.json")),"Use a fresh isolated test directory");
        var managerType = Assembly.GetType("Sputnik.StickyNotes")!;
        var manager = Activator.CreateInstance(managerType, data, new Action<Window>(w=>{w.Left=100;w.Top=100;}),new Action(()=>{}))!;
        var a = (string)Call(manager,"CreateText","Первая заметка")!;
        var b = (string)Call(manager,"CreateText","Вторая заметка")!;
        Window WindowFor(string id) => (Window)((IDictionary)managerType.GetField("windows",Flags)!.GetValue(manager)!)[id]!;
        var first = WindowFor(a); var second = WindowFor(b);
        try
        {
            first.Left=100;first.Top=100;second.Left=457;second.Top=106;Pump();
            Rect stationary=Bounds(first), raw=Bounds(second);
            var dpi = PresentationSource.FromVisual(second)!.CompositionTarget.TransformToDevice;
            Rect wanted=Snap(raw,[stationary],dpi.M11);
            Equal(Propose(second,raw),wanted,"Native WM_MOVING hook not applied");
            Equal(Bounds(first),stationary,"Neighbour moved");
            first.Hide();Pump();Equal(Propose(second,raw),raw,"Hidden note still attracts");
            first.Show();Pump();
            // Restoring or arranging positions must not itself trigger magnetism.
            second.Left=457;second.Top=106;Pump();Equal(Bounds(second),raw,"Programmatic placement unexpectedly snapped");
            var aligned=Propose(second,raw);
            SetWindowPos(new WindowInteropHelper(second).Handle,IntPtr.Zero,(int)Math.Round(aligned.X),(int)Math.Round(aligned.Y),0,0,0x0015);
            Pump();Equal(Bounds(second),wanted,"Native snapped position not applied");
            Call(manager,"Flush");second.Close();Call(manager,"OpenById",b);second=WindowFor(b);Pump();
            Equal(Bounds(second),wanted,"Snapped position not restored");
            Console.WriteLine("PASS edge/centre alignment, four docking directions, nearest target, 12-DIP gap, 100/125/150/200% DPI, negative monitors; real WM_MOVING hook, hidden target exclusion, stationary neighbour, programmatic placement, saved/reopened position. Isolated notes only.");
        }
        finally { Call(manager,"CloseWindows"); }
    }
    static Rect Bounds(Window window) { GetWindowRect(new WindowInteropHelper(window).Handle,out var r);return new(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top); }
    static Rect Propose(Window window,Rect rect)
    {
        var r=new NativeRect{Left=(int)rect.Left,Top=(int)rect.Top,Right=(int)rect.Right,Bottom=(int)rect.Bottom};
        IntPtr memory=Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try { Marshal.StructureToPtr(r,memory,false);SendMessage(new WindowInteropHelper(window).Handle,0x0216,IntPtr.Zero,memory);r=Marshal.PtrToStructure<NativeRect>(memory);return new(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top); }
        finally { Marshal.FreeHGlobal(memory); }
    }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
}
