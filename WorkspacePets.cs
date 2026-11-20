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
    private readonly Dictionary<string, BitmapSource> petThumbnails = [];

    private void ShowPets(string? message = null)
    {
        var builtins = Read(Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "pets.json")) as JsonArray ?? new();
        var pets = builtins.Concat(PetPackages.Installed(Pets)).OfType<JsonObject>().ToArray();
        var page = PageFields("Питомцы", AsyncButton("Добавить питомца", ImportPet, true, "M8 2V14 M2 8H14"));
        var workspace = WorkspaceData();
        var select = new ComboBox { ItemsSource = pets.Select(pet => Value(pet, "name")).ToArray(), MinHeight = 36 };
        AutomationProperties.SetName(select, "Активный питомец");
        select.SelectedIndex = Array.FindIndex(pets, pet => Value(pet, "id") == Value(workspace, "active", "klyaksa"));
        var preview = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(6) };
        AutomationProperties.SetName(preview, "Предпросмотр выбранного питомца");
        RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.HighQuality);
        var previewStatus = Text("…", "Muted", 12);
        previewStatus.HorizontalAlignment = HorizontalAlignment.Center;
        previewStatus.VerticalAlignment = VerticalAlignment.Center;
        var previewLayers = new Grid();
        previewLayers.Children.Add(preview);
        previewLayers.Children.Add(previewStatus);
        var portrait = new Border { Width = 84, Height = 104, Child = previewLayers, CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 14, 0) };
        portrait.UseTheme(Border.BackgroundProperty, "Field");
        var profile = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        profile.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        profile.ColumnDefinitions.Add(new ColumnDefinition());
        profile.Children.Add(portrait);
        var choice = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Field(choice, "Активный питомец", select);
        var motionCount = Text("", "Muted", 12);
        choice.Children.Add(motionCount);
        Grid.SetColumn(choice, 1);
        profile.Children.Add(choice);
        page.Children.Add(profile);

        System.Windows.Shapes.Path Icon(string geometry, string role = "Ink")
        {
            var icon = new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry), Width = 16, Height = 16,
                Stretch = Stretch.Uniform, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            icon.UseTheme(System.Windows.Shapes.Shape.StrokeProperty, role);
            return icon;
        }
        Button ActionRow(string label, string geometry, Action action, bool arrow = false)
        {
            var button = Button(label, action);
            AutomationProperties.SetName(button, label);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.MinHeight = 38;
            button.Padding = new Thickness(10, 7, 10, 7);
            AutomationProperties.SetName(button, label);
            var content = new DockPanel();
            var icon = Icon(geometry);
            icon.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(icon, Dock.Left);
            content.Children.Add(icon);
            if (arrow)
            {
                var next = Icon("M5 2L11 8L5 14", "Muted");
                next.Width = next.Height = 12;
                DockPanel.SetDock(next, Dock.Right);
                content.Children.Add(next);
            }
            var text = Text(label, size: 13);
            text.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(text);
            button.Content = content;
            return button;
        }
        var motions = ActionRow("Движения", "M4 2L14 8L4 14Z", () => Desktop?.ShowWorkspace("motion"), arrow: true);
        motions.Margin = new Thickness(0, 12, 0, 0);
        page.Children.Add(motions);
        var paletteLabel = new StackPanel();
        paletteLabel.Children.Add(Text("Палитра питомца", size: 13));
        var paletteHint = Text("Оформлять окна в его цветах", "Muted", 12);
        paletteHint.Margin = new Thickness(0, 2, 0, 0);
        paletteLabel.Children.Add(paletteHint);
        var useTheme = new CheckBox { Content = paletteLabel, IsChecked = workspace["themed"]?.GetValue<bool>() != false,
            Margin = new Thickness(0, 14, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(useTheme, "Применять палитру питомца");
        page.Children.Add(useTheme);
        void Apply()
        {
            if (select.SelectedIndex < 0) return;
            SelectPet(Value(pets[select.SelectedIndex], "id"), useTheme.IsChecked == true);
        }
        int previewRevision = 0;
        async void ShowSelection()
        {
            int revision = ++previewRevision;
            preview.Source = null;
            motions.IsEnabled = select.SelectedIndex >= 0;
            if (select.SelectedIndex < 0)
            {
                motionCount.Text = "Выберите питомца";
                previewStatus.Text = "—";
                previewStatus.Visibility = Visibility.Visible;
                return;
            }
            var pet = pets[select.SelectedIndex];
            int count = (pet["animations"] as JsonArray)?.Count ?? 0;
            string unit = count % 100 is >= 11 and <= 14 ? "движений" : (count % 10) switch { 1 => "движение", 2 or 3 or 4 => "движения", _ => "движений" };
            motionCount.Text = $"{count} {unit}";
            previewStatus.Text = "…";
            previewStatus.Visibility = Visibility.Visible;
            try
            {
                bool bundled = builtins.OfType<JsonObject>().Any(item => Value(item, "id") == Value(pet, "id"));
                string root = bundled ? Path.Combine(AppContext.BaseDirectory, "wwwroot") : Path.Combine(Pets, Value(pet, "id"));
                var neutral = pet["neutral"] as JsonObject;
                var clip = pet["animations"]![0]!.AsObject();
                var source = neutral ?? (clip["sheets"] as JsonArray)?[0]?.AsObject() ?? clip;
                string file = Path.Combine(root, Value(source, "file"));
                int width = neutral is null ? clip["frameWidth"]?.GetValue<int>() ?? 0 : 0;
                int height = neutral is null ? clip["frameHeight"]?.GetValue<int>() ?? 0 : 0;
                string key = $"{file}|{File.GetLastWriteTimeUtc(file).Ticks}|{width}x{height}";
                if (!petThumbnails.TryGetValue(key, out var thumbnail))
                {
                    thumbnail = await Task.Run(() => ReadPetThumbnail(file, width, height));
                }
                if (Dispatcher.HasShutdownStarted) return;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (revision != previewRevision || !page.IsLoaded) return;
                    // Keep only small, independent thumbnails; never retain full animation sheets.
                    if (petThumbnails.Count >= 32) petThumbnails.Clear();
                    petThumbnails[key] = thumbnail;
                    preview.Source = thumbnail;
                    previewStatus.Visibility = Visibility.Collapsed;
                });
            }
            catch (TaskCanceledException) { }
            catch (Exception)
            {
                if (Dispatcher.HasShutdownStarted) return;
                _ = Dispatcher.InvokeAsync(() =>
                {
                    if (revision != previewRevision || !page.IsLoaded) return;
                    previewStatus.Text = "—";
                });
            }
        }
        select.SelectionChanged += (_, _) => { Apply(); ShowSelection(); };
        useTheme.Click += (_, _) => Apply();
        page.Loaded += (_, _) => ShowSelection();
        page.Unloaded += (_, _) => previewRevision++;
        var separator = new Border { Height = 1, Margin = new Thickness(0, 16, 0, 8) };
        separator.UseTheme(Border.BackgroundProperty, "Line");
        page.Children.Add(separator);
        var download = ActionRow("Скачать питомцев", "M8 1V11 M4 7L8 11L12 7 M2 12V15H14V12", () =>
            Process.Start(new ProcessStartInfo("https://github.com/GamoffVad/Sputnik/releases/latest") { UseShellExecute = true }));
        download.Background = Brushes.Transparent;
        page.Children.Add(download);
        if (message is not null) { var status = Status(); status.Text = message; page.Children.Add(status); }
    }

    private static BitmapSource ReadPetThumbnail(string file, int width, int height)
    {
        using var stream = File.OpenRead(file);
        int sourceHeight = height;
        if (sourceHeight <= 0)
        {
            var header = new PngBitmapDecoder(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            sourceHeight = header.Frames[0].PixelHeight;
            stream.Position = 0;
        }
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        if (width > 0 && height > 0) image.SourceRect = new Int32Rect(0, 0, width, height);
        image.DecodePixelHeight = Math.Min(192, sourceHeight);
        image.EndInit();
        image.Freeze();
        image.Freeze();
        var pixels = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int stride = pixels.PixelWidth * 4;
        var bytes = new byte[stride * pixels.PixelHeight];
        pixels.CopyPixels(bytes, stride, 0);
        int left = pixels.PixelWidth, top = pixels.PixelHeight, right = -1, bottom = -1;
        for (int y = 0; y < pixels.PixelHeight; y++)
        for (int x = 0; x < pixels.PixelWidth; x++)
        {
            if (bytes[y * stride + x * 4 + 3] < 12) continue;
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        if (right < left) return image;
        left = Math.Max(0, left - 2); top = Math.Max(0, top - 2);
        right = Math.Min(pixels.PixelWidth - 1, right + 2); bottom = Math.Min(pixels.PixelHeight - 1, bottom + 2);
        var cropped = new CroppedBitmap(image, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
        var result = new WriteableBitmap(cropped);
        result.Freeze();
        return result;
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
        Task<BitmapSource?>? neutralTask = null;
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
                var neutral = staged["neutral"] as JsonObject;
                neutralTask ??= Task.Run(() => neutral is null ? null : DesktopPetWindow.ReadSheet(Path.Combine(folder, Value(neutral, "file"))));
                var neutralFrame = await neutralTask;
                var neutralViewport = neutral?["viewport"]?.Deserialize<PetViewport>(
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
                    if (neutralFrame is not null && (index == 0 || index == count - 1))
                    {
                        image.Source = PetFrames.Present(neutralFrame, neutralViewport);
                        return;
                    }
                    int part = 0;
                    while (index >= counts[part]) index -= counts[part++];
                    var frame = new CroppedBitmap(atlases[part], new Int32Rect(index % columns * width, index / columns * height, width, height));
                    frame.Freeze();
                    ImageSource presentation = PetFrames.Present(frame, viewport);
                    if (neutralFrame is not null)
                    {
                        var dpi = VisualTreeHelper.GetDpi(image);
                        var pixels = new Size(Math.Max(1, image.ActualWidth) * dpi.DpiScaleX, Math.Max(1, image.ActualHeight) * dpi.DpiScaleY);
                        presentation = PetFrames.Transition(presentation, PetFrames.Present(neutralFrame, neutralViewport),
                            last, count, neutral?["blendFrames"]?.GetValue<int>() ?? 3, pixels, RenderOptions.GetBitmapScalingMode(image));
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
