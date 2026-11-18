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
