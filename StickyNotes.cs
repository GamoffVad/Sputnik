using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Sputnik;

internal sealed class StickyNotes(string directory, Action<Window> position, Action settings)
{
    internal sealed record Note(string Id, StickyNoteContent.BlockData[] Content, double? Left = null, double? Top = null,
        string? Color = null, double? Width = null, double? Height = null, bool Pinned = false, bool Ruled = false);
    private readonly string path = Path.Combine(directory, "notes.json");
    private readonly List<Note> notes = Load(directory);
    private readonly Dictionary<string, StickyNoteWindow> windows = [];
    private readonly NoteSharing sharing = new(directory);
    private bool autoArrange = ReadAutoArrange(directory);
    private bool arranging, layoutQueued;

    public bool IsVisible => windows.Values.Any(window => window.IsVisible);

    public void Toggle(bool reposition = false)
    {
        if (IsVisible)
        {
            foreach (var window in windows.Values) window.Hide();
        }
        else Show(reposition);
    }

    public void Show(bool reposition = false)
    {
        if (notes.Count == 0) Create();
        else foreach (var note in notes.ToArray()) Open(note, reposition && !autoArrange);
        if (autoArrange) ArrangeVisible();
    }

    private void Create()
    {
        string preferences = Path.Combine(directory, "workspace.json");
        bool autoColor = File.Exists(preferences) &&
            JsonNode.Parse(File.ReadAllText(preferences))?["noteColorMode"]?.ToString() == "auto";
        var note = new Note(Guid.NewGuid().ToString("N"), [],
            Color: autoColor ? StickyNoteWindow.NextColor(notes.LastOrDefault()?.Color) : null);
        notes.Add(note);
        Save();
        Open(note);
        if (autoArrange) ArrangeVisible(notify: true);
    }

    private void Open(Note note, bool reposition = false)
    {
        if (windows.TryGetValue(note.Id, out var existing))
        {
            if (reposition) position(existing);
            if (!existing.IsVisible) existing.Show();
            existing.Activate();
            return;
        }
        var window = new StickyNoteWindow(note.Content, Create, content =>
        {
            int index = notes.FindIndex(item => item.Id == note.Id);
            if (index < 0) return;
            notes[index] = notes[index] with { Content = content };
            Save();
        }, () =>
        {
            notes.RemoveAll(item => item.Id == note.Id);
            Save();
        }, note.Color, color =>
        {
            int index = notes.FindIndex(item => item.Id == note.Id);
            if (index < 0) return;
            notes[index] = notes[index] with { Color = color };
            Save();
        }, note.Pinned, pinned =>
        {
            int index = notes.FindIndex(item => item.Id == note.Id);
            if (index < 0) return;
            notes[index] = notes[index] with { Pinned = pinned };
            Save();
        }, sharing.Load, settings);
        window.ConfigureArrangement(() => ArrangeVisible(notify: true), () => autoArrange, SetAutoArrange);
        window.ConfigureRuling(note.Ruled, enabled =>
        {
            int index = notes.FindIndex(item => item.Id == note.Id);
            if (index < 0) return;
            notes[index] = notes[index] with { Ruled = enabled };
            Save();
        });
        window.PlacementChanged += (_, _) => QueueArrange();
        window.SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(
            (IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (message == 0x007E || (message == 0x001A && wParam.ToInt64() == 0x002F)) QueueArrange();
                return IntPtr.Zero;
            });
        var area = SystemParameters.WorkArea;
        var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (note.Width is { } width)
            window.Width = Compatibility.Clamp(width, window.MinWidth, Math.Max(window.MinWidth, desktop.Width));
        if (note.Height is { } height)
            window.Height = Compatibility.Clamp(height, window.MinHeight, Math.Max(window.MinHeight, desktop.Height));
        position(window);
        if (!reposition && note.Left is double left && note.Top is double top && desktop.Contains(new Point(left + window.Width / 2, top + 20)))
        {
            window.Left = left;
            window.Top = top;
        }
        else if (!reposition && area.Contains(new Point(window.Left, window.Top)))
        {
            int shift = windows.Count % 7 * 25;
            window.Top = Math.Max(area.Top, Math.Min(area.Bottom - window.Height, window.Top + shift));
            if (shift != 0 && window.Top + window.Height >= area.Bottom)
                window.Top = Math.Max(area.Top, window.Top - shift);
        }
        windows.Add(note.Id, window);
        var savePosition = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        void PersistPosition()
        {
            savePosition.Stop();
            int index = notes.FindIndex(item => item.Id == note.Id);
            if (index < 0) return;
            var old = notes[index];
            var updated = old with { Left = window.Left, Top = window.Top, Width = window.Width, Height = window.Height };
            if (old == updated) return;
            notes[index] = updated;
            Save();
        }
        savePosition.Tick += (_, _) => PersistPosition();
        void ScheduleSave()
        {
            if (arranging) return;
            savePosition.Stop();
            savePosition.Start();
        }
        window.LocationChanged += (_, _) => ScheduleSave();
        window.SizeChanged += (_, _) => ScheduleSave();
        window.IsVisibleChanged += (_, _) => { if (!window.IsVisible) PersistPosition(); else QueueArrange(); };
        window.Closed += (_, _) =>
        {
            PersistPosition();
            windows.Remove(note.Id);
            QueueArrange();
            (Application.Current as App)?.RefreshNotesCheck();
        };
        window.Show();
        PersistPosition();
        window.Activate();
    }

