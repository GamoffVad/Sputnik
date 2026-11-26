using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace Sputnik;

internal sealed class StickyNoteWindow : Window
{
    internal const double MinimumWidth = 310, MinimumHeight = 185;
    private const string PinIcon = "M7 3H15L13 6V10L16 13H6L9 10V6Z M11 13V18";
    private readonly RichTextBox editor = new();
    private readonly TextBlock placeholder;
    private readonly DispatcherTimer autosave = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private readonly Action<StickyNoteContent.BlockData[]> save;
    private bool dirty, deleted;
    private Action? requestDeletion;
    internal void RequestDeletion() => requestDeletion?.Invoke();

    private static readonly (string Id, string Name, PetTheme Theme)[] Colors =
    [
        ("yellow", "Жёлтый", new("#F6CC67", "#FFF2C5", "#FFE6A3")),
        ("pink", "Розовый", new("#F19ABD", "#FDE1EB", "#F9CDE0")),
        ("green", "Зелёный", new("#A9CF85", "#E9F3D7", "#DBEBC1")),
        ("blue", "Голубой", new("#8DC9EB", "#E1F2FE", "#CEE8FA")),
        ("purple", "Сиреневый", new("#C7A8EC", "#F0E6FD", "#E6D7FC")),
    ];

    internal static string NextColor(string? previous)
    {
        int index = Array.FindIndex(Colors, preset => preset.Id == previous);
        return Colors[(index + 1) % Colors.Length].Id;
    }

