using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        void Require(bool valid, string message) { if (!valid) throw new Exception(message); }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Sputnik;component/Components/Controls.xaml") });
        string data = Path.GetFullPath("artifacts/pet-theme-check/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        string path = Path.Combine(data, "workspace.json");
        File.WriteAllText(path, """{"appearanceMode":"custom","themed":false,"customTheme":{"accent":"#D3AE63","background":"#211C16","surface":"#30271F"}}""");
        var window = new Sputnik.MainWindow();
        try
        {
            var type = window.GetType();
            type.GetField("data", flags)!.SetValue(window, data);
            JsonNode Saved() => JsonNode.Parse(File.ReadAllText(path))!;
            string custom = Saved()["customTheme"]!.ToJsonString();
            foreach (bool enabled in new[] { true, false, true })
            {
                type.GetMethod("SelectPet", flags)!.Invoke(window, ["christmas-tree", enabled]);
                var saved = Saved();
                Require(saved["active"]!.ToString() == "christmas-tree", "Pet selection was not saved");
                Require(saved["appearanceMode"]!.ToString() == (enabled ? "pet" : "default") && saved["themed"]!.GetValue<bool>() == enabled, "Explicit palette selection is overridden by custom mode");
                Require(saved["customTheme"]!.ToJsonString() == custom, "Saved custom palette was discarded");
            }
            var assembly = typeof(Sputnik.App).Assembly;
            var themeType = assembly.GetType("Sputnik.PetTheme")!;
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var theme = JsonSerializer.Deserialize(File.ReadAllText("PET/christmas-tree/theme.json"), themeType, options)!;
            assembly.GetType("Sputnik.ThemePalette")!.GetMethod("Apply", flags)!.Invoke(null, [theme]);
            foreach (var pair in new[] { ("Accent", "#91B879"), ("Surface", "#273328"), ("Background", "#182119") })
            {
                var control = new Border();
                control.SetResourceReference(Border.BackgroundProperty, "Theme" + pair.Item1);
                Require(((SolidColorBrush)control.Background).Color == (Color)ColorConverter.ConvertFromString(pair.Item2), "Dynamic theme role not propagated: " + pair.Item1);
            }
            Console.WriteLine("PASS WPF pet palette: custom → pet → default → pet, saved custom colors retained, tree JSON deserializes and all three dynamic brush roles update.");
        }
        finally { window.Close(); }
    }
}
