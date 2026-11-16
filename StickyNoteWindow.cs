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

    private static readonly (string Id, string Name, PetTheme Theme)[] Colors =
    [
        ("yellow", "Жёлтый", new("#F6CC67", "#FFF2C5", "#FFE6A3")),
        ("pink", "Розовый", new("#F19ABD", "#FDE1EB", "#F9CDE0")),
        ("green", "Зелёный", new("#A9CF85", "#E9F3D7", "#DBEBC1")),
        ("blue", "Голубой", new("#8DC9EB", "#E1F2FE", "#CEE8FA")),
        ("purple", "Сиреневый", new("#C7A8EC", "#F0E6FD", "#E6D7FC")),
    ];

    public StickyNoteWindow(StickyNoteContent.BlockData[] content, Action create, Action<StickyNoteContent.BlockData[]> save, Action delete,
        string? color, Action<string?> saveColor, bool pinned, Action<bool> savePin, Func<NoteSharing.Channel[]> channels, Action sharingSettings)
    {
        this.save = save;
        Title = "Заметка Спутника";
        Width = 340;
        Height = 320;
        MinWidth = MinimumWidth;
        MinHeight = MinimumHeight;
        WindowStyle = WindowStyle.None;
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
        }
        ApplyColor();
        RoundedWindow.Apply(this, frame);
        var header = new DockPanel { Height = 39 };
        header.UseTheme(Panel.BackgroundProperty, "Accent");
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        header.MouseLeftButtonDown += (_, e) => { if (!e.Handled) DragMove(); };
        var add = Button("+", "Новая заметка", create, header: true);
        DockPanel.SetDock(add, Dock.Left);
        header.Children.Add(add);
        var close = Button("×", "Закрыть заметку", Close, header: true);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var more = Button("⋯", "Действия с заметкой", () => { }, header: true);
        DockPanel.SetDock(more, Dock.Right);
        header.Children.Add(more);
        var share = Button(Glyph("M6 10L13 5 M6 11L13 16 M5 8A2 2 0 1 0 5 12A2 2 0 1 0 5 8 M15 2A2 2 0 1 0 15 6A2 2 0 1 0 15 2 M15 14A2 2 0 1 0 15 18A2 2 0 1 0 15 14", "AccentInk"), "Поделиться заметкой", () => { }, 36, header: true);
        DockPanel.SetDock(share, Dock.Right);
        header.Children.Add(share);
        var pin = Button(Glyph(PinIcon, "AccentInk"), "Закрепить поверх окон", () => { }, 26, header: true);
        var pinBadge = new Border { Child = pin, CornerRadius = new CornerRadius(7), Margin = new Thickness(1, 3, 1, 3), Padding = new Thickness(2) };
        pin.Height = 29;
        DockPanel.SetDock(pinBadge, Dock.Right);
        header.Children.Add(pinBadge);
        void UpdatePin()
        {
            if (Topmost) pinBadge.UseTheme(Border.BackgroundProperty, "AccentInk");
            else pinBadge.Background = Brushes.Transparent;
            pin.Content = Glyph(PinIcon, Topmost ? "Accent" : "AccentInk");
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
        var heading = new TextBlock { Text = "Заметка", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        heading.UseTheme(TextBlock.ForegroundProperty, "AccentInk");
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
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(Button(Glyph("M3 5H17 M7 5V3H13V5 M5 5L6 17H14L15 5 M8 8V14 M12 8V14"), "Удалить заметку", () =>
        {
            delete();
            deleted = true;
            Close();
        }, 34));
        menuPanel.Children.Add(actions);
        var sharePanel = new StackPanel();
        var shareFrame = new Border { Child = sharePanel, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(5) };
        shareFrame.UseTheme(Border.BackgroundProperty, "Surface");
        shareFrame.UseTheme(Border.BorderBrushProperty, "Edge");
        var shareMenu = new Popup { PlacementTarget = share, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Child = shareFrame };
        DockPanel.SetDock(shareMenu, Dock.Top);
        layout.Children.Add(shareMenu);
        share.Click += (_, _) =>
        {
            if (shareMenu.IsOpen) { shareMenu.IsOpen = false; return; }
            menu.IsOpen = false;
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
        body.Children.Add(editor);
        placeholder = new TextBlock { Text = "Напишите заметку…", FontSize = 15,
            Margin = new Thickness(14, 12, 0, 0), IsHitTestVisible = false };
        placeholder.UseTheme(TextBlock.ForegroundProperty, "Muted");
        body.Children.Add(placeholder);
        layout.Children.Add(body);

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
        Closing += (_, _) => { menu.IsOpen = false; shareMenu.IsOpen = false; Flush(); };
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
        var marker = new InlineUIContainer(Check(false), paragraph.ContentStart) { BaselineAlignment = BaselineAlignment.Center };
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
        var mark = new FrameworkElementFactory(typeof(TextBlock), "Mark");
        mark.SetValue(TextBlock.TextProperty, "✓");
        mark.SetResourceReference(TextBlock.ForegroundProperty, "ThemeAccentInk");
        mark.SetValue(TextBlock.FontSizeProperty, 12d);
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

    private void Flush()
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
        var button = new Button { Content = symbol, Width = width, Height = 37,
            FontSize = symbol is string { Length: 1 } ? 20 : 14, Cursor = Cursors.Hand, ToolTip = title };
        AutomationProperties.SetName(button, title);
        button.UseTheme(Control.ForegroundProperty, header ? "AccentInk" : "Ink");
        var fill = new FrameworkElementFactory(typeof(Border), "Fill");
        fill.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        fill.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        fill.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = fill };
        foreach (var property in new[] { IsMouseOverProperty, IsKeyboardFocusedProperty })
        {
            var highlight = new Trigger { Property = property, Value = true };
            highlight.Setters.Add(new Setter(Border.BackgroundProperty,
                new DynamicResourceExtension(header ? "ThemeAccentHover" : "ThemeHover"), "Fill"));
            template.Triggers.Add(highlight);
        }
        button.Template = template;
        button.Click += (_, _) => click();
        return button;
    }
}
