using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Sputnik;

internal sealed class QuickChatWindow : Window
{
    private readonly TextBox input;
    private readonly Border frame;
    private readonly Action<Window> position;
    private readonly Action openFull;
    private readonly Func<string, Task<string>> send;
    private bool waiting;
    private bool dismissed;

    public QuickChatWindow(Action<Window> position, Action openFull, Func<string, Task<string>> send)
    {
        this.position = position;
        this.openFull = openFull;
        this.send = send;
        Title = "Быстрый чат Спутника";
        Width = 300;
        Height = 38;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        this.UseTheme(FontFamilyProperty, "Font");
        frame = new Border { BorderThickness = new Thickness(1) };
        frame.UseTheme(Border.BackgroundProperty, "Surface");
        frame.UseTheme(Border.BorderBrushProperty, "Edge");
        Content = frame;
        RoundedWindow.Apply(this, frame);
        input = new TextBox
        {
            BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Padding = new Thickness(0), MinHeight = 0, FontSize = 14, MaxLength = 10000, VerticalContentAlignment = VerticalAlignment.Center
        };
        input.UseTheme(Control.ForegroundProperty, "Ink");
        input.UseTheme(TextBoxBase.CaretBrushProperty, "Accent");
        AutomationProperties.SetName(input, "Сообщение модели");
        input.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await Submit();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Dismiss();
            e.Handled = true;
        };
    }

    public void Compose()
    {
        if (waiting)
        {
            dismissed = false;
            ShowWaiting();
            return;
        }
        if (frame.Child is DockPanel { Tag: "compose" } && IsVisible)
        {
            Activate();
            input.Focus();
            return;
        }
        var row = new DockPanel { Tag = "compose", Margin = new Thickness(10, 4, 4, 4) };
        var submit = Button("Отправить", primary: true);
        submit.ToolTip = "Отправить (Enter)";
        AutomationProperties.SetName(submit, "Отправить");
        var arrow = new Path
        {
            Data = Geometry.Parse("M2 8H15 M9 2L15 8 9 14"),
            Width = 16, Height = 16, Stretch = Stretch.Uniform,
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        arrow.UseTheme(Shape.StrokeProperty, "AccentInk");
        submit.Content = arrow;
        submit.Click += async (_, _) => await Submit();
        DockPanel.SetDock(submit, Dock.Right);
        row.Children.Add(submit);
        var hint = new TextBlock { Text = "Спросить питомца…", FontSize = 14,
            IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        hint.UseTheme(TextBlock.ForegroundProperty, "Muted");
        var field = new Grid { Margin = new Thickness(0, 0, 10, 0) };
        if (input.Parent is Panel previous) previous.Children.Remove(input);
        field.Children.Add(input);
        field.Children.Add(hint);
        BindingOperations.SetBinding(hint, VisibilityProperty, new System.Windows.Data.Binding(nameof(TextBox.Text))
        {
            Source = input, Converter = new HintVisibility()
        });
        row.Children.Add(field);
        Present(row, 300, 38);
        Activate();
        input.Focus();
    }

    private async Task Submit()
    {
        string message = input.Text.Trim();
        if (waiting || message.Length == 0) return;
        input.Clear();
        dismissed = false;
        waiting = true;
        ShowWaiting();
        string answer;
        try { answer = await send(message); }
        catch (Exception error) { answer = error.Message; }
        finally { waiting = false; }
        if (dismissed || Application.Current.Dispatcher.HasShutdownStarted) return;
        ShowAnswer(answer);
    }

    private void ShowWaiting()
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(dots, "Ожидание ответа");
        for (int index = 0; index < 3; index++)
        {
            var scale = new ScaleTransform(.65, .65);
            var dot = new Ellipse
            {
                Width = 10, Height = 10, Margin = new Thickness(4, 0, 4, 0),
                RenderTransform = scale, RenderTransformOrigin = new Point(.5, .5)
            };
            dot.UseTheme(Shape.FillProperty, "Accent");
            if (SystemParameters.ClientAreaAnimation)
            {
                var pulse = new DoubleAnimation(.55, 1.17, TimeSpan.FromMilliseconds(420))
                {
                    BeginTime = TimeSpan.FromMilliseconds(index * 145), AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
            }
            dots.Children.Add(dot);
        }
        Present(dots, 65, 30);
    }

    private void ShowAnswer(string answer)
    {
        var text = new TextBlock { Text = answer, FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 21 };
        text.UseTheme(TextBlock.ForegroundProperty, "Ink");
        text.Measure(new Size(276, double.PositiveInfinity));
        var stack = new DockPanel { Margin = new Thickness(11, 7, 11, 7) };
        var full = Button("Открыть чат");
        full.HorizontalAlignment = HorizontalAlignment.Right;
        full.Click += (_, _) => { Hide(); openFull(); };
        DockPanel.SetDock(full, Dock.Bottom);
        stack.Children.Add(full);
        stack.Children.Add(new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Present(stack, 300, Compatibility.Clamp(text.DesiredSize.Height + 45, 63, 195));
    }

    internal void Dismiss()
    {
        dismissed = true;
        Hide();
    }

    private void Present(UIElement child, double width, double height)
    {
        ShowActivated = child is DockPanel { Tag: "compose" };
        frame.Child = child;
        Width = width;
        Height = height;
        position(this);
        if (!IsVisible) Show();
        var slide = new TranslateTransform(0, 5);
        frame.RenderTransform = slide;
        if (!SystemParameters.ClientAreaAnimation) { slide.Y = 0; return; }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        frame.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(5, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
    }

    private static Button Button(string title, bool primary = false)
    {
        var button = new Button
        {
            Content = title, MinHeight = 26, Padding = new Thickness(8, 2, 8, 2)
        };
        if (primary) button.SetResourceReference(StyleProperty, "SputnikPrimaryButton");
        return button;
    }

    private sealed class HintVisibility : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type target, object parameter, System.Globalization.CultureInfo culture) =>
            string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type target, object parameter, System.Globalization.CultureInfo culture) =>
            DependencyProperty.UnsetValue;
    }
}
