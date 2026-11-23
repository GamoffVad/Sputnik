using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sputnik;

internal static class SiteIconMask
{
    public static string? Ensure(string folder, string file)
    {
        if (!SiteIcons.IsCachedName(file)) return null;
        string name = file[..^4] + "-mono-v3.png";
        string path = Path.Combine(folder, name);
        if (File.Exists(path) && File.GetLastWriteTimeUtc(path) >= File.GetLastWriteTimeUtc(Path.Combine(folder, file))) return name;
        try
        {
            using var source = File.OpenRead(Path.Combine(folder, file));
            var image = BitmapFrame.Create(source, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            string temp = path + "." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(temp, StickyNoteContent.ToPng(Create(image)));
                Compatibility.ReplaceFile(temp, path);
            }
            finally { File.Delete(temp); }
            return name;
        }
        catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException) { return null; }
    }

    internal static BitmapSource Create(BitmapSource image)
    {
        var bitmap = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        // Transparent corners are common on app icons. Sample the outer bands
        // for the color that surrounds the mark, considering opaque pixels only.
        var samples = new List<int>();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            if (pixels[i + 3] >= 224 && (x < width / 5d || x >= width * .8 || y < height / 5d || y >= height * .8))
                samples.Add(i);
        }
        var dominant = samples.GroupBy(i => (pixels[i] / 16, pixels[i + 1] / 16, pixels[i + 2] / 16))
            .OrderByDescending(group => group.Count()).FirstOrDefault();
        double Gray(int i) => pixels[i + 2] * .2126 + pixels[i + 1] * .7152 + pixels[i] * .0722;
        var alpha = new byte[width * height];
        int[] background = [];
        if (dominant is not null)
        {
            double b = dominant.Average(i => pixels[i]), g = dominant.Average(i => pixels[i + 1]), r = dominant.Average(i => pixels[i + 2]);
            // Compression and antialiasing can split one background color across
            // several histogram buckets. Count nearby shades together.
            background = samples.Where(i => Math.Pow(pixels[i] - b, 2) + Math.Pow(pixels[i + 1] - g, 2) +
                Math.Pow(pixels[i + 2] - r, 2) <= 48 * 48).ToArray();
        }
        if (background.Length > 0 && samples.Count >= width * height / 6d && background.Length >= samples.Count * .62)
        {
            // Turn grayscale contrast into opacity before tinting with the theme.
            // Keeping the original opacity alone flattens an opaque badge and its
            // contrasting letter into the same solid shape (e.g. Pinterest).
            double gray = background.Average(Gray);
            double contrast = Enumerable.Range(0, alpha.Length).Where(p => pixels[p * 4 + 3] >= 224)
                .Select(p => Math.Abs(Gray(p * 4) - gray)).DefaultIfEmpty(0).Max();
            if (contrast >= 24)
            {
                for (int p = 0; p < alpha.Length; p++)
                {
                    int i = p * 4;
                    alpha[p] = (byte)Math.Round(pixels[i + 3] * Compatibility.Clamp((Math.Abs(Gray(i) - gray) - 8) / (contrast - 8), 0, 1));
                }
            }
        }
        if (!alpha.Any(value => value >= 128))
        {
            // With no uniform backdrop, retain grayscale details and existing
            // transparent holes. A flat, single-color glyph keeps its silhouette.
            var visible = Enumerable.Range(0, alpha.Length).Where(p => pixels[p * 4 + 3] >= 224).ToArray();
            double low = visible.Select(p => Gray(p * 4)).DefaultIfEmpty(0).Min();
            double high = visible.Select(p => Gray(p * 4)).DefaultIfEmpty(0).Max();
            for (int p = 0; p < alpha.Length; p++)
                alpha[p] = (byte)Math.Round(pixels[p * 4 + 3] * (high - low < 24 ? 1 :
                    .2 + .8 * Compatibility.Clamp((Gray(p * 4) - low) / (high - low), 0, 1)));
        }

        int left = width, right = -1, top = height, bottom = -1;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            if (alpha[y * width + x] < 12) continue;
            left = Math.Min(left, x);
            right = Math.Max(right, x);
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
        }
        if (right < left) return bitmap;
        int cropWidth = right - left + 1, cropHeight = bottom - top + 1;
        var mask = new byte[cropWidth * cropHeight * 4];
        for (int y = 0; y < cropHeight; y++)
        for (int x = 0; x < cropWidth; x++)
        {
            int i = (y * cropWidth + x) * 4;
            mask[i] = mask[i + 1] = mask[i + 2] = 255;
            mask[i + 3] = alpha[(y + top) * width + x + left];
        }
        return BitmapSource.Create(cropWidth, cropHeight, 96, 96, PixelFormats.Bgra32, null, mask, cropWidth * 4);
    }
}
