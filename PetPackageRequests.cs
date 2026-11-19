using System.IO;

namespace Sputnik;

// The single-instance event has no payload. Commit the file request before signaling it.
internal static class PetPackageRequests
{
    internal static bool Enqueue(IEnumerable<string> arguments, string data)
    {
        bool queued = false;
        foreach (string argument in arguments)
        {
            if (argument.Length > 32767 || !string.Equals(Path.GetExtension(argument), ".pet", StringComparison.OrdinalIgnoreCase) || !File.Exists(argument)) continue;
            string folder = Path.Combine(data, "pet-open-requests");
            Directory.CreateDirectory(folder);
            string ready = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".request");
            File.WriteAllText(ready + ".tmp", Path.GetFullPath(argument));
            File.Move(ready + ".tmp", ready);
            queued = true;
        }
        return queued;
    }

    internal static string[] Drain(string data)
    {
        var paths = new List<string>();
        string folder = Path.Combine(data, "pet-open-requests");
        if (!Directory.Exists(folder)) return [];
        foreach (string request in Directory.EnumerateFiles(folder, "*.request").Take(32))
        {
            try
            {
                if (new FileInfo(request).Length <= 131072)
                {
                    string path = File.ReadAllText(request);
                    if (File.Exists(path) && string.Equals(Path.GetExtension(path), ".pet", StringComparison.OrdinalIgnoreCase)) paths.Add(path);
                }
                File.Delete(request);
            }
            catch (IOException) { }
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
