using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;

namespace Sputnik;

internal sealed class StickyNotes(string directory, Action<Window> position, Action settings)
{
    internal sealed record Note(string Id, StickyNoteContent.BlockData[] Content, double? Left = null, double? Top = null,
        string? Color = null, double? Width = null, double? Height = null, bool Pinned = false, bool Ruled = false);
    private readonly string path = Path.Combine(directory, "notes.json");
    private readonly List<Note> notes = Load(directory);
    private readonly Dictionary<string, StickyNoteWindow> windows = [];
    private readonly Dictionary<string, Action> placements = [];
    private readonly NoteSharing sharing = new(directory);
    internal event Action? Changed;
    internal event Action<string>? Deleted;
    internal Action<string>? Remind;
    internal Note[] All => notes.ToArray();
    internal static string Text(Note note) => string.Join("\n", Lines(note.Content));
    private static IEnumerable<string> Lines(IEnumerable<StickyNoteContent.BlockData> blocks)
    {
        foreach (var block in blocks)
        {
            if (block.Inlines is { } inlines) yield return string.Concat(inlines.Select(item => item.Text ?? (item.Kind == "check" ? item.Checked ? "[x] " : "[ ] " : item.Kind == "image" ? "[Изображение]" : "")));
            if (block.Items is { } items) foreach (var line in items.SelectMany(Lines)) yield return line;
        }
    }
    internal void OpenById(string id)
    {
        var note = notes.FirstOrDefault(item => item.Id == id) ?? throw new InvalidOperationException("Заметка удалена или не найдена.");
        Open(note);
    }
    internal void RequestDelete(string id) { OpenById(id); windows[id].RequestDeletion(); }
    internal string CreateText(string text)
    {
        var note = new Note(Guid.NewGuid().ToString("N"), text.Split('\n').Select(line => new StickyNoteContent.BlockData("text", [new("run", line)])).ToArray());
        notes.Add(note); Save(); Open(note); return note.Id;
    }

    public bool IsVisible => windows.Values.Any(window => window.IsVisible);
    internal void Flush()
    {
        foreach (var window in windows.Values.ToArray()) window.Flush();
        foreach (var persist in placements.Values.ToArray()) persist();
    }
    internal void CloseWindows() { foreach (var window in windows.Values.ToArray()) window.Close(); }

    internal void Arrange()
    {
        if (notes.Count == 0) return;
        // Explicit arrangement only. Normal reopening still restores the user's
        // individual coordinates, and arranging never changes note content.
        foreach (var note in notes.ToArray()) Open(note);
        Rect area = SystemParameters.WorkArea;
        const double margin = 16, gap = 12;
        double x = area.Left + margin, y = area.Top + margin, rowHeight = 0;
        int overflow = 0;
        foreach (var note in notes)
        {
            var window = windows[note.Id];
            double width = window.Width, height = window.Height;
            if (x > area.Left + margin && x + width > area.Right - margin)
            {
                x = area.Left + margin; y += rowHeight + gap; rowHeight = 0;
            }
            if (y + height > area.Bottom - margin)
            {
                // More notes than can fit without resizing: start an offset
                // layer with reachable headers, rather than leave the screen.
                overflow++;
                x = area.Left + margin + overflow * 24 % Math.Max(24, area.Width / 3);
                y = area.Top + margin + overflow * 24 % Math.Max(24, area.Height / 3);
                rowHeight = 0;
            }
            window.Left = Compatibility.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width));
            window.Top = Compatibility.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height));
            x = window.Left + width + gap;
            rowHeight = Math.Max(rowHeight, height);
        }
        Flush();
    }

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
        string preferences = Path.Combine(directory, "workspace.json");
        bool autoColor = File.Exists(preferences) &&
            JsonNode.Parse(File.ReadAllText(preferences))?["noteColorMode"]?.ToString() == "auto";
        var note = new Note(Guid.NewGuid().ToString("N"), [],
            Color: autoColor ? StickyNoteWindow.NextColor(notes.LastOrDefault()?.Color) : null);
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
        }, () => Delete(note.Id), note.Color, color =>
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
        }, sharing.Load, settings, () => Remind?.Invoke(note.Id), note.Ruled, ruled =>
        {
            int index = notes.FindIndex(item => item.Id == note.Id);
            if (index < 0) return;
            var previous = notes[index];
            notes[index] = previous with { Ruled = ruled };
            try { Save(); }
            catch { notes[index] = previous; throw; }
        }, Arrange);
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
        else
        {
            position(window);
            if (area.Contains(new Point(window.Left, window.Top)))
            {
                int shift = windows.Count % 7 * 25;
                window.Top = Math.Max(area.Top, Math.Min(area.Bottom - window.Height, window.Top + shift));
                if (shift != 0 && window.Top + window.Height >= area.Bottom)
                    window.Top = Math.Max(area.Top, window.Top - shift);
            }
        }
        windows.Add(note.Id, window);
        NoteSnapping.Attach(window, () => windows.Values);
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
        placements.Add(note.Id, PersistPosition);
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
            placements.Remove(note.Id);
            (Application.Current as App)?.RefreshNotesCheck();
        };
        window.Show();
        PersistPosition();
        window.Activate();
    }

    private void Delete(string id)
    {
        int index = notes.FindIndex(item => item.Id == id);
        if (index < 0) return;
        var removed = notes[index];
        notes.RemoveAt(index);
        try
        {
            var transfer = new DataTransfer(directory);
            var portable = transfer.Create("notes");
            portable["data"] = JsonSerializer.SerializeToNode(notes);
            var changes = new Dictionary<string, string?> { ["notes.json"] = JsonSerializer.Serialize(notes),
                [DataTransfer.FileName("notes")] = portable.ToJsonString() };
            var workspace = FeatureFiles.Read(directory, "workspace.json") as JsonObject;
            if (workspace?["reminders"] is JsonArray reminders)
            {
                var linked = reminders.Where(item => item?["noteId"]?.ToString() == id).ToArray();
                foreach (var item in linked) reminders.Remove(item);
                if (linked.Length > 0)
                {
                    changes["workspace.json"] = workspace.ToJsonString();
                    var package = transfer.Create("reminders"); package["data"] = reminders.DeepClone();
                    changes[DataTransfer.FileName("reminders")] = package.ToJsonString();
                }
            }
            FeatureFiles.Commit(directory, changes);
        }
        catch
        {
            notes.Insert(index, removed);
            throw;
        }
        Changed?.Invoke();
        Deleted?.Invoke(id);
    }

    private void Save(bool sync = true)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(notes));
        Compatibility.ReplaceFile(temporary, path);
        if (sync) { DataTransfer.SyncFor(path); Changed?.Invoke(); }
    }

    internal static List<Note> Load(string directory)
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
