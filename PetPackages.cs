using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace Sputnik;

internal static class PetPackages
{
    private const long Limit = 96L * 1024 * 1024;

    public static JsonObject Stage(string path, string pending, string library)
    {
        Directory.CreateDirectory(pending);
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > 40 || archive.Entries.Any(entry => entry.Length > Limit) ||
            archive.Entries.Sum(entry => entry.Length) > Limit)
            throw new InvalidDataException("Пакет слишком велик.");

        var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
        if (!entries.TryGetValue("manifest.json", out var entry) || entry.Length > 65536)
            throw new InvalidDataException("В пакете нет допустимого manifest.json.");

        using var stream = entry.Open();
        var manifest = JsonNode.Parse(stream)?.AsObject() ?? throw new InvalidDataException("Некорректный манифест.");
        if (manifest["schemaVersion"]?.GetValue<int>() != 1)
            throw new InvalidDataException("Поддерживаются пакеты версии 1.");
        string id = Required(manifest, "id", 50);
        if (!Regex.IsMatch(id, @"\A[a-z0-9][a-z0-9-]{0,48}\z"))
            throw new InvalidDataException("Недопустимый идентификатор питомца.");
        if (IsBundled(id))
            throw new InvalidDataException("Этот питомец уже входит в библиотеку Спутника.");
        if (Directory.Exists(Path.Combine(library, id)))
            throw new InvalidDataException("Питомец с таким идентификатором уже загружен.");

        string name = Required(manifest, "name", 60);
        string description = manifest["description"]?.ToString() ?? "";
        if (description.Length > 300)
            throw new InvalidDataException("Описание длиннее 300 символов.");
        var theme = manifest["theme"]?.AsObject() ?? throw new InvalidDataException("Добавьте тему питомца.");
        var cleanTheme = new JsonObject { ["name"] = Required(theme, "name", 35) };
        foreach (string key in new[] { "accent", "background", "surface" })
        {
            string color = Required(theme, key, 7);
            if (!Regex.IsMatch(color, @"\A#[0-9a-fA-F]{6}\z"))
                throw new InvalidDataException("Цвет темы должен иметь формат #RRGGBB.");
            cleanTheme[key] = color;
        }

        var animations = manifest["animations"]?.AsArray();
        if (animations is null || animations.Count is < 1 or > 24)
            throw new InvalidDataException("Добавьте от одной до 24 анимаций.");

        foreach (var stale in Directory.EnumerateDirectories(pending))
            if (Directory.GetCreationTimeUtc(stale) < DateTime.UtcNow.AddDays(-1))
                Directory.Delete(stale, true);

