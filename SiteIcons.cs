using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace Sputnik;

internal sealed class SiteIcons(string folder)
{
    private const int PreferredSize = 192;
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        UseCookies = false, MaxAutomaticRedirections = 4,
#if NET48
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
#else
        AutomaticDecompression = DecompressionMethods.All,
#endif
    });

    public Task<string?> Find(string target) => Find(target, false);

    internal Task<string?> Refresh(string target) => Find(target, true);

    private async Task<string?> Find(string target, bool refresh)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var page) || !IsWeb(page)) return null;
        string file = CacheName(page);
        string path = Path.Combine(folder, file);
        bool cached = File.Exists(path);
        string checkedPath = path + ".quality-v2";
        if (cached && (!refresh || File.Exists(checkedPath) && File.GetLastWriteTimeUtc(checkedPath) > DateTime.UtcNow.AddDays(-30))) return file;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var candidates = new List<(Uri Address, int Size)>();
        BitmapSource? best = null;
        if (cached)
        {
            try { best = Decode(File.ReadAllBytes(path)); }
            catch (Exception error) when (error is IOException or NotSupportedException or FileFormatException or ArgumentException) { }
            if (best is not null && Size(best) >= PreferredSize)
            {
                File.WriteAllText(checkedPath, "");
                return file;
            }
        }
        int previousSize = best is null ? 0 : Size(best);
        Uri? manifest = null;
        void Add(Uri basis, string relative, int size)
        {
            if (relative.Length > 0 && Uri.TryCreate(basis, relative, out var uri) && IsWeb(uri) &&
                !uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) candidates.Add((uri, size));
        }
        // Mail's desktop page only advertises a 32 px favicon; its mobile
        // launcher icon is served by Mail's own CDN (also used by Firefox).
        if (page.Host.Equals("mail.ru", StringComparison.OrdinalIgnoreCase) || page.Host.EndsWith(".mail.ru", StringComparison.OrdinalIgnoreCase))
            Add(page, "https://limg.imgsmail.ru/touchsplash/v/i/android-touch-icon-192x192-f70c697eeb.png", 192);
        try
        {
            using var response = await Client.GetAsync(page, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            var address = response.RequestMessage?.RequestUri ?? page;
            if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType?.Contains("html") == true)
            {
                byte[] head = await ReadBytes(response.Content, 196608, deadline.Token);
                foreach (Match tag in Regex.Matches(Encoding.UTF8.GetString(head), @"<link\b[^>]*>", RegexOptions.IgnoreCase))
                {
                    string rel = Attribute(tag.Value, "rel");
                    var relations = rel.Split([' '], StringSplitOptions.RemoveEmptyEntries);
                    if (relations.Any(value => value.Equals("manifest", StringComparison.OrdinalIgnoreCase)) &&
                        Uri.TryCreate(address, Attribute(tag.Value, "href"), out var manifestUri) && IsWeb(manifestUri)) manifest = manifestUri;
                    if (!relations.Any(value => value.Equals("icon", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("apple-touch-icon", StringComparison.OrdinalIgnoreCase) || value.Equals("apple-touch-icon-precomposed", StringComparison.OrdinalIgnoreCase))) continue;
                    if (candidates.Count < 20) Add(address, Attribute(tag.Value, "href"), DeclaredSize(Attribute(tag.Value, "sizes"), rel.IndexOf("apple", StringComparison.OrdinalIgnoreCase) >= 0 ? 180 : 32));
                }
            }
            Add(address, "/apple-touch-icon.png", 180);
            Add(address, "/apple-touch-icon-precomposed.png", 180);
            Add(address, "/favicon.ico", 32);
        }
        catch (HttpRequestException) { Add(page, "/favicon.ico", 32); }
        catch (OperationCanceledException) { }

        if (manifest is not null && !deadline.IsCancellationRequested)
        {
            try
            {
                using var response = await Client.GetAsync(manifest, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (response.IsSuccessStatusCode)
                {
                    byte[] bytes = await ReadBytes(response.Content, 65537, deadline.Token);
                    if (bytes.Length <= 65536)
                    {
                        using var json = JsonDocument.Parse(bytes);
                        if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("icons", out var icons) && icons.ValueKind == JsonValueKind.Array)
                            foreach (var icon in icons.EnumerateArray().Take(20))
                                if (icon.ValueKind == JsonValueKind.Object && icon.TryGetProperty("src", out var src) && src.ValueKind == JsonValueKind.String)
                                    Add(response.RequestMessage?.RequestUri ?? manifest, src.GetString()!,
                                        DeclaredSize(icon.TryGetProperty("sizes", out var sizes) && sizes.ValueKind == JsonValueKind.String ? sizes.GetString()! : "", 192));
                    }
                }
            }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or JsonException) { }
        }

        foreach (var icon in candidates.OrderByDescending(candidate => candidate.Size).Select(candidate => candidate.Address).Distinct().Take(10))
        {
            if (deadline.IsCancellationRequested) break;
            try
            {
                using var response = await Client.GetAsync(icon, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1048576) continue;
                byte[] bytes = await ReadBytes(response.Content, 1048577, deadline.Token);
                if (bytes.Length == 0 || bytes.Length > 1048576) continue;
                var bitmap = Decode(bytes);
                if (bitmap is null || best is not null && Size(bitmap) <= Size(best)) continue;
                best = bitmap;
                if (Size(best) >= PreferredSize) break;
            }
            catch (Exception error) when (error is HttpRequestException or IOException or NotSupportedException or FileFormatException or ArgumentException) { }
            catch (OperationCanceledException) { break; }
        }
        if (best is null) return cached ? file : null;
        Directory.CreateDirectory(folder);
        if (Size(best) > previousSize)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(temporary, StickyNoteContent.ToPng(best));
                Compatibility.ReplaceFile(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        File.WriteAllText(checkedPath, "");
        return file;
    }

    private static int Size(BitmapSource bitmap) => Math.Min(bitmap.PixelWidth, bitmap.PixelHeight);

    private static BitmapSource? Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        var frame = decoder.Frames.Where(image => image.PixelWidth is > 0 and <= 1024 && image.PixelHeight is > 0 and <= 1024)
            .OrderByDescending(Size).FirstOrDefault();
        if (frame is null) return null;
        // Copy while the stream is open so ICO frames and their alpha survive
        // later async continuations, cache writes and rendering on the UI thread.
        var bitmap = new WriteableBitmap(frame);
        bitmap.Freeze();
        return bitmap;
    }

    private static int DeclaredSize(string sizes, int fallback)
    {
        int best = 0;
        foreach (Match match in Regex.Matches(sizes, @"(\d{1,4})x(\d{1,4})", RegexOptions.IgnoreCase))
            if (int.TryParse(match.Groups[1].Value, out int width) && int.TryParse(match.Groups[2].Value, out int height))
                best = Math.Max(best, Math.Min(1024, Math.Min(width, height)));
        return best > 0 ? best : fallback;
    }

    public static bool IsCachedName(string? value) =>
        value is { Length: 68 } && value.EndsWith(".png", StringComparison.Ordinal) && value.Take(64).All(Uri.IsHexDigit);

    private static bool IsWeb(Uri uri) => uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0;

    private static string CacheName(Uri page)
    {
        var address = Encoding.UTF8.GetBytes(page.GetLeftPart(UriPartial.Path) + page.Query);
#if NET48
        using var sha256 = SHA256.Create();
        var hash = BitConverter.ToString(sha256.ComputeHash(address)).Replace("-", "");
#else
        var hash = Convert.ToHexString(SHA256.HashData(address));
#endif
        return hash.ToLowerInvariant() + ".png";
    }

    private static string Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, """\s""" + name + """\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))""", RegexOptions.IgnoreCase);
        return WebUtility.HtmlDecode(match.Groups.Cast<Group>().Skip(1).FirstOrDefault(group => group.Success)?.Value ?? "");
    }

    private static async Task<byte[]> ReadBytes(HttpContent content, int limit, CancellationToken token)
    {
#if NET48
        using var stream = await content.ReadAsStreamAsync();
#else
        await using var stream = await content.ReadAsStreamAsync(token);
#endif
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (output.Length < limit)
        {
            int length = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, limit - (int)output.Length), token);
            if (length == 0) break;
            output.Write(buffer, 0, length);
        }
        return output.ToArray();
    }
}
