using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Sputnik;

public partial class MainWindow
{
    private void ShowPets(string? message = null)
    {
        var builtins = Read(Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "pets.json")) as JsonArray ?? new();
        var pets = builtins.Concat(PetPackages.Installed(Pets)).OfType<JsonObject>().ToArray();
        var page = PageFields("Питомцы", AsyncButton("Добавить питомца", ImportPet, true));
        var workspace = WorkspaceData();
        var select = new ComboBox { ItemsSource = pets.Select(pet => Value(pet, "name")).ToArray() };
        AutomationProperties.SetName(select, "Активный питомец");
        select.SelectedIndex = Array.FindIndex(pets, pet => Value(pet, "id") == Value(workspace, "active", "klyaksa"));
        var useTheme = new CheckBox { Content = "Применять палитру питомца", IsChecked = workspace["themed"]?.GetValue<bool>() != false };
        Field(page, "Активный питомец", select);
        page.Children.Add(useTheme);
        void Apply()
        {
            if (select.SelectedIndex < 0) return;
            SelectPet(Value(pets[select.SelectedIndex], "id"), useTheme.IsChecked == true);
        }
        select.SelectionChanged += (_, _) => Apply();
        useTheme.Click += (_, _) => Apply();
        page.Children.Add(Actions(Button("Движения", () => Desktop?.ShowWorkspace("motion"))));
        page.Children.Add(Actions(Button("Скачать питомцев", () => Process.Start(new ProcessStartInfo("https://github.com/GamoffVad/Sputnik/releases/latest") { UseShellExecute = true }))));
        if (message is not null) { var status = Status(); status.Text = message; page.Children.Add(status); }
    }

    private void SelectPet(string id, bool themed) => UpdateWorkspace(current =>
    {
        current["active"] = id;
        current["themed"] = themed;
        if (themed || Value(current, "appearanceMode") != "custom") current["appearanceMode"] = themed ? "pet" : "default";
    }, true);

    private async Task ImportPet()
    {
        var path = Choose("Пакет питомца (*.pet)|*.pet");
        if (path is null) return;
        await PreviewPetPackage(path);
    }

    internal async Task OpenPetPackage(string path)
    {
        OpenSection("pets");
        try { await PreviewPetPackage(path); }
        catch (Exception error) { ShowPets(error.Message); }
    }

    private async Task PreviewPetPackage(string path)
    {
        var host = PanelHost.Content;
        var staged = await Task.Run(() => PetPackages.Stage(path, Pending, Pets));
        if (!ReferenceEquals(PanelHost.Content, host) || !IsVisible) return;
        var page = Editor(Value(staged, "name", "Предпросмотр питомца"));
        var motion = new ComboBox();
        var useTheme = new CheckBox { Content = "Применить палитру питомца", IsChecked = true };
        var clips = ((JsonArray)staged["animations"]!).OfType<JsonObject>().ToArray();
        string token = Value(staged, "token");
        string folder = Path.Combine(Pending, token);
        var image = new Image { Height = 166, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, DesktopPetWindow.PetScalingMode(DesktopPetWindow.IsSmoothingOn(WorkspaceData())));
        var stage = new Border { Child = image, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 1, 0, 8) };
        stage.UseTheme(Border.BackgroundProperty, "Background");
        var status = Status();
        motion.ItemsSource = clips.Select(clip => Value(clip, "name")).ToArray();
        AutomationProperties.SetName(motion, "Движение");
        Field(page, "Движение", motion);
        page.Children.Add(stage);
        page.Children.Add(useTheme);
        page.Children.Add(Actions(Button(staged["replacesExisting"]?.GetValue<bool>() == true ? "Обновить питомца" : "Установить питомца", () =>
        {
            var installed = PetPackages.Install(token, Pending, Pets);
            SelectPet(Value(installed, "id"), useTheme.IsChecked == true);
            Desktop?.ReloadPet();
            OpenSection("pets");
        }, true)));
        page.Children.Add(status);

        int revision = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000d / 30) };
        var elapsed = new Stopwatch();
        void Stop() { revision++; timer.Stop(); }
        page.Unloaded += (_, _) => Stop();
        page.IsVisibleChanged += (_, _) => { if (!page.IsVisible) Stop(); else LoadMotion(); };
        motion.SelectionChanged += (_, _) => LoadMotion();
        motion.SelectedIndex = 0;

        async void LoadMotion()
        {
            if (motion.SelectedIndex < 0 || !page.IsVisible) return;
            Stop();
            int sequence = revision;
            var clip = clips[motion.SelectedIndex];
            var sources = (clip["sheets"] as JsonArray)?.OfType<JsonObject>().ToArray() ?? [clip];
            string[] paths = sources.Select(part => Path.Combine(folder, Value(part, "file"))).ToArray();
            status.Text = "Загрузка кадров…";
            try
            {
                var atlases = await Task.Run(() => paths.Select(DesktopPetWindow.ReadSheet).ToArray());
                if (sequence != revision || !page.IsVisible) return;
                int columns = clip["columns"]!.GetValue<int>(), width = clip["frameWidth"]!.GetValue<int>(), height = clip["frameHeight"]!.GetValue<int>();
                int count = clip["frameCount"]!.GetValue<int>(), fps = clip["fps"]!.GetValue<int>();
                var viewport = clip["viewport"]?.Deserialize<PetViewport>(
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                int[] counts = sources.Select(part => part["frames"]?.GetValue<int>() ?? count).ToArray();
                int last = -1;
                void Render()
                {
                    int index = (int)(elapsed.Elapsed.TotalSeconds * fps) % count;
                    if (index == last) return;
                    last = index;
                    int part = 0;
                    while (index >= counts[part]) index -= counts[part++];
                    var frame = new CroppedBitmap(atlases[part], new Int32Rect(index % columns * width, index / columns * height, width, height));
                    frame.Freeze();
                    image.Source = PetFrames.Present(frame, viewport);
                }
                timer.Stop();
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000d / 30) };
                timer.Tick += (_, _) => Render();
                elapsed.Restart();
                Render();
                timer.Start();
                status.Text = "";
            }
            catch (Exception error) { if (sequence == revision) status.Text = error.Message; }
        }
    }
}
