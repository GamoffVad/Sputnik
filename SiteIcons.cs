using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace Sputnik;

internal sealed class SiteIcons(string folder)
{
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        UseCookies = false, MaxAutomaticRedirections = 4,
#if NET48
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
#else
        AutomaticDecompression = DecompressionMethods.All,
#endif
    });

    public async Task<string?> Find(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var page) || !IsWeb(page)) return null;
        string file = CacheName(page);
        string path = Path.Combine(folder, file);
        if (File.Exists(path)) return file;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var candidates = new List<Uri>();
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
                    if (!rel.Split([' '], StringSplitOptions.RemoveEmptyEntries).Any(value => value.Equals("icon", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("apple-touch-icon", StringComparison.OrdinalIgnoreCase))) continue;
                    if (Uri.TryCreate(address, Attribute(tag.Value, "href"), out var icon) && IsWeb(icon))
                        candidates.Add(icon);
                    if (candidates.Count == 5) break;
                }
            }
            candidates.Add(new Uri(address, "/favicon.ico"));
        }
        catch (HttpRequestException) { candidates.Add(new Uri(page, "/favicon.ico")); }
        catch (OperationCanceledException) { return null; }

        foreach (var icon in candidates.Distinct())
        {
            try
            {
                using var response = await Client.GetAsync(icon, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1048576) continue;
                byte[] bytes = await ReadBytes(response.Content, 1048577, deadline.Token);
                if (bytes.Length == 0 || bytes.Length > 1048576) continue;
                using var stream = new MemoryStream(bytes);
                BitmapSource bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
                if (Math.Max(bitmap.PixelWidth, bitmap.PixelHeight) > 1024) continue;
                byte[] png = StickyNoteContent.ToPng(bitmap);
                Directory.CreateDirectory(folder);
                string temporary = path + "." + Guid.NewGuid().ToString("N");
                File.WriteAllBytes(temporary, png);
                Compatibility.ReplaceFile(temporary, path);
                return file;
            }
            catch (Exception error) when (error is HttpRequestException or IOException or NotSupportedException or FileFormatException or ArgumentException) { }
            catch (OperationCanceledException) { return null; }
        }
        return null;
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