        string token = Guid.NewGuid().ToString("N");
        string workspace = Path.Combine(pending, token);
        Directory.CreateDirectory(workspace);
        try
        {
            var clips = new JsonArray();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < animations.Count; index++)
            {
                var clip = animations[index]?.AsObject() ?? throw new InvalidDataException("Некорректная анимация.");
                string title = Required(clip, "name", 60);
                if (!names.Add(title)) throw new InvalidDataException("Названия анимаций должны различаться.");
                int width = Number(clip, "frameWidth", 1, 2048);
                int height = Number(clip, "frameHeight", 1, 2048);
                int columns = Number(clip, "columns", 1, 64);
                int count = Number(clip, "frameCount", 1, 480);
                int fps = Number(clip, "fps", 1, 60);
                var installed = new JsonObject { ["name"] = title, ["frameWidth"] = width, ["frameHeight"] = height, ["columns"] = columns, ["frameCount"] = count, ["fps"] = fps };
                void ExtractSheet(JsonObject source, string asset, int frames)
                {
                    string file = Required(source, "file", 160);
                    if (!Regex.IsMatch(file, @"\A(?:[A-Za-z0-9_-]+/)*[A-Za-z0-9_-]+\.png\z", RegexOptions.IgnoreCase) ||
                        !entries.TryGetValue(file, out var image))
                        throw new InvalidDataException("Укажите корректный путь к PNG-листу.");
                    string target = Path.Combine(workspace, asset);
                    image.ExtractToFile(target);
                    using var png = File.OpenRead(target);
                    var frame = new PngBitmapDecoder(png, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                    if (frame.PixelWidth > 8192 || frame.PixelHeight > 8192 ||
                        (long)frame.PixelWidth * frame.PixelHeight > 32_000_000 ||
                        frame.PixelWidth != width * columns || frame.PixelHeight != height * ((frames + columns - 1) / columns))
                        throw new InvalidDataException("Размер PNG не соответствует сетке кадров.");
                }
                if (clip["sheets"] is JsonArray sheets)
                {
                    if (sheets.Count is < 1 or > 12) throw new InvalidDataException("Некорректное число PNG-листов.");
                    var installedSheets = new JsonArray();
                    int total = 0;
                    for (int page = 0; page < sheets.Count; page++)
                    {
                        var source = sheets[page] as JsonObject ?? throw new InvalidDataException("Некорректный PNG-лист.");
                        int frames = Number(source, "frames", 1, count - total);
                        string asset = $"clip-{index}-{page}.png";
                        ExtractSheet(source, asset, frames);
                        installedSheets.Add(new JsonObject { ["file"] = asset, ["frames"] = frames });
                        total += frames;
                    }
                    if (total != count) throw new InvalidDataException("Число кадров в PNG-листах не совпадает с анимацией.");
                    installed["sheets"] = installedSheets;
                }
                else
                {
                    string asset = $"clip-{index}.png";
                    ExtractSheet(clip, asset, count);
                    installed["file"] = asset;
                }
                clips.Add(installed);
            }

            var result = new JsonObject { ["id"] = id, ["name"] = name, ["description"] = description, ["theme"] = cleanTheme, ["animations"] = clips };
            if (manifest["displayScale"] is JsonValue value && value.TryGetValue<double>(out double scale) && scale is >= 0.75 and <= 1.6)
                result["displayScale"] = scale;
            File.WriteAllText(Path.Combine(workspace, "pet.json"), result.ToJsonString());
            var preview = (JsonObject)result.DeepClone();
            preview["token"] = token;
            return preview;
        }
        catch
        {
            Directory.Delete(workspace, true);
            throw;
        }
    }

    public static JsonObject Install(string token, string pending, string library)
    {
        if (!Regex.IsMatch(token, @"\A[0-9a-f]{32}\z"))
            throw new InvalidDataException("Откройте файл питомца снова.");
        string source = Path.Combine(pending, token);
        var pet = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "pet.json")))!.AsObject();
        string id = pet["id"]!.GetValue<string>();
        string destination = Path.Combine(library, id);
        if (Directory.Exists(destination))
            throw new InvalidDataException("Этот питомец уже в библиотеке.");
        Directory.Move(source, destination);
        return pet;
    }

    public static JsonArray Installed(string library)
    {
        var pets = new JsonArray();
        if (!Directory.Exists(library)) return pets;
        var bundledIds = ReadBundledIds();
        foreach (string directory in Directory.EnumerateDirectories(library))
        {
            string file = Path.Combine(directory, "pet.json");
            if (!File.Exists(file)) continue;
            var pet = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            if (pet["id"]?.GetValue<string>() is string id && bundledIds.Contains(id)) continue;
            pets.Add(pet);
        }
        return pets;
    }

    private static bool IsBundled(string id) => ReadBundledIds().Contains(id);

    private static HashSet<string> ReadBundledIds()
    {
        string manifest = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "pets.json");
        var pets = JsonNode.Parse(File.ReadAllText(manifest))!.AsArray();
        return new HashSet<string>(pets.Select(pet => pet?["id"]?.GetValue<string>()).OfType<string>(), StringComparer.Ordinal);
    }

    private static string Required(JsonObject obj, string key, int max)
    {
        string? value = obj[key]?.GetValue<string>()?.Trim();
        return value is not null && !string.IsNullOrWhiteSpace(value) && value.Length <= max ? value :
            throw new InvalidDataException($"Проверьте поле {key}.");
    }

    private static int Number(JsonObject obj, string key, int low, int high)
    {
        int value = obj[key]?.GetValue<int>() ?? 0;
        return value >= low && value <= high ? value : throw new InvalidDataException($"Проверьте значение {key}.");
    }
}
