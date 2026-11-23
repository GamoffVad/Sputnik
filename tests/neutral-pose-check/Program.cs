using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

class Check
{
    [STAThread]
    static void Main()
    {
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", Path.GetFullPath("bin/Release/net10.0-windows/"));
        string data = Path.GetFullPath("artifacts/neutral-pose-check/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        var assembly = typeof(Sputnik.App).Assembly;
        var packages = assembly.GetType("Sputnik.PetPackages")!;
        object Invoke(string name, params object[] args) => packages.GetMethod(name)!.Invoke(null, args)!;
        byte[] Png(int width, int height, Color color)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(width / 4, height / 4, width / 2, height / 2));
            var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            image.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
        }
        var assets = new Dictionary<string, byte[]> {
            ["neutral.png"] = Png(64, 96, Colors.Gold),
            ["small.png"] = Png(24 * 3, 32, Colors.Crimson),
            ["large.png"] = Png(40 * 3, 28, Colors.Cyan)
        };
        var manifest = JsonNode.Parse("""
        {"schemaVersion":3,"id":"qa-shared-neutral","name":"QA shared neutral",
        "theme":{"name":"QA","accent":"#FFFFFF","surface":"#222222","background":"#111111"},
        "neutral":{"file":"neutral.png","viewport":{"x":-16,"y":-16,"width":128,"height":128}},
        "animations":[
          {"name":"Small source","file":"small.png","frameWidth":24,"frameHeight":32,"columns":3,"frameCount":3,"fps":24},
          {"name":"Large source","file":"large.png","frameWidth":40,"frameHeight":28,"columns":3,"frameCount":3,"fps":24,"viewport":{"x":-5,"y":-10,"width":60,"height":60}}
        ]}
        """)!;
        string Package(JsonNode metadata, int extras = 38)
        {
            string path = Path.Combine(data, Guid.NewGuid() + ".pet");
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(metadata.ToJsonString());
            foreach (var pair in assets) using (var stream = zip.CreateEntry(pair.Key).Open()) stream.Write(pair.Value);
            for (int i = 0; i < extras; i++) using (var stream = zip.CreateEntry($"extra-{i}.png").Open()) stream.Write(assets["small.png"]);
            return path;
        }
        string pending = Path.Combine(data, "pending"), library = Path.Combine(data, "pets");
        var preview = (JsonObject)Invoke("Stage", Package(manifest), pending, library);
        Assert(preview["neutral"]!["viewport"]!["x"]!.GetValue<double>() == -16, "Neutral viewport lost");
        Invoke("Install", preview["token"]!.ToString(), pending, library);
        string folder = Path.Combine(library, "qa-shared-neutral");
        foreach (var pair in new[] { ("neutral.png", "neutral.png"), ("small.png", "clip-0.png"), ("large.png", "clip-1.png") })
            Assert(assets[pair.Item1].SequenceEqual(File.ReadAllBytes(Path.Combine(folder, pair.Item2))), "Importer modified native PNG");
        void Reject(JsonNode metadata, int extras = 38)
        {
            try { Invoke("Stage", Package(metadata, extras), pending, library); }
            catch (TargetInvocationException e) when (e.InnerException is InvalidDataException) { return; }
            throw new Exception("Invalid package accepted");
        }
        var bad = manifest.DeepClone(); bad.AsObject().Remove("neutral"); Reject(bad);
        bad = manifest.DeepClone(); bad["neutral"]!["file"] = "../neutral.png"; Reject(bad);
        bad = manifest.DeepClone(); bad["neutral"]!["viewport"]!["width"] = 0; Reject(bad);
        bad = manifest.DeepClone(); bad["neutral"]!["viewport"]!["x"] = 100; Reject(bad);
        bad = manifest.DeepClone(); bad["neutral"]!["blendFrames"] = 9; Reject(bad);
        bad = manifest.DeepClone(); bad["neutral"]!["blendFrames"] = -1; Reject(bad);
        var noBlend = manifest.DeepClone(); noBlend["neutral"]!["blendFrames"] = 0;
        var noBlendPreview = (JsonObject)Invoke("Stage", Package(noBlend), pending, library);
        Assert(noBlendPreview["neutral"]!["blendFrames"]!.GetValue<int>() == 0, "Explicit transition disable lost");
        Reject(manifest, 253); // 257 entries, above the 256-entry collection limit.
        Console.WriteLine("PASS: 42-entry v3 package, native PNG bytes preserved; missing neutral, traversal, invalid viewports and excess entries rejected.");

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Sputnik;component/Components/Controls.xaml") });
        var transition = assembly.GetType("Sputnik.PetFrames")!.GetMethod("Transition")!;
        foreach (byte opacity in new byte[] { 128, 255 })
        {
            var red = BitmapFrame.Create(new MemoryStream(Png(32, 32, Colors.Red)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var blue = BitmapFrame.Create(new MemoryStream(Png(32, 32, Color.FromArgb(opacity, 0, 0, 255))), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var mixed = (BitmapSource)transition.Invoke(null, new object[] { red, blue, 1, 7, 2, new Size(32, 32), BitmapScalingMode.NearestNeighbor })!;
            byte[] pixels = new byte[32 * 32 * 4]; mixed.CopyPixels(pixels, 32 * 4, 0);
            int centre = (16 * 32 + 16) * 4;
            Assert(Math.Abs(pixels[centre + 3] - (opacity + 255 + 1) / 2) <= 1, "Crossfade changed overlap opacity");
            Assert(Math.Abs(pixels[centre] - (opacity + 1) / 2) <= 1 && Math.Abs(pixels[centre + 2] - 128) <= 1, "Crossfade mixed straight RGB instead of premultiplied colour");
            Assert(pixels[3] == 0, "Crossfade made the background opaque");
        }
        Console.WriteLine("PASS: transition blends premultiplied colour/alpha correctly, retains opaque overlap and transparent background.");
        var type = assembly.GetType("Sputnik.DesktopPetWindow")!;
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var window = (Window)Activator.CreateInstance(type, new Action<string>(_ => { }), new Action(() => { }))!;
        object Call(string name, params object[] args) => type.GetMethod(name, flags)!.Invoke(window, args)!;
        try
        {
            var pet = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(folder, "pet.json")), type.GetNestedType("Pet", BindingFlags.NonPublic)!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Call("UsePet", pet, folder, null);
            ((DispatcherTimer)type.GetField("timer", flags)!.GetValue(window)!).Stop();
            var sprite = (Image)type.GetField("sprite", flags)!.GetValue(window)!;
            byte[] Render(int size, Color background)
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, size, size));
                    dc.DrawImage(sprite.Source, new Rect(0, 0, size, size));
                }
                var image = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
                byte[] pixels = new byte[size * size * 4]; image.CopyPixels(pixels, size * 4, 0); return pixels;
            }
            foreach (int size in new[] { 184, 230, 368, 960 })
            foreach (Color background in new[] { Colors.Black, Colors.White })
            {
                byte[] baseline = null;
                for (int clip = 0; clip < 2; clip++)
                {
                    Call("SetPlayback", "clip", clip);
                    foreach (int frame in new[] { 0, 2 })
                    {
                        Call("ShowFrame", frame); var pixels = Render(size, background);
                        baseline ??= pixels; Assert(pixels.SequenceEqual(baseline), "Endpoint changed across native resolutions/viewports");
                    }
                    Call("ShowFrame", 1);
                    Assert(!Render(size, background).SequenceEqual(baseline), "Action frame incorrectly replaced with neutral");
                }
            }
            Console.WriteLine("PASS: all endpoint pixels match across different native frame sizes/viewports, four display sizes and black/white backgrounds; action frames remain animated.");
        }
        finally { window.Close(); app.Shutdown(); }
    }
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
