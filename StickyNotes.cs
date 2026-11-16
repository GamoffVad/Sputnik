using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;

namespace Sputnik;

internal sealed class StickyNotes(string directory, Action<Window> position, Action settings)
{
    internal sealed record Note(string Id, StickyNoteContent.BlockData[] Content, double? Left = null, double? Top = null,
        string? Color = null, double? Width = null, double? Height = null, bool Pinned = false);
    private readonly string path = Path.Combine(directory, "notes.json");
    private readonly List<Note> notes = Load(directory);
    private readonly Dictionary<string, StickyNoteWindow> windows = [];
    private readonly NoteSharing sharing = new(directory);

    public bool IsVisible => windows.Values.Any(window => window.IsVisible);

    public void Toggle()
    {
        if (IsVisible)
        {
            foreach (var window in windows.Values) window.Hide();
        }
        else Show();
    }

    public void Show()
    {
        if (notes.Count == 0) Create();
        else foreach (var note in notes.ToArray()) Open(note);
    }

    private void Create()
    {
        var note = new Note(Guid.NewGuid().ToString("N"), []);
        notes.Add(note);
        Save();
        Open(note);
    }

    private void Open(Note note)
    {
        if (windows.TryGetValue(note.Id, out var existing))
        {
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
        position(window);
        var area = SystemParameters.WorkArea;
        var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (note.Width is { } width)
            window.Width = Compatibility.Clamp(width, window.MinWidth, Math.Max(window.MinWidth, desktop.Width));
        if (note.Height is { } height)
            window.Height = Compatibility.Clamp(height, window.MinHeight, Math.Max(window.MinHeight, desktop.Height));
        if (note.Left is double left && note.Top is double top && desktop.Contains(new Point(left + window.Width / 2, top + 20)))
        {
            window.Left = left;
            window.Top = top;
        }
        else if (area.Contains(new Point(window.Left, window.Top)))
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
            savePosition.Stop();
            savePosition.Start();
        }
        window.LocationChanged += (_, _) => ScheduleSave();
        window.SizeChanged += (_, _) => ScheduleSave();
        window.IsVisibleChanged += (_, _) => { if (!window.IsVisible) PersistPosition(); };
        window.Closed += (_, _) =>
        {
            PersistPosition();
            windows.Remove(note.Id);
            (Application.Current as App)?.RefreshNotesCheck();
        };
        window.Show();
        PersistPosition();
        window.Activate();
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
