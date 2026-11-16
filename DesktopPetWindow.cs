using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Sputnik;

internal sealed class DesktopPetWindow : Window
{
    private const double DefaultPetSize = 230;
    private sealed class AnimationClip
    {
        public required string Name { get; init; }
        public required string File { get; init; }
        public int FrameWidth { get; init; }
        public int FrameHeight { get; init; }
        public int Columns { get; init; } = 1;
        public int FrameCount { get; init; }
        public int Fps { get; init; }
    }

    private sealed class Pet
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required AnimationClip[] Animations { get; init; }
        public PetTheme? Theme { get; init; }
    }


    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly string Assets = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    private readonly Pet[] builtins = JsonSerializer.Deserialize<Pet[]>(System.IO.File.ReadAllText(Path.Combine(Assets, "assets", "pets.json")), Json)!;
    private readonly Image sprite = new() { Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(.5, 1) };
    private readonly ScaleTransform idle = new();
    private readonly DesktopControls controls;
    private readonly Stopwatch elapsed = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Queue<AnimationClip> upcoming = new();
    private readonly HashSet<AnimationClip> unavailable = new();
    private readonly Dictionary<AnimationClip, BitmapSource> bundledSheets = new();
    private Pet? active;
    private AnimationClip? current;
    private BitmapSource? sheet;
    private string root = Assets;
    private int lastFrame = -1;
    private bool paused;
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
        RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.HighQuality);
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
            Left = area.Right - Width - 24;
            Top = area.Bottom - Height - 18;
        };
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
            ? Math.Clamp(spacingPercent, 0, 100) / 100 : 1);
        controls.SetFavorites(favorites);

        double ReadScale(string name) => workspace?[name] is JsonValue value && value.TryGetValue<double>(out double percent)
            ? Math.Clamp(percent, 60, 160) / 100
            : 1;
    }

    private void ResizePet(double scale)
    {
        Rect area = controls.WorkArea();
        double size = Math.Min(DefaultPetSize * scale, Math.Min(area.Width, area.Height));
        if (Width == size && Height == size) return;
        double oldWidth = Width, oldHeight = Height;
        Width = Height = size;
        if (!IsLoaded) return;
        Left = Math.Clamp(Left + (oldWidth - size) / 2, area.Left, area.Right - size);
        Top = Math.Clamp(Top + oldHeight - size, area.Top, area.Bottom - size);
    }

    private void SelectFromStore(string desired)
    {
        Pet? selected = builtins.FirstOrDefault(pet => pet.Id == desired);
        string folder = Assets;
        if (selected is null && desired.Length is > 0 and < 50 &&
            desired.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
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
            Random.Shared.Shuffle(order);
            if (order.Length > 1 && order[0] == current)
            {
                int swap = Random.Shared.Next(1, order.Length);
                (order[0], order[swap]) = (order[swap], order[0]);
            }

            foreach (var clip in order) upcoming.Enqueue(clip);
        }

        while (upcoming.TryDequeue(out var next))
        {
            try
            {
                sheet = GetSheet(next);
                current = next;
                lastFrame = -1;
                elapsed.Restart();
                if (paused) elapsed.Stop();
                ShowFrame(0);
                return true;
            }
            catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException or FormatException or InvalidOperationException)
            {
                unavailable.Add(next);
            }
        }
        return false;
    }

    private BitmapSource GetSheet(AnimationClip clip)
    {
        if (bundledSheets.TryGetValue(clip, out var cached)) return cached;

        using var stream = System.IO.File.OpenRead(Path.Combine(root, clip.File));
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        if (root == Assets) bundledSheets.Add(clip, bitmap);
        return bitmap;
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

        if (current.FrameWidth <= 0 || current.FrameHeight <= 0)
            sprite.Source = sheet;
        else
        {
            int x = index % current.Columns * current.FrameWidth;
            int y = index / current.Columns * current.FrameHeight;
            var frame = new CroppedBitmap(sheet, new Int32Rect(x, y, current.FrameWidth, current.FrameHeight));
            frame.Freeze();
            sprite.Source = frame;
        }
        lastFrame = index;
    }
}
