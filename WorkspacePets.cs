using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Sputnik;

public partial class MainWindow
{
    private void ShowPets()
    {
        var builtins = Read(Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "pets.json")) as JsonArray ?? new();
        var pets = builtins.Concat(PetPackages.Installed(Pets)).OfType<JsonObject>()
            .GroupBy(pet => Value(pet, "id")).Select(group => group.First()).ToArray();
        var add = WorkspaceUi.IconButton("Добавить питомца", "M8 2V14 M2 8H14", true);
        add.Click += async (_, _) =>
        {
            add.IsEnabled = false;
            try { await ImportPet(); }
            catch (Exception error) { ShowError(error); }
            finally { add.IsEnabled = true; }
        };
        var page = PageFields("Питомцы", add, 20);
        var workspace = WorkspaceData();
        var select = new ComboBox { ItemsSource = pets.Select(pet => Value(pet, "name")).ToArray() };
        AutomationProperties.SetName(select, "Активный питомец");
        select.SelectedIndex = Math.Max(0, Array.FindIndex(pets, pet => Value(pet, "id") == Value(workspace, "active", "klyaksa")));
        var image = new Image { Width = 116, Height = 138, Stretch = Stretch.Uniform };
        AutomationProperties.SetName(image, "Предпросмотр выбранного питомца");
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var name = Text("", size: 18); name.FontWeight = FontWeights.SemiBold;
        var details = Text("", "Muted", 12); details.Margin = new Thickness(0, 5, 0, 0);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        labels.Children.Add(name); labels.Children.Add(details);
        var hero = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
        hero.ColumnDefinitions.Add(new ColumnDefinition());
        hero.Children.Add(image); Grid.SetColumn(labels, 1); hero.Children.Add(labels);
        page.Children.Add(hero);
        Field(page, "Выбрать питомца", select);
        var useTheme = new CheckBox { Content = "Палитра питомца", IsChecked = workspace["themed"]?.GetValue<bool>() != false,
            Margin = new Thickness(0, 4, 0, 4) };
        AutomationProperties.SetName(useTheme, "Применять палитру питомца");
        var palette = new StackPanel { Orientation = Orientation.Horizontal };
        var swatches = new List<Border>();
        foreach (string key in new[] { "accent", "surface", "background" })
        {
            var swatch = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 5, 0), BorderThickness = new Thickness(1) };
            swatch.UseTheme(Border.BorderBrushProperty, "Line");
            swatches.Add(swatch); palette.Children.Add(swatch);
        }
        var paletteRow = new DockPanel { Margin = new Thickness(0, 4, 0, 10) };
        DockPanel.SetDock(palette, Dock.Right); paletteRow.Children.Add(palette); paletteRow.Children.Add(useTheme);
        page.Children.Add(paletteRow);
        var status = Status(11); page.Children.Add(status);
        var motions = Button("Настроить движения", () => { if (Desktop is { } app) app.ShowWorkspace("motion"); else OpenSection("motion"); });
        motions.HorizontalAlignment = HorizontalAlignment.Stretch; motions.HorizontalContentAlignment = HorizontalAlignment.Center;
        page.Children.Add(motions);
        int revision = 0;
        async Task Preview()
        {
            int sequence = ++revision;
            if (select.SelectedIndex < 0) { name.Text = "Нет питомцев"; image.Source = null; return; }
            var pet = pets[select.SelectedIndex];
            name.Text = Value(pet, "name"); image.Source = null;
            int count = (pet["animations"] as JsonArray)?.Count ?? 0;
            details.Text = count == 1 ? "1 движение" : count is >= 2 and <= 4 ? $"{count} движения" : $"{count} движений";
            details.Text += "\nВыбран для рабочего стола";
            string[] colors = ["accent", "surface", "background"];
            for (int i = 0; i < swatches.Count; i++)
            {
                string color = Value(pet["theme"], colors[i], "#808080");
                swatches[i].Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                swatches[i].ToolTip = color;
            }
            try
            {
                string root = builtins.Any(p => Value(p, "id") == Value(pet, "id")) ? Path.Combine(AppContext.BaseDirectory, "wwwroot") : Path.Combine(Pets, Value(pet, "id"));
                var source = await Task.Run(() => PetPortrait(pet, root));
                await Dispatcher.InvokeAsync(() =>
                {
                    if (sequence != revision || !page.IsVisible) return;
                    image.Source = source; status.Text = "";
                });
            }
            catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
            { await Dispatcher.InvokeAsync(() => { if (sequence == revision) status.Text = "Предпросмотр недоступен. Питомца можно выбрать."; }); }
        }
        void Apply()
        {
            if (select.SelectedIndex < 0) return;
            SelectPet(Value(pets[select.SelectedIndex], "id"), useTheme.IsChecked == true);
        }
        select.SelectionChanged += async (_, _) => { Apply(); await Preview(); };
        useTheme.Click += (_, _) => Apply();
        page.Loaded += async (_, _) => await Preview();
        page.Unloaded += (_, _) => revision++;
        page.IsVisibleChanged += async (_, _) => { if (page.IsVisible && page.IsLoaded) await Preview(); else revision++; };
    }

    private static ImageSource PetPortrait(JsonObject pet, string root)
    {
        var clip = (pet["animations"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault() ?? throw new InvalidDataException("Нет движений.");
        var neutral = (clip["neutral"] ?? pet["neutral"]) as JsonObject;
        if (neutral is not null) return PortraitBounds(DesktopPetWindow.ReadSheet(Path.Combine(root, Value(neutral, "file"))));
        string file = Value(clip, "file");
        if (file.Length == 0) file = Value((clip["sheets"] as JsonArray)?.FirstOrDefault(), "file");
        var bitmap = DesktopPetWindow.ReadSheet(Path.Combine(root, file));
        int width = clip["frameWidth"]?.GetValue<int>() ?? bitmap.PixelWidth;
        int height = clip["frameHeight"]?.GetValue<int>() ?? bitmap.PixelHeight;
        var first = new CroppedBitmap(bitmap, new Int32Rect(0, 0, width, height)); first.Freeze();
        return PortraitBounds(first);
    }

    private static BitmapSource PortraitBounds(BitmapSource source)
    {
        BitmapSource bitmap = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32
            ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = bitmap.PixelWidth, height = bitmap.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height]; bitmap.CopyPixels(pixels, stride, 0);
        int left = width, right = -1, top = height, bottom = -1;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            if (pixels[y * stride + x * 4 + 3] > 8)
            { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        if (right < left) return source;
        left = Math.Max(0, left - 4); top = Math.Max(0, top - 4);
        right = Math.Min(width - 1, right + 4); bottom = Math.Min(height - 1, bottom + 4);
        var result = new CroppedBitmap(bitmap, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
        result.Freeze(); return result;
    }

    private void SelectPet(string id, bool themed) => UpdateWorkspace(current =>
    {
        current["active"] = id;
        current["themed"] = themed;
        current["appearanceMode"] = themed ? "pet" : "default";
    }, true);

    private async Task ImportPet()
    {
        var path = Choose("Пакет питомца (*.pet)|*.pet");
        if (path is null) return;
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
                var neutral = (clip["neutral"] ?? staged["neutral"]) as JsonObject;
                var neutralFrame = neutral is null ? null : await Task.Run(() => DesktopPetWindow.ReadSheet(Path.Combine(folder, Value(neutral, "file"))));
                var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var viewport = clip["viewport"]?.Deserialize<PetViewport>(json);
                var neutralViewport = neutral?["viewport"]?.Deserialize<PetViewport>(json);
                if (sequence != revision || !page.IsVisible) return;
                int columns = clip["columns"]!.GetValue<int>(), width = clip["frameWidth"]!.GetValue<int>(), height = clip["frameHeight"]!.GetValue<int>();
                int count = clip["frameCount"]!.GetValue<int>(), fps = clip["fps"]!.GetValue<int>();
                int[] counts = sources.Select(part => part["frames"]?.GetValue<int>() ?? count).ToArray();
                int last = -1;
                void Render()
                {
                    int index = (int)(elapsed.Elapsed.TotalSeconds * fps) % count;
                    if (index == last) return;
                    last = index;
                    int absoluteIndex = index;
                    ImageSource presentation;
                    if (neutralFrame is not null && (index == 0 || index == count - 1))
                        presentation = PetFrames.Present(neutralFrame, neutralViewport);
                    else
                    {
                        int part = 0;
                        while (index >= counts[part]) index -= counts[part++];
                        var frame = new CroppedBitmap(atlases[part], new Int32Rect(index % columns * width, index / columns * height, width, height));
                        presentation = PetFrames.Present(frame, viewport);
                    }
                    if (neutralFrame is not null)
                    {
                        double side = Math.Min(image.ActualWidth > 0 ? image.ActualWidth : image.Height, image.Height);
                        double dpi = VisualTreeHelper.GetDpi(image).DpiScaleX;
                        presentation = PetFrames.Transition(presentation, PetFrames.Present(neutralFrame, neutralViewport),
                            absoluteIndex, count, neutral?["blendFrames"]?.GetValue<int>() ?? 3,
                            new Size(side * dpi, side * dpi), RenderOptions.GetBitmapScalingMode(image));
                    }
                    image.Source = presentation;
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
