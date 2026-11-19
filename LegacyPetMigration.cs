using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;

namespace Sputnik;

internal static class LegacyPetMigration
{
    // Only preserve the user's active former built-in. Fresh installations have
    // no backup and get Klyaksa alone; already imported pets are never changed.
    internal static void Run(string data)
    {
        string workspace = Path.Combine(data, "workspace.json");
        string assets = Path.Combine(data, "legacy-bundled-pets");
        string catalog = Path.Combine(assets, "pets.json");
        if (!File.Exists(workspace) || !File.Exists(catalog)) return;
        string? active = JsonNode.Parse(File.ReadAllText(workspace))?["active"]?.GetValue<string>();
        if (string.IsNullOrEmpty(active) || active == "klyaksa") return;
        string library = Path.Combine(data, "pets");
        if (Directory.Exists(Path.Combine(library, active))) return;
        var pet = JsonNode.Parse(File.ReadAllText(catalog))?.AsArray().OfType<JsonObject>()
            .FirstOrDefault(p => p["id"]?.GetValue<string>() == active)?.DeepClone().AsObject();
        if (pet is null) return;
        pet["schemaVersion"] = 1;
        string package = Path.Combine(data, "legacy-migration-" + Guid.NewGuid().ToString("N") + ".pet");
        try
        {
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                var added = new HashSet<string>(StringComparer.Ordinal);
                foreach (var clip in pet["animations"]!.AsArray().OfType<JsonObject>())
                {
                    var sheets = clip["sheets"] as JsonArray;
                    var parts = sheets is null ? new[] { clip } : sheets.OfType<JsonObject>();
                    foreach (var part in parts)
                    {
                        string file = part["file"]!.GetValue<string>();
                        if (!file.StartsWith("assets/", StringComparison.Ordinal)) throw new InvalidDataException("Некорректный ресурс старого питомца.");
                        string source = Path.GetFullPath(Path.Combine(assets, file.Substring(7)));
                        if (!source.StartsWith(Path.GetFullPath(assets) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Некорректный путь старого питомца.");
                        if (clip["frameWidth"] is null)
                        {
                            using var png = File.OpenRead(source);
                            var frame = new PngBitmapDecoder(png, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                            clip["frameWidth"] = frame.PixelWidth; clip["frameHeight"] = frame.PixelHeight; clip["columns"] = 1;
                        }
                        if (added.Add(file)) zip.CreateEntryFromFile(source, file, CompressionLevel.NoCompression);
                    }
                }
                using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
                writer.Write(pet.ToJsonString());
            }
            string pending = Path.Combine(data, "pet-pending");
            var staged = PetPackages.Stage(package, pending, library);
            PetPackages.Install(staged["token"]!.GetValue<string>(), pending, library);
        }
        finally { File.Delete(package); }
    }
}
