using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sputnik;

internal static class SiteIconMask
{
    public static string? Ensure(string folder, string file)
    {
        if (!SiteIcons.IsCachedName(file)) return null;
        string name = file[..^4] + "-mono.png";
        string path = Path.Combine(folder, name);
        if (File.Exists(path)) return name;
        try
        {
            using var source = File.OpenRead(Path.Combine(folder, file));
            var image = BitmapFrame.Create(source, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            string temp = path + "." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(temp, StickyNoteContent.ToPng(Create(image)));
                File.Move(temp, path, overwrite: true);
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
        var alpha = new byte[width * height];
        if (dominant is not null && samples.Count >= width * height / 6d && dominant.Count() >= samples.Count * .62)
        {
            double b = dominant.Average(i => pixels[i]), g = dominant.Average(i => pixels[i + 1]), r = dominant.Average(i => pixels[i + 2]);
            for (int p = 0; p < alpha.Length; p++)
            {
                int i = p * 4;
                double distance = Math.Sqrt(Math.Pow(pixels[i] - b, 2) + Math.Pow(pixels[i + 1] - g, 2) + Math.Pow(pixels[i + 2] - r, 2));
                alpha[p] = (byte)Math.Round(pixels[i + 3] * Math.Clamp((distance - 14) / 72, 0, 1));
            }
        }
        if (!alpha.Any(value => value >= 128))
            for (int p = 0; p < alpha.Length; p++) alpha[p] = pixels[p * 4 + 3];

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
