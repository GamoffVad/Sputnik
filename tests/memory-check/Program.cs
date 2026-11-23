using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class Check
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly Assembly = typeof(Sputnik.App).Assembly;
    static Type T(string name) => Assembly.GetType("Sputnik." + name)!;
    static object? Call(object target, string name, params object?[] args) =>
        (target as Type ?? target.GetType()).GetMethod(name, Flags)!.Invoke(target is Type ? null : target, args);
    static object? Field(object target, string name) => target.GetType().GetField(name, Flags)!.GetValue(target);
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static byte[] Pixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[source.PixelWidth * source.PixelHeight * 4];
        converted.CopyPixels(bytes, source.PixelWidth * 4, 0); return bytes;
    }
    static BitmapSource Read(string path) => (BitmapSource)Call(T("DesktopPetWindow"), "ReadSheet", path)!;
    static BitmapSource Detach(BitmapSource source) => (BitmapSource)Call(T("DesktopPetWindow"), "DetachBitmap", source)!;
    static void WaitForPreload(object target)
    {
        if (Field(target, target.GetType().Name == "PetPreviewSheets" ? "next" : "preloaded") is Task next)
            next.GetAwaiter().GetResult();
    }
    static BitmapSource? held;
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference AdvanceFrame(object window, int frame)
    {
        var previous = new WeakReference(Field(window, "sheet"));
        WaitForPreload(window);
        Call(window, "ShowFrame", frame);
        return previous;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference AdvanceClip(object window, int clip)
    {
        var previous = new WeakReference(Field(window, "sheet"));
        Call(window, "SetPlayback", "clip", clip);
        return previous;
    }
    static void AssertCollected(WeakReference previous, string message)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert(!previous.IsAlive, message);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static BitmapSource ProfileCrop(string path, bool detached)
    {
        var source = Read(path);
        var crop = new CroppedBitmap(source, new Int32Rect(0, 0, 960, 960)); crop.Freeze();
        return detached ? Detach(crop) : crop;
    }
    [STAThread]
    static void Main(string[] args)
    {
        if (args[0] == "--profile")
        {
            held = ProfileCrop(args[2], args[1] == "detached"); Pixels(held);
            // Only the benchmark forces collection, to measure retained native
            // storage. The application never forces GC or trims its working set.
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Thread.Sleep(400);
            using var process = Process.GetCurrentProcess(); process.Refresh();
            Console.WriteLine($"{args[1]} working={process.WorkingSet64 / 1048576d:F1}MiB private={process.PrivateMemorySize64 / 1048576d:F1}MiB; {held.PixelWidth}x{held.PixelHeight}");
            return;
        }
        string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        const int width = 24, height = 32;
        var paths = Enumerable.Range(0, 3).Select(i => Path.Combine(root, $"sheet-{i}.png")).ToArray();
        for (int page = 0; page < paths.Length; page++)
        {
            var pixels = new byte[width * 2 * height * 4];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width * 2; x++)
            {
                int at = (y * width * 2 + x) * 4;
                pixels[at] = (byte)(25 + page * 40); pixels[at + 1] = (byte)(10 + x * 3);
                pixels[at + 2] = (byte)(20 + y * 5); pixels[at + 3] = (byte)(x < width ? 128 : 255);
            }
            var image = BitmapSource.Create(width * 2, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 2 * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(paths[page]); encoder.Save(file);
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Sputnik;component/Components/Controls.xaml") });
        var sheet = Read(paths[0]); var crop = new CroppedBitmap(sheet, new Int32Rect(0, 0, width, height)); crop.Freeze();
        var copy = Detach(crop);
        Assert(copy is not CroppedBitmap && copy is not BitmapImage && copy.IsFrozen && Pixels(copy).SequenceEqual(Pixels(crop)), "Detaching changed native pixels or retained crop source");
        var clip = new JsonObject { ["name"] = "QA", ["frameWidth"] = width, ["frameHeight"] = height, ["columns"] = 2,
            ["frameCount"] = 6, ["fps"] = 24, ["sheets"] = new JsonArray(paths.Select(path => (JsonNode)new JsonObject { ["file"] = Path.GetFileName(path), ["frames"] = 2 }).ToArray()) };
        var metadata = new JsonObject { ["id"] = "qa-memory", ["name"] = "QA memory", ["animations"] = new JsonArray(clip) };
        var portrait = (BitmapSource)Call(T("MainWindow"), "PetPortrait", metadata, root)!;
        Assert(portrait is not CroppedBitmap && portrait is not BitmapImage && Pixels(portrait).SequenceEqual(Pixels(crop)), "Portrait retains the full atlas or changed alpha");
        var window = (Window)Activator.CreateInstance(T("DesktopPetWindow"), new Action<string>(_ => { }), new Action(() => { }))!;
        try
        {
            ((DispatcherTimer)Field(window, "timer")!).Stop();
            var pet = JsonSerializer.Deserialize(metadata.ToJsonString(), T("DesktopPetWindow").GetNestedType("Pet", BindingFlags.NonPublic)!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Call(window, "UsePet", pet, root, null);
            for (int frame = 0; frame < 6; frame++)
            {
                var previous = AdvanceFrame(window, frame);
                var expected = new CroppedBitmap(Read(paths[frame / 2]), new Int32Rect((frame % 2) * width, 0, width, height));
                var actual = ((Image)Field(window, "sprite")!).Source as BitmapSource;
                Assert(actual is not null && Pixels(actual).SequenceEqual(Pixels(expected)), "Desktop lost/reordered native frame " + frame);
                if (frame is 2 or 4) AssertCollected(previous, "Historical segmented atlas retained");
            }
            string assets = (string)T("DesktopPetWindow").GetField("Assets", Flags)!.GetValue(null)!;
            string fixture = "qa-memory-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.Combine(assets, fixture));
            var standalone = new JsonArray();
            for (int page = 0; page < paths.Length; page++)
            {
                File.Copy(paths[page], Path.Combine(assets, fixture, Path.GetFileName(paths[page])));
                standalone.Add(new JsonObject { ["name"] = "Builtin " + page, ["file"] = fixture + "/" + Path.GetFileName(paths[page]),
                    ["frameWidth"] = width, ["frameHeight"] = height, ["columns"] = 2, ["frameCount"] = 2, ["fps"] = 24 });
            }
            var bundled = new JsonObject { ["id"] = "qa-builtin", ["name"] = "QA built-in memory", ["animations"] = standalone };
            var bundledPet = JsonSerializer.Deserialize(bundled.ToJsonString(), T("DesktopPetWindow").GetNestedType("Pet", BindingFlags.NonPublic)!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Call(window, "UsePet", bundledPet, assets, null);
            Call(window, "SetPlayback", "clip", 0);
            AssertCollected(AdvanceClip(window, 1), "Historical built-in atlas retained after changing clips");
            AssertCollected(AdvanceClip(window, 2), "Second historical built-in atlas retained");
            var sameSheet = Field(window, "sheet");
            Call(window, "SetPlayback", "clip", 2);
            Assert(ReferenceEquals(sameSheet, Field(window, "sheet")), "Pinned single-sheet loop decoded an identical atlas again");
            Assert(Field(window, "preloaded") is null, "Pinned single-sheet loop retains unused preload");
        }
        finally { window.Close(); }
        var task = (Task)Call(T("PetPreviewSheets"), "Open", (object)paths)!; task.GetAwaiter().GetResult();
        var preview = task.GetType().GetProperty("Result")!.GetValue(task)!;
        try
        {
            foreach (int index in new[] { 0, 1, 2, 0 })
            {
                object?[] parameters = { index, null };
                if (!(bool)Call(preview, "TryGet", parameters)!)
                {
                    WaitForPreload(preview); parameters[1] = null;
                    Assert((bool)Call(preview, "TryGet", parameters)!, "Preview failed to advance");
                }
                Assert(Pixels((BitmapSource)parameters[1]!).SequenceEqual(Pixels(Read(paths[index]))), "Preview changed native sheet pixels");
                WaitForPreload(preview);
            }
        }
        finally { ((IDisposable)preview).Dispose(); }
        Assert(Field(preview, "current") is null && Field(preview, "next") is null, "Hidden preview retains decoded atlases");
        app.Shutdown();
        Console.WriteLine("PASS exact native/alpha pixels, six desktop frames in order, collected segmented AND built-in historical atlases, reused pinned sheet, bounded looping preview and disposal; process64=" + Environment.Is64BitProcess);
    }
}
