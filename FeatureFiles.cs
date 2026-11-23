using System.IO;
using System.Text.Json.Nodes;

namespace Sputnik;

internal static class FeatureFiles
{
    internal static JsonObject Object(string directory, string name) => Read(directory, name) is { } node
        ? node as JsonObject ?? throw new InvalidDataException("В файле ожидается объект данных: " + name) : new();
    internal static JsonArray Array(string directory, string name) => Read(directory, name) is { } node
        ? node as JsonArray ?? throw new InvalidDataException("В файле ожидается список данных: " + name) : new();
    internal static JsonNode? Read(string directory, string name)
    {
        string path = Path.Combine(directory, name);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 64_000_000) throw new InvalidDataException("Файл данных слишком велик.");
        return JsonNode.Parse(File.ReadAllText(path));
    }
    internal static void Write(string directory, string name, JsonNode value) => Commit(directory,
        new Dictionary<string, string?> { [name] = value.ToJsonString() });

    internal static void Commit(string directory, IReadOnlyDictionary<string, string?> changes)
    {
        Directory.CreateDirectory(directory);
        var before = new Dictionary<string, string?>();
        foreach (string name in changes.Keys)
        {
            if (Path.GetFileName(name) != name || name.Length == 0) throw new InvalidDataException("Недопустимое имя файла данных.");
            string path = Path.Combine(directory, name);
            before[name] = File.Exists(path) ? File.ReadAllText(path) : null;
        }
        var written = new List<string>();
        try
        {
            foreach (var change in changes)
            {
                Replace(Path.Combine(directory, change.Key), change.Value);
                written.Add(change.Key);
            }
        }
        catch
        {
            foreach (string name in written.AsEnumerable().Reverse()) Replace(Path.Combine(directory, name), before[name]);
            throw;
        }
    }
    private static void Replace(string path, string? content)
    {
        if (content is null) { if (File.Exists(path)) File.Delete(path); return; }
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, content); Compatibility.ReplaceFile(temporary, path); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
