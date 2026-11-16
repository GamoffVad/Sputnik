using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Sputnik;

internal sealed class DesktopPetWindow : Window
{
    private const double DefaultPetSize = 230;
    private sealed record AnimationSheet(string File, int Frames);
    private sealed class AnimationClip
    {
        public required string Name { get; init; }
        public string File { get; init; } = "";
        public AnimationSheet[]? Sheets { get; init; }
        public int FrameWidth { get; init; }
        public int FrameHeight { get; init; }
        public int Columns { get; init; } = 1;
        public int FrameCount { get; init; }
        public int Fps { get; init; }

        public (string File, int Index, string? Next) Locate(int index)
        {
            if (Sheets is null) return (File, index, null);
            for (int sheet = 0; sheet < Sheets.Length; sheet++)
            {
                var part = Sheets[sheet];
                if (index < part.Frames)
                    return (part.File, index, Sheets.ElementAtOrDefault(sheet + 1)?.File);
                index -= part.Frames;
            }
            throw new InvalidDataException("В PNG-листах не хватает кадров питомца.");
        }
    }

    private sealed class Pet
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required AnimationClip[] Animations { get; init; }
        public PetTheme? Theme { get; init; }
        public double DisplayScale { get; init; } = 1;
    }


    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly string Assets = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    private readonly Pet[] builtins = JsonSerializer.Deserialize<Pet[]>(System.IO.File.ReadAllText(Path.Combine(Assets, "assets", "pets.json")), Json)!;
    private readonly Image sprite = new() { Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(.5, 1) };
    private readonly ScaleTransform idle = new();
    private readonly DesktopControls controls;
    private readonly Stopwatch elapsed = new();
    private readonly Random random = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Queue<AnimationClip> upcoming = new();
    private readonly HashSet<AnimationClip> unavailable = new();
    private readonly Dictionary<string, BitmapSource> bundledSheets = new();
    private Pet? active;
    private AnimationClip? current;
    private BitmapSource? sheet;
    private string? sheetFile;
    private string? preloadedFile;
    private Task<BitmapSource?>? preloaded;
    private string root = Assets;
    private int lastFrame = -1;
    private bool paused;
    private double preferredSize = DefaultPetSize;
    private AnimationClip? pinned;

    public DesktopPetWindow(Action<string> openWorkspace, Action exitApp)
    {
        Title = "Питомец Спутника";
        Width = Height = DefaultPetSize;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        UseLayoutRounding = true;
        sprite.RenderTransform = idle;
        RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.NearestNeighbor);
        Content = sprite;
        controls = new DesktopControls(this, openWorkspace, exitApp);
        sprite.MouseRightButtonUp += (_, args) =>
        {
            args.Handled = true;
            controls.ShowMenu(active?.Name ?? "Спутник", (Application.Current as App)?.NotesVisible == true);
        };

        Point dragStart = default;
        int clickCount = 0;
        sprite.MouseLeftButtonDown += (_, args) =>
        {
            dragStart = args.GetPosition(this);
            clickCount = args.ClickCount;
            sprite.CaptureMouse();
        };
        sprite.MouseMove += (_, args) =>
        {
            if (!sprite.IsMouseCaptured || args.LeftButton != MouseButtonState.Pressed) return;
            Vector delta = args.GetPosition(this) - dragStart;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            sprite.ReleaseMouseCapture();
            DragMove();
            controls.FollowPet();
        };
        sprite.MouseLeftButtonUp += (_, _) =>
        {
            if (!sprite.IsMouseCaptured) return;
            sprite.ReleaseMouseCapture();
            controls.ShowSpheres();
            if (clickCount == 3) openWorkspace("quick");
            else if (clickCount == 2 && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) openWorkspace("notes");
        };

        Loaded += (_, _) =>
        {
            Rect area = SystemParameters.WorkArea;
            double size = Math.Min(preferredSize, Math.Min(area.Width, area.Height));
            var frame = DesktopControls.ClampPetFrame(new Rect(area.Right - size - 24, area.Bottom - size - 18, size, size), area);
            Width = Height = frame.Width;
            Left = frame.Left;
            Top = frame.Top;
        };
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(HandleScreenChange);
        timer.Tick += (_, _) => RenderFrame();
        timer.Start();
        LocationChanged += (_, _) => Follow();
        SizeChanged += (_, _) => Follow();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Follow);

        void Follow()
        {
            controls.FollowPet();
            (Application.Current as App)?.FollowQuickChat();
        }
        Closed += (_, _) => { timer.Stop(); controls.Dispose(); };
    }

    internal void PositionPanel(Window panel, string section) => controls.PositionPanel(panel, section);
    internal void ShowFavoriteSpheres() => controls.ShowSpheres();
    internal void PositionBubble(Window bubble) => controls.PositionBubble(bubble);
    internal void WatchQuickChat(QuickChatWindow window) => controls.WatchQuickChat(window);
    internal void SetNotesChecked(bool visible) => controls.SetNotesChecked(visible);

    internal int AnimationCount => active?.Animations.Length ?? 0;
    internal string[] AnimationNames() => active?.Animations.Select(clip => clip.Name).ToArray() ?? [];

    internal void SetPlayback(string command, int index)
    {
        if (command is "pause" or "resume")
        {
            paused = command == "pause";
            if (paused) elapsed.Stop(); else elapsed.Start();
            return;
        }

        paused = false;
        upcoming.Clear();
        pinned = command == "clip" ? active?.Animations.ElementAtOrDefault(index) : null;
        PlayNext();
    }

    public void UpdateFromStore()
    {
        var workspace = ReadWorkspace();
        string desired = workspace?["active"]?.ToString() ?? "klyaksa";
        if (desired != active?.Id) SelectFromStore(desired);
        var favorites = (workspace?["favorites"] as JsonArray)?.OfType<JsonObject>()
            .Select(item => new DesktopControls.Favorite(item["kind"]?.ToString() ?? "", item["title"]?.ToString() ?? "",
                item["target"]?.ToString() ?? "", item["icon"]?.ToString() ?? "", item["siteIcon"]?.ToString() ?? "",
                item["monochrome"]?.GetValue<bool>() == true))
            .Where(item => item.Kind is "site" or "app" && item.Title.Length > 0 && item.Target.Length > 0)
            .ToArray() ?? [];
        var manual = workspace?["customTheme"] as JsonObject;
        var theme = workspace?["appearanceMode"]?.ToString() == "custom" && manual is not null
            ? manual.Deserialize<PetTheme>(Json)
            : workspace?["themed"]?.GetValue<bool>() == false ? null : active?.Theme;
        ThemePalette.Apply(theme);
        ThemePalette.ApplyFont(workspace?["font"]?.ToString());
        ResizePet(ReadScale("petScale"));
        controls.SetSphereSize(DesktopControls.DefaultOrbSize * ReadScale("sphereScale"));
        controls.SetSphereSpread(workspace?["sphereSpread"] is JsonValue spread && spread.TryGetValue<double>(out double spacingPercent)
            ? Compatibility.Clamp(spacingPercent, 0, 100) / 100 : 1);
        controls.SetSphereOffset(workspace?["sphereOffset"] is JsonValue offset && offset.TryGetValue<double>(out double distance)
            ? Compatibility.Clamp(distance, 0, 80) : DesktopControls.DefaultSphereOffset);
        controls.SetFavorites(favorites);

        double ReadScale(string name) => workspace?[name] is JsonValue value && value.TryGetValue<double>(out double percent)
            ? Compatibility.Clamp(percent, 60, 160) / 100
            : 1;
    }

    private IntPtr HandleScreenChange(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int moving = 0x0216, exitMove = 0x0232, displayChange = 0x007E, settingChange = 0x001A;
        const int setWorkArea = 0x002F;
        if (message == moving && lParam != IntPtr.Zero && DesktopControls.ConstrainDrag(hwnd, lParam))
        {
            handled = true;
            return new IntPtr(1);
        }
        if (message is exitMove or displayChange || (message == settingChange && wParam.ToInt64() == setWorkArea))
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ConstrainToScreen));
        return IntPtr.Zero;
    }

    private void ResizePet(double scale)
    {
        preferredSize = DefaultPetSize * scale * Compatibility.Clamp(active?.DisplayScale ?? 1, 0.75, 1.6);
        if (IsLoaded) ConstrainToScreen();
    }

    private void ConstrainToScreen()
    {
        if (!IsLoaded || !IsVisible) return;
        Rect area = controls.WorkArea();
        double size = Math.Min(preferredSize, Math.Min(area.Width, area.Height));
        var frame = DesktopControls.ClampPetFrame(new Rect(Left + (Width - size) / 2, Top + Height - size, size, size), area);
        Width = Height = frame.Width;
        Left = frame.Left;
        Top = frame.Top;
    }

    private void SelectFromStore(string desired)
    {
        Pet? selected = builtins.FirstOrDefault(pet => pet.Id == desired);
        string folder = Assets;
        if (selected is null && desired.Length is > 0 and < 50 &&
            desired.All(character => character is '-' or >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
        {
            folder = Path.Combine(App.DataDirectory, "pets", desired);
            try
            {
                selected = JsonSerializer.Deserialize<Pet>(System.IO.File.ReadAllText(Path.Combine(folder, "pet.json")), Json);
            }
            catch (Exception error) when (error is IOException or JsonException) { }
        }

        UsePet(selected ?? builtins.FirstOrDefault(pet => pet.Id == "klyaksa") ?? builtins[0],
            selected is null ? Assets : folder);
    }

    private static JsonNode? ReadWorkspace()
    {
        try
        {
            string path = Path.Combine(App.DataDirectory, "workspace.json");
            return JsonNode.Parse(System.IO.File.ReadAllText(path));
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            return null;
        }
    }

    private void UsePet(Pet selected, string folder)
    {
        active = selected;
        controls.SetPetName(selected.Name);
        root = folder;
        upcoming.Clear();
        unavailable.Clear();
        bundledSheets.Clear();
        current = null;
        sheet = null;
        sheetFile = null;
        preloaded = null;
        preloadedFile = null;
        pinned = null;
        idle.ScaleY = 1;
        if (!PlayNext() && selected.Id != "hudi")
            UsePet(builtins[0], Assets);
    }

    private bool PlayNext()
    {
        if (active is null) return false;
        if (upcoming.Count == 0)
        {
            var order = active.Animations.Where(clip => !unavailable.Contains(clip) && (pinned is null || clip == pinned)).ToArray();
            for (int index = order.Length - 1; index > 0; index--)
            {
                int swap = random.Next(index + 1);
                (order[index], order[swap]) = (order[swap], order[index]);
            }
            if (order.Length > 1 && order[0] == current)
            {
                int swap = random.Next(1, order.Length);
                (order[0], order[swap]) = (order[swap], order[0]);
            }

            foreach (var clip in order) upcoming.Enqueue(clip);
        }

        while (upcoming.Count != 0)
        {
            var next = upcoming.Dequeue();
            try
            {
                var first = next.Locate(0);
                sheet = GetSheet(first.File, next.Sheets is not null);
                sheetFile = first.File;
                current = next;
                lastFrame = -1;
                elapsed.Restart();
                if (paused) elapsed.Stop();
                ShowFrame(0);
                PreloadSheet(first.Next ?? upcoming.FirstOrDefault()?.Locate(0).File);
                return true;
            }
            catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException or FormatException or InvalidOperationException)
            {
                unavailable.Add(next);
            }
        }
        return false;
    }

    private BitmapSource GetSheet(string file, bool segmented)
    {
        if (bundledSheets.TryGetValue(file, out var cached)) return cached;
        var bitmap = preloadedFile == file && preloaded?.Status == TaskStatus.RanToCompletion
            ? preloaded.Result : null;
        bitmap ??= ReadSheet(Path.Combine(root, file));
        if (root == Assets || segmented)
        {
            if (segmented && bundledSheets.Count >= 2)
                bundledSheets.Remove(bundledSheets.Keys.First(key => key != sheetFile));
            bundledSheets.Add(file, bitmap);
        }
        return bitmap;
    }

    internal static BitmapSource ReadSheet(string file)
    {
        using var stream = System.IO.File.OpenRead(file);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void PreloadSheet(string? file)
    {
        if (file is null || bundledSheets.ContainsKey(file) || preloadedFile == file) return;
        string path = Path.Combine(root, file);
        preloadedFile = file;
        preloaded = Task.Run<BitmapSource?>(() =>
        {
            try { return ReadSheet(path); }
            catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException or FormatException or InvalidOperationException)
            { return null; }
        });
    }

    private void RenderFrame()
    {
        if (current is null || paused) return;

        if (current.FrameCount == 1)
        {
            if (active?.Animations.Length == 1)
                idle.ScaleY = 1 + .012 * Math.Pow(Math.Sin(elapsed.Elapsed.TotalSeconds * Math.PI / 4.4), 2);
            else if (elapsed.Elapsed.TotalSeconds >= 1d / current.Fps && !PlayNext())
                UsePet(builtins[0], Assets);
            return;
        }

        int frame = (int)(elapsed.Elapsed.TotalSeconds * current.Fps);
        if (frame >= current.FrameCount)
        {
            if (!PlayNext() && active?.Id != "hudi")
                UsePet(builtins[0], Assets);
            return;
        }
        ShowFrame(frame);
    }

    private void ShowFrame(int index)
    {
        if (index == lastFrame || current is null || sheet is null) return;

        var part = current.Locate(index);
        if (sheetFile != part.File)
        {
            if (preloadedFile == part.File && preloaded?.IsCompleted == false) return;
            try
            {
                sheet = GetSheet(part.File, current.Sheets is not null);
                sheetFile = part.File;
            }
            catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException or FormatException or InvalidOperationException)
            {
                unavailable.Add(current);
                if (!PlayNext() && active?.Id != "hudi") UsePet(builtins[0], Assets);
                return;
            }
            PreloadSheet(part.Next ?? upcoming.FirstOrDefault()?.Locate(0).File);
        }
        if (current.FrameWidth <= 0 || current.FrameHeight <= 0)
            sprite.Source = sheet;
        else
        {
            int x = part.Index % current.Columns * current.FrameWidth;
            int y = part.Index / current.Columns * current.FrameHeight;
            var frame = new CroppedBitmap(sheet, new Int32Rect(x, y, current.FrameWidth, current.FrameHeight));
            frame.Freeze();
            sprite.Source = frame;
        }
        lastFrame = index;
    }
}
