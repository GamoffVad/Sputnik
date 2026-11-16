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
    private void ShowSharing()
    {
        var channels = new NoteSharing(data).Load();
        var page = PageFields("Каналы отправки", Button("Добавить", () => EditShareChannel(null), true));
        foreach (var channel in channels)
            Entry(page, channel.Name, "",
                Button("Изменить", () => EditShareChannel(channel)));
        if (channels.Length == 0) page.Children.Add(Text("Добавьте канал для отправки заметок.", "Muted"));
    }

    private void EditShareChannel(NoteSharing.Channel? existing)
    {
        var page = Editor(existing is null ? "Добавить канал" : "Изменить канал");
        var name = Input("Название", existing?.Name ?? "", 40);
        var template = Input("Ссылка для отправки", existing?.Template ?? "");
        Field(page, "Название", name);
        Field(page, "Ссылка для отправки", template);
        page.Children.Add(Text("Подставьте {text} в параметр текста. Для темы письма используйте {title}.", "Muted", 12));
        void SaveChannel(bool remove)
        {
            var sharing = new NoteSharing(data);
            var latest = sharing.Load().ToList();
            int index = existing is null ? -1 : latest.IndexOf(existing);
            if (remove) { if (index >= 0) latest.RemoveAt(index); }
            else
            {
                var replacement = new NoteSharing.Channel(name.Text.Trim(), template.Text.Trim());
                if (index >= 0) latest[index] = replacement; else latest.Add(replacement);
            }
            sharing.Save(latest.ToArray());
            OpenSection("sharing");
        }
        var buttons = Actions(Button("Сохранить", () => SaveChannel(false), true));
        if (existing is not null) buttons.Children.Add(Button("Удалить", () => SaveChannel(true)));
        page.Children.Add(buttons);
    }

    private void ShowManage()
    {
        var page = PageFields("Управление");
        var start = new CheckBox { Content = "Запускать питомца при входе в Windows", IsChecked = App.AutoStartEnabled() };
        start.Click += (_, _) =>
        {
            try { App.SetAutoStart(start.IsChecked == true); }
            catch (Exception error) { start.IsChecked = App.AutoStartEnabled(); ShowError(error); }
        };
        page.Children.Add(start);
        page.Children.Add(Actions(Button("Горячие клавиши", ShowHotkeys)));
    }

    private void ShowHotkeys()
    {
        var page = Editor("Горячие клавиши");
        var bindings = App.ReadHotkeys();
        foreach (var entry in DesktopControls.Sections)
        {
            var current = bindings[entry.Action];
            uint modifiers = current?["modifiers"]?.GetValue<uint>() ?? 0;
            uint key = current?["key"]?.GetValue<uint>() ?? 0;
            var capture = Input(entry.Title, HotkeyLabel(modifiers, key));
            capture.IsReadOnly = true;
            capture.ToolTip = "Нажмите нужное сочетание или Delete для сброса";
            capture.PreviewKeyDown += (_, e) =>
            {
                var pressed = e.Key == Key.System ? e.SystemKey : e.Key;
                if (pressed == Key.Escape || pressed == Key.Tab) return;
                e.Handled = true;
                if (pressed is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
                    return;
                var mod = Keyboard.Modifiers;
                uint flags = (uint)mod;
                uint code = pressed is Key.Back or Key.Delete ? 0 : (uint)KeyInterop.VirtualKeyFromKey(pressed);
                try
                {
                    Desktop?.SetHotkey(entry.Action, code == 0 ? 0 : flags, code);
                    capture.Text = HotkeyLabel(flags, code);
                }
                catch (Exception error) { ShowError(error); }
            };
            Field(page, entry.Title, capture);
        }
    }

    private static string HotkeyLabel(uint modifiers, uint key)
    {
        if (key == 0) return "Не назначено";
        string[] names = ["Alt", "Ctrl", "Shift", "Win"];
        return string.Join(" + ", names.Where((_, position) => (modifiers & (1u << position)) != 0)
            .Append(KeyInterop.KeyFromVirtualKey((int)key).ToString()));
    }

    private void ShowAppearance()
    {
        var state = WorkspaceData();
        var page = PageFields("Внешний вид");
        var mode = new ComboBox { ItemsSource = new[] { "Палитра питомца", "Своя палитра", "Графит" } };
        mode.SelectedIndex = Value(state, "appearanceMode") == "custom" ? 1 : state["themed"]?.GetValue<bool>() == false ? 2 : 0;
        Field(page, "Цветовая схема", mode);
        var colors = new StackPanel();
        var custom = state["customTheme"];
        var background = Input("Фон", Value(custom, "background", "#181C20"), 7);
        var surface = Input("Поверхности", Value(custom, "surface", "#23292F"), 7);
        var accent = Input("Акцент", Value(custom, "accent", "#A5E4F6"), 7);
        Field(colors, "Фон (#RRGGBB)", background);
        Field(colors, "Поверхности (#RRGGBB)", surface);
        Field(colors, "Акцент (#RRGGBB)", accent);
        page.Children.Add(colors);
        void ShowColors() => colors.Visibility = mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        mode.SelectionChanged += (_, _) => ShowColors();
        ShowColors();
        var fonts = new ComboBox { ItemsSource = ThemePalette.InstalledFonts, IsTextSearchEnabled = true,
            SelectedItem = Value(state, "font", "Segoe UI") };
        Field(page, "Шрифт", fonts);
        (string Key, string Label, double Min, double Max, double Initial, string Unit)[] sizes =
        [
            ("petScale", "Размер питомца", 60, 160, 100, "%"), ("sphereScale", "Размер сфер", 60, 160, 100, "%"),
            ("sphereSpread", "Между сферами", 0, 100, 100, "%"), ("sphereOffset", "Отступ от питомца", 0, 80, 10, " пкс"),
        ];
        var sliders = new Dictionary<string, Slider>();
        foreach (var entry in sizes)
        {
            var slider = new Slider
            {
                Minimum = entry.Min, Maximum = entry.Max, TickFrequency = 5, IsSnapToTickEnabled = true,
                Value = state[entry.Key]?.GetValue<double>() ?? entry.Initial,
                Margin = new Thickness(0, 0, 0, 10)
            };
            var label = Text("", "Muted", 13);
            void ShowSize() => label.Text = $"{entry.Label}   {slider.Value:0}{entry.Unit}";
            ShowSize();
            slider.ValueChanged += (_, _) => ShowSize();
            AutomationProperties.SetName(slider, entry.Label);
            sliders.Add(entry.Key, slider);
            page.Children.Add(label);
            page.Children.Add(slider);
        }
        var status = Text("", "Muted", 12);
        page.Children.Add(Actions(Button("Сохранить", () =>
        {
            if (mode.SelectedIndex == 1 && new[] { background.Text, surface.Text, accent.Text }.Any(color =>
                color.Length != 7 || color[0] != '#' || !color.Skip(1).All(Uri.IsHexDigit)))
                throw new InvalidOperationException("Укажите цвета в формате #RRGGBB.");
            UpdateWorkspace(workspace =>
            {
                workspace["appearanceMode"] = mode.SelectedIndex switch { 1 => "custom", 2 => "default", _ => "pet" };
                workspace["themed"] = mode.SelectedIndex == 0;
                if (mode.SelectedIndex == 1)
                    workspace["customTheme"] = new JsonObject { ["background"] = background.Text, ["surface"] = surface.Text, ["accent"] = accent.Text };
                workspace["font"] = fonts.SelectedItem as string ?? "Segoe UI";
                foreach (var pair in sliders) workspace[pair.Key] = pair.Value.Value;
            }, true);
            status.Text = "Оформление сохранено.";
        }, true), Button("Сбросить", () =>
        {
            UpdateWorkspace(workspace =>
            {
                foreach (string key in sizes.Select(entry => entry.Key).Concat(new[] { "font", "customTheme", "appearanceMode" })) workspace.Remove(key);
                workspace["themed"] = true;
            }, true);
            OpenSection("theme");
        }), Button("Показать сферы", () => Desktop?.PreviewFavoriteSpheres())));
        page.Children.Add(status);
    }

    private void ShowFavorites()
    {
        var page = PageFields("Сайты и программы", Button("Добавить", () => EditFavorite(null), true));
        var favorites = WorkspaceData()["favorites"] as JsonArray ?? new JsonArray();
        foreach (var item in favorites.OfType<JsonObject>())
            Entry(page, Value(item, "title"), Value(item, "target"),
                Button("Открыть", () => OpenTarget(Value(item, "kind"), Value(item, "target"))),
                Button("Изменить", () => EditFavorite(item)));
        if (favorites.Count == 0) page.Children.Add(Text("Добавьте сайт или приложение в избранное.", "Muted"));
    }

    private void EditFavorite(JsonObject? existing)
    {
        var page = Editor(existing is null ? "Добавить в избранное" : "Изменить избранное");
        var kind = new ComboBox { ItemsSource = new[] { "Сайт", "Программа" }, SelectedIndex = Value(existing, "kind") == "app" ? 1 : 0 };
        var title = Input("Название", Value(existing, "title"), 80);
        var target = Input("Адрес или путь", Value(existing, "target"));
        const string automaticLabel = "Значок сайта автоматически";
        var icons = new ComboBox { ItemsSource = FavoriteIcons.Names.Prepend(automaticLabel).ToArray(), IsTextSearchEnabled = true };
        icons.SelectedItem = Value(existing, "iconSource") == "library" ? Value(existing, "icon") : automaticLabel;
        var monochrome = new CheckBox { Content = "Монохромный", IsChecked = existing?["monochrome"]?.GetValue<bool>() == true };
        var browse = Button("Выбрать приложение", () =>
        {
            if (Choose("Приложение Windows (*.exe)|*.exe") is { } file) target.Text = file;
        });
        void UpdateKind() => browse.Visibility = kind.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        kind.SelectionChanged += (_, _) => UpdateKind();
        UpdateKind();
        Field(page, "Тип", kind);
        Field(page, "Название", title);
        Field(page, "Адрес или путь", target);
        page.Children.Add(browse);
        Field(page, "Значок", icons);
        page.Children.Add(monochrome);
        string id = Value(existing, "id", Guid.NewGuid().ToString());
        var actions = Actions(AsyncButton("Сохранить", async () =>
        {
            string label = title.Text.Trim(), value = target.Text.Trim();
            if (label.Length == 0 || value.Length == 0) throw new InvalidOperationException("Укажите название и адрес.");
            bool site = kind.SelectedIndex != 1;
            if (site) HttpUri(value);
            else if (!Compatibility.IsFullyQualifiedPath(value) || !File.Exists(value) || !Path.GetExtension(value).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Выберите существующее приложение Windows (.exe).");
            string selected = icons.SelectedItem as string ?? automaticLabel;
            bool automatic = site && selected == automaticLabel;
            string? siteIcon = automatic ? await new SiteIcons(Favicons).Find(value) : null;
            var favorite = new JsonObject
            {
                ["id"] = id, ["kind"] = site ? "site" : "app", ["title"] = label, ["target"] = value,
                ["icon"] = selected == automaticLabel ? site ? "globe" : "cpu" : selected,
                ["siteIcon"] = siteIcon, ["iconSource"] = automatic ? "site" : "library", ["monochrome"] = monochrome.IsChecked == true
            };
            UpdateWorkspace(state =>
            {
                var items = Items(state, "favorites");
                var previous = items.FirstOrDefault(item => Value(item, "id") == id);
                if (previous is null) items.Add(favorite); else items[items.IndexOf(previous)] = favorite;
            }, true);
            if (page.IsLoaded) OpenSection("favorites");
        }, true));
        if (existing is not null) actions.Children.Add(Button("Удалить", () =>
        {
            UpdateWorkspace(state => { var items = Items(state, "favorites"); items.Remove(items.FirstOrDefault(item => Value(item, "id") == id)); }, true);
            OpenSection("favorites");
        }));
        page.Children.Add(actions);
    }
}
