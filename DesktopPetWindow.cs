using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
    private sealed class NeutralPose
    {
        public required string File { get; init; }
        public PetViewport? Viewport { get; init; }
        public int BlendFrames { get; init; } = 3;
    }
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
        public PetViewport? Viewport { get; init; }

        public NeutralPose? Neutral { get; init; }

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
        public NeutralPose? Neutral { get; init; }
    }


    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly string Assets = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    private readonly Pet[] builtins = JsonSerializer.Deserialize<Pet[]>(System.IO.File.ReadAllText(Path.Combine(Assets, "assets", "pets.json")), Json)!;
    private readonly Image sprite = new() { Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(.5, 1) };
    private readonly ScaleTransform idle = new();
    private readonly DesktopControls controls;
    private readonly Stopwatch elapsed = new();
    private double playbackSpeed = 1;
    private double animationOffset;
    internal double AnimationSeconds => animationOffset + elapsed.Elapsed.TotalSeconds * playbackSpeed;
    internal void ApplyPlaybackSpeed(double speed)
    {
        if(double.IsNaN(speed) || double.IsInfinity(speed) || speed is < .25 or > 2) throw new ArgumentOutOfRangeException(nameof(speed));
        if(speed == playbackSpeed) return;
        double seconds = AnimationSeconds; bool running = elapsed.IsRunning;
        elapsed.Reset(); if(running) elapsed.Start();
        animationOffset = seconds; playbackSpeed = speed;
    }
    private readonly Random random = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Queue<AnimationClip> upcoming = new();
    private readonly HashSet<AnimationClip> unavailable = new();
    private HashSet<string> disabledMotions = new(StringComparer.Ordinal);
    private Pet? active;
    private AnimationClip? current;
    private BitmapSource? sheet;
    private BitmapSource? displayedFrame;
    private BitmapSource? neutralFrame;
    private ImageSource? entryFrame;
    private NeutralPose? CurrentNeutral => current?.Neutral ?? active?.Neutral;
    private PetViewport? displayedViewport;
    private string? sheetFile;
    private string? preloadedFile;
    private Task<BitmapSource?>? preloaded;
    private string root = Assets;
    private DateTime loadedManifestUtc;
    private int lastFrame = -1;
    private bool paused;
    private double preferredSize = DefaultPetSize;
    private AnimationClip? pinned;
    private bool dragging;
    private readonly DesktopLayer layer;
    private Point? restingOrigin;
    private Point? standingAnchor;
    private double groundLevel = 1;
    private readonly Dictionary<string, double> groundLevels = new(StringComparer.Ordinal);
    internal Point GroundAnchor => new(Left + Width / 2, Top + Height * groundLevel);
    private Rect placementArea;
    private readonly Dictionary<AnimationClip, Rect?[]> visibleBounds = new();
    private byte[] alphaScan = [];

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
        layer = new DesktopLayer(this);
        UseLayoutRounding = true;
        sprite.RenderTransform = idle;
        RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.NearestNeighbor);
        Content = sprite;
        sprite.SizeChanged += (_, _) =>
        {
            if (CurrentNeutral is not { BlendFrames: > 0 } || current is null || lastFrame < 0) return;
            int frame = lastFrame;
            lastFrame = -1;
            ShowFrame(frame);
        };
        controls = new DesktopControls(this, openWorkspace, exitApp);
        sprite.MouseRightButtonUp += (_, args) =>
        {
            args.Handled = true;
            controls.ShowMenu(active?.Name ?? "Спутник", (Application.Current as App)?.NotesVisible == true);
        };

        Point dragStart = default;
        int clickCount = 0;
        bool pressed = false;
        bool resumeAfterDrag = false;
        PreviewMouseLeftButtonDown += (_, args) =>
        {
            dragStart = args.GetPosition(this);
            clickCount = args.ClickCount;
            pressed = true;
            sprite.CaptureMouse();
        };
        sprite.MouseMove += (_, args) =>
        {
            if (!pressed || !sprite.IsMouseCaptured) return;
            if (args.LeftButton != MouseButtonState.Pressed) { FinishDrag(); return; }
            Point pointer = args.GetPosition(this);
            Vector delta = pointer - dragStart;
            if (!dragging)
            {
                if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                resumeAfterDrag = elapsed.IsRunning;
                elapsed.Stop();
                dragging = true;
                controls.HideSpheres();
            }
            if (!MoveDragTo(PointToScreen(pointer), dragStart))
            {
                App.WriteDiagnostic("pet-drag", $"SetWindowPos failed: {Marshal.GetLastWin32Error()}");
                FinishDrag();
            }
            args.Handled = true;
        };
        sprite.LostMouseCapture += (_, _) => FinishDrag();
        PreviewMouseLeftButtonUp += (_, args) =>
        {
            if (!pressed) return;
            bool moved = dragging;
            FinishDrag();
            args.Handled = true;
            if (moved) return;
            if (clickCount == 3) openWorkspace("quick");
            else if (clickCount == 2 && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) openWorkspace("notes");
            else if (!controls.ShowSpheres()) openWorkspace("favorites");
        };

        void FinishDrag()
        {
            bool moved = dragging;
            dragging = false;
            pressed = false;
            if (sprite.IsMouseCaptured) sprite.ReleaseMouseCapture();
            if (!moved) return;
            restingOrigin = new Point(Left, Top);
            standingAnchor = GroundAnchor;
            if (resumeAfterDrag) elapsed.Start();
            controls.FollowPet();
        }

        Loaded += (_, _) =>
        {
            Rect area = SystemParameters.WorkArea;
            double size = Math.Min(preferredSize, Math.Min(area.Width, area.Height));
            var frame = DesktopControls.ClampPetFrame(new Rect(area.Right - size - 24, area.Bottom - size - 18, size, size), area);
            Width = Height = frame.Width;
            Left = frame.Left;
            Top = frame.Top;
            placementArea = area;
            restingOrigin = frame.TopLeft;
            standingAnchor = GroundAnchor;
        };
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(HandleScreenChange);
        timer.Tick += (_, _) => RenderFrame();
        timer.Start();
        LocationChanged += (_, _) => Follow();
        SizeChanged += (_, _) => Follow();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { ApplyPetSize(); Follow(); });

        void Follow()
        {
            controls.FollowPet();
            (Application.Current as App)?.FollowQuickChat();
        }
        Closed += (_, _) => { timer.Stop(); controls.Dispose(); layer.Dispose(); };
    }

    internal void PositionPanel(Window panel, string section) => controls.PositionPanel(panel, section);
    internal bool HasContextMenu => controls.HasContextMenu;
    internal void ShowFavoriteSpheres() => controls.ShowSpheres();
    internal void PositionBubble(Window bubble) => controls.PositionBubble(bubble);
    internal void WatchQuickChat(QuickChatWindow window) => controls.WatchQuickChat(window);
    internal void SetNotesChecked(bool visible) => controls.SetNotesChecked(visible);

    internal int AnimationCount => active?.Animations.Length ?? 0;
    internal (string Pet, (string Name, bool Enabled)[] Clips) MotionSettings() =>
        (active?.Id ?? "", active?.Animations.Select(clip => (clip.Name, MotionEnabled(clip))).ToArray() ?? []);

    private bool MotionEnabled(AnimationClip clip) => !disabledMotions.Contains(clip.Name);
    private bool HasEnabledMotion => active?.Animations.Any(MotionEnabled) == true;

    internal void SetPlayback(string command, int index)
    {
        if (command == "toggle-pause") command = paused ? "resume" : "pause";
        if (command == "shuffle")
        {
            SetShufflePreference(!shuffleMotions);
            return;
        }
        if (command is "pause" or "resume")
        {
            paused = command == "pause";
            if (paused || !HasEnabledMotion) elapsed.Stop();
            else if (current is not null && MotionEnabled(current)) elapsed.Start();
            else PlayNext();
            return;
        }

        var choice = command == "clip" ? active?.Animations.ElementAtOrDefault(index) : null;
        if (!HasEnabledMotion || (command == "clip" && (choice is null || !MotionEnabled(choice)))) return;
        paused = false;
        upcoming.Clear();
        pinned = choice;
        PlayNext();
        pinned = null;
    }

    internal void RefreshFavoriteIcons() => controls.RefreshSiteIcons();
    private bool shuffleMotions = true;
    internal (bool Paused, bool Shuffle) PlaybackState => (paused, shuffleMotions);
    internal void SetShufflePreference(bool enabled)
    {
        if (shuffleMotions == enabled) return;
        shuffleMotions = enabled; upcoming.Clear(); pinned = null;
    }
    private bool resumeFromTray;
    internal void PrepareForTray()
    {
        controls.HideSpheres();
        controls.CloseMenu();
        resumeFromTray = elapsed.IsRunning;
        elapsed.Stop();
        timer.Stop();
    }
    internal void ReturnFromTray()
    {
        timer.Start();
        if (resumeFromTray && !paused && HasEnabledMotion) elapsed.Start();
        resumeFromTray = false;
    }

    public void UpdateFromStore()
    {
        var workspace = ReadWorkspace();
        bool nextShuffle = workspace?["shuffleMotions"]?.GetValue<bool>() != false;
        SetShufflePreference(nextShuffle);
        double speed = workspace?["animationSpeed"] is JsonValue value && value.TryGetValue<double>(out double rate) && rate is >=.25 and <=2 ? rate : 1;
        ApplyPlaybackSpeed(speed);
        ApplyTopmost(IsTopmostOn(workspace));
        string desired = workspace?["active"]?.ToString() ?? "klyaksa";
        if (desired != active?.Id || (root != Assets && File.GetLastWriteTimeUtc(Path.Combine(root, "pet.json")) != loadedManifestUtc)) SelectFromStore(desired, workspace);
        else ApplyMotionSettings(workspace);
        var favorites = (workspace?["favorites"] as JsonArray)?.OfType<JsonObject>()
            .Select(DesktopControls.Favorite.FromSettings)
            .Where(item => item.Kind is "site" or "app" && item.Title.Length > 0 && item.Target.Length > 0)
            .ToArray() ?? [];
        var manual = workspace?["customTheme"] as JsonObject;
        var theme = workspace?["appearanceMode"]?.ToString() == "custom" && manual is not null
            ? manual.Deserialize<PetTheme>(Json)
            : workspace?["themed"]?.GetValue<bool>() == false ? null : active?.Theme;
        ThemePalette.Apply(theme);
        ThemePalette.ApplyFont(workspace?["font"]?.ToString());
        ApplyAppearanceSizes(SizeSetting("petScale", 100), SizeSetting("sphereScale", 100),
            SizeSetting("sphereSpread", 100), SizeSetting("sphereOffset", DesktopControls.DefaultSphereOffset), IsSmoothingOn(workspace));
        controls.SetFavorites(favorites);

        double SizeSetting(string name, double fallback) =>
            workspace?[name] is JsonValue value && value.TryGetValue<double>(out double number) ? number : fallback;
    }

    internal static bool IsSmoothingOn(JsonNode? workspace) =>
        workspace?["petSmoothing"] is JsonValue value && value.TryGetValue<bool>(out bool enabled) && enabled;

    internal static bool IsTopmostOn(JsonNode? workspace) =>
        workspace?["petTopmost"] is not JsonValue value || !value.TryGetValue<bool>(out bool enabled) || enabled;

    internal void ApplyTopmost(bool enabled) => layer.Apply(enabled);

    internal static BitmapScalingMode PetScalingMode(bool smoothing) =>
        smoothing ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor;

    internal void ApplyAppearanceSizes(double petPercent, double spherePercent, double spreadPercent, double offset, bool smoothing)
    {
        RenderOptions.SetBitmapScalingMode(sprite, PetScalingMode(smoothing));
        ResizePet(Compatibility.Clamp(petPercent, 60, 160) / 100);
        controls.SetSphereSize(DesktopControls.DefaultOrbSize * Compatibility.Clamp(spherePercent, 60, 160) / 100);
        controls.SetSphereSpread(Compatibility.Clamp(spreadPercent, 0, 100) / 100);
        controls.SetSphereOffset(Compatibility.Clamp(offset, DesktopControls.MinimumSphereOffset, DesktopControls.MaximumSphereOffset));
    }

    private bool MoveDragTo(Point pointerOnScreen, Point grabbedPoint)
    {
        // Physical screen coordinates keep the grabbed point under the cursor across DPI boundaries.
        Point origin = PointToScreen(new Point());
        Vector movement = pointerOnScreen - PointToScreen(grabbedPoint);
        const uint noSize = 0x0001, noZOrder = 0x0004, noActivate = 0x0010;
        return SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
            (int)Math.Round(origin.X + movement.X), (int)Math.Round(origin.Y + movement.Y),
            0, 0, noSize | noZOrder | noActivate);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    private IntPtr HandleScreenChange(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int displayChange = 0x007E, settingChange = 0x001A;
        const int setWorkArea = 0x002F;
        if (message == displayChange || (message == settingChange && wParam.ToInt64() == setWorkArea))
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyPetSize));
        return IntPtr.Zero;
    }

    private void ResizePet(double scale)
    {
        double displayScale = active?.DisplayScale ?? 1;
        preferredSize = DefaultPetSize * scale * Compatibility.Clamp(displayScale, 0.75, PetPackages.MaximumDisplayScale);
        if (IsLoaded) ApplyPetSize();
    }

    private void ApplyPetSize()
    {
        if (!IsLoaded || !IsVisible || dragging) return;
        placementArea = controls.WorkArea();
        double size = Math.Min(preferredSize, Math.Min(placementArea.Width, placementArea.Height));
        Point origin = restingOrigin ?? new Point(Left, Top);
        standingAnchor ??= new Point(origin.X + Width / 2, origin.Y + Height * groundLevel);
        restingOrigin = new Point(standingAnchor.Value.X - size / 2, standingAnchor.Value.Y - size * groundLevel);
        Width = Height = size;
        PositionAtRest();
    }

    private void PositionAtRest()
    {
        if (dragging || restingOrigin is not { } origin) return;
        if (Math.Abs(Left - origin.X) > .01) Left = origin.X;
        if (Math.Abs(Top - origin.Y) > .01) Top = origin.Y;
    }

    // Use the full canvas so animation frames cannot change placement or menu anchors.
    internal Rect VisibleContent() => new(0, 0, 1, 1);

    internal Rect VisibleBounds()
    {
        if (current is null || lastFrame < 0 || displayedFrame is not { } image)
            return new Rect(0, 0, 1, 1);
        if (!visibleBounds.TryGetValue(current, out var bounds))
            visibleBounds[current] = bounds = new Rect?[current.FrameCount];
        if (bounds[lastFrame] is { } cached) return cached;
        if (image.Format != PixelFormats.Bgra32 && image.Format != PixelFormats.Pbgra32)
            image = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int width = image.PixelWidth, height = image.PixelHeight, stride = width * 4;
        if (alphaScan.Length < stride * height) alphaScan = new byte[stride * height];
        image.CopyPixels(alphaScan, stride, 0);
        int left = width, right = -1, top = height, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0, address = y * stride + 3; x < width; x++, address += 4)
            {
                if (alphaScan[address] == 0) continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = y;
            }
        }
        var viewport = displayedViewport?.Bounds ?? new Rect(0, 0, width, height);
        double ratio = viewport.Width / viewport.Height;
        double w = Math.Min(1, ratio), h = Math.Min(1, 1 / ratio);
        var visible = right < left ? new Rect(.5, .5, 0, 0) : new Rect(
            (1 - w) / 2 + w * (left - viewport.X) / viewport.Width,
            (1 - h) / 2 + h * (top - viewport.Y) / viewport.Height,
            w * (right - left + 1) / viewport.Width, h * (bottom - top + 1) / viewport.Height);
        bounds[lastFrame] = visible;
        return visible;
    }

    private void SelectFromStore(string desired, JsonNode? workspace)
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
            selected is null ? Assets : folder, workspace);
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

    private static HashSet<string> DisabledMotions(JsonNode? workspace, string id) =>
        new((workspace?["disabledMotions"] as JsonObject)?[id] is JsonArray items
            ? items.OfType<JsonValue>().Select(item => item.TryGetValue<string>(out var name) ? name : "").Where(name => name.Length > 0)
            : [], StringComparer.Ordinal);

    private void ApplyMotionSettings(JsonNode? workspace)
    {
        if (active is null) return;
        var next = DisabledMotions(workspace, active.Id);
        if (disabledMotions.SetEquals(next)) return;
        disabledMotions = next;
        upcoming.Clear();
        if (pinned is not null && !MotionEnabled(pinned)) pinned = null;
        if (!HasEnabledMotion) elapsed.Stop();
        else if (current is null || !MotionEnabled(current)) PlayNext();
        else if (!paused) elapsed.Start();
    }

    private void UsePet(Pet selected, string folder, JsonNode? workspace = null)
    {
        // Retain the unrounded placement, rather than accumulating HWND pixel
        // rounding every time a different canvas/scale is selected.
        Point? anchor = IsLoaded && active is not null ? standingAnchor ?? GroundAnchor : null;
        active = selected;
        disabledMotions = DisabledMotions(workspace, selected.Id);
        controls.SetPetName(selected.Name);
        root = folder;
        loadedManifestUtc = File.GetLastWriteTimeUtc(Path.Combine(root, "pet.json"));
        upcoming.Clear();
        unavailable.Clear();
        visibleBounds.Clear();
        current = null;
        sheet = null;
        displayedFrame = null;
        neutralFrame = null;
        entryFrame = null;
        displayedViewport = null;
        sheetFile = null;
        preloaded = null;
        preloadedFile = null;
        pinned = null;
        idle.ScaleY = 1;
        if (!PlayNext() && !(!HasEnabledMotion && PlayNext(showStill: true)) && selected.Id != "hudi" && selected != builtins[0])
        {
            UsePet(builtins[0], Assets);
            return;
        }
        groundLevel = ReferenceGround(selected);
        if (anchor is { } fixedAnchor)
        {
            standingAnchor = fixedAnchor;
            restingOrigin = new Point(fixedAnchor.X - Width / 2, fixedAnchor.Y - Height * groundLevel);
            PositionAtRest();
        }
    }

    private double ReferenceGround(Pet selected)
    {
        var clip = current ?? selected.Animations.FirstOrDefault();
        string key = Path.GetFullPath(root) + "|" + selected.Id + "|" + clip?.Name + "|" + loadedManifestUtc.Ticks;
        if (groundLevels.TryGetValue(key, out double cached)) return cached;
        double result = 1;
        try
        {
            // Pick one resting reference for the whole pet. Never follow the
            // animated bounds: jumping, bowing and cloth movement remain natural.
            var neutral = clip?.Neutral ?? selected.Neutral;
            if (neutral is not null)
                result = ReferenceGroundLevel(neutralFrame ?? ReadSheet(Path.Combine(root, neutral.File)), neutral.Viewport);
            else if (clip is not null)
            {
                string file = clip.Locate(0).File;
                var bitmap = sheetFile == file && sheet is not null ? sheet : ReadSheet(Path.Combine(root, file));
                BitmapSource first = clip.FrameWidth > 0 && clip.FrameHeight > 0
                    ? new CroppedBitmap(bitmap, new Int32Rect(0, 0, clip.FrameWidth, clip.FrameHeight)) : bitmap;
                result = ReferenceGroundLevel(first, clip.Viewport);
            }
        }
        catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException or InvalidOperationException)
        { App.WriteDiagnostic("pet-ground", selected.Id + ": " + error.Message); }
        groundLevels[key] = result;
        return result;
    }

    internal static double ReferenceGroundLevel(BitmapSource source, PetViewport? viewport)
    {
        BitmapSource frame = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32
            ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = frame.PixelWidth, height = frame.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height]; frame.CopyPixels(pixels, stride, 0);
        // Ignore a soft contact shadow and isolated specks below the feet.
        int minimum = Math.Max(1, (int)Math.Ceiling(width * .01)), bottom = -1;
        foreach (int opacity in new[] { 192, 64 })
        {
            for (int y = height - 1; y >= 0; y--)
            {
                int count = 0;
                for (int x = 0; x < width; x++)
                    if (pixels[y * stride + x * 4 + 3] >= opacity && ++count >= minimum) break;
                if (count >= minimum) { bottom = y + 1; break; }
            }
            if (bottom >= 0) break;
        }
        if (bottom < 0) return 1;
        var canvas = viewport?.Bounds ?? new Rect(0, 0, width, height);
        double scale = 1 / Math.Max(canvas.Width, canvas.Height);
        return Compatibility.Clamp((1 - canvas.Height * scale) / 2 + (bottom - canvas.Y) * scale, 0, 1);
    }

    private bool PlayNext(bool showStill = false)
    {
        if (active is null) return false;
        if (upcoming.Count == 0)
        {
            var order = active.Animations.Where(clip => !unavailable.Contains(clip) &&
                (showStill || (MotionEnabled(clip) && (pinned is null || clip == pinned)))).ToArray();
            for (int index = shuffleMotions ? order.Length - 1 : 0; index > 0; index--)
            {
                int swap = random.Next(index + 1);
                (order[index], order[swap]) = (order[swap], order[index]);
            }
            if (!shuffleMotions && pinned is null && current is not null)
            {
                int index = Array.IndexOf(order, current);
                if (index >= 0) order = order.Skip(index + 1).Concat(order.Take(index + 1)).ToArray();
            }
            if (shuffleMotions && order.Length > 1 && order[0] == current)
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
                sheet = GetSheet(first.File);
                sheetFile = first.File;
                var previousPose = CurrentNeutral;
                var outgoing = current is null ? null : displayedFrame;
                var outgoingViewport = displayedViewport;
                current = next;
                bool changedPose = previousPose?.File != CurrentNeutral?.File || previousPose?.Viewport != CurrentNeutral?.Viewport;
                // A CroppedBitmap keeps its entire atlas alive. A transition
                // only needs this one frame, with the original viewport.
                entryFrame = changedPose && outgoing is not null
                    ? PetFrames.Present(DetachBitmap(outgoing), outgoingViewport) : null;
                if (changedPose) neutralFrame = null;
                lastFrame = -1;
                elapsed.Restart();
                animationOffset = 0;
                if (paused || showStill) elapsed.Stop();
                ShowFrame(0);
                if (showStill) upcoming.Clear();
                else PreloadSheet(first.Next ?? upcoming.FirstOrDefault()?.Locate(0).File);
                return true;
            }
            catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException or FormatException or InvalidOperationException)
            {
                unavailable.Add(next);
            }
        }
        return false;
    }

    private BitmapSource GetSheet(string file)
    {
        if (sheetFile == file && sheet is not null) return sheet;
        var bitmap = preloadedFile == file && preloaded?.Status == TaskStatus.RanToCompletion
            ? preloaded.Result : null;
        bitmap ??= ReadSheet(Path.Combine(root, file));
        if (preloadedFile == file)
        {
            preloaded = null;
            preloadedFile = null;
        }
        // Both bundled and imported atlases can be large. Reuse the current
        // sheet for pinned loops, but retain no historical decoded atlases.
        return bitmap;
    }

    internal static BitmapSource DetachBitmap(BitmapSource source)
    {
        int stride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        var copy = BitmapSource.Create(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY,
            source.Format, source.Palette, pixels, stride);
        copy.Freeze();
        return copy;
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
        if (file is null || file == sheetFile)
        {
            preloaded = null;
            preloadedFile = null;
            return;
        }
        if (preloadedFile == file) return;
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
        if (current is null || !elapsed.IsRunning) return;

        if (current.FrameCount == 1)
        {
            if (active?.Animations.Length == 1)
                idle.ScaleY = 1 + .012 * Math.Pow(Math.Sin(AnimationSeconds * Math.PI / 4.4), 2);
            else if (AnimationSeconds >= 1d / current.Fps && !PlayNext())
                UsePet(builtins[0], Assets);
            return;
        }

        int frame = (int)(AnimationSeconds * current.Fps);
        if (frame >= current.FrameCount)
        {
            // A slow sheet load or a busy UI must not silently skip the rest
            // of a clip when the wall clock moves past its nominal duration.
            if (lastFrame < current.FrameCount - 1)
            {
                ShowFrame(Math.Max(0, lastFrame + 1));
                return;
            }
            if (!PlayNext() && active?.Id != "hudi")
                UsePet(builtins[0], Assets);
            return;
        }
        // Show every source frame in sequence, including after delayed loads.
        ShowFrame(Math.Min(frame, Math.Max(0, lastFrame + 1)));
    }

    private void ShowFrame(int index)
    {
        if (index == lastFrame || current is null || sheet is null) return;

        if (CurrentNeutral is { } neutral && (index == 0 || index == current.FrameCount - 1))
        {
            neutralFrame ??= ReadSheet(Path.Combine(root, neutral.File));
            displayedFrame = neutralFrame;
            displayedViewport = neutral.Viewport;
            PresentFrame(index);
            return;
        }

        var part = current.Locate(index);
        if (sheetFile != part.File)
        {
            if (preloadedFile == part.File && preloaded?.IsCompleted == false) return;
            try
            {
                sheet = GetSheet(part.File);
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
            displayedFrame = sheet;
        else
        {
            int x = part.Index % current.Columns * current.FrameWidth;
            int y = part.Index / current.Columns * current.FrameHeight;
            var frame = new CroppedBitmap(sheet, new Int32Rect(x, y, current.FrameWidth, current.FrameHeight));
            frame.Freeze();
            displayedFrame = frame;
        }
        displayedViewport = current.Viewport;
        PresentFrame(index);
    }

    private void PresentFrame(int index)
    {
        if (displayedFrame is null || current is null) return;
        ImageSource presentation = PetFrames.Present(displayedFrame, displayedViewport);
        if (CurrentNeutral is { BlendFrames: > 0 } pose && neutralFrame is not null)
        {
            var dpi = VisualTreeHelper.GetDpi(sprite);
            var pixels = new Size(Math.Max(1, sprite.ActualWidth > 0 ? sprite.ActualWidth : Width) * dpi.DpiScaleX,
                Math.Max(1, sprite.ActualHeight > 0 ? sprite.ActualHeight : Height) * dpi.DpiScaleY);
            int entrySpan = Math.Min(pose.BlendFrames, (current.FrameCount - 1) / 2);
            ImageSource reference = entryFrame is not null && index < entrySpan
                ? entryFrame : PetFrames.Present(neutralFrame, pose.Viewport);
            presentation = PetFrames.Transition(presentation, reference,
                index, current.FrameCount, pose.BlendFrames, pixels, RenderOptions.GetBitmapScalingMode(sprite));
            if (presentation is BitmapSource combined && !ReferenceEquals(combined, displayedFrame))
            {
                displayedFrame = combined;
                displayedViewport = null;
            }
        }
        sprite.Source = presentation;
        lastFrame = index;
    }
}

internal sealed record PetViewport(double X, double Y, double Width, double Height)
{
    public Rect Bounds => new(X, Y, Width, Height);
}

internal static class PetFrames
{
    public static ImageSource Present(BitmapSource frame, PetViewport? viewport)
    {
        if (viewport is null) return frame;
        var bounds = viewport.Bounds;
        var drawing = new DrawingGroup { ClipGeometry = new RectangleGeometry(bounds) };
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(bounds)));
        drawing.Children.Add(new ImageDrawing(frame, new Rect(0, 0, frame.PixelWidth, frame.PixelHeight)));
        drawing.Freeze();
        var image = new DrawingImage(drawing); image.Freeze(); return image;
    }

    public static ImageSource Transition(ImageSource frame, ImageSource neutral, int index, int count, int frames, Size pixels, BitmapScalingMode scaling)
    {
        int span = Math.Min(frames, (count - 1) / 2);
        if (span <= 0) return frame;
        double amount = Math.Min(1, Math.Min(index, count - 1 - index) / (double)span);
        // Ease into and out of the shared pose. A linear mix changes the entire
        // silhouette and shadow immediately on the frame next to a clip boundary.
        if (span >= 6) amount = amount * amount * (3 - 2 * amount);
        int width = Math.Max(1, (int)Math.Ceiling(pixels.Width)), height = Math.Max(1, (int)Math.Ceiling(pixels.Height));
        byte[] Render(ImageSource source)
        {
            var visual = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(visual, scaling);
            double scale = Math.Min(width / source.Width, height / source.Height);
            double w = source.Width * scale, h = source.Height * scale;
            using (var dc = visual.RenderOpen()) dc.DrawImage(source, new Rect((width - w) / 2, (height - h) / 2, w, h));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var data = new byte[width * height * 4]; bitmap.CopyPixels(data, width * 4, 0); return data;
        }
        // Use the same rasterization for endpoints, blends and the main action.
        // Switching between a native DrawingImage and a screen-sized bitmap
        // changes edge filtering at non-native sizes, especially on white.
        byte[] after = Render(amount <= 0 ? neutral : frame);
        if (amount is > 0 and < 1)
        {
            byte[] before = Render(neutral); int mix = (int)Math.Round(amount * 256);
            for (int i = 0; i < after.Length; i++) after[i] = (byte)((before[i] * (256 - mix) + after[i] * mix + 128) >> 8);
        }
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, after, width * 4);
        result.Freeze(); return result;
    }
}
