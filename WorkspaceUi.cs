using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Sputnik;

internal static class WorkspaceUi
{
    internal const string SendIcon = "M8 14V2 M2 8L8 2 14 8";
    internal const string ExpandIcon = "M9 1H15V7 M15 1L8 8 M6 3H2V14H13V10";

    internal static Button IconButton(string label, string geometry, bool primary = false)
    {
        var icon = new Path
        {
            Data = Geometry.Parse(geometry), Width = 16, Height = 16, Stretch = Stretch.Uniform,
            StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform()
        };
        var button = new Button { Content = icon, Width = 32, Height = 32, Padding = new Thickness(8), ToolTip = label };
        if (primary) button.SetResourceReference(FrameworkElement.StyleProperty, "SputnikPrimaryButton");
        icon.SetBinding(Shape.StrokeProperty, new Binding(nameof(Control.Foreground)) { Source = button });
        AutomationProperties.SetName(button, label);
        button.MouseEnter += (_, _) => Scale(1.08);
        button.MouseLeave += (_, _) => Scale(1);
        button.PreviewMouseLeftButtonDown += (_, _) => Scale(.9);
        button.PreviewMouseLeftButtonUp += (_, _) => Scale(button.IsMouseOver ? 1.08 : 1);
        button.PreviewKeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) Scale(.9); };
        button.PreviewKeyUp += (_, e) => { if (e.Key is Key.Space or Key.Enter) Scale(1); };
        button.IsEnabledChanged += (_, _) => Scale(1);
        return button;

        void Scale(double value)
        {
            var transform = (ScaleTransform)icon.RenderTransform;
            var animation = SystemParameters.ClientAreaAnimation ? new DoubleAnimation(value, TimeSpan.FromMilliseconds(100))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } } : null;
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
            if (animation is null) transform.ScaleX = transform.ScaleY = value;
        }
    }

    internal static TextBox MessageText(string text)
    {
        var field = new TextBox
        {
            Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0),
            MinHeight = 0, FontSize = 14, VerticalContentAlignment = VerticalAlignment.Top,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        field.UseTheme(Control.ForegroundProperty, "Ink");
        return field;
    }

    internal static void Reveal(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        var slide = new TranslateTransform();
        element.RenderTransform = slide;
        if (!SystemParameters.ClientAreaAnimation) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
    }

    internal static void SetWaiting(TextBlock label, bool waiting)
    {
        label.BeginAnimation(UIElement.OpacityProperty, null);
        label.Opacity = 1;
        if (!waiting || !SystemParameters.ClientAreaAnimation) return;
        label.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.55, 1, TimeSpan.FromMilliseconds(650))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
    }
}
