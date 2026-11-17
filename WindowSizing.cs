using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Sputnik;

internal static class WindowSizing
{
    private sealed class State { public string? Key; public bool Enabled; }
    private static readonly ConditionalWeakTable<Window, State> States = new();
    private static readonly Dictionary<string, Size> Sizes = [];

    public static void UseSize(Window window, string key, double width, double height, Size? limit = null)
    {
        States.GetOrCreateValue(window).Key = key;
        var size = Sizes.TryGetValue(key, out var saved) ? saved : new Size(width, height);
        window.Width = Math.Max(window.MinWidth, Math.Min(size.Width, limit?.Width ?? window.MaxWidth));
        window.Height = Math.Max(window.MinHeight, Math.Min(size.Height, limit?.Height ?? window.MaxHeight));
    }

    private static void Remember(Window window)
    {
        if (States.GetOrCreateValue(window).Key is { } key)
            Sizes[key] = new Size(window.Width, window.Height);
    }

    public static void Enable(Window window, FrameworkElement content)
    {
        var state = States.GetOrCreateValue(window);
        if (state.Enabled) return;
        state.Enabled = true;
        window.ResizeMode = ResizeMode.CanResize;
        var layer = new Grid();
        window.Content = null;
        layer.Children.Add(content);
        window.Content = layer;

        void Edge(int x, int y, string name)
        {
            bool corner = x != 0 && y != 0;
            var grip = new Thumb
            {
                HorizontalAlignment = x < 0 ? HorizontalAlignment.Left : x > 0 ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                VerticalAlignment = y < 0 ? VerticalAlignment.Top : y > 0 ? VerticalAlignment.Bottom : VerticalAlignment.Stretch,
                Width = x == 0 ? double.NaN : corner ? 11 : 5,
                Height = y == 0 ? double.NaN : corner ? 11 : 5,
                Cursor = x == 0 ? Cursors.SizeNS : y == 0 ? Cursors.SizeWE : x == y ? Cursors.SizeNWSE : Cursors.SizeNESW,
                Focusable = corner && x > 0 && y > 0,
                IsTabStop = corner && x > 0 && y > 0,
                ToolTip = "Изменить размер окна"
            };
            AutomationProperties.SetName(grip, "Размер окна: " + name);
            AutomationProperties.SetHelpText(grip, "Перетащите край; в правом нижнем углу доступны клавиши со стрелками.");
            var hit = new FrameworkElementFactory(typeof(Border));
            hit.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var template = new ControlTemplate(typeof(Thumb)) { VisualTree = hit };
            if (corner && x > 0 && y > 0)
            {
                var mark = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
                mark.Name = "ResizeMark";
                mark.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M2 8L8 2 M6 8L8 6"));
                mark.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.0);
                mark.SetValue(System.Windows.Shapes.Shape.StrokeStartLineCapProperty, PenLineCap.Round);
                mark.SetValue(System.Windows.Shapes.Shape.StrokeEndLineCapProperty, PenLineCap.Round);
                mark.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ThemeMuted");
                // Inset the decoration without shrinking or moving the corner's drag target.
                mark.SetValue(UIElement.RenderTransformProperty, new TranslateTransform(-4, -4));
                mark.SetValue(UIElement.OpacityProperty, .45);
                mark.SetValue(UIElement.IsHitTestVisibleProperty, false);
                hit.AppendChild(mark);
                foreach (var property in new[] { UIElement.IsMouseOverProperty, UIElement.IsKeyboardFocusedProperty })
                {
                    var highlight = new Trigger { Property = property, Value = true };
                    highlight.Setters.Add(new Setter(UIElement.OpacityProperty, .9, "ResizeMark"));
                    template.Triggers.Add(highlight);
                }
            }
            grip.Template = template;
            Point pointer = default;
            Rect initial = default;
            Matrix fromDevice = Matrix.Identity;
            grip.DragStarted += (_, _) =>
            {
                GetCursorPos(out var start);
                pointer = new Point(start.X, start.Y);
                initial = new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
                fromDevice = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            };
            grip.DragDelta += (_, _) =>
            {
                GetCursorPos(out var current);
                var delta = fromDevice.Transform(new Vector(current.X - pointer.X, current.Y - pointer.Y));
                ResizeFrom(window, initial, delta, x, y);
            };
            grip.DragCompleted += (_, e) =>
            {
                if (e.Canceled) ResizeFrom(window, initial, new Vector(), x, y);
                else Remember(window);
            };
            grip.KeyDown += (_, e) =>
            {
                var delta = e.Key switch
                {
                    Key.Left => new Vector(-10, 0), Key.Right => new Vector(10, 0),
                    Key.Up => new Vector(0, -10), Key.Down => new Vector(0, 10), _ => new Vector()
                };
                if (delta.LengthSquared == 0) return;
                ResizeFrom(window, new Rect(window.Left, window.Top, window.Width, window.Height), delta, x, y);
                Remember(window);
                e.Handled = true;
            };
            layer.Children.Add(grip);
        }

        Edge(-1, 0, "слева"); Edge(1, 0, "справа");
        Edge(0, -1, "сверху"); Edge(0, 1, "снизу");
        Edge(-1, -1, "слева сверху"); Edge(1, -1, "справа сверху");
        Edge(-1, 1, "слева снизу"); Edge(1, 1, "справа снизу");
    }

    internal static void ResizeFrom(Window window, Rect initial, Vector delta, int x, int y)
    {
        double width = x == 0 ? initial.Width : Compatibility.Clamp(initial.Width + x * delta.X,
            Math.Max(1, window.MinWidth), Math.Max(window.MinWidth, Math.Min(window.MaxWidth, SystemParameters.VirtualScreenWidth)));
        double height = y == 0 ? initial.Height : Compatibility.Clamp(initial.Height + y * delta.Y,
            Math.Max(1, window.MinHeight), Math.Max(window.MinHeight, Math.Min(window.MaxHeight, SystemParameters.VirtualScreenHeight)));
        window.Width = width;
        window.Height = height;
        if (x < 0) window.Left = initial.Right - width;
        if (y < 0) window.Top = initial.Bottom - height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);
}
