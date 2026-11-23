using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using IOPath = System.IO.Path;

static class Check
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Assembly Assembly = typeof(Sputnik.App).Assembly;
    static Type T(string name) => Assembly.GetType("Sputnik." + name)!;
    static object? Call(object target, string method, params object?[] args) =>
        (target as Type ?? target.GetType()).GetMethod(method, Flags)!.Invoke(target is Type ? null : target, args);
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static IEnumerable<TNode> Find<TNode>(DependencyObject root) where TNode : DependencyObject
    {
        if (root is TNode node) yield return node;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Find<TNode>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    static TNode Named<TNode>(DependencyObject root, string name) where TNode : DependencyObject =>
        Find<TNode>(root).Single(node => AutomationProperties.GetName(node) == name);
    static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    static void Layout(Window window) { window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
    static bool VisiblePixels(BitmapSource source)
    {
        var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4 + 3] > 32) > 8;
    }
    static void Render(Window window, string path)
    {
        Layout(window); var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * 2), (int)Math.Ceiling(root.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(root); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); png.Save(file);
    }
    [STAThread]
    static void Main(string[] args)
    {
        string data = IOPath.GetFullPath(args[0]); Directory.CreateDirectory(data);
        string programFolder = IOPath.Combine(data, "Программы с пробелами"); Directory.CreateDirectory(programFolder);
        string program = IOPath.Combine(programFolder, "Тестовая программа.exe");
        File.Copy(IOPath.Combine(AppContext.BaseDirectory, "Check.exe"), program, true);
        var native = (BitmapSource)Call(T("FavoriteIcons"), "ReadApplicationIcon", program)!;
        Assert(native is not null && native.IsFrozen && VisiblePixels(native), "Executable icon was not extracted");
        Assert(Call(T("FavoriteIcons"), "ReadApplicationIcon", IOPath.Combine(data, "missing.exe")) is null, "Missing file accepted");
        string textFile = IOPath.Combine(data, "not-an-app.txt"); File.WriteAllText(textFile, "fixture");
        Assert(Call(T("FavoriteIcons"), "ReadApplicationIcon", textFile) is null, "Non-application accepted");
        string noIcon = IOPath.Combine(data, "no-icon.exe"); File.WriteAllText(noIcon, "No executable icon resource");
        Assert(Call(T("FavoriteIcons"), "ReadApplicationIcon", noIcon) is null, "Missing icon resource did not fall back");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Sputnik;component/Components/Controls.xaml") });
        string store = IOPath.Combine(data, "workspace.json"); File.WriteAllText(store, "{\"favorites\":[]}");
        var window = new Sputnik.MainWindow { Left = 100, Top = 100 };
        typeof(Sputnik.MainWindow).GetField("data", Flags)!.SetValue(window, data);
        var controlsType = T("DesktopControls");
        var favoriteType = controlsType.GetNestedType("Favorite", BindingFlags.NonPublic)!;
        object FromSettings(JsonObject favorite) => favoriteType.GetMethod("FromSettings", Flags)!.Invoke(null, new object[] { favorite })!;
        object? Icon(JsonObject favorite) => Call(controlsType, "ReadFavoriteIcon", FromSettings(favorite));
        JsonObject Saved() => JsonNode.Parse(File.ReadAllText(store))!["favorites"]![0]!.AsObject();
        try
        {
            window.Show(); Call(window, "OpenSection", "favorites"); Layout(window);
            Click(Named<Button>(window, "Добавить сайт или программу")); Layout(window);
            Named<RadioButton>(window, "Программа").IsChecked = true;
            Named<TextBox>(window, "Название").Text = "QA application";
            Named<TextBox>(window, "Адрес или путь").Text = program;
            var icons = Named<ComboBox>(window, "Значок"); icons.SelectedIndex = 0;
            var mono = Named<CheckBox>(window, "Монохромный"); mono.IsChecked = false; Layout(window);
            var preview = Named<Border>(window, "Предпросмотр значка");
            Assert(preview.Child is Image color && color.Source is BitmapSource colorImage && VisiblePixels(colorImage), "Color executable preview missing");
            mono.IsChecked = true; Layout(window);
            Assert(preview.Child is Rectangle { OpacityMask: ImageBrush { ImageSource: BitmapSource mask } } && VisiblePixels(mask), "Monochrome executable preview missing");
            Render(window, IOPath.Combine(data, "application-monochrome.png"));
            Click(Named<Button>(window, "Сохранить")); Layout(window);
            var saved = Saved();
            Assert(saved["iconSource"]!.ToString() == "app" && saved["monochrome"]!.GetValue<bool>(), "Application icon source or monochrome mode was not saved");
            Assert(Icon(saved) is Rectangle { OpacityMask: ImageBrush { ImageSource: BitmapSource listMask } } && VisiblePixels(listMask), "Saved/sphere icon missing");
            Assert(Find<Viewbox>(window).Any(v => v.Child is Rectangle { OpacityMask: ImageBrush }), "Favorite list lost monochrome icon");
            var host = new Window { Width = 230, Height = 230, Left = 500, Top = 500 };
            var controls = Activator.CreateInstance(controlsType, host, new Action<string>(_ => { }), new Action(() => { }))!;
            try
            {
                var favorites = Array.CreateInstance(favoriteType, 1); favorites.SetValue(FromSettings(saved), 0);
                Call(controls, "SetFavorites", favorites);
                var spheres = (Window[])controlsType.GetField("spheres", Flags)!.GetValue(controls)!;
                Assert(((Viewbox)((Button)spheres.Single().Content).Content).Child is Rectangle { OpacityMask: ImageBrush }, "Desktop sphere lost application icon");
            }
            finally { ((IDisposable)controls).Dispose(); host.Close(); }
            Call(window, "EditFavorite", saved); Layout(window);
            Assert(Named<ComboBox>(window, "Значок").SelectedIndex == 0 && Named<CheckBox>(window, "Монохромный").IsChecked == true,
                "Editor did not restore application icon mode");
            Assert(Named<Border>(window, "Предпросмотр значка").Child is Rectangle { OpacityMask: ImageBrush }, "Reopened preview missing");
            Named<CheckBox>(window, "Монохромный").IsChecked = false;
            Click(Named<Button>(window, "Сохранить")); Layout(window);
            Assert(Icon(Saved()) is Image { Source: BitmapSource savedColor } && VisiblePixels(savedColor), "Color mode did not survive save");
            Call(window, "EditFavorite", Saved()); Layout(window);
            Named<ComboBox>(window, "Значок").SelectedValue = "cpu";
            Click(Named<Button>(window, "Сохранить")); Layout(window);
            Assert(Saved()["iconSource"]!.ToString() == "library" && Icon(Saved()) is null, "Explicit library icon was overridden");
            Console.WriteLine("PASS native EXE icon, spaced Unicode path, mono/color preview, save/reopen, favorite list, desktop sphere, explicit library choice, missing-file fallback; process64=" + Environment.Is64BitProcess);
        }
        finally { window.Close(); app.Shutdown(); }
    }
}
