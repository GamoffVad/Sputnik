using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Sputnik;

public partial class MainWindow
{
    private void ShowChatHistory()
    {
        chatHistory = null; chatScroll = null; chatSendState = null; chatTitle = null; displayedChat = null;
        var chats = new ChatHistory(data);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var create = WorkspaceUi.IconButton("Новый чат", "M8 2V14 M2 8H14", true);
        create.Click += (_, _) => { chatDrafts.Remove(chats.Create()); ShowChat(); };
        actions.Children.Add(create);
        var back = WorkspaceUi.IconButton("Вернуться в чат", "M10 2L4 8 10 14");
        back.Margin = new Thickness(5, 0, 0, 0); back.Click += (_, _) => ShowChat(); actions.Children.Add(back);
        var page = Page("История чатов", actions, 20);
        var search = Input("Поиск чатов", max: 200);
        search.ToolTip = "Поиск по названию и сообщениям";
        var searchField = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        searchField.Children.Add(search);
        var searchHint = Text("Поиск по чатам…", "Muted", 12);
        searchHint.Margin = new Thickness(10, 0, 10, 0); searchHint.VerticalAlignment = VerticalAlignment.Center;
        searchHint.IsHitTestVisible = false; searchField.Children.Add(searchHint);
        DockPanel.SetDock(searchField, Dock.Top); page.Children.Add(searchField);
        var caption = Text("", "Muted", 11); caption.Margin = new Thickness(1, 0, 0, 9);
        DockPanel.SetDock(caption, Dock.Top); page.Children.Add(caption);
        var rows = new StackPanel();
        page.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        string? confirming = null;
        void Draw()
        {
            rows.Children.Clear();
            var state = chats.Load();
            string term = search.Text.Trim();
            var all = state["chats"]!.AsArray().OfType<JsonObject>().Where(c => c["messages"] is JsonArray { Count: > 0 })
                .OrderByDescending(c => Value(c, "updatedAt")).ToArray();
            var matches = all.Where(c => term.Length == 0 || Value(c, "title").IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                c["messages"]!.AsArray().Any(m => Value(m, "content").IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0)).ToArray();
            caption.Text = term.Length == 0 ? $"Бесед: {all.Length}" : $"Найдено: {matches.Length} из {all.Length}";
            if (matches.Length == 0)
            {
                var empty = Text(all.Length == 0 ? "Здесь появятся ваши беседы" : "Ничего не найдено", size: 17);
                empty.Margin = new Thickness(1, 18, 1, 7); rows.Children.Add(empty);
                rows.Children.Add(Text(all.Length == 0 ? "Начните новый чат кнопкой +." : "Попробуйте другое слово или очистите поиск.", "Muted", 12));
            }
            foreach (var chat in matches)
            {
                string id = Value(chat, "id"), title = Value(chat, "title", "Чат");
                var block = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                var row = new Grid { Margin = new Thickness(0, 3, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var labels = new StackPanel { Margin = new Thickness(1, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
                var heading = Text(title, size: 13); heading.FontWeight = FontWeights.SemiBold;
                heading.TextWrapping = TextWrapping.NoWrap; heading.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(heading);
                var messages = chat["messages"]!.AsArray();
                var excerpt = Text(ChatHistory.Title(Value(messages.LastOrDefault(), "content")), "Muted", 12);
                excerpt.TextWrapping = TextWrapping.NoWrap; excerpt.TextTrimming = TextTrimming.CharacterEllipsis;
                excerpt.Margin = new Thickness(0, 4, 0, 4); labels.Children.Add(excerpt);
                string stamp = DateTimeOffset.TryParse(Value(chat, "updatedAt"), out var time) ? time.LocalDateTime.ToString("dd.MM.yyyy · HH:mm") : "";
                labels.Children.Add(Text(stamp + (state["active"]?.ToString() == id ? " · Текущий" : ""), "Muted", 10));
                row.Children.Add(labels);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var open = WorkspaceUi.IconButton("Открыть чат «" + title + "»", "M6 2H14V10 M14 2L5 11 M11 8V14H2V5H8");
                open.Click += (_, _) => { chats.Select(id); ShowChat(); };
                buttons.Children.Add(open);
                var delete = WorkspaceUi.IconButton("Удалить чат «" + title + "»", "M2 4H14 M5 4V2H11V4 M4 4L5 14H11L12 4 M7 7V11 M9 7V11");
                delete.Margin = new Thickness(4, 0, 0, 0); delete.Click += (_, _) => { confirming = id; Draw(); };
                buttons.Children.Add(delete); Grid.SetColumn(buttons, 1); row.Children.Add(buttons); block.Children.Add(row);
                if (confirming == id)
                {
                    var confirmation = new StackPanel { Margin = new Thickness(10) };
                    var question = Text("Удалить этот чат?", size: 13); question.FontWeight = FontWeights.SemiBold; confirmation.Children.Add(question);
                    var warning = Text("Сообщения будут удалены. Отменить удаление нельзя.", "Muted", 12);
                    warning.Margin = new Thickness(0, 5, 0, 8); confirmation.Children.Add(warning);
                    var error = Status(11); AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
                    var confirm = Button("Удалить", () =>
                    {
                        try { chats.Delete(id); chatDrafts.Remove(id); confirming = null; Draw(); }
                        catch (Exception problem) { error.Text = "Не удалось удалить чат. " + problem.Message; }
                    });
                    AutomationProperties.SetName(confirm, "Удалить чат навсегда");
                    var cancel = Button("Отмена", () => { confirming = null; Draw(); });
                    var confirmActions = new StackPanel { Orientation = Orientation.Horizontal };
                    cancel.Margin = new Thickness(0, 0, 8, 0); confirmActions.Children.Add(cancel); confirmActions.Children.Add(confirm);
                    confirmation.Children.Add(confirmActions); confirmation.Children.Add(error);
                    var frame = new Border { Child = confirmation, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 8) };
                    frame.UseTheme(Border.BackgroundProperty, "Field"); block.Children.Add(frame);
                    confirmation.Loaded += (_, _) => cancel.Focus();
                }
                var divider = new Border { Height = 1, Margin = new Thickness(0, 5, 0, 0) };
                divider.UseTheme(Border.BackgroundProperty, "Line"); block.Children.Add(divider); rows.Children.Add(block);
            }
        }
        search.TextChanged += (_, _) => { searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; confirming = null; Draw(); };
        Draw();
    }
}
