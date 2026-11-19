using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sputnik;

internal sealed record PetViewport(double X, double Y, double Width, double Height)
{
    public Rect Bounds => new(X, Y, Width, Height);
}

internal static class PetFrames
{
    public static ImageSource Transition(ImageSource frame, ImageSource neutral, int index, int count, int frames, Size pixels, BitmapScalingMode scaling)
    {
        int span = Math.Min(frames, (count - 1) / 2);
        if (span <= 0) return frame;
        double amount = Math.Min(1, Math.Min(index, count - 1 - index) / (double)span);
        if (amount >= 1) return frame;
        if (amount <= 0) return neutral;
        int width = Math.Max(1, (int)Math.Ceiling(pixels.Width));
        int height = Math.Max(1, (int)Math.Ceiling(pixels.Height));
        byte[] Render(ImageSource source)
        {
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, scaling);
            double scale = Math.Min(width / source.Width, height / source.Height);
            double w = source.Width * scale, h = source.Height * scale;
            using (var drawing = visual.RenderOpen())
                drawing.DrawImage(source, new Rect((width - w) / 2, (height - h) / 2, w, h));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var bytes = new byte[width * height * 4];
            bitmap.CopyPixels(bytes, width * 4, 0);
            return bytes;
        }
        // Mix premultiplied pixels directly: two opacity layers would dim opaque overlap.
        byte[] before = Render(neutral), after = Render(frame);
        int mix = (int)Math.Round(amount * 256);
        for (int i = 0; i < after.Length; i++)
            after[i] = (byte)((before[i] * (256 - mix) + after[i] * mix + 128) >> 8);
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, after, width * 4);
        result.Freeze();
        return result;
    }

    public static ImageSource Present(BitmapSource frame, PetViewport? viewport)
    {
        if (viewport is null) return frame;
        var bounds = viewport.Bounds;
        var drawing = new DrawingGroup { ClipGeometry = new RectangleGeometry(bounds) };
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(bounds)));
        drawing.Children.Add(new ImageDrawing(frame, new Rect(0, 0, frame.PixelWidth, frame.PixelHeight)));
        drawing.Freeze();
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
}