    private void SetAutoArrange(bool enabled)
    {
        if (enabled && !ArrangeVisible(notify: true)) return;
        string preferences = Path.Combine(directory, "workspace.json");
        var state = File.Exists(preferences) ? JsonNode.Parse(File.ReadAllText(preferences))!.AsObject() : new JsonObject();
        state["noteAutoArrange"] = enabled;
        string temporary = preferences + ".tmp";
        File.WriteAllText(temporary, state.ToJsonString());
        Compatibility.ReplaceFile(temporary, preferences);
        autoArrange = enabled;
    }

    private void QueueArrange()
    {
        if (!autoArrange || arranging || layoutQueued || windows.Values.Any(window => window.IsChangingPlacement)) return;
        layoutQueued = true;
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            layoutQueued = false;
            if (autoArrange && !windows.Values.Any(window => window.IsChangingPlacement)) ArrangeVisible();
        }));
    }

    private bool ArrangeVisible(bool notify = false)
    {
        var visible = notes.Select(note => windows.TryGetValue(note.Id, out var window) ? window : null)
            .Where(window => window is { IsVisible: true }).Cast<StickyNoteWindow>().ToArray();
        var plans = new List<(StickyNoteWindow Window, Rect Bounds)>();
        foreach (var group in visible.GroupBy(StickyNoteLayout.Monitor))
        {
            var items = group.ToArray();
            if (!StickyNoteLayout.TryArrange(StickyNoteLayout.WorkArea(items[0]), items.Select(window => window.Height).ToArray(), out var bounds))
            {
                if (notify) MessageBox.Show(items[0],
                    "На экране не хватает места для всех открытых заметок. Закройте часть заметок или перенесите их на другой монитор и повторите выравнивание.",
                    "Выравнивание заметок", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            plans.AddRange(items.Select((window, index) => (window, bounds[index])));
        }
        if (plans.Count == 0) return true;
        arranging = true;
        try
        {
            foreach (var plan in plans)
            {
                var window = plan.Window;
                window.Width = plan.Bounds.Width;
                window.Height = plan.Bounds.Height;
                window.Left = plan.Bounds.Left;
                window.Top = plan.Bounds.Top;
                int index = notes.FindIndex(note => windows.TryGetValue(note.Id, out var item) && ReferenceEquals(item, window));
                notes[index] = notes[index] with { Left = window.Left, Top = window.Top, Width = window.Width, Height = window.Height };
            }
            Save();
        }
        finally { arranging = false; }
        return true;
    }

    private static bool ReadAutoArrange(string directory)
    {
        string preferences = Path.Combine(directory, "workspace.json");
        return File.Exists(preferences) && JsonNode.Parse(File.ReadAllText(preferences))?["noteAutoArrange"]?.GetValue<bool>() == true;
    }

    private void Save()
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(notes));
        Compatibility.ReplaceFile(temporary, path);
    }

    private static List<Note> Load(string directory)
    {
        string current = Path.Combine(directory, "notes.json");
        if (File.Exists(current)) return JsonSerializer.Deserialize<List<Note>>(File.ReadAllText(current)) ?? [];
        string legacy = Path.Combine(directory, "workspace.json");
        if (!File.Exists(legacy) || JsonNode.Parse(File.ReadAllText(legacy))?["notes"] is not JsonArray prior) return [];
        return prior.OfType<JsonObject>().Select(note =>
        {
            string title = note["title"]?.ToString() ?? "";
            string body = note["body"]?.ToString() ?? "";
            return new Note(Guid.NewGuid().ToString("N"),
                Compatibility.NormalizeLineEndings(title.Length == 0 ? body : title + "\n" + body).Split('\n').Select((line, index) =>
                    new StickyNoteContent.BlockData("text", [new("run", line, Bold: index == 0 && title.Length != 0)])).ToArray());
        }).ToList();
    }
}
