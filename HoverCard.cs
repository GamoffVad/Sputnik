using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace Sputnik;

internal sealed class HoverCard
{
    private static readonly DependencyProperty CardProperty = DependencyProperty.RegisterAttached(
        "Card", typeof(HoverCard), typeof(HoverCard));
    private static HoverCard? current;
    private readonly FrameworkElement target;
    private readonly string title;
    private readonly TextBlock heading;
    private readonly Border frame;
    private readonly Popup popup;
    private readonly DispatcherTimer delay = new() { Interval = TimeSpan.FromMilliseconds(180) };

    internal static HoverCard Attach(FrameworkElement target, string title, string? detail = null)
    {
        if (target.GetValue(CardProperty) is HoverCard previous) previous.Close();
        var card = new HoverCard(target, title, detail);
        target.SetValue(CardProperty, card);
        ToolTipService.SetIsEnabled(target, false);
        target.ToolTip = null;
        return card;
    }

    private HoverCard(FrameworkElement target, string title, string? detail)
    {
        this.target = target;
        this.title = title;
        heading = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, MaxWidth = 260 };
        heading.UseTheme(TextBlock.ForegroundProperty, "Ink");
        var content = new StackPanel();
        content.Children.Add(heading);
        if (!string.IsNullOrEmpty(detail))
        {
            var caption = new TextBlock { Text = detail, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                MaxWidth = 260, Margin = new Thickness(0, 4, 0, 0) };
            caption.UseTheme(TextBlock.ForegroundProperty, "Muted");
            content.Children.Add(caption);
        }
        frame = new Border { Child = content, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(3), IsHitTestVisible = false };
        frame.UseTheme(Border.BorderBrushProperty, "Line");
        frame.UseTheme(TextElement.FontFamilyProperty, "Font");
        popup = new Popup { Child = frame, PlacementTarget = target, Placement = PlacementMode.Custom,
            AllowsTransparency = true, StaysOpen = true, Focusable = false, IsHitTestVisible = false };
        popup.CustomPopupPlacementCallback = (size, anchor, _) =>
        [
            new(new Point((anchor.Width - size.Width) / 2, -size.Height - 7), PopupPrimaryAxis.Horizontal),
            new(new Point((anchor.Width - size.Width) / 2, anchor.Height + 7), PopupPrimaryAxis.Horizontal),
        ];
        delay.Tick += (_, _) => { delay.Stop(); if (target.IsMouseOver) Show(); };
        target.MouseEnter += (_, _) => { heading.Text = title; delay.Stop(); delay.Start(); };
        target.MouseLeave += (_, _) => Close();
        target.GotKeyboardFocus += (_, _) => { heading.Text = title; Show(); };
        target.LostKeyboardFocus += (_, _) => Close();
        target.PreviewMouseDown += (_, _) => Close();
        target.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        target.Unloaded += (_, _) => Close();
        target.IsVisibleChanged += (_, _) => { if (!target.IsVisible) Close(); };
    }

    internal void ShowMessage(string message) { heading.Text = message; Show(); }

    private void Show()
    {
        if (!target.IsVisible) return;
        if (current != this) current?.Close();
        current = this;
        var brush = (target.TryFindResource("ThemeSurface") as Brush ?? Brushes.Black).CloneCurrentValue();
        brush.Opacity = .89;
        frame.Background = brush;
        popup.IsOpen = true;
        WorkspaceUi.Reveal(frame);
    }

    private void Close()
    {
        delay.Stop();
        popup.IsOpen = false;
        if (current == this) current = null;
    }
}
