using System.IO;
using System.Text.Json;
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
        WindowMaterial.Configure(state);
        var root = Page("Внешний вид");
        var page = new StackPanel();
        var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var mode = new ComboBox { ItemsSource = new[] { "Палитра питомца", "Своя палитра", "Графит" } };
        mode.SelectedIndex = Value(state, "appearanceMode") == "custom" ? 1 : state["themed"]?.GetValue<bool>() == false ? 2 : 0;
        Field(page, "Цветовая схема", mode);
        var colors = new StackPanel();
        var custom = state["customTheme"];
        var background = Input("Фон", Value(custom, "background", "#181C20"), 7);
        var surface = Input("Поверхности", Value(custom, "surface", "#23292F"), 7);
        var accent = Input("Акцент", Value(custom, "accent", "#A5E4F6"), 7);
        Row(colors, Labeled("Фон", background), Labeled("Поверхности", surface), Labeled("Акцент", accent));
        page.Children.Add(colors);
        void ShowColors() => colors.Visibility = mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        mode.SelectionChanged += (_, _) => ShowColors();
        ShowColors();
        var fonts = new ComboBox { ItemsSource = ThemePalette.InstalledFonts, IsTextSearchEnabled = true,
            SelectedItem = Value(state, "font", "Segoe UI") };
        var noteColors = new ComboBox
        {
            ItemsSource = new[] { "По теме питомца", "Автоцвет" },
            SelectedIndex = Value(state, "noteColorMode") == "auto" ? 1 : 0,
            ToolTip = "По теме питомца — как сейчас. Автоцвет — каждая новая заметка получает следующий цвет палитры."
        };
        AutomationProperties.SetName(noteColors, "Цвет новых заметок");
        Row(page, Labeled("Шрифт", fonts), Labeled("Цвет новых заметок", noteColors));
        (string Key, string Label, double Min, double Max, double Initial, string Unit)[] sizes =
        [
            ("petScale", "Размер питомца", 60, 160, 100, "%"), ("sphereScale", "Размер сфер", 60, 160, 100, "%"),
            ("sphereSpread", "Между сферами", 0, 100, 100, "%"), ("sphereOffset", "Отступ от питомца", DesktopControls.MinimumSphereOffset, DesktopControls.MaximumSphereOffset, DesktopControls.DefaultSphereOffset, " пкс"),
        ];
        var sliders = new Dictionary<string, Slider>();
        var sizeFields = new List<FrameworkElement>();
        var smoothing = new CheckBox
        {
            Content = "Сглаживание WPF",
            IsChecked = DesktopPetWindow.IsSmoothingOn(state),
            ToolTip = "Сглаживать изображение питомца при отображении"
        };
        bool previewing = false;
        var glass = new CheckBox { Content = "Матовое стекло", IsChecked = state["windowGlass"]?.GetValue<bool>() == true,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = "Полупрозрачный фон с размытием окон позади" };
        AutomationProperties.SetName(glass, "Матовое стекло");
        var transparency = new Slider { Minimum = 0, Maximum = 70, TickFrequency = 5, IsSnapToTickEnabled = true,
            Value = state["windowTransparency"]?.GetValue<double>() ?? 25 };
        AutomationProperties.SetName(transparency, "Прозрачность окон");
        var transparencyLabel = Text("", "Muted", 12);
        var materialField = new StackPanel();
        materialField.Children.Add(transparencyLabel);
        materialField.Children.Add(transparency);
        void MaterialChanged()
        {
            transparencyLabel.Text = $"Прозрачность · {transparency.Value:0}%";
            transparency.IsEnabled = glass.IsChecked == true;
            if (!page.IsVisible) return;
            previewing = true;
            WindowMaterial.Preview(transparency.Value, glass.IsChecked == true);
        }
        MaterialChanged();
        glass.Checked += (_, _) => MaterialChanged();
        glass.Unchecked += (_, _) => MaterialChanged();
        transparency.ValueChanged += (_, _) => MaterialChanged();
        Row(page, glass, materialField);
        void PreviewSizes(bool showSpheres = false)
        {
            if (!page.IsVisible) return;
            previewing = true;
            Desktop?.PreviewAppearanceSizes(sliders["petScale"].Value, sliders["sphereScale"].Value,
                sliders["sphereSpread"].Value, sliders["sphereOffset"].Value, smoothing.IsChecked == true);
            if (showSpheres)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    // Show after the outside-click hook has handled the slider click.
                    if (page.IsVisible) Desktop?.PreviewFavoriteSpheres();
                }));
        }
        void RestoreSizes()
        {
            if (!previewing) return;
            previewing = false;
            Desktop?.RefreshPet();
            WindowMaterial.Configure(WorkspaceData());
        }
        page.IsVisibleChanged += (_, _) => { if (!page.IsVisible) RestoreSizes(); };
        page.Unloaded += (_, _) => RestoreSizes();
        foreach (var entry in sizes)
        {
            var slider = new Slider
            {
                Minimum = entry.Min, Maximum = entry.Max, TickFrequency = 5, IsSnapToTickEnabled = true,
                Value = state[entry.Key]?.GetValue<double>() ?? entry.Initial,
            };
            var label = Text("", "Muted", 12);
            void ShowSize()
            {
                label.Text = $"{entry.Label} · {slider.Value:0}{entry.Unit}";
                Frame.InvalidateVisual();
            }
            ShowSize();
            slider.ValueChanged += (_, _) => { ShowSize(); PreviewSizes(entry.Key != "petScale"); };
            AutomationProperties.SetName(slider, entry.Label);
            if (entry.Key == "sphereOffset")
                slider.ToolTip = "Отрицательные значения приближают сферы к центру питомца";
            sliders.Add(entry.Key, slider);
            var field = new StackPanel { Margin = new Thickness(0, 1, 0, 6) };
            field.Children.Add(label);
            field.Children.Add(slider);
            sizeFields.Add(field);
        }
        Row(page, sizeFields.Take(2).ToArray());
        Row(page, sizeFields.Skip(2).ToArray());
        smoothing.Click += (_, _) => PreviewSizes();
        page.Children.Add(smoothing);
        var status = Status();
        Button IconAction(string label, string geometry, Action action, bool primary = false)
        {
            var button = WorkspaceUi.IconButton(label, geometry, primary);
            button.Click += (_, _) =>
            {
                try { action(); }
                catch (Exception error) { status.Text = error.Message; }
            };
            return button;
        }
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        footer.Children.Add(Actions(IconAction("Сохранить", "M2 1H12L15 4V15H1V1Z M4 1V6H11V1 M4 15V10H12V15", () =>
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
                workspace["noteColorMode"] = noteColors.SelectedIndex == 1 ? "auto" : "pet";
                workspace["petSmoothing"] = smoothing.IsChecked == true;
                workspace["windowGlass"] = glass.IsChecked == true;
                workspace["windowTransparency"] = transparency.Value;
                foreach (var pair in sliders) workspace[pair.Key] = pair.Value.Value;
            }, true);
            previewing = false;
            Hide();
        }, true), IconAction("Сбросить", "M2 2V6H6 M2 6A6 6 0 1 1 2 11", () =>
        {
            UpdateWorkspace(workspace =>
            {
                foreach (string key in sizes.Select(entry => entry.Key).Concat(new[] { "font", "customTheme", "appearanceMode", "petSmoothing", "noteColorMode", "windowGlass", "windowTransparency" })) workspace.Remove(key);
                workspace["themed"] = true;
            }, true);
            previewing = false;
            OpenSection("theme");
        })));
        footer.Children.Add(status);
    }

    private void ShowFavorites()
    {
        var add = WorkspaceUi.IconButton("Добавить сайт или программу", "M8 2V14 M2 8H14", true);
        add.Click += (_, _) => EditFavorite(null);
        var page = PageFields("Сайты и программы", add, 18);
        var favorites = WorkspaceData()["favorites"] as JsonArray ?? new JsonArray();
        foreach (var item in favorites.OfType<JsonObject>())
        {
            string title = Value(item, "title"), target = Value(item, "target"), kind = Value(item, "kind");
            var favorite = new DesktopControls.Favorite(kind, title, target, Value(item, "icon"),
                Value(item, "siteIcon"), item["monochrome"]?.GetValue<bool>() == true);
            var outline = new System.Windows.Shapes.Path
            {
                Data = FavoriteIcons.Get(favorite.Icon) ?? FavoriteIcons.Get(kind == "app" ? "cpu" : "globe"),
                Width = 22, Height = 22, Stretch = Stretch.Uniform, StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
            };
            outline.UseTheme(System.Windows.Shapes.Shape.StrokeProperty, "Accent");
            var icon = new Viewbox { Child = DesktopControls.ReadSiteIcon(favorite) ?? outline, Width = 24, Height = 24,
                Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            var caption = Text(title, size: 13.5);
            caption.FontWeight = FontWeights.SemiBold;
            caption.TextWrapping = TextWrapping.NoWrap;
            caption.TextTrimming = TextTrimming.CharacterEllipsis;
            string detail = target;
            if (kind == "site" && Uri.TryCreate(target, UriKind.Absolute, out var address))
                detail = address.Host + (address.AbsolutePath == "/" ? "" : address.AbsolutePath);
            else if (kind == "app") detail = Path.GetFileName(target);
            var addressLabel = Text(detail, "Muted", 12);
            addressLabel.TextWrapping = TextWrapping.NoWrap;
            addressLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(caption);
            labels.Children.Add(addressLabel);
            var content = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            content.Children.Add(icon);
            content.Children.Add(labels);
            content.Margin = new Thickness(10, 6, 2, 6);
            content.ToolTip = target;
            var open = WorkspaceUi.IconButton($"Открыть «{title}»", WorkspaceUi.ExpandIcon);
            open.Click += (_, _) => { try { OpenTarget(kind, target); } catch (Exception error) { ShowError(error); } };
            open.Background = Brushes.Transparent;
            open.BorderThickness = new Thickness(0);
            HoverCard.Attach(open, $"Открыть «{title}»");
            open.VerticalAlignment = VerticalAlignment.Center;
            var edit = WorkspaceUi.IconButton($"Изменить «{title}»", "M10 2L14 6 M2 10L10 2 14 6 6 14H2Z");
            edit.Background = Brushes.Transparent;
            edit.BorderThickness = new Thickness(0);
            HoverCard.Attach(edit, $"Изменить «{title}»");
            edit.Margin = new Thickness(2, 0, 5, 0);
            edit.VerticalAlignment = VerticalAlignment.Center;
            edit.Click += (_, _) => EditFavorite(item);
            var row = new DockPanel();
            DockPanel.SetDock(edit, Dock.Right);
            row.Children.Add(edit);
            DockPanel.SetDock(open, Dock.Right);
            row.Children.Add(open);
            row.Children.Add(content);
            var surface = new Border { Child = row, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 4), MinHeight = 48 };
            surface.UseTheme(Border.BackgroundProperty, "Field");
            page.Children.Add(surface);
        }
        if (favorites.Count == 0) page.Children.Add(Text("Добавьте сайт или программу кнопкой +.", "Muted", 13));
    }

    private void EditFavorite(JsonObject? existing)
    {
        var page = Editor(existing is null ? "Добавить в избранное" : "Изменить избранное");
        var kind = new ComboBox { ItemsSource = new[] { "Сайт", "Программа" }, SelectedIndex = Value(existing, "kind") == "app" ? 1 : 0 };
        var title = Input("Название", Value(existing, "title"), 80);
        var target = Input("Адрес или путь", Value(existing, "target"));
        const string automaticLabel = "Значок сайта автоматически";
        var icons = new ComboBox { SelectedValuePath = "Tag", MaxDropDownHeight = 270, IsTextSearchEnabled = true };
        AutomationProperties.SetName(icons, "Значок");
        var iconGrid = new FrameworkElementFactory(typeof(WrapPanel));
        iconGrid.SetValue(FrameworkElement.WidthProperty, 308d);
        icons.ItemsPanel = new ItemsPanelTemplate(iconGrid);
        var automaticItem = new ComboBoxItem { Content = automaticLabel, Tag = automaticLabel,
            Width = 308, Height = 36, Padding = new Thickness(8, 4, 8, 4) };
        AutomationProperties.SetName(automaticItem, automaticLabel);
        icons.Items.Add(automaticItem);
        foreach (string name in FavoriteIcons.Names)
        {
            var glyph = new System.Windows.Shapes.Path { Data = FavoriteIcons.Get(name), Width = 20, Height = 20,
                Stretch = Stretch.Uniform, StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            glyph.UseTheme(System.Windows.Shapes.Shape.StrokeProperty, "Ink");
            var choice = new ComboBoxItem { Content = glyph, Tag = name, Width = 44, Height = 40,
                Padding = new Thickness(8), HorizontalContentAlignment = HorizontalAlignment.Center };
            choice.SetResourceReference(StyleProperty, "SputnikIconChoice");
            AutomationProperties.SetName(choice, name);
            TextSearch.SetText(choice, name);
            HoverCard.Attach(choice, name);
            icons.Items.Add(choice);
        }
        icons.SelectedValue = Value(existing, "iconSource") == "library" ? Value(existing, "icon") : automaticLabel;
        if (icons.SelectedIndex < 0) icons.SelectedIndex = 0;
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
        var status = Status();
        string id = Value(existing, "id", Guid.NewGuid().ToString());
        var actions = Actions(Button("Сохранить", () =>
        {
            string label = title.Text.Trim(), value = target.Text.Trim();
            if (label.Length == 0 || value.Length == 0)
            {
                status.Text = "Укажите название и адрес.";
                (label.Length == 0 ? title : target).Focus();
                return;
            }
            bool site = kind.SelectedIndex != 1;
            if (site) value = WebsiteAddress(value);
            else if (!Compatibility.IsFullyQualifiedPath(value) || !File.Exists(value) || !Path.GetExtension(value).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                status.Text = "Выберите существующее приложение Windows (.exe).";
                target.Focus();
                return;
            }
            string selected = icons.SelectedValue as string ?? automaticLabel;
            bool automatic = site && selected == automaticLabel;
            var favorite = new JsonObject
            {
                ["id"] = id, ["kind"] = site ? "site" : "app", ["title"] = label, ["target"] = value,
                ["icon"] = selected == automaticLabel ? site ? "globe" : "cpu" : selected,
                ["iconSource"] = automatic ? "site" : "library", ["monochrome"] = monochrome.IsChecked == true
            };
            UpdateWorkspace(state =>
            {
                var items = Items(state, "favorites");
                var previous = items.FirstOrDefault(item => Value(item, "id") == id);
                if (previous is null) items.Add(favorite); else items[items.IndexOf(previous)] = favorite;
            }, true);
            OpenSection("favorites");
            if (automatic) _ = AddFavoriteSiteIcon(id, value);
        }, true));
        if (existing is not null) actions.Children.Add(Button("Удалить", () =>
        {
            UpdateWorkspace(state => { var items = Items(state, "favorites"); items.Remove(items.FirstOrDefault(item => Value(item, "id") == id)); }, true);
            OpenSection("favorites");
        }));
        page.Children.Add(actions);
        page.Children.Add(status);
    }

    private async Task AddFavoriteSiteIcon(string id, string address)
    {
        try
        {
            if (await new SiteIcons(Favicons).Find(address) is not { } icon) return;
            UpdateWorkspace(workspace =>
            {
                var items = workspace["favorites"] as JsonArray;
                var match = items?.OfType<JsonObject>().FirstOrDefault(item =>
                    Value(item, "id") == id && Value(item, "target") == address && Value(item, "iconSource") == "site");
                if (match is not null) match["siteIcon"] = icon;
            }, true);
        }
        catch (Exception error) when (error is IOException or System.Net.Http.HttpRequestException or InvalidOperationException or JsonException)
        {
            App.WriteDiagnostic("favorite-icon", error.GetType().Name);
        }
    }
}
