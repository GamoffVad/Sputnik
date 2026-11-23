using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;

namespace Sputnik;

internal sealed class DataTransfer(string directory)
{
    internal static readonly string[] Kinds = ["notes", "settings", "reminders", "dates"];
    private static readonly string[] SettingsKeys = ["active", "disabledMotions", "themed", "appearanceMode", "customTheme", "font",
        "petScale", "sphereScale", "sphereSpread", "sphereOffset", "petSmoothing", "petTopmost", "windowGlass", "windowTransparency", "noteColorMode", "animationSpeed"];
    private static readonly string[] NativeFiles = ["workspace.json", "notes.json", "model.json", "hotkeys.json", "note-channels.json", "desktop-only-autostart-off", "dates.json", "chats.json"];
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    internal sealed record Result(string Kind, int Count, string Backup);
    internal static string FileName(string kind) => kind + ".sputnik-" + kind + ".json";
    private JsonNode? ReadNative(string name) => File.Exists(Path.Combine(directory, name)) ? JsonNode.Parse(File.ReadAllText(Path.Combine(directory, name))) : null;

    internal JsonObject Create(string kind)
    {
        JsonNode content;
        var workspace = ReadNative("workspace.json") as JsonObject ?? new();
        switch (kind)
        {
            case "notes": content = JsonSerializer.SerializeToNode(StickyNotes.Load(directory))!; break;
            case "dates": content = JsonSerializer.SerializeToNode(new PersonalDates(directory).Load(), PersonalDates.Json)!; break;
            case "reminders": content = workspace["reminders"]?.DeepClone() ?? new JsonArray(); break;
            case "settings":
                var settings = new JsonObject();
                foreach (string key in SettingsKeys) if (workspace.ContainsKey(key)) settings[key] = workspace[key]?.DeepClone();
                var model = ReadNative("model.json");
                content = new JsonObject
                {
                    ["workspace"] = settings,
                    ["model"] = new JsonObject { ["url"] = model?["url"]?.DeepClone(), ["model"] = model?["model"]?.DeepClone() },
                    ["hotkeys"] = ReadNative("hotkeys.json") ?? new JsonObject(),
                    ["channels"] = JsonSerializer.SerializeToNode(new NoteSharing(directory).Load()),
                    ["autoStart"] = App.AutoStartEnabled()
                };
                break;
            default: throw new InvalidOperationException("Неизвестный тип данных.");
        }
        return new JsonObject { ["format"] = "sputnik." + kind, ["version"] = 1, ["exportedAt"] = DateTimeOffset.UtcNow.ToString("O"), ["data"] = content };
    }

    internal void Export(string kind, string path)
    {
        if (NativeFiles.Any(name => string.Equals(Path.GetFullPath(path), Path.GetFullPath(Path.Combine(directory, name)), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Выберите другое имя: этот файл используется приложением для хранения данных.");
        Write(path, Create(kind).ToJsonString(Pretty));
    }

    // Portable files stay current alongside the older internal JSON stores.
    // This retains compatibility with existing installations and their backups.
    internal static void SyncFor(string path)
    {
        string? root = Path.GetDirectoryName(path);
        if (root is null) return;
        string[] kinds = Path.GetFileName(path) switch
        {
            "notes.json" => ["notes"], "dates.json" => ["dates"], "workspace.json" => ["settings", "reminders"],
            "model.json" or "hotkeys.json" or "note-channels.json" or "desktop-only-autostart-off" => ["settings"], _ => []
        };
        Sync(root, kinds);
    }

    internal static void SyncAll(string root) => Sync(root, Kinds);
    private static void Sync(string root, IEnumerable<string> kinds)
    {
        var transfer = new DataTransfer(root);
        foreach (string kind in kinds)
        {
            try
            {
                var package = transfer.Create(kind);
                string path = Path.Combine(root, FileName(kind));
                if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path))?["data"]?.ToJsonString() == package["data"]?.ToJsonString()) continue;
                Write(path, package.ToJsonString(Pretty));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or System.Security.SecurityException)
            { App.WriteDiagnostic("data-files", error.GetType().Name); }
        }
    }

