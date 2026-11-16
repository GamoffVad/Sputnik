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

    private static TextBlock Status(double size = 12)
    {
        var text = Text("", "Muted", size);
        text.SetResourceReference(StyleProperty, "SputnikStatus");
        return text;
    }

    private static TextBox Input(string label, string content = "", int max = 1800)
    {
        var input = new TextBox { Text = content, MaxLength = max };
        AutomationProperties.SetName(input, label);
        return input;
    }

    private static StackPanel Labeled(string label, FrameworkElement input)
    {
        var text = Text(label, "Muted", 12);
        text.FontWeight = FontWeights.SemiBold;
        text.Margin = new Thickness(0, 0, 0, 3);
        AutomationProperties.SetLabeledBy(input, text);
        var field = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
        field.Children.Add(text);
        field.Children.Add(input);
        return field;
    }

    private static void Field(Panel panel, string label, FrameworkElement input) =>
        panel.Children.Add(Labeled(label, input));

    private static void Row(Panel panel, params FrameworkElement[] fields)
    {
        var row = new Grid();
        foreach (var field in fields)
        {
            int column = row.ColumnDefinitions.Count;
            row.ColumnDefinitions.Add(new ColumnDefinition());
            if (column < fields.Length - 1) field.Margin = new Thickness(0, 0, 8, field.Margin.Bottom);
            Grid.SetColumn(field, column);
            row.Children.Add(field);
        }
        panel.Children.Add(row);
    }

    private Button Button(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label };
        if (primary) button.SetResourceReference(StyleProperty, "SputnikPrimaryButton");
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
        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 2) };
        foreach (var action in actions)
        {
            if (action is FrameworkElement element) element.Margin = new Thickness(0, 0, 6, 4);
            row.Children.Add(action);
        }
        return row;
    }

    private static void Entry(Panel panel, string title, string detail, params UIElement[] actions)
    {
        var text = Text(title, size: 13);
        text.FontWeight = FontWeights.SemiBold;
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(text);
        if (detail.Length > 0) labels.Children.Add(Text(detail, "Muted", 12));
        var row = new DockPanel();
        var buttons = Actions(actions);
        buttons.Margin = new Thickness(8, 0, -6, 0);
        foreach (FrameworkElement button in buttons.Children) button.Margin = new Thickness(0, 0, 6, 0);
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        row.Children.Add(labels);
        var entry = new Border { Child = row, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(8) };
        entry.UseTheme(Border.BackgroundProperty, "Field");
        panel.Children.Add(entry);
    }

    private DockPanel Page(string title, UIElement? action = null)
    {
        var root = new DockPanel { Margin = new Thickness(14, 10, 14, 12) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        if (action is not null)
        {
            if (action is FrameworkElement element)
            {
                element.VerticalAlignment = VerticalAlignment.Center;
                element.Margin = new Thickness(8, 0, 0, 0);
            }
            DockPanel.SetDock(action, Dock.Right);
            header.Children.Add(action);
        }
        var heading = Text(title, "Ink", 22);
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
        var fields = new StackPanel();
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
        var (pet, clips) = Desktop?.MotionSettings() ?? ("", []);
        PopulateMotions(page, pet, clips);
    }

    private void PopulateMotions(StackPanel page, string pet, (string Name, bool Enabled)[] clips)
    {
        if (clips.Length == 0)
        {
            page.Children.Add(Text("У питомца пока нет движений.", "Muted"));
            return;
        }
        var shuffle = Button("Случайный порядок", () => Desktop?.SetPlayback("shuffle", 0), true);
        var pause = Button("Пауза", () => Desktop?.SetPlayback("pause", 0));
        var resume = Button("Продолжить", () => Desktop?.SetPlayback("resume", 0));
        page.Children.Add(Actions(shuffle, pause, resume));
        var enabled = new HashSet<string>(clips.Where(clip => clip.Enabled).Select(clip => clip.Name), StringComparer.Ordinal);
        void UpdateActions() => shuffle.IsEnabled = pause.IsEnabled = resume.IsEnabled = enabled.Count != 0;
        UpdateActions();

        for (int i = 0; i < clips.Length; i++)
        {
            var (name, isEnabled) = clips[i];
            int index = i;
            var play = Button("", () => Desktop?.SetPlayback("clip", index));
            play.SetResourceReference(StyleProperty, "SputnikIconButton");
            var icon = new System.Windows.Shapes.Path
            {
                Width = 8, Height = 10, Stretch = System.Windows.Media.Stretch.Fill,
                Data = System.Windows.Media.Geometry.Parse("M 0,0 L 8,5 0,10 Z")
            };
            icon.UseTheme(System.Windows.Shapes.Shape.FillProperty, "Ink");
            play.Content = icon;
            play.IsEnabled = isEnabled;
            play.ToolTip = "Воспроизвести";
            AutomationProperties.SetName(play, $"Воспроизвести «{name}»");
            var toggle = new CheckBox { Content = Text(name, size: 13), IsChecked = isEnabled };
            AutomationProperties.SetName(toggle, name);
            toggle.Click += (_, _) =>
            {
                try
                {
                    bool on = toggle.IsChecked == true;
                    var disabled = clips.Select(clip => clip.Name)
                        .Where(clip => clip == name ? !on : !enabled.Contains(clip));
                    SaveDisabledMotions(pet, disabled);
                    if (on) enabled.Add(name); else enabled.Remove(name);
                    play.IsEnabled = on;
                    UpdateActions();
                }
                catch (Exception error)
                {
                    toggle.IsChecked = enabled.Contains(name);
                    ShowError(error);
                }
            };
            var row = new DockPanel();
            DockPanel.SetDock(play, Dock.Right);
            row.Children.Add(play);
            row.Children.Add(toggle);
            var surface = new Border { Child = row, Padding = new Thickness(5, 2, 4, 2), Margin = new Thickness(0, 0, 0, 4),
                CornerRadius = new CornerRadius(7) };
            surface.UseTheme(Border.BackgroundProperty, "Field");
            page.Children.Add(surface);
        }
    }

    private void SaveDisabledMotions(string pet, IEnumerable<string> disabled) => UpdateWorkspace(workspace =>
    {
        var preferences = workspace["disabledMotions"] as JsonObject ?? new JsonObject();
        var list = disabled.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray();
        if (list.Length == 0) preferences.Remove(pet);
        else preferences[pet] = new JsonArray(list);
        if (preferences.Count == 0) workspace.Remove("disabledMotions");
        else if (workspace["disabledMotions"] is not JsonObject) workspace["disabledMotions"] = preferences;
    }, true);

    private void ShowChat()
    {
        var page = Page("Чат", Button("Модель и API", () => Desktop?.ShowWorkspace("model")));
        var input = Input("Сообщение модели", max: 10000);
        var status = Status();
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
        submit.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(submit, Dock.Right);
        composer.Children.Add(submit);
        composer.Children.Add(input);
        var footer = new StackPanel();
        footer.Children.Add(status);
        footer.Children.Add(composer);
        DockPanel.SetDock(footer, Dock.Bottom);
        page.Children.Add(footer);
        chatHistory = new StackPanel();
        chatScroll = new ScrollViewer { Content = chatHistory, Margin = new Thickness(0, 0, 0, 6),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
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
        var provider = new ComboBox { MaxDropDownHeight = 375,
            ItemsSource = Providers.Select(item => item.Name).Append("Другой провайдер").ToArray() };
        var url = Input("Базовый URL", Value(saved, "url"));
        var key = new PasswordBox { MaxLength = 1800 };
        AutomationProperties.SetName(key, "API-ключ");
        var clear = new CheckBox { Content = "Удалить сохранённый ключ", Margin = new Thickness(0, 0, 0, 6),
            Visibility = saved?["secret"] is null ? Visibility.Collapsed : Visibility.Visible };
        var model = new ComboBox { IsEditable = true, IsTextSearchEnabled = false, Text = Value(saved, "model") };
        AutomationProperties.SetName(model, "ID модели");
        var status = Status(13);
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
