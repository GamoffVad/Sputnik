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
        Present(dots, 65, 30);
    }

    private void ShowAnswer(string answer)
    {
        var text = WorkspaceUi.MessageText(answer);
        AutomationProperties.SetName(text, "Ответ модели");
        var measure = new TextBlock { Text = answer, FontFamily = FontFamily, FontSize = text.FontSize, TextWrapping = TextWrapping.Wrap };
        measure.Measure(new Size(276, double.PositiveInfinity));
        var stack = new DockPanel { Margin = new Thickness(10, 6, 10, 10) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
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
        Present(stack, 300, Compatibility.Clamp(measure.DesiredSize.Height + 54, 78, 195));
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
        bool loading = child is StackPanel;
        MinWidth = loading ? 65 : 220;
        MinHeight = loading ? 30 : child is DockPanel { Tag: "compose" } ? 44 : 78;
        Width = width;
        Height = height;
        position(this);
        if (!IsVisible) Show();
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