    internal JsonObject Read(string kind, string path)
    {
        if (!Kinds.Contains(kind) || new FileInfo(path).Length > (kind == "notes" ? 64_000_000 : 8_000_000))
            throw new InvalidOperationException("Файл слишком велик или имеет неподдерживаемый формат.");
        try
        {
            var package = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new JsonException();
            if (package["format"]?.GetValue<string>() != "sputnik." + kind || package["version"]?.GetValue<int>() != 1 || package["data"] is null)
                throw new InvalidOperationException("Выберите файл нужного типа в формате Спутника, версии 1.");
            Validate(kind, package["data"]!);
            return package;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidCastException or NullReferenceException or ArgumentException or NotSupportedException)
        { throw new InvalidOperationException("Файл повреждён или содержит недопустимые данные.", error); }
    }

    internal Result Import(JsonObject package)
    {
        string kind = package["format"]!.GetValue<string>().Substring("sputnik.".Length);
        var data = package["data"]!;
        Validate(kind, data);
        var changes = new Dictionary<string, string?>();
        int count = 0;
        if (kind == "notes")
        {
            var notes = StickyNotes.Load(directory).ToDictionary(note => note.Id, StringComparer.Ordinal);
            var incoming = data.Deserialize<StickyNotes.Note[]>()!;
            foreach (var note in incoming) notes[note.Id] = note;
            changes["notes.json"] = JsonSerializer.Serialize(notes.Values);
            count = incoming.Length;
        }
        else if (kind == "dates")
        {
            var dates = new PersonalDates(directory).Load().ToDictionary(item => item.Id);
            foreach (var item in data.Deserialize<PersonalDate[]>(PersonalDates.Json)!) { dates[item.Id] = item; count++; }
            changes["dates.json"] = JsonSerializer.Serialize(dates.Values, PersonalDates.Json);
        }
        else if (kind == "reminders")
        {
            var workspace = ReadNative("workspace.json") as JsonObject ?? new();
            var items = workspace["reminders"] as JsonArray ?? new JsonArray();
            if (workspace["reminders"] is null) workspace["reminders"] = items;
            foreach (var item in data.AsArray())
            {
                var previous = items.FirstOrDefault(prior => prior?["id"]?.ToString() == item!["id"]!.ToString());
                if (previous is null) items.Add(item!.DeepClone()); else items[items.IndexOf(previous)] = item!.DeepClone();
                count++;
            }
            changes["workspace.json"] = workspace.ToJsonString();
        }
        else if (kind == "settings")
        {
            var workspace = ReadNative("workspace.json") as JsonObject ?? new();
            foreach (var item in data["workspace"]!.AsObject())
                if (SettingsKeys.Contains(item.Key) && (item.Key != "active" || item.Value is JsonValue id && id.TryGetValue<string>(out var pet) && AvailablePet(pet))) workspace[item.Key] = item.Value?.DeepClone();
            changes["workspace.json"] = workspace.ToJsonString();
            if (data["model"] is JsonObject incoming && incoming["url"] is JsonValue)
            {
                var model = ReadNative("model.json") as JsonObject ?? new();
                if (model["url"]?.ToString() != incoming["url"]!.ToString()) model.Remove("secret");
                model["url"] = incoming["url"]!.DeepClone(); model["model"] = incoming["model"]?.DeepClone();
                changes["model.json"] = model.ToJsonString();
            }
            if (data["hotkeys"] is JsonObject hotkeys) changes["hotkeys.json"] = hotkeys.ToJsonString();
            if (data["channels"] is JsonArray channels) changes["note-channels.json"] = channels.ToJsonString();
            if (data["autoStart"] is JsonValue autoStart) changes["desktop-only-autostart-off"] = autoStart.GetValue<bool>() ? null : "";
            count = data["workspace"]!.AsObject().Count;
        }
        string backup = Path.Combine(directory, "import-backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        var manifest = new JsonObject();
        foreach (string name in changes.Keys)
        {
            string path = Path.Combine(directory, name);
            manifest[name] = File.Exists(path);
            if (File.Exists(path)) File.Copy(path, Path.Combine(backup, name));
        }
        Write(Path.Combine(backup, "manifest.json"), manifest.ToJsonString());
        try
        {
            foreach (var change in changes)
            {
                string path = Path.Combine(directory, change.Key);
                if (change.Value is null) { if (File.Exists(path)) File.Delete(path); }
                else Write(path, change.Value);
            }
            SyncAll(directory);
            return new Result(kind, count, backup);
        }
        catch { Restore(backup); throw; }
    }

    internal void Restore(string backup)
    {
        string allowed = Path.GetFullPath(Path.Combine(directory, "import-backups")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(backup).StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Недопустимый путь резервной копии.");
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(backup, "manifest.json")))!.AsObject();
        foreach (var item in manifest)
        {
            if (!NativeFiles.Contains(item.Key)) throw new InvalidOperationException("Недопустимый файл резервной копии.");
            string path = Path.Combine(directory, item.Key);
            if (item.Value!.GetValue<bool>()) Write(path, File.ReadAllText(Path.Combine(backup, item.Key)));
            else if (File.Exists(path)) File.Delete(path);
        }
        SyncAll(directory);
    }

