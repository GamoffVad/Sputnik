using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class Check
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Assembly Assembly = typeof(Sputnik.App).Assembly;
    static Type T(string name) => Assembly.GetType("Sputnik." + name)!;
    static object? Call(object target, string method, params object[] args)
    {
        try { return (target as Type ?? target.GetType()).GetMethod(method, Flags)!.Invoke(target is Type ? null : target, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null) { throw error.InnerException; }
    }
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Reject(Action operation)
    {
        try { operation(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException) { return; }
        throw new Exception("Unsafe library operation was accepted");
    }
    static IEnumerable<TNode> Find<TNode>(DependencyObject root) where TNode : DependencyObject
    {
        if (root is TNode match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Find<TNode>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    static void Layout(Window window) { window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
    static Button Named(Window window, string name) => Find<Button>(window).Single(b => AutomationProperties.GetName(b) == name);
    static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    static void Render(Window window, string path)
    {
        Layout(window); var root = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * 2), (int)Math.Ceiling(root.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        image.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); encoder.Save(file);
    }
    [STAThread]
    static void Main(string[] args)
    {
        string data = Path.GetFullPath(args[0]); Directory.CreateDirectory(data);
        string library = Path.Combine(data, "pets"), folder = Path.Combine(library, "qa-remove"); Directory.CreateDirectory(folder);
        var pixels = Enumerable.Repeat((byte)255, 24 * 32 * 4).ToArray();
        var bitmap = BitmapSource.Create(24, 32, 96, 96, PixelFormats.Bgra32, null, pixels, 24 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(folder, "clip.png"))) encoder.Save(file);
        byte[] original = File.ReadAllBytes(Path.Combine(folder, "clip.png"));
        var pet = JsonNode.Parse("""
        {"id":"qa-remove","name":"Тестовый питомец","theme":{"name":"QA","accent":"#A5E4F6","surface":"#23292F","background":"#181C20"},
        "animations":[{"name":"Покой","file":"clip.png","frameWidth":24,"frameHeight":32,"frameCount":1,"columns":1,"fps":24}]}
        """)!;
        File.WriteAllText(Path.Combine(folder, "pet.json"), pet.ToJsonString());
        var packages = T("PetPackages");
        Reject(() => Call(packages, "Remove", "../outside", library));
        Reject(() => Call(packages, "Remove", "klyaksa", library));
        Reject(() => Call(packages, "RestoreRemoved", "../outside", library));
        string token = (string)Call(packages, "Remove", "qa-remove", library)!;
        Assert(!Directory.Exists(folder) && ((JsonArray)Call(packages, "Installed", library)!).Count == 0, "Removed pet remains installed");
        string archive = Path.Combine(data, "pet-backups", "deleted-" + token);
        Assert(original.SequenceEqual(File.ReadAllBytes(Path.Combine(archive, "clip.png"))), "Removal changed animation data");
        Directory.CreateDirectory(folder);
        Reject(() => Call(packages, "RestoreRemoved", token, library));
        Directory.Delete(folder); // An empty synthetic collision fixture only.
        Call(packages, "RestoreRemoved", token, library);
        Assert(original.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "clip.png"))), "Undo changed animation data");
        string store = Path.Combine(data, "workspace.json");
        File.WriteAllText(store, "{\"active\":\"qa-remove\",\"themed\":false,\"appearanceMode\":\"custom\",\"favorites\":[],\"reminders\":[]}");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Sputnik;component/Components/Controls.xaml") });
        var window = new Sputnik.MainWindow { Left = 100, Top = 100 };
        typeof(Sputnik.MainWindow).GetField("data", Flags)!.SetValue(window, data);
        try
        {
            window.Show(); Call(window, "OpenSection", "pets"); Layout(window);
            var remove = Named(window, "Удалить питомца"); Assert(remove.IsEnabled, "Installed pet cannot be removed");
            Render(window, Path.Combine(data, "pets-removal.png"));
            Click(remove); Layout(window);
            Assert(!Directory.Exists(folder) && JsonNode.Parse(File.ReadAllText(store))!["active"]!.ToString() == "klyaksa", "Active removal did not fall back to Klyaksa");
            Assert(!Named(window, "Удалить питомца").IsEnabled, "Bundled pet removal is enabled");
            Render(window, Path.Combine(data, "pets-undo.png"));
            Click(Find<Button>(window).Single(b => Equals(b.Content, "Вернуть"))); Layout(window);
            Assert(Directory.Exists(folder) && JsonNode.Parse(File.ReadAllText(store))!["active"]!.ToString() == "qa-remove", "Undo did not restore and select pet");
            Assert(original.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "clip.png"))), "UI changed animation pixels");
            Assert(JsonNode.Parse(File.ReadAllText(store))!["appearanceMode"]!.ToString() == "custom", "Removal changed appearance preferences");
            Console.WriteLine("PASS installed pet removal/undo, active fallback, built-in protection, traversal rejection, no overwrite, exact animation bytes; process64=" + Environment.Is64BitProcess);
        }
        finally { window.Close(); app.Shutdown(); }
    }
}
