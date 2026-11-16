using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Sputnik;

public partial class MainWindow
{
    private bool editing;
    private StackPanel? chatHistory;
    private ScrollViewer? chatScroll;
    private static App? Desktop => Application.Current as App;

    private JsonObject WorkspaceData() => Read(Store) as JsonObject ?? new JsonObject();

    private void UpdateWorkspace(Action<JsonObject> update, bool refreshPet = false)
    {
        var current = WorkspaceData();
        update(current);
        string json = current.ToJsonString();
        if (json.Length > 8_000_000) throw new InvalidOperationException("Локальные данные слишком велики.");
        Save(Store, json);
        if (refreshPet) Desktop?.RefreshPet();
    }

    private static JsonArray Items(JsonObject data, string key)
    {
        if (data[key] is JsonArray existing) return existing;
        var created = new JsonArray();
        data[key] = created;
        return created;
    }

    private static string Value(JsonNode? node, string key, string fallback = "") => node?[key]?.ToString() ?? fallback;

    private static TextBlock Text(string content, string role = "Ink", double size = 14)
    {
        var text = new TextBlock { Text = content, FontSize = size, TextWrapping = TextWrapping.Wrap };
        text.UseTheme(TextBlock.ForegroundProperty, role);
        return text;
    }

    private static TextBox Input(string label, string content = "", int max = 1800)
    {
        var input = new TextBox { Text = content, MaxLength = max };
        AutomationProperties.SetName(input, label);
        return input;
    }

    private static void Field(Panel panel, string label, FrameworkElement input)
    {
        var text = Text(label, "Muted", 13);
        text.Margin = new Thickness(0, 1, 0, 4);
        input.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(text);
        panel.Children.Add(input);
    }