    private bool AvailablePet(string id)
    {
        if (id.Length is < 1 or > 80 || id.Any(c => !char.IsLetterOrDigit(c) && c is not ('-' or '_'))) return false;
        if (File.Exists(Path.Combine(directory, "pets", id, "pet.json"))) return true;
        string catalog = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "pets.json");
        return File.Exists(catalog) && JsonNode.Parse(File.ReadAllText(catalog)) is JsonArray pets && pets.Any(pet => pet?["id"]?.ToString() == id);
    }

    internal static void Validate(string kind, JsonNode data)
    {
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        if (kind == "notes")
        {
            Require(data is JsonArray { Count: <= 1000 }, "В файле может быть не более 1000 заметок.");
            var notes = data.Deserialize<StickyNotes.Note[]>()!;
            Require(notes.Select(note => note?.Id).Distinct(StringComparer.Ordinal).Count() == notes.Length, "В файле повторяются ID заметок.");
            foreach (var note in notes)
            {
                Require(note is not null && note.Id is { Length: > 0 and <= 120 } && note.Content is not null, "Проверьте структуру заметок.");
                Require(new[] { note!.Left, note.Top, note.Width, note.Height }.All(number => number is null || !double.IsNaN(number.Value) && !double.IsInfinity(number.Value) && Math.Abs(number.Value) <= 100000), "Недопустимый размер или положение заметки.");
                Require(note.Color is null or "yellow" or "pink" or "green" or "blue" or "purple", "Неизвестный цвет заметки.");
                int total = 0;
                void Blocks(StickyNoteContent.BlockData[] blocks, int depth)
                {
                    Require(depth <= 12 && (total += blocks.Length) <= 10000, "Заметка слишком сложная.");
                    foreach (var block in blocks)
                    {
                        Require(block is not null && block.Kind is "text" or "list", "Неизвестный блок заметки.");
                        if (block!.Kind == "list") { Require(block.Items is not null, "Не заданы элементы списка."); foreach (var list in block.Items!) { Require(list is not null, "Повреждённый список."); Blocks(list!, depth + 1); } }
                        else foreach (var inline in block.Inlines ?? [])
                        {
                            Require(inline is not null && inline.Kind is "run" or "line" or "check" or "image", "Неизвестный элемент заметки.");
                            Require(inline!.Text is null || inline.Text.Length <= 200000, "Текст заметки слишком длинный.");
                            if (inline.Kind == "image")
                            {
                                byte[] png = inline.Png ?? [];
                                Require(png.Length is >= 24 and <= 8_000_000 && png.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Изображение должно быть в формате PNG.");
                                int width = System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(png, 16)), height = System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(png, 20));
                                Require(width is > 0 and <= 4096 && height is > 0 and <= 4096, "Изображение в заметке слишком большое.");
                                using var stream = new MemoryStream(png);
                                var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                                Require(image.PixelWidth == width && image.PixelHeight == height, "Повреждённое изображение в заметке.");
                            }
                        }
                    }
                }
                Blocks(note.Content!, 0);
            }
        }
        else if (kind == "dates") PersonalDates.Validate(data.AsArray());
        else if (kind == "reminders")
        {
            Require(data is JsonArray { Count: <= 10000 }, "В файле может быть не более 10000 напоминаний.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reminder in data.AsArray())
            {
                Require(reminder is JsonObject && reminder["id"] is JsonValue && reminder["title"] is JsonValue, "Проверьте структуру напоминаний.");
                string id = reminder!["id"]!.GetValue<string>(), title = reminder["title"]!.GetValue<string>();
                Require(id.Length is > 0 and <= 120 && ids.Add(id) && title.Trim().Length is > 0 and <= 120, "Проверьте ID и название напоминания.");
                Require(ReminderScheduler.AsLocalTime(ReminderScheduler.Due(reminder)) is not null, "Недопустимая дата напоминания.");
                string repeat = reminder["repeat"]?.GetValue<string>() ?? "none";
                Require(repeat is "none" or "daily" or "weekly" or "custom" || int.TryParse(repeat, out int interval) && interval > 0, "Недопустимый повтор.");
                if (repeat == "custom") Require(reminder["repeatMinutes"] is JsonValue minutes && minutes.TryGetValue<int>(out int number) && number > 0, "Интервал должен быть целым числом минут, от 1.");
                if (reminder["done"] is not null) _ = reminder["done"]!.GetValue<bool>();
                if (reminder["scheduled"] is not null) Require(ReminderScheduler.AsLocalTime(reminder["scheduled"]!.GetValue<long>()) is not null, "Недопустимая дата повтора.");
            }
        }
        else if (kind == "settings")
        {
            Require(data is JsonObject && data["workspace"] is JsonObject, "В файле отсутствуют настройки.");
            var workspace = data["workspace"]!.AsObject();
            if(workspace["animationSpeed"] is {} speed) Require(speed.GetValue<double>() is >=.25 and <=2,"Скорость анимаций должна быть от 0,25× до 2×.");
            foreach (string key in new[] { "themed", "petSmoothing", "petTopmost", "windowGlass" }) if (workspace[key] is not null) _ = workspace[key]!.GetValue<bool>();
            foreach (var entry in new[] { ("petScale", 60d, 160d), ("sphereScale", 60d, 160d), ("sphereSpread", 0d, 100d), ("sphereOffset", -400d, 80d), ("windowTransparency", 0d, 70d) })
                if (workspace[entry.Item1] is not null) { double number = workspace[entry.Item1]!.GetValue<double>(); Require(number >= entry.Item2 && number <= entry.Item3, "Недопустимое значение: " + entry.Item1); }
            foreach (string key in new[] { "active", "font", "appearanceMode", "noteColorMode" }) if (workspace[key] is not null) Require(workspace[key]!.GetValue<string>().Length <= 180, "Недопустимая настройка: " + key);
            if (workspace["customTheme"] is not null) foreach (string key in new[] { "accent", "surface", "background" }) { string color = workspace["customTheme"]![key]!.GetValue<string>(); Require(color.Length == 7 && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit), "Цвета должны быть в формате #RRGGBB."); }
            if (workspace["disabledMotions"] is not null)
                foreach (var entry in workspace["disabledMotions"]!.AsObject()) Require(entry.Value is JsonArray array && array.All(item => item is JsonValue value && value.TryGetValue<string>(out var name) && name.Length <= 180), "Недопустимый список движений.");
            if (data["model"] is JsonObject model && model["url"] is not null)
            {
                Require(Uri.TryCreate(model["url"]!.GetValue<string>(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0, "Недопустимый адрес API.");
                Require(model["model"] is null || model["model"]!.GetValue<string>().Length <= 300, "Недопустимый ID модели.");
            }
            var shortcuts = new HashSet<string>();
            if (data["hotkeys"] is not null) foreach (var shortcut in data["hotkeys"]!.AsObject())
            {
                Require(DesktopControls.Sections.Any(section => section.Action == shortcut.Key), "Неизвестная горячая клавиша.");
                uint modifiers = shortcut.Value!["modifiers"]!.GetValue<uint>(), key = shortcut.Value["key"]!.GetValue<uint>();
                Require(modifiers is > 0 and <= 15 && key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x87 && key != 0x7B && shortcuts.Add(modifiers + ":" + key), "Недопустимое или повторяющееся сочетание клавиш.");
            }
            if (data["channels"] is not null) NoteSharing.Validate(data["channels"]!.Deserialize<NoteSharing.Channel[]>()!);
            if (data["autoStart"] is not null) _ = data["autoStart"]!.GetValue<bool>();
        }
        else throw new InvalidOperationException("Неизвестный формат данных.");
    }

    private static void Write(string path, string content)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, content); Compatibility.ReplaceFile(temporary, path); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
