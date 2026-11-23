using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Sputnik;

public partial class MainWindow
{
    private void ShowNotesList()
    {
        Desktop?.FlushNotes();
        Button Icon(string label, string geometry, Action action, bool primary = false)
        {
            var button = WorkspaceUi.IconButton(label, geometry, primary);
            button.Click += (_, _) => { try { action(); } catch (Exception error) { ShowError(error); } };
            return button;
        }
        var add = Icon("Новая заметка", "M8 2V14 M2 8H14", () => Desktop?.CreateNote(""), true);
        var back = Button("Назад", () => NavigateTool("tools"));
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal };
        var arrange = Icon("Упорядочить заметки", "M1 1H6V6H1Z M10 1H15V6H10Z M1 10H6V15H1Z M10 10H15V15H10Z", () => Desktop?.ArrangeNotes());
        arrange.Margin = new Thickness(0, 0, 6, 0);
        add.Margin = new Thickness(0, 0, 6, 0);
        headerActions.Children.Add(add); headerActions.Children.Add(arrange); headerActions.Children.Add(back);
        var root = Page("Все заметки", headerActions, 20);
        var top = new StackPanel { Margin = new Thickness(0, 2, 0, 8) };
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var search = Input("Поиск заметок");
        search.BorderThickness = new Thickness(0); search.Background = Brushes.Transparent;
        search.MinHeight = 34; search.Padding = new Thickness(0, 4, 0, 4);
        var searchIcon = new Path { Data = Geometry.Parse("M10.5 10.5L15 15 M12 7A5 5 0 1 1 2 7A5 5 0 1 1 12 7"),
            Width = 15, Height = 15, StrokeThickness = 1.6, Stretch = Stretch.Uniform,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 9, 0) };
        searchIcon.UseTheme(Shape.StrokeProperty, "Muted");
        var clear = Icon("Очистить поиск", "M4 4L12 12 M12 4L4 12", () => { search.Clear(); search.Focus(); });
        clear.Width = clear.Height = 28; clear.Margin = new Thickness(3, 0, 4, 0); clear.VerticalAlignment = VerticalAlignment.Center;
        var field = new Grid();
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        field.ColumnDefinitions.Add(new ColumnDefinition());
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        field.Children.Add(searchIcon); Grid.SetColumn(search, 1); field.Children.Add(search);
        Grid.SetColumn(clear, 2); field.Children.Add(clear);
        var prompt = Text("Поиск по тексту", "Muted", 13);
        prompt.IsHitTestVisible = false; prompt.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(prompt, 1); field.Children.Add(prompt);
        var searchFrame = new Border { Child = field, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        searchFrame.UseTheme(Border.BackgroundProperty, "Field"); searchFrame.UseTheme(Border.BorderBrushProperty, "Line");
        search.GotKeyboardFocus += (_, _) => searchFrame.UseTheme(Border.BorderBrushProperty, "Accent");
        search.LostKeyboardFocus += (_, _) => searchFrame.UseTheme(Border.BorderBrushProperty, "Line");
        top.Children.Add(searchFrame);
        var count = Text("", "Muted", 11); count.Margin = new Thickness(1, 7, 0, 0);
        AutomationProperties.SetName(count, "Количество заметок");
        AutomationProperties.SetLiveSetting(count, AutomationLiveSetting.Polite);
        top.Children.Add(count);
        var list = new StackPanel();
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
        root.Children.Add(scroll);
        void Render()
        {
            var all = StickyNotes.Load(data);
            var notes = all.Where(note => StickyNotes.Text(note).IndexOf(search.Text, StringComparison.CurrentCultureIgnoreCase) >= 0).ToArray();
            list.Children.Clear();
            prompt.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            clear.Visibility = search.Text.Length == 0 ? Visibility.Hidden : Visibility.Visible;
            int n = notes.Length;
            string word = n % 100 is >= 11 and <= 14 ? "заметок" : n % 10 == 1 ? "заметка" : n % 10 is >= 2 and <= 4 ? "заметки" : "заметок";
            count.Text = $"{n} {word}" + (search.Text.Length > 0 ? $" из {all.Count}" : "");
            foreach (var note in notes)
            {
                string raw = StickyNotes.Text(note).Trim();
                bool image = raw.Contains("[Изображение]");
                var lines = raw.Replace("[Изображение]", "").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
                bool empty = !image && lines.Length == 0;
                string title = lines.FirstOrDefault() ?? (image ? "Заметка с изображением" : "Пустая заметка");
                if (title.Length > 120) title = title.Substring(0, 120) + "…";
                string preview = string.Join(" · ", lines.Skip(1));
                if (image && lines.Length > 0) preview = preview.Length > 0 ? "С изображением · " + preview : "С изображением";
                if (empty) preview = "Без содержимого";
                if (preview.Length > 180) preview = preview.Substring(0, 180) + "…";
                var heading = Text(title, empty ? "Muted" : "Ink", 13);
                heading.FontWeight = empty ? FontWeights.Normal : FontWeights.SemiBold;
                heading.FontStyle = empty ? FontStyles.Italic : FontStyles.Normal;
                heading.TextWrapping = TextWrapping.NoWrap;
                heading.TextTrimming = TextTrimming.CharacterEllipsis; heading.ToolTip = title;
                var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                labels.Children.Add(heading);
                if (preview.Length > 0)
                {
                    var excerpt = Text(preview, "Muted", 12);
                    excerpt.Margin = new Thickness(0, 4, 0, 0); excerpt.LineHeight = 16;
                    excerpt.MaxHeight = 32; excerpt.TextTrimming = TextTrimming.CharacterEllipsis; excerpt.ClipToBounds = true;
                    labels.Children.Add(excerpt);
                }
                FrameworkElement content = labels;
                if (empty)
                {
                    var blank = new Path { Data = Geometry.Parse("M3 1H10L14 5V15H3Z M10 1V5H14"),
                        Width = 17, Height = 20, Stretch = Stretch.Uniform, StrokeThickness = 1.3,
                        StrokeLineJoin = PenLineJoin.Round, Margin = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
                    blank.UseTheme(Shape.StrokeProperty, "Muted");
                    AutomationProperties.SetName(blank, "Пустая заметка");
                    var emptyContent = new Grid();
                    emptyContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    emptyContent.ColumnDefinitions.Add(new ColumnDefinition());
                    emptyContent.Children.Add(blank); Grid.SetColumn(labels, 1); emptyContent.Children.Add(labels);
                    content = emptyContent;
                }
                var open = Icon("Открыть заметку", "M2 3V14H13V9 M7 9L14 2 M9 2H14V7", () => Desktop?.OpenNote(note.Id));
                var remove = Icon("Удалить заметку", "M2 4H14 M6 4V2H10V4 M4 4L5 14H11L12 4 M7 7V11 M9 7V11", () => Desktop?.DeleteNote(note.Id));
                AutomationProperties.SetHelpText(open, title); AutomationProperties.SetHelpText(remove, title);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                open.Margin = new Thickness(0, 0, 4, 0); actions.Children.Add(open); actions.Children.Add(remove);
                var row = new Grid { MinHeight = 52, Margin = new Thickness(1, 2, 1, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(content); Grid.SetColumn(actions, 1); row.Children.Add(actions);
                var entry = new Border { Child = row, Padding = new Thickness(2, 6, 0, 7), BorderThickness = new Thickness(0, 0, 0, 1) };
                entry.UseTheme(Border.BorderBrushProperty, "Line"); list.Children.Add(entry);
            }
            if (n == 0)
            {
                var empty = Text(all.Count == 0 ? "Здесь будут ваши заметки.\nСоздайте первую кнопкой +." : "Ничего не найдено.\nПопробуйте другое слово или очистите поиск.", "Muted", 13);
                empty.Margin = new Thickness(2, 18, 2, 0); list.Children.Add(empty);
            }
        }
        void Refresh()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!list.IsLoaded || !list.IsVisible) return;
                try { Render(); } catch (Exception error) { count.Text = "Не удалось обновить список: " + error.Message; }
            }));
        }
        void Activated(object? sender, EventArgs args) => Refresh();
        search.TextChanged += (_, _) => { Render(); scroll.ScrollToTop(); };
        root.Loaded += (_, _) => { if (Desktop is { } app) app.NotesChanged += Refresh; this.Activated += Activated; };
        root.Unloaded += (_, _) => { if (Desktop is { } app) app.NotesChanged -= Refresh; this.Activated -= Activated; };
        Render();
    }
}
