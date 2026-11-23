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
    private Button? submitButton;
    private bool waiting;
    private bool dismissed;
    private Size? answerSize;
    private Window? waitingSurface;
    private Point? answerPosition;
    internal event Action? PresentationChanged;
    internal bool IsPresented => IsVisible || waitingSurface?.IsVisible == true;
    internal Window VisibleSurface => waitingSurface?.IsVisible == true ? waitingSurface : this;
    internal void FollowPosition()
    {
        if (IsPresented && !(VisibleSurface == this && frame.Child is Grid && answerPosition is not null)) position(VisibleSurface);
    }

    public QuickChatWindow(Action<Window> position, Action openFull, Func<string, Task<string>> send)
    {
        this.position = position;
        this.openFull = openFull;
        this.send = send;
        Title = "Быстрый чат Спутника";
        Width = 300;
        Height = 44;
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
        IsVisibleChanged += (_, _) => PresentationChanged?.Invoke();
        Closed += (_, _) => waitingSurface?.Close();
        input = new TextBox
        {
            BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Padding = new Thickness(0), MinHeight = 0, FontSize = 14, MaxLength = 10000, VerticalContentAlignment = VerticalAlignment.Center
        };
        input.UseTheme(Control.ForegroundProperty, "Ink");
        input.UseTheme(TextBoxBase.CaretBrushProperty, "Accent");
        AutomationProperties.SetName(input, "Сообщение модели");
        input.TextChanged += (_, _) => { if (submitButton is not null) submitButton.IsEnabled = !string.IsNullOrWhiteSpace(input.Text); };
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
        var submit = submitButton = WorkspaceUi.IconButton("Отправить сообщение (Enter)", WorkspaceUi.SendIcon, true);
        submit.IsEnabled = !string.IsNullOrWhiteSpace(input.Text);
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
        Present(row, 300, 44);
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
        AutomationProperties.SetLiveSetting(dots, AutomationLiveSetting.Polite);
        for (int index = 0; index < 3; index++)
        {
            var scale = new ScaleTransform(.65, .65);
            var dot = new Ellipse
            {
                Width = 6, Height = 6, Margin = new Thickness(4, 0, 4, 0),
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
            dot.Unloaded += (_, _) =>
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            };
            dots.Children.Add(dot);
        }
        waitingSurface ??= new Window
        {
            Title = "Ожидание ответа Спутника", Width = 65, Height = 30,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = Brushes.Transparent,
            ShowInTaskbar = false, ShowActivated = false, Topmost = Topmost
        };
        waitingSurface.Owner = Owner;
        waitingSurface.Content = dots;
        position(waitingSurface);
        if (!waitingSurface.IsVisible) waitingSurface.Show();
        Hide();
        PresentationChanged?.Invoke();
    }

    private void ShowAnswer(string answer)
    {
        var text = WorkspaceUi.MessageText(answer);
        AutomationProperties.SetName(text, "Ответ модели");
        var measure = new TextBlock { Text = answer, FontFamily = FontFamily, FontSize = text.FontSize, TextWrapping = TextWrapping.Wrap };
        measure.Measure(new Size(276, double.PositiveInfinity));
        var stack = new DockPanel { Margin = new Thickness(10, 6, 10, 10) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        header.Background = Brushes.Transparent;
        header.Cursor = Cursors.SizeAll;
        header.ToolTip = "Перетащите, чтобы переместить ответ";
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled) return;
            DragMove();
            answerPosition = new Point(Left, Top);
        };
        var full = WorkspaceUi.IconButton("Открыть полный чат", WorkspaceUi.ExpandIcon);
        full.Click += (_, _) => { Hide(); openFull(); };
        DockPanel.SetDock(full, Dock.Right);
        header.Children.Add(full);
        var title = new TextBlock { Text = "Ответ", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.UseTheme(TextBlock.ForegroundProperty, "Muted");
        header.Children.Add(title);
        DockPanel.SetDock(header, Dock.Top);
        stack.Children.Add(header);
        stack.Children.Add(new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

        var grip = new Thumb
        {
            Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 2, 2), Cursor = Cursors.SizeNWSE, ToolTip = "Изменить размер", Focusable = true
        };
        AutomationProperties.SetName(grip, "Изменить размер окна ответа");
        var gripShape = new FrameworkElementFactory(typeof(Path));
        gripShape.SetValue(Path.DataProperty, Geometry.Parse("M3 15L15 3 M8 15L15 8"));
        gripShape.SetValue(Shape.StrokeThicknessProperty, 1.2);
        gripShape.SetResourceReference(Shape.StrokeProperty, "ThemeMuted");
        grip.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = gripShape };
        void Resize(double dx, double dy)
        {
            Width = Compatibility.Clamp(Width + dx, MinWidth, Math.Max(MinWidth, SystemParameters.VirtualScreenWidth));
            Height = Compatibility.Clamp(Height + dy, MinHeight, Math.Max(MinHeight, SystemParameters.VirtualScreenHeight));
            answerSize = new Size(Width, Height);
        }
        grip.DragDelta += (_, e) => Resize(e.HorizontalChange, e.VerticalChange);
        grip.KeyDown += (_, e) =>
        {
            var delta = e.Key switch { Key.Left => (-10, 0), Key.Right => (10, 0), Key.Up => (0, -10), Key.Down => (0, 10), _ => (0, 0) };
            if (delta == (0, 0)) return;
            Resize(delta.Item1, delta.Item2);
            e.Handled = true;
        };

        var layer = new Grid();
        layer.Children.Add(stack);
        layer.Children.Add(grip);
        double width = answerSize?.Width ?? 300;
        double height = answerSize?.Height ?? Compatibility.Clamp(measure.DesiredSize.Height + 54, 78, 195);
        Present(layer, width, height);
    }

    internal void Dismiss()
    {
        dismissed = true;
        waitingSurface?.Hide();
        Hide();
        PresentationChanged?.Invoke();
    }

    private void Present(UIElement child, double width, double height)
    {
        ShowActivated = child is DockPanel { Tag: "compose" };
        frame.Child = child;
        bool loading = child is StackPanel;
        MinWidth = loading ? 65 : 220;
        MinHeight = loading ? 30 : child is DockPanel { Tag: "compose" } ? 44 : 78;
        Width = width;
        Height = height;
        position(this);
        if (child is Grid && answerPosition is { } saved)
        {
            var area = SystemParameters.WorkArea;
            Left = Compatibility.Clamp(saved.X, area.Left, Math.Max(area.Left, area.Right - Width));
            Top = Compatibility.Clamp(saved.Y, area.Top, Math.Max(area.Top, area.Bottom - Height));
        }
        if (!IsVisible) Show();
        waitingSurface?.Hide();
        PresentationChanged?.Invoke();
        // The rounded-window clip was computed before this replacement child
        // had a layout, leaving a zero-sized clip on reopen at the same size.
        UpdateLayout();
        RoundedWindow.UpdateShape(this, frame);
        WorkspaceUi.Reveal(frame);
    }

    private sealed class HintVisibility : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type target, object parameter, System.Globalization.CultureInfo culture) =>
            string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type target, object parameter, System.Globalization.CultureInfo culture) =>
            DependencyProperty.UnsetValue;
    }
}
