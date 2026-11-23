using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Sputnik;

// Notifications keep their content-sized dimensions when placed beside the pet.
internal sealed class NotificationWindow : Window
{
    internal NotificationWindow(string heading, string message, string? detail, Action<string> act,
        Action? open = null, string openLabel = "Открыть заметку")
    {
        Title = heading + " Спутника";
        Width = 360;
        MaxHeight = 360;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        UseLayoutRounding = true;
        this.UseTheme(FontFamilyProperty, "Font");
        this.UseTheme(ForegroundProperty, "Ink");

        var body = new StackPanel { Margin = new Thickness(18, 14, 18, 18) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var close = WorkspaceUi.IconButton("Закрыть уведомление (Esc)", "M4 4 L12 12 M12 4 L4 12");
        close.Width = close.Height = 28;
        close.Background = Brushes.Transparent;
        close.Margin = new Thickness(8, -3, -6, -3);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var bell = new Path
        {
            Data = Geometry.Parse("M3 11 L4.5 9 V6 A3.5 3.5 0 0 1 11.5 6 V9 L13 11 Z M6.5 13 A1.5 1.5 0 0 0 9.5 13"),
            Width = 16, Height = 16, Stretch = Stretch.Uniform, StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round, Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        bell.UseTheme(Shape.StrokeProperty, "Accent");
        DockPanel.SetDock(bell, Dock.Left);
        header.Children.Add(bell);
        var label = new TextBlock { Text = heading, FontSize = 12, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        label.UseTheme(TextBlock.ForegroundProperty, "Muted");
        header.Children.Add(label);
        body.Children.Add(header);

        var content = new StackPanel();
        var text = new TextBlock { Text = message, FontSize = 18, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, LineHeight = 25 };
        text.UseTheme(TextBlock.ForegroundProperty, "Ink");
        content.Children.Add(text);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            var subtitle = new TextBlock { Text = detail, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                MaxHeight = 52, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 7, 0, 0) };
            subtitle.UseTheme(TextBlock.ForegroundProperty, "Muted");
            content.Children.Add(subtitle);
        }

        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12,
            MaxHeight = 50, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        error.UseTheme(TextBlock.ForegroundProperty, "Ink");
        content.Children.Add(error);
        void Run(Action action, bool finish)
        {
            try { action(); if (finish) Close(); }
            catch (Exception failure)
            {
                error.Text = failure.Message;
                error.Visibility = Visibility.Visible;
                FitContent();
            }
        }
        if (open is not null)
        {
            var link = new Button { Content = openLabel, Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 5, 4, 5),
                Margin = new Thickness(0, 6, 0, 0) };
            link.UseTheme(ForegroundProperty, "Accent");
            link.Click += (_, _) => Run(open, false);
            content.Children.Add(link);
        }
        body.Children.Add(new ScrollViewer { Content = content, MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });
        var actions = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        foreach (var (caption, command, column) in new[] { ("Готово", "done", 0), ("На 10 мин", "snooze", 1) })
        {
            var button = new Button { Content = caption, Height = 36,
                Margin = column == 0 ? new Thickness(0, 0, 5, 0) : new Thickness(5, 0, 0, 0) };
            if (column == 0) button.SetResourceReference(StyleProperty, "SputnikPrimaryButton");
            AutomationProperties.SetName(button, caption);
            button.Click += (_, _) => Run(() => act(command), true);
            Grid.SetColumn(button, column);
            actions.Children.Add(button);
        }
        body.Children.Add(actions);
        var frame = new Border { Child = body, BorderThickness = new Thickness(1) };
        frame.UseTheme(Border.BackgroundProperty, "Surface");
        frame.UseTheme(Border.BorderBrushProperty, "Line");
        Content = frame;
        RoundedWindow.Apply(this, frame);
        close.Click += (_, _) => Run(() => act("dismiss"), true);
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            args.Handled = true;
            Run(() => act("dismiss"), true);
        };
        FitContent();
    }

    private void FitContent()
    {
        var frame = (FrameworkElement)Content;
        frame.Measure(new Size(Width, double.PositiveInfinity));
        Height = Math.Min(MaxHeight, frame.DesiredSize.Height);
    }
}
