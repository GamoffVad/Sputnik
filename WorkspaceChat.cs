using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Sputnik;

public partial class MainWindow
{
    private readonly Dictionary<string, string> chatDrafts = new();
    private void ShowChat()
    {
        var chats = new ChatHistory(data);
        var active = chats.Active();
        if (active is null) { chats.Create(); active = chats.Active()!; }
        displayedChat = active["id"]!.ToString();
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var history = WorkspaceUi.IconButton("История чатов", "M8 3A5 5 0 1 1 3 8 M1 3V8H6 M8 5V8L10 10");
        history.Click += (_, _) => ShowChatHistory();
        actions.Children.Add(history);
        var create = WorkspaceUi.IconButton("Новый чат", "M8 2V14 M2 8H14");
        create.Margin = new Thickness(5, 0, 0, 0);
        create.Click += (_, _) => { chatDrafts.Remove(chats.Create()); ShowChat(); };
        actions.Children.Add(create);
        var settings = WorkspaceUi.IconButton("Модель и API", "M2 4H14 M2 12H14 M5 1V7 M11 9V15");
        settings.Margin = new Thickness(5, 0, 0, 0);
        settings.Click += (_, _) => OpenSection("model");
        actions.Children.Add(settings);
        var page = Page("Чат", actions, 20);
        chatTitle = Text(Value(active, "title", "Новый чат"), "Muted", 12);
        chatTitle.TextWrapping = TextWrapping.NoWrap;
        chatTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        chatTitle.Margin = new Thickness(1, 0, 0, 12);
        DockPanel.SetDock(chatTitle, Dock.Top);
        page.Children.Add(chatTitle);
        var input = Input("Сообщение модели", max: 10000);
        string draftId = displayedChat!;
        input.Text = chatDrafts.TryGetValue(draftId, out var draft) ? draft : "";
        input.TextChanged += (_, _) => chatDrafts[draftId] = input.Text;
        input.AcceptsReturn = true;
        input.TextWrapping = TextWrapping.Wrap;
        input.MinHeight = 38;
        input.MaxHeight = 96;
        input.FontSize = 14;
        input.Padding = new Thickness(9, 8, 9, 8);
        input.BorderThickness = new Thickness(0);
        input.Background = System.Windows.Media.Brushes.Transparent;
        input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        input.ToolTip = "Enter — отправить · Shift+Enter — новая строка";
        var status = Status();
        status.Margin = new Thickness(2, 0, 0, 5);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        status.Unloaded += (_, _) => WorkspaceUi.SetWaiting(status, false);
        var submit = WorkspaceUi.IconButton("Отправить сообщение (Enter)", WorkspaceUi.SendIcon, true);
        bool pending = false;
        void UpdateComposer() => submit.IsEnabled = !pending && !sending && !string.IsNullOrWhiteSpace(input.Text);
        chatSendState = UpdateComposer;
        input.TextChanged += (_, _) => UpdateComposer();
        UpdateComposer();
        async Task Submit()
        {
            string text = input.Text.Trim();
            if (text.Length == 0 || sending || pending) return;
            pending = true;
            input.Clear();
            UpdateComposer();
            status.Text = "Модель отвечает…";
            WorkspaceUi.SetWaiting(status, true);
            try { await SendChat(text); status.Text = ""; }
            catch (Exception error)
            {
                status.Text = error.Message;
                if (input.Text.Length == 0) input.Text = text;
            }
            finally
            {
                pending = false;
                WorkspaceUi.SetWaiting(status, false);
                UpdateComposer();
                if (input.IsVisible) input.Focus();
            }
        }
        submit.Click += async (_, _) => await Submit();
        input.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
            e.Handled = true;
            await Submit();
        };
        var hint = Text("Написать сообщение…", "Muted", 14);
        hint.Margin = new Thickness(9, 8, 9, 8);
        hint.IsHitTestVisible = false;
        hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        input.TextChanged += (_, _) => hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var field = new Grid();
        field.Children.Add(input);
        field.Children.Add(hint);
        var composer = new DockPanel { Margin = new Thickness(4) };
        submit.Margin = new Thickness(4, 0, 0, 0);
        submit.VerticalAlignment = VerticalAlignment.Bottom;
        DockPanel.SetDock(submit, Dock.Right);
        composer.Children.Add(submit);
        composer.Children.Add(field);
        var composerFrame = new Border { Child = composer, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        composerFrame.UseTheme(Border.BackgroundProperty, "Field");
        composerFrame.UseTheme(Border.BorderBrushProperty, "Line");
        input.GotKeyboardFocus += (_, _) => composerFrame.UseTheme(Border.BorderBrushProperty, "Accent");
        input.LostKeyboardFocus += (_, _) => composerFrame.UseTheme(Border.BorderBrushProperty, "Line");
        var footer = new StackPanel();
        footer.Children.Add(status);
        footer.Children.Add(composerFrame);
        var keyboardHint = Text("Enter — отправить · Shift+Enter — новая строка", "Muted", 10);
        keyboardHint.Margin = new Thickness(3, 5, 0, 0);
        footer.Children.Add(keyboardHint);
        DockPanel.SetDock(footer, Dock.Bottom);
        page.Children.Add(footer);
        chatHistory = new StackPanel();
        renderedMessages.Clear();
        chatScroll = new ScrollViewer { Content = chatHistory, Margin = new Thickness(0, 0, 0, 8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        page.Children.Add(chatScroll);
        RefreshChatHistory(active["messages"]!.AsArray(), false);
    }

    private void RefreshChatHistory(JsonArray history, bool animate = true)
    {
        if (chatHistory is null) return;
        var messages = history.Skip(Math.Max(0, history.Count - 200))
            .Select(item => (Role: Value(item, "role"), Content: Value(item, "content"))).ToArray();
        bool follow = !animate || chatScroll is null || chatScroll.ScrollableHeight - chatScroll.VerticalOffset < 32
            || (messages.Length > 0 && messages[messages.Length - 1].Role == "user");
        int common = 0;
        while (common < Math.Min(messages.Length, renderedMessages.Count) && messages[common] == renderedMessages[common]) common++;
        if (renderedMessages.Count == 0) chatHistory.Children.Clear();
        while (chatHistory.Children.Count > common) chatHistory.Children.RemoveAt(chatHistory.Children.Count - 1);
        for (int index = common; index < messages.Length; index++)
        {
            var message = messages[index];
            bool user = message.Role == "user";
            var content = WorkspaceUi.MessageText(message.Content);
            content.FontSize = 14;
            AutomationProperties.SetName(content, user ? "Ваше сообщение" : "Ответ модели");
            var messageBody = new StackPanel();
            var author = Text(user ? "Вы" : "Спутник", "Muted", 11);
            author.FontWeight = FontWeights.SemiBold;
            author.Margin = new Thickness(0, 0, 0, 5);
            messageBody.Children.Add(author);
            messageBody.Children.Add(content);
            var bubble = new Border
            {
                Child = messageBody, Padding = new Thickness(user ? 12 : 2, 10, user ? 12 : 2, 10),
                Margin = new Thickness(user ? 32 : 0, 0, user ? 0 : 16, 12),
                CornerRadius = new CornerRadius(12), HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Stretch
            };
            if (user) bubble.UseTheme(Border.BackgroundProperty, "Hover");
            chatHistory.Children.Add(bubble);
            if (animate && index == messages.Length - 1) WorkspaceUi.Reveal(bubble);
        }
        renderedMessages.Clear();
        renderedMessages.AddRange(messages);
        if (messages.Length == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(3, 18, 3, 12) };
            var title = Text("С чего начнём?", size: 22); title.FontWeight = FontWeights.SemiBold;
            empty.Children.Add(title);
            var detail = Text("Задайте вопрос, обсудите идею или попросите помочь с планом.", "Muted", 13);
            detail.Margin = new Thickness(0, 8, 0, 0); empty.Children.Add(detail);
            chatHistory.Children.Add(empty);
        }
        if (follow && chatScroll is { } scroll)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(scroll.ScrollToEnd));
    }

}