    private Button Button(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 6, 0) };
        if (primary)
        {
            button.UseTheme(BackgroundProperty, "Accent");
            button.UseTheme(ForegroundProperty, "AccentInk");
        }
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception error) { ShowError(error); }
        };
        return button;
    }

    private Button AsyncButton(string label, Func<Task> action, bool primary = false)
    {
        var button = Button(label, () => { }, primary);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            catch (Exception error) { ShowError(error); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private static WrapPanel Actions(params UIElement[] actions)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 3, 0, 8) };
        foreach (var action in actions) row.Children.Add(action);
        return row;
    }

    private static void Entry(Panel panel, string title, string detail, params UIElement[] actions)
    {
        var text = Text(title);
        text.FontWeight = FontWeights.SemiBold;
        panel.Children.Add(text);
        if (detail.Length > 0) panel.Children.Add(Text(detail, "Muted", 12));
        panel.Children.Add(Actions(actions));
    }

    private DockPanel Page(string title, UIElement? action = null)
    {
        var root = new DockPanel { Margin = new Thickness(15, 9, 11, 11) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 11) };
        if (action is not null)
        {
            DockPanel.SetDock(action, Dock.Right);
            header.Children.Add(action);
        }
        var heading = Text(title, "Ink", 24);
        heading.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetName(heading, title);
        header.Children.Add(heading);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        PanelHost.Content = root;
        return root;
    }

    private StackPanel PageFields(string title, UIElement? action = null)
    {
        var fields = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        Page(title, action).Children.Add(new ScrollViewer
        {
            Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        return fields;
    }

    private StackPanel Editor(string title)
    {
        editing = true;
        return PageFields(title, Button("Назад", () => OpenSection(section)));
    }

    private bool CloseEditor()
    {
        if (!editing) return false;
        OpenSection(section);
        return true;
    }

    private void ShowError(Exception error) => MessageBox.Show(this, error.Message, "Спутник", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void OpenNativeSection(string value)
    {
        editing = false;
        chatHistory = null;
        chatScroll = null;
        switch (value)
        {
            case "model": ShowModel(); break;
            case "chat": ShowChat(); break;
            case "motion": ShowMotions(); break;
            case "pets": ShowPets(); break;
            case "favorites": ShowFavorites(); break;
            case "theme": ShowAppearance(); break;
            case "manage": ShowManage(); break;
            case "sharing": ShowSharing(); break;
            case "reminders": ShowReminders(); break;
            default: ShowChat(); break;
        }
    }

    private void ShowMotions()
    {
        var page = PageFields("Движения");
        page.Children.Add(Actions(Button("Случайный порядок", () => Desktop?.SetPlayback("shuffle", 0), true),
            Button("Пауза", () => Desktop?.SetPlayback("pause", 0)), Button("Продолжить", () => Desktop?.SetPlayback("resume", 0))));
        var names = Desktop?.AnimationNames() ?? [];
        for (int i = 0; i < names.Length; i++)
        {
            int index = i;
            page.Children.Add(Actions(Button(names[i], () => Desktop?.SetPlayback("clip", index))));
        }
        if (names.Length == 0) page.Children.Add(Text("У питомца пока нет движений.", "Muted"));
    }

    private void ShowChat()
    {
        var page = Page("Чат", Button("Модель и API", () => Desktop?.ShowWorkspace("model")));
        var input = Input("Сообщение модели", max: 10000);
        var status = Text("", "Muted", 12);
        var submit = AsyncButton("Отправить", Submit, true);
        async Task Submit()
        {
            string text = input.Text.Trim();
            if (text.Length == 0 || sending) return;
            input.Clear();
            status.Text = "Модель отвечает…";
            try { await SendChat(text); status.Text = ""; }
            catch (Exception error) { status.Text = error.Message; }
        }
        input.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await Submit();
        };
        var composer = new DockPanel();
        DockPanel.SetDock(submit, Dock.Right);
        composer.Children.Add(submit);
        composer.Children.Add(input);
        var footer = new StackPanel();
        footer.Children.Add(status);
        footer.Children.Add(composer);
        DockPanel.SetDock(footer, Dock.Bottom);
        page.Children.Add(footer);
        chatHistory = new StackPanel();
        chatScroll = new ScrollViewer { Content = chatHistory, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        page.Children.Add(chatScroll);
        RefreshChatHistory((WorkspaceData()["messages"] as JsonArray) ?? new JsonArray());
    }

    private void RefreshChatHistory(JsonArray history)
    {
        if (chatHistory is null) return;
        chatHistory.Children.Clear();
        foreach (var item in history.Skip(Math.Max(0, history.Count - 200)))
        {
            var bubble = new Border
            {
                Child = Text(Value(item, "content")), Padding = new Thickness(9, 6, 9, 6),
                Margin = new Thickness(0, 0, 4, 6), CornerRadius = new CornerRadius(7)
            };
            bubble.UseTheme(Border.BackgroundProperty, Value(item, "role") == "user" ? "Hover" : "Background");
            chatHistory.Children.Add(bubble);
        }
        if (history.Count == 0) chatHistory.Children.Add(Text("С чего начнём?", "Muted"));
        chatScroll?.ScrollToEnd();
    }

    private static readonly (string Name, string Url)[] Providers =
    [
        ("OpenAI", "https://api.openai.com/v1"), ("Claude (Anthropic)", "https://api.anthropic.com/v1"),
        ("Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai"), ("DeepSeek", "https://api.deepseek.com/v1"),
        ("OpenRouter", "https://openrouter.ai/api/v1"), ("Grok (xAI)", "https://api.x.ai/v1"),
        ("Mistral AI", "https://api.mistral.ai/v1"), ("Groq", "https://api.groq.com/openai/v1"),
        ("Kimi (Moonshot AI)", "https://api.moonshot.ai/v1"), ("Together AI", "https://api.together.ai/v1"),
        ("Cerebras", "https://api.cerebras.ai/v1"), ("Ollama (локально)", "http://localhost:11434/v1"),
        ("LM Studio (локально)", "http://localhost:1234/v1")
    ];

    private void ShowModel()
    {
        var saved = Read(Settings);
        var fields = PageFields("Модель и API");
        var provider = new ComboBox { ItemsSource = Providers.Select(item => item.Name).Append("Другой провайдер").ToArray() };
        var url = Input("Базовый URL", Value(saved, "url"));
        var key = new PasswordBox { MaxLength = 1800 };
        AutomationProperties.SetName(key, "API-ключ");
        var clear = new CheckBox { Content = "Удалить сохранённый ключ", Margin = new Thickness(0, 0, 0, 11) };
        var model = new ComboBox { IsEditable = true, IsTextSearchEnabled = false, Text = Value(saved, "model") };
        AutomationProperties.SetName(model, "ID модели");
        var status = Text("", "Muted", 13);
        bool syncing = false;
        int revision = 0;
        int ProviderIndex()
        {
            string address = url.Text.Trim().TrimEnd('/');
            int index = Array.FindIndex(Providers, item => item.Url == address);
            return index >= 0 ? index : address.Length > 0 ? Providers.Length : -1;
        }
        provider.SelectedIndex = ProviderIndex();
        provider.SelectionChanged += (_, _) =>
        {
            if (syncing || provider.SelectedIndex < 0) return;
            if (provider.SelectedIndex == Providers.Length) { url.Focus(); return; }
            if (ProviderIndex() != provider.SelectedIndex) url.Text = Providers[provider.SelectedIndex].Url;
        };
        url.TextChanged += (_, _) =>
        {
            if (syncing) return;
            syncing = true;
            provider.SelectedIndex = ProviderIndex();
            syncing = false;
            revision++;
            key.Clear();
            clear.IsChecked = false;
            model.Items.Clear();
            model.Text = "";
            status.Text = "";
        };

        JsonObject Draft() => new()
        {
            ["url"] = url.Text.Trim(), ["key"] = key.Password, ["clearKey"] = clear.IsChecked == true, ["model"] = model.Text.Trim(),
        };
        model.DropDownOpened += async (_, _) =>
        {
            int request = ++revision;
            var draft = Draft();
            status.Text = "Загрузка списка моделей…";
            try
            {
                var result = await ListModels(draft);
                if (revision != request || !fields.IsLoaded) return;
                bool reenterKey = false;
                if (result.Url is { } corrected)
                {
                    reenterKey = key.Password.Length == 0 && Value(draft, "url") == Value(saved, "url")
                        && saved?["secret"] is not null && clear.IsChecked != true;
                    syncing = true;
                    url.Text = corrected;
                    provider.SelectedIndex = ProviderIndex();
                    syncing = false;
                }
                string selected = model.Text;
                model.Items.Clear();
                foreach (string id in result.Models) model.Items.Add(id);
                model.Text = selected;
                status.Text = reenterKey ? "Адрес API уточнён. Введите ключ для этого URL перед сохранением."
                    : result.Models.Length > 0 ? "" : "Список пуст. Введите ID модели вручную.";
            }
            catch (Exception error) { if (revision == request) status.Text = error.Message; }
        };
        Field(fields, "Провайдер", provider);
        Field(fields, "Базовый URL", url);
        Field(fields, saved?["secret"] is null ? "API-ключ" : "API-ключ (сохранён)", key);
        fields.Children.Add(clear);
        Field(fields, "ID модели", model);
        fields.Children.Add(Actions(AsyncButton("Проверить подключение", async () =>
        {
            status.Text = "Проверяю подключение…";
            try { status.Text = await Send(Draft(), new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Ответь одним словом: OK." }), true); }
            catch (Exception error) { status.Text = error.Message; }
        }), Button("Сохранить", () =>
        {
            SaveModel(Draft());
            key.Clear();
            clear.IsChecked = false;
            status.Text = "Настройки сохранены.";
        }, true)));
        fields.Children.Add(status);
    }
}
