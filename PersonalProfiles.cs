using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sputnik;

internal sealed class PersonalProfiles(string directory)
{
    internal JsonObject Load()
    {
        var value = FeatureFiles.Object(directory, "profiles.json");
        if (value["items"] is null) { value["active"] = "default"; value["items"] = new JsonArray(new JsonObject { ["id"] = "default", ["name"] = "Основной", ["snapshot"] = Capture() }); }
        if (value["items"] is not JsonArray items || items.Count is < 1 or > 30 || !items.Any(i => i?["id"]?.ToString() == value["active"]?.ToString())) throw new InvalidOperationException("Повреждён список профилей.");
        foreach(var item in items) if(item?["snapshot"] is JsonObject snapshot)snapshot["tasks"]??=new JsonArray();
        return value;
    }
    internal string Create(string name, bool copy)
    {
        name = name.Trim(); if (name.Length is < 1 or > 60) throw new InvalidOperationException("Название профиля — от 1 до 60 символов.");
        var value = Load(); var items = value["items"]!.AsArray();
        if (items.Count >= 30) throw new InvalidOperationException("Можно создать до 30 профилей.");
        string id = Guid.NewGuid().ToString("N");
        items.Add(new JsonObject { ["id"] = id, ["name"] = name, ["snapshot"] = copy ? Capture() : Empty() });
        FeatureFiles.Write(directory, "profiles.json", value); return id;
    }
    internal void Rename(string id, string name)
    {
        if (name.Trim().Length is < 1 or > 60) throw new InvalidOperationException("Название профиля — от 1 до 60 символов.");
        var value = Load(); value["items"]!.AsArray().First(i => i!["id"]!.ToString() == id)!["name"] = name.Trim(); FeatureFiles.Write(directory, "profiles.json", value);
    }
    internal void Delete(string id)
    {
        var value = Load(); if (id == value["active"]!.ToString()) throw new InvalidOperationException("Сначала переключитесь в другой профиль.");
        var items = value["items"]!.AsArray(); var item = items.FirstOrDefault(i => i!["id"]!.ToString() == id); if (item is not null) items.Remove(item); FeatureFiles.Write(directory, "profiles.json", value);
    }
    internal void Switch(string id)
    {
        var value = Load(); string previous = value["active"]!.ToString(); if (id == previous) return;
        var items = value["items"]!.AsArray();
        var target = items.FirstOrDefault(i => i!["id"]!.ToString() == id) ?? throw new InvalidOperationException("Профиль не найден.");
        items.First(i => i!["id"]!.ToString() == previous)!["snapshot"] = Capture();
        var snapshot = target["snapshot"]!.AsObject(); Validate(snapshot);
        var workspace = FeatureFiles.Object(directory, "workspace.json"); workspace["favorites"] = snapshot["favorites"]!.DeepClone(); workspace["reminders"] = snapshot["reminders"]!.DeepClone();
        var changes = new Dictionary<string,string?> { ["workspace.json"] = workspace.ToJsonString() };
        foreach (var entry in Files) changes[entry.File] = snapshot[entry.Key]!.ToJsonString();
        value["active"] = id; changes["profiles.json"] = value.ToJsonString();
        FeatureFiles.Commit(directory, changes); DataTransfer.SyncAll(directory);
    }
    internal static readonly (string Key,string File)[] Files = [("notes","notes.json"),("hotkeys","hotkeys.json"),("notifications","notification-settings.json"),("dates","dates.json"),("inbox","notifications.json"),("tasks","tasks.json")];
    internal JsonObject Capture()
    {
        var workspace = FeatureFiles.Object(directory,"workspace.json");
        var result = Empty(); result["notes"] = JsonSerializer.SerializeToNode(StickyNotes.Load(directory));
        foreach (var entry in Files.Where(f => f.Key != "notes")) if (FeatureFiles.Read(directory,entry.File) is {} v) result[entry.Key] = v;
        result["favorites"] = workspace["favorites"]?.DeepClone() ?? new JsonArray(); result["reminders"] = workspace["reminders"]?.DeepClone() ?? new JsonArray();
        Validate(result); return result;
    }
    internal static JsonObject Empty() => new() { ["notes"]=new JsonArray(),["favorites"]=new JsonArray(),["reminders"]=new JsonArray(),["hotkeys"]=new JsonObject(),["notifications"]=new JsonObject(),["dates"]=new JsonArray(),["inbox"]=new JsonArray(),["tasks"]=new JsonArray() };
    internal static void Validate(JsonObject snapshot)
    {
        DataTransfer.Validate("notes",snapshot["notes"]!); DataTransfer.Validate("reminders",snapshot["reminders"]!); DataTransfer.Validate("dates",snapshot["dates"]!);
        PersonalTasks.Validate(snapshot["tasks"]!.AsArray());
        DataTransfer.Validate("settings", new JsonObject { ["workspace"] = new JsonObject(), ["hotkeys"] = snapshot["hotkeys"]!.DeepClone() });
        if (snapshot["notifications"] is not JsonObject || snapshot["inbox"] is not JsonArray || snapshot["favorites"] is not JsonArray {Count: <= 500}) throw new InvalidOperationException("Повреждены данные профиля.");
        foreach (var item in snapshot["favorites"]!.AsArray()) if (item is not JsonObject || item["target"]?.ToString().Length is not (> 0 and <= 8000) || item["kind"]?.ToString() is not ("site" or "app")) throw new InvalidOperationException("Повреждено избранное профиля.");
    }
}