    public StickyNoteWindow(StickyNoteContent.BlockData[] content, Action create, Action<StickyNoteContent.BlockData[]> save, Action delete,
        string? color, Action<string?> saveColor, bool pinned, Action<bool> savePin, Func<NoteSharing.Channel[]> channels, Action sharingSettings, Action? remind = null,
        bool ruled = false, Action<bool>? saveRuled = null, Action? arrange = null)
    {
        this.save = save;
        Title = "Заметка Спутника";
        Width = 340;
        Height = 320;
        MinWidth = MinimumWidth;
        MinHeight = MinimumHeight;
        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = pinned;
        this.UseTheme(FontFamilyProperty, "Font");
        var layout = new DockPanel();
        var frame = new Border { Child = layout, BorderThickness = new Thickness(1) };
        frame.UseTheme(Border.BackgroundProperty, "Surface");
        frame.UseTheme(Border.BorderBrushProperty, "Edge");
        Content = frame;
        void ApplyColor()
        {
            Resources.Clear();
            if (Colors.FirstOrDefault(preset => preset.Id == color).Theme is { } theme)
                ThemePalette.Fill(Resources, theme);
            else WindowMaterial.Refresh();
        }
        ApplyColor();
        RoundedWindow.Apply(this, frame);
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerFrame = new Border { Child = header, Height = 44, Padding = new Thickness(8, 0, 8, 0),
            BorderThickness = new Thickness(0, 0, 0, 1) };
        headerFrame.UseTheme(Border.BackgroundProperty, "Field");
        headerFrame.UseTheme(Border.BorderBrushProperty, "Edge");
        DockPanel.SetDock(headerFrame, Dock.Top);
        layout.Children.Add(headerFrame);
        header.MouseLeftButtonDown += (_, e) => { if (!e.Handled) DragMove(); };
        var add = Button(Glyph("M8 3V13 M3 8H13", "Ink"), "Новая заметка", create, 30, header: true);
        add.Margin = new Thickness(0, 0, 8, 0);
        header.Children.Add(add);
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(headerActions, 2);
        header.Children.Add(headerActions);
        var close = Button(Glyph("M4 4L12 12 M12 4L4 12"), "Скрыть заметку", Close, 30, header: true);
        Action requestDelete = () => { };
        var remove = Button(Glyph("M3 5H17 M7 5V3H13V5 M5 5L6 17H14L15 5 M8 8V14 M12 8V14"),
            "Удалить заметку", () => requestDelete(), 30, header: true);
        remove.ToolTip = "Удалить заметку навсегда";
        var more = Button(Glyph("M3 7.5A.5 .5 0 1 0 3 8.5A.5 .5 0 1 0 3 7.5 M8 7.5A.5 .5 0 1 0 8 8.5A.5 .5 0 1 0 8 7.5 M13 7.5A.5 .5 0 1 0 13 8.5A.5 .5 0 1 0 13 7.5"), "Действия с заметкой", () => { }, 30, header: true);
        var share = Button(Glyph("M6 10L13 5 M6 11L13 16 M5 8A2 2 0 1 0 5 12A2 2 0 1 0 5 8 M15 2A2 2 0 1 0 15 6A2 2 0 1 0 15 2 M15 14A2 2 0 1 0 15 18A2 2 0 1 0 15 14"), "Поделиться заметкой", () => { }, 30, header: true);
        var pin = Button(Glyph(PinIcon), "Закрепить поверх окон", () => { }, 30, header: true);
        var pinBadge = new Border { Child = pin, CornerRadius = new CornerRadius(6) };
        headerActions.Children.Add(pinBadge);
        headerActions.Children.Add(share);
        headerActions.Children.Add(more);
        var separator = new Border { Width = 1, Height = 16, Margin = new Thickness(5, 0, 5, 0) };
        separator.UseTheme(Border.BackgroundProperty, "Line");
        headerActions.Children.Add(separator);
        headerActions.Children.Add(remove);
        headerActions.Children.Add(close);
        void UpdatePin()
        {
            if (Topmost) pinBadge.UseTheme(Border.BackgroundProperty, "Accent");
            else pinBadge.Background = Brushes.Transparent;
            pin.Content = Glyph(PinIcon, Topmost ? "AccentInk" : "Ink");
            ((FrameworkElement)pin.Content).Width = ((FrameworkElement)pin.Content).Height = 14;
            string action = Topmost ? "Снять закрепление" : "Закрепить поверх окон";
            pin.ToolTip = action;
            AutomationProperties.SetName(pin, action);
        }
        pin.Click += (_, _) =>
        {
            Topmost = !Topmost;
            UpdatePin();
            Activate();
            savePin(Topmost);
        };
        UpdatePin();
        var heading = new TextBlock { Text = "Заметка", FontSize = 12, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Перетащите для выравнивания с соседней заметкой. Alt — без примагничивания" };
        heading.UseTheme(TextBlock.ForegroundProperty, "Ink");
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);

        var menuPanel = new StackPanel();
        var menuFrame = new Border { Child = menuPanel, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(5) };
        menuFrame.UseTheme(Border.BackgroundProperty, "Surface");
        menuFrame.UseTheme(Border.BorderBrushProperty, "Edge");
        var menu = new Popup
        {
            PlacementTarget = more, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = menuFrame,
        };
        DockPanel.SetDock(menu, Dock.Top);
        layout.Children.Add(menu);
        more.Click += (_, _) => { menu.IsOpen = !menu.IsOpen; if (menu.IsOpen) ((Panel)menuPanel.Children[0]).Children[0].Focus(); };
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3, 2, 3, 5) };
        var colorButtons = new List<(Button Button, string? Id)>();
        void AddColor(string? id, string label, string? accent)
        {
            var circle = new Border { Width = 17, Height = 17, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1) };
            circle.UseTheme(Border.BorderBrushProperty, "Edge");
            if (accent is null) circle.UseTheme(Border.BackgroundProperty, "Accent");
            else circle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent));
            var mark = new TextBlock { Text = "✓", FontSize = 11, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center };
            circle.Child = mark;
            var choice = Button(circle, label, () => { color = id; ApplyColor(); saveColor(id); menu.IsOpen = false; }, 27);
            choice.Height = 29;
            colorButtons.Add((choice, id));
            colorRow.Children.Add(choice);
        }
        AddColor(null, "По теме питомца", null);
        foreach (var preset in Colors) AddColor(preset.Id, preset.Name, preset.Theme.Accent);
        menu.Opened += (_, _) =>
        {
            foreach (var option in colorButtons)
                ((Border)option.Button.Content).Child.Visibility = color == option.Id ? Visibility.Visible : Visibility.Hidden;
        };
        menuPanel.Children.Add(colorRow);
        var ruling = new CheckBox { Content = "Тетрадные линии", IsChecked = ruled,
            Margin = new Thickness(8, 7, 8, 9), ToolTip = "Показывать линии, как в тетради" };
        AutomationProperties.SetName(ruling, "Тетрадные линии");
        menuPanel.Children.Add(ruling);
        var actions = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        if (arrange is not null) actions.Children.Add(Button("Упорядочить заметки", "Упорядочить заметки", () => { menu.IsOpen = false; arrange(); }, 192));
        if (remind is not null) actions.Children.Add(Button("Напомнить", "Напомнить о заметке", () => { menu.IsOpen = false; remind(); }, 192));
        actions.Children.Add(Button("Удалить заметку", "Удалить заметку", () => requestDelete(), 192));
        menuPanel.Children.Add(actions);
        var sharePanel = new StackPanel();
        var shareFrame = new Border { Child = sharePanel, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(5) };
        shareFrame.UseTheme(Border.BackgroundProperty, "Surface");
        shareFrame.UseTheme(Border.BorderBrushProperty, "Edge");
        var shareMenu = new Popup { PlacementTarget = share, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Child = shareFrame };
        DockPanel.SetDock(shareMenu, Dock.Top);
        layout.Children.Add(shareMenu);
        var deletePanel = new StackPanel { Width = 242 };
        var deleteTitle = new TextBlock { Text = "Удалить заметку навсегда?", FontSize = 14,
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        deleteTitle.UseTheme(TextBlock.ForegroundProperty, "Ink");
        deletePanel.Children.Add(deleteTitle);
        var deleteHint = new TextBlock { Text = "Текст и изображения будут удалены.", FontSize = 12,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) };
        deleteHint.UseTheme(TextBlock.ForegroundProperty, "Muted");
        deletePanel.Children.Add(deleteHint);
        var deleteError = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        deleteError.UseTheme(TextBlock.ForegroundProperty, "Ink");
        AutomationProperties.SetLiveSetting(deleteError, AutomationLiveSetting.Assertive);
        deletePanel.Children.Add(deleteError);
        var deleteFrame = new Border { Child = deletePanel, CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1), Padding = new Thickness(10) };
        deleteFrame.UseTheme(Border.BackgroundProperty, "Surface");
        deleteFrame.UseTheme(Border.BorderBrushProperty, "Edge");
        var deleteMenu = new Popup { PlacementTarget = remove, Placement = PlacementMode.Bottom,
            StaysOpen = false, AllowsTransparency = true, Child = deleteFrame };
        DockPanel.SetDock(deleteMenu, Dock.Top);
        layout.Children.Add(deleteMenu);
        var deleteActions = new StackPanel { Orientation = Orientation.Horizontal };
        var cancelDelete = Button("Отмена", "Отмена", () => { deleteMenu.IsOpen = false; editor.Focus(); }, 86);
        deleteActions.Children.Add(cancelDelete);
        var confirmDelete = Button("Удалить навсегда", "Удалить навсегда", () =>
        {
            if (deleted) return;
            try { delete(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                deleteError.Text = "Не удалось удалить заметку. " + error.Message;
                deleteError.Visibility = Visibility.Visible;
                return;
            }
            deleted = true;
            dirty = false;
            autosave.Stop();
            deleteMenu.IsOpen = false;
            Close();
        }, 148);
        confirmDelete.Margin = new Thickness(8, 0, 0, 0);
        confirmDelete.FontWeight = FontWeights.SemiBold;
        deleteActions.Children.Add(confirmDelete);
        deletePanel.Children.Add(deleteActions);
        requestDelete = () =>
        {
            menu.IsOpen = shareMenu.IsOpen = false;
            deleteError.Text = "";
            deleteError.Visibility = Visibility.Collapsed;
            deleteMenu.IsOpen = true;
            cancelDelete.Focus();
        };
        requestDeletion = requestDelete;
        deletePanel.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            deleteMenu.IsOpen = false;
            remove.Focus();
            e.Handled = true;
        };
        share.Click += (_, _) =>
        {
            if (shareMenu.IsOpen) { shareMenu.IsOpen = false; return; }
            menu.IsOpen = false;
            deleteMenu.IsOpen = false;
            var configured = channels();
            int page = 0;
            void DrawChannels()
            {
                sharePanel.Children.Clear();
                foreach (var channel in configured.Skip(page * 4).Take(4))
                {
                    string label = channel.Template.StartsWith("https://web.max.ru/:share?", StringComparison.Ordinal)
                        ? channel.Name + " (в браузере)" : channel.Name;
                    sharePanel.Children.Add(Button(label, label, () =>
                    {
                        shareMenu.IsOpen = false;
                        try { NoteSharing.Open(channel, editor.Document); }
                        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or UriFormatException)
                        { MessageBox.Show(this, error.Message, "Поделиться заметкой"); }
                    }, 165));
                }
                if (configured.Length > 4)
                {
                    var next = Button("Следующие каналы", "Следующие каналы", () =>
                    {
                        page = (page + 1) % ((configured.Length + 3) / 4);
                        DrawChannels();
                    }, 165);
                    sharePanel.Children.Add(next);
                }
                sharePanel.Children.Add(Button("Настроить каналы", "Настроить каналы", () =>
                {
                    shareMenu.IsOpen = false;
                    sharingSettings();
                }, 165));
            }
            DrawChannels();
            shareMenu.IsOpen = true;
            sharePanel.Children[0].Focus();
        };
        sharePanel.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { shareMenu.IsOpen = false; share.Focus(); e.Handled = true; } };
        menuPanel.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { menu.IsOpen = false; more.Focus(); e.Handled = true; } };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 1, 4, 1) };
        var footerLayout = new DockPanel();
        var grip = new Thumb { Width = 21, Height = 21, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, ToolTip = "Изменить размер", Focusable = true };
        AutomationProperties.SetName(grip, "Изменить размер заметки");
        var gripShape = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        gripShape.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M3 15L15 3 M8 15L15 8"));
        gripShape.SetValue(Shape.StrokeThicknessProperty, 1.2);
        gripShape.SetResourceReference(Shape.StrokeProperty, "ThemeMuted");
        grip.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = gripShape };
        void Resize(double dx, double dy)
        {
            Width = Compatibility.Clamp(Width + dx, MinWidth, Math.Max(MinWidth, SystemParameters.VirtualScreenWidth));
            Height = Compatibility.Clamp(Height + dy, MinHeight, Math.Max(MinHeight, SystemParameters.VirtualScreenHeight));
        }
        grip.DragDelta += (_, e) => Resize(e.HorizontalChange, e.VerticalChange);
        grip.KeyDown += (_, e) =>
        {
            var delta = e.Key switch { Key.Left => (-10, 0), Key.Right => (10, 0), Key.Up => (0, -10), Key.Down => (0, 10), _ => (0, 0) };
            if (delta == (0, 0)) return;
            Resize(delta.Item1, delta.Item2);
            e.Handled = true;
        };
        DockPanel.SetDock(grip, Dock.Right);
        footerLayout.Children.Add(grip);
        footerLayout.Children.Add(toolbar);
        var footer = new Border { Child = footerLayout, BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.UseTheme(Border.BorderBrushProperty, "Edge");
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        editor.Padding = new Thickness(14, 11, 14, 10);
        editor.Background = Brushes.Transparent;
        editor.BorderThickness = new Thickness(0);
        editor.FontSize = 15;
        editor.IsDocumentEnabled = true;
        editor.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        editor.UseTheme(Control.ForegroundProperty, "Ink");
        editor.UseTheme(TextBoxBase.CaretBrushProperty, "Accent");
        AutomationProperties.SetName(editor, "Текст заметки");
        editor.Document.PagePadding = new Thickness(0);
        var paragraphStyle = new Style(typeof(Paragraph));
        paragraphStyle.Setters.Add(new Setter(Paragraph.MarginProperty, new Thickness(0, 0, 0, 3)));
        editor.Resources.Add(typeof(Paragraph), paragraphStyle);
        editor.Document.Blocks.Clear();
        StickyNoteContent.Write(editor.Document.Blocks, content, Check);
        if (editor.Document.Blocks.Count == 0) editor.Document.Blocks.Add(new Paragraph());
        var body = new Grid();
        var paper = new NoteRuling { IsHitTestVisible = false, Visibility = ruled ? Visibility.Visible : Visibility.Collapsed };
        paper.UseTheme(NoteRuling.InkProperty, "Ink");
        body.Children.Add(paper);
        body.Children.Add(editor);
        placeholder = new TextBlock { Text = "Напишите заметку…", FontSize = 15,
            Margin = new Thickness(14, 12, 0, 0), IsHitTestVisible = false };
        placeholder.UseTheme(TextBlock.ForegroundProperty, "Muted");
        body.Children.Add(placeholder);
        layout.Children.Add(body);
        void SetRuling(bool enabled)
        {
            paper.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            // WPF seals styles once used; create a fresh paragraph style.
            paragraphStyle = new Style(typeof(Paragraph));
            paragraphStyle.Setters.Add(new Setter(Paragraph.MarginProperty, enabled ? new Thickness(0) : new Thickness(0, 0, 0, 3)));
            if (enabled)
            {
                paragraphStyle.Setters.Add(new Setter(Block.LineHeightProperty, 24d));
                // Images may be taller than one notebook row; never clip them.
                paragraphStyle.Setters.Add(new Setter(Block.LineStackingStrategyProperty, LineStackingStrategy.MaxHeight));
            }
            editor.Resources[typeof(Paragraph)] = paragraphStyle;
        }
        SetRuling(ruled);
        editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        {
            paper.ScrollOffset = e.VerticalOffset;
            paper.InvalidateVisual();
        }));
        ruling.Click += (_, _) =>
        {
            bool enabled = ruling.IsChecked == true;
            try { saveRuled?.Invoke(enabled); }
            catch { ruling.IsChecked = !enabled; throw; }
            SetRuling(enabled);
            editor.Focus();
        };

        AddTool("B", "Полужирный (Ctrl+B)", () => EditingCommands.ToggleBold.Execute(null, editor), FontWeights.Bold);
        AddTool("I", "Курсив (Ctrl+I)", () => EditingCommands.ToggleItalic.Execute(null, editor), italic: true);
        AddTool("U̲", "Подчёркивание (Ctrl+U)", () => EditingCommands.ToggleUnderline.Execute(null, editor));
        AddTool("a̶b̶", "Зачёркивание", Strike);
        AddTool(Glyph("M3 5H4 M3 10H4 M3 15H4 M7 5H18 M7 10H18 M7 15H18"), "Маркированный список", () => EditingCommands.ToggleBullets.Execute(null, editor));
        AddTool(Glyph("M3 3H17V17H3Z M6 10L9 13 14 7"), "Чек-лист", ToggleCheck);
        AddTool(Glyph("M2 3H18V17H2Z M3 14L8 9 12 13 15 10 18 13 M13 6H14"), "Вставить изображение", InsertImage);

        void AddTool(object symbol, string name, Action command, FontWeight? weight = null, bool italic = false)
        {
            var tool = Button(symbol, name, () => { editor.Focus(); command(); }, 39);
            tool.FontWeight = weight ?? FontWeights.Medium;
            tool.FontStyle = italic ? FontStyles.Italic : FontStyles.Normal;
            toolbar.Children.Add(tool);
        }

        RefreshPlaceholder();
        editor.TextChanged += (_, _) => Changed();
        editor.PreviewKeyDown += EditorKey;
        autosave.Tick += (_, _) => Flush();
        Loaded += (_, _) => editor.Focus();
        Closing += (_, _) => { menu.IsOpen = shareMenu.IsOpen = deleteMenu.IsOpen = false; Flush(); };
    }

    private void EditorKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && editor.Selection.IsEmpty &&
            editor.CaretPosition.Paragraph is { Inlines.FirstInline: InlineUIContainer { Child: CheckBox } first } paragraph)
        {
            e.Handled = true;
            if (string.IsNullOrWhiteSpace(new TextRange(first.ElementEnd, paragraph.ContentEnd).Text))
            {
                paragraph.Inlines.Remove(first);
                return;
            }
            var start = first.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
            var split = editor.CaretPosition.CompareTo(start) < 0 ? start : editor.CaretPosition;
            var next = split.InsertParagraphBreak();
            if (next.Paragraph is { } target)
            {
                var marker = InsertCheck(target);
                editor.CaretPosition = marker.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
            }
        }
    }

    private void ToggleCheck()
    {
        if (editor.Selection.Start.Paragraph is not { } start) return;
        var paragraphs = new List<Paragraph> { start };
        if (!editor.Selection.IsEmpty)
        {
            for (TextPointer? point = start.ElementEnd; point is not null && point.CompareTo(editor.Selection.End) < 0;
                point = point.GetNextContextPosition(LogicalDirection.Forward))
            {
                if (point.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart &&
                    point.GetAdjacentElement(LogicalDirection.Forward) is Paragraph paragraph)
                    paragraphs.Add(paragraph);
            }
        }
        if (paragraphs.All(paragraph => paragraph.Inlines.FirstInline is InlineUIContainer { Child: CheckBox }))
        {
            foreach (var paragraph in paragraphs) paragraph.Inlines.Remove(paragraph.Inlines.FirstInline!);
            return;
        }
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Inlines.FirstInline is InlineUIContainer { Child: CheckBox }) continue;
            var marker = InsertCheck(paragraph);
            if (editor.Selection.IsEmpty) editor.CaretPosition = marker.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
        }
    }

    private InlineUIContainer InsertCheck(Paragraph paragraph)
    {
        var marker = new InlineUIContainer(Check(false), paragraph.ContentStart) { BaselineAlignment = BaselineAlignment.TextBottom };
        return marker;
    }

    private CheckBox Check(bool value)
    {
        var check = new CheckBox { IsChecked = value, Width = 17, Height = 17, MinHeight = 0, Margin = new Thickness(0, 0, 7, 0), Cursor = Cursors.Hand };
        AutomationProperties.SetName(check, "Отметить пункт");
        var box = new FrameworkElementFactory(typeof(Border), "Box");
        box.SetResourceReference(Border.BackgroundProperty, "ThemeSurface");
        box.SetResourceReference(Border.BorderBrushProperty, "ThemeAccent");
        box.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        box.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        // A font glyph carries asymmetric ascent/descent whitespace. A vector
        // tick stays centred in the box regardless of the selected note font.
        var mark = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path), "Mark");
        mark.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M0 4L3 7L9 0"));
        mark.SetResourceReference(Shape.StrokeProperty, "ThemeAccentInk");
        mark.SetValue(WidthProperty, 9d);
        mark.SetValue(HeightProperty, 7d);
        mark.SetValue(Shape.StretchProperty, Stretch.Fill);
        mark.SetValue(Shape.StrokeThicknessProperty, 1.45d);
        mark.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
        mark.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
        mark.SetValue(Shape.StrokeLineJoinProperty, PenLineJoin.Round);
        mark.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        mark.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        mark.SetValue(VisibilityProperty, Visibility.Hidden);
        box.AppendChild(mark);
        var template = new ControlTemplate(typeof(CheckBox)) { VisualTree = box };
        var state = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        state.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("ThemeAccent"), "Box"));
        state.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible, "Mark"));
        template.Triggers.Add(state);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Box"));
        template.Triggers.Add(focus);
        check.Template = template;
        check.Checked += (_, _) => Changed();
        check.Unchecked += (_, _) => Changed();
        return check;
    }

    private void Strike()
    {
        var selection = editor.Selection;
        var current = selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        var next = new TextDecorationCollection(current?.Where(d => d.Location != TextDecorationLocation.Strikethrough) ?? []);
        if (current?.Any(d => d.Location == TextDecorationLocation.Strikethrough) != true) next.Add(TextDecorations.Strikethrough);
        selection.ApplyPropertyValue(Inline.TextDecorationsProperty, next);
    }

    private void InsertImage()
    {
        var picker = new OpenFileDialog { Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            using var stream = File.OpenRead(picker.FileName);
            var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            byte[] png = StickyNoteContent.ToPng(image);
            editor.Selection.Text = "";
            var inserted = StickyNoteContent.ImageInline(png, editor.CaretPosition);
            editor.CaretPosition = inserted.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
        }
        catch (Exception error) when (error is IOException or NotSupportedException or FileFormatException or ArgumentException)
        {
            MessageBox.Show(this, "Изображение не удалось открыть.", "Спутник");
        }
    }

    private void Changed()
    {
        dirty = true;
        RefreshPlaceholder();
        autosave.Stop();
        autosave.Start();
    }

    private void RefreshPlaceholder() => placeholder.Visibility =
        editor.Document.Blocks.FirstBlock is Paragraph { Inlines.Count: 0 } && editor.Document.Blocks.Count == 1
            ? Visibility.Visible : Visibility.Collapsed;

    internal void Flush()
    {
        autosave.Stop();
        if (!dirty || deleted) return;
        save(StickyNoteContent.Read(editor.Document.Blocks));
        dirty = false;
    }

    private static FrameworkElement Glyph(string data, string role = "Ink")
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data), StrokeThickness = 1.4, Stretch = Stretch.Uniform,
            Width = 17, Height = 17, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        path.UseTheme(Shape.StrokeProperty, role);
        return path;
    }

    private static Button Button(object symbol, string title, Action click, double width = 43, bool header = false)
    {
        if (header && symbol is System.Windows.Shapes.Path icon) icon.Width = icon.Height = 14;
        var button = new Button { Content = symbol, Width = width, Height = header ? 30 : 37, Padding = new Thickness(0),
            FontSize = symbol is string { Length: 1 } ? 20 : 14, Cursor = Cursors.Hand, ToolTip = title };
        AutomationProperties.SetName(button, title);
        button.UseTheme(Control.ForegroundProperty, "Ink");
        var fill = new FrameworkElementFactory(typeof(Border), "Fill");
        fill.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        fill.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        fill.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        fill.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        fill.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = fill };
        foreach (var property in new[] { IsMouseOverProperty, IsKeyboardFocusedProperty })
        {
            var highlight = new Trigger { Property = property, Value = true };
            highlight.Setters.Add(new Setter(Border.BackgroundProperty,
                new DynamicResourceExtension("ThemeHover"), "Fill"));
            template.Triggers.Add(highlight);
        }
        var focused = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("ThemeAccent"), "Fill"));
        template.Triggers.Add(focused);
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(OpacityProperty, .65, "Fill"));
        template.Triggers.Add(pressed);
        button.Template = template;
        button.Click += (_, _) => click();
        return button;
    }
}

// A decoration beneath the editor: it never enters note content or exports.
internal sealed class NoteRuling : FrameworkElement
{
    internal static readonly DependencyProperty InkProperty = DependencyProperty.Register(
        nameof(Ink), typeof(Brush), typeof(NoteRuling), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Ink { get => (Brush)GetValue(InkProperty); set => SetValue(InkProperty, value); }
    internal double ScrollOffset { get; set; }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (ActualWidth <= 28) return;
        var pen = new Pen(Ink, 1);
        drawing.PushOpacity(.15);
        double first = 35 - ScrollOffset % 24;
        for (double y = first; y < ActualHeight; y += 24)
            drawing.DrawLine(pen, new Point(14, y), new Point(ActualWidth - 14, y));
        drawing.Pop();
    }
}
