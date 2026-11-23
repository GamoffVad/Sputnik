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
        var add = WorkspaceUi.IconButton("Добавить канал", "M8 2V14 M2 8H14", true);
        add.Click += (_, _) => EditShareChannel(null);
        var page = PageFields("Каналы отправки", add, 18);
        foreach (var channel in channels)
        {
            bool mail = channel.Template.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
            string host = Uri.TryCreate(channel.Template, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : "";
            bool telegram = host == "t.me", max = host is "max.ru" or "web.max.ru";
            string detail = mail ? "Почтовое приложение" : telegram ? "Выбор чата в Telegram" : max ? "Отправка в MAX" : host.Length > 0 ? host : "Свой канал";
            string geometry = mail ? "M1 3H15V13H1Z M1 4L8 9 15 4" : telegram ? "M1 7L15 1 10 15 7 9Z M7 9L15 1"
                : max ? "M2 2H14V11H7L2 15Z" : "M6 5L8 3A3 3 0 0 1 13 8L11 10 M10 11L8 13A3 3 0 0 1 3 8L5 6 M5 11L11 5";
            var glyph = new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry), Width = 18, Height = 18,
                Stretch = Stretch.Uniform, StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            glyph.UseTheme(System.Windows.Shapes.Shape.StrokeProperty, "Accent");
            var tile = new Border { Child = glyph, Width = 32, Height = 32, CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            tile.UseTheme(Border.BackgroundProperty, "Hover");
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = Text(channel.Name, size: 13);
            name.FontWeight = FontWeights.SemiBold;
            name.TextWrapping = TextWrapping.NoWrap;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            labels.Children.Add(name);
            var description = Text(detail, "Muted", 11);
            description.TextWrapping = TextWrapping.NoWrap;
            description.TextTrimming = TextTrimming.CharacterEllipsis;
            description.Margin = new Thickness(0, 3, 0, 0);
            labels.Children.Add(description);
            var edit = new System.Windows.Shapes.Path { Data = Geometry.Parse("M10 2L14 6 M2 10L10 2 14 6 6 14H2Z"),
                Width = 15, Height = 15, Stretch = Stretch.Uniform, StrokeThickness = 1.4, Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            edit.UseTheme(System.Windows.Shapes.Shape.StrokeProperty, "Muted");
            var row = new DockPanel();
            DockPanel.SetDock(tile, Dock.Left); row.Children.Add(tile);
            DockPanel.SetDock(edit, Dock.Right); row.Children.Add(edit);
            row.Children.Add(labels);
            var button = Button("Изменить", () => EditShareChannel(channel));
            button.Content = row;
            button.MinHeight = 58;
            button.Padding = new Thickness(10);
            button.Margin = new Thickness(0, 0, 0, 6);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.UseTheme(Control.BackgroundProperty, "Field");
            AutomationProperties.SetName(button, "Изменить канал «" + channel.Name + "»");
            page.Children.Add(button);
        }
        if (channels.Length == 0) page.Children.Add(Text("Нет каналов. Добавьте первый кнопкой +.", "Muted", 13));
        var hint = Text("Отправляйте заметки через меню «Поделиться» в окне заметки.", "Muted", 11);
        hint.Margin = new Thickness(2, 5, 0, 0);
        page.Children.Add(hint);
    }

    private void EditShareChannel(NoteSharing.Channel? existing)
    {
        editing = true;
        var root = Page(existing is null ? "Добавить канал" : "Изменить канал", Button("Назад", () => OpenSection("sharing")), 18);
        var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var page = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var name = Input("Название", existing?.Name ?? "", 40);
        var template = Input("Ссылка для отправки", existing?.Template ?? "");
        name.Height = 34;
        template.Height = 62;
        template.TextWrapping = TextWrapping.Wrap;
        template.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Field(page, "Название", name);
        Field(page, "Ссылка для отправки", template);
        page.Children.Add(Text("{text} — текст заметки, {title} — тема письма. Ссылка должна содержать {text}.", "Muted", 11));
        var status = Status(11);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Assertive);
        name.TextChanged += (_, _) => status.Text = "";
        template.TextChanged += (_, _) => status.Text = "";
        footer.Children.Add(status);
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
        var save = Button("Сохранить", () =>
        {
            try { SaveChannel(false); }
            catch (Exception error) { status.Text = error.Message; }
        }, true);
        save.Height = 34;
        save.IsDefault = true;
        var buttons = new Grid();
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.Children.Add(save);
        var secondary = existing is null ? Button("Отмена", () => OpenSection("sharing")) : Button("Удалить", () => SaveChannel(true));
        secondary.Height = 34;
        secondary.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(secondary, 1);
        buttons.Children.Add(secondary);
        footer.Children.Add(buttons);
    }

    private void ShowManage()
    {
        var page = PageFields("Управление", titleSize: 18);
        var status = Status(11);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        Grid Content(string title, string detail, string geometry, bool navigate = false)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var glyph = new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry), Width = 17, Height = 17,
                StrokeThickness = 1.5, Stretch = Stretch.Uniform, StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            glyph.UseTheme(System.Windows.Shapes.Shape.StrokeProperty, "Accent");
            var tile = new Border { Child = glyph, Width = 30, Height = 30, CornerRadius = new CornerRadius(7),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            tile.UseTheme(Border.BackgroundProperty, "Hover");
            row.Children.Add(tile);
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var caption = Text(title, size: 13);
            caption.FontWeight = FontWeights.SemiBold;
            labels.Children.Add(caption);
            var description = Text(detail, "Muted", 11);
            description.Margin = new Thickness(0, 3, 0, 0);
            labels.Children.Add(description);
            Grid.SetColumn(labels, 1);
            row.Children.Add(labels);
            if (navigate)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                var arrow = new System.Windows.Shapes.Path { Data = Geometry.Parse("M1 1L6 6 1 11"), Width = 5, Height = 10,
                    Stretch = Stretch.Uniform, StrokeThickness = 1.4, HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center, StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
                arrow.UseTheme(System.Windows.Shapes.Shape.StrokeProperty, "Muted");
                Grid.SetColumn(arrow, 2);
                row.Children.Add(arrow);
            }
            return row;
        }
        var start = new CheckBox { Content = Content("Автозапуск", "При входе в Windows", "M8 1V8 M4 3A6 6 0 1 0 12 3"),
            IsChecked = App.AutoStartEnabled(), Margin = new Thickness(0, 0, 0, 8) };
        start.SetResourceReference(StyleProperty, "SputnikSettingsToggle");
        AutomationProperties.SetName(start, "Запускать питомца при входе в Windows");
        start.Click += (_, _) =>
        {
            try { App.SetAutoStart(start.IsChecked == true); status.Text = ""; }
            catch (Exception error) { start.IsChecked = App.AutoStartEnabled(); status.Text = error.Message; }
        };
        page.Children.Add(start);
        void Navigation(string title, string detail, string geometry, Action open)
        {
            var button = Button(title, open);
            button.Content = Content(title, detail, geometry, true);
            button.MinHeight = 64;
            button.Padding = new Thickness(10);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Margin = new Thickness(0, 0, 0, 8);
            button.UseTheme(Control.BackgroundProperty, "Field");
            AutomationProperties.SetName(button, title);
            page.Children.Add(button);
        }
        Navigation("Горячие клавиши", "Быстрый доступ к функциям", "M1 3H15V13H1Z M4 6H4.1 M7 6H7.1 M10 6H10.1 M13 6H13.1 M4 9H4.1 M7 9H7.1 M10 9H10.1 M13 9H13.1 M5 11H11", ShowHotkeys);
        Navigation("Импорт и экспорт", "Заметки, настройки, напоминания", "M3 1H10L14 5V15H3Z M10 1V5H14 M5 10H11 M9 8L11 10 9 12", ShowDataFiles);
        var hint = Text("Изменения сохраняются автоматически", "Muted", 11);
        hint.Margin = new Thickness(2, 2, 0, 0);
        page.Children.Add(hint);
        page.Children.Add(status);
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
        Action flushAppearance = () => { };
        var topmost = new CheckBox { Content = "Поверх всех окон", IsChecked = DesktopPetWindow.IsTopmostOn(state),
            ToolTip = "Питомец и его сферы остаются поверх приложений и панели задач Windows" };
        AutomationProperties.SetName(topmost, "Поверх всех окон");
        topmost.Click += (_, _) =>
        {
            previewing = true;
            Desktop?.PreviewPetTopmost(topmost.IsChecked == true);
        };
        page.Children.Add(topmost);
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
            flushAppearance();
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
        var speed = new Slider { Minimum=.25, Maximum=2, TickFrequency=.05, IsSnapToTickEnabled=true, Value=state["animationSpeed"]?.GetValue<double>()??1 };
        AutomationProperties.SetName(speed,"Скорость анимаций");
        var speedLabel = Text("", "Muted",12);
        void ShowSpeed() => speedLabel.Text=$"Скорость анимаций · {speed.Value:0.00}×";
        ShowSpeed();
        speed.ValueChanged += (_,_) => {ShowSpeed();if(page.IsVisible){previewing=true;Desktop?.PreviewPlaybackSpeed(speed.Value);}};
        page.Children.Add(speedLabel);page.Children.Add(speed);
        page.Children.Add(Button("Обычная скорость · 1×",()=>speed.Value=1));
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
        var autosave = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        bool pending = false;
        void PersistAppearance()
        {
            autosave.Stop();
            if (!pending) return;
            bool validColors = mode.SelectedIndex != 1 || new[] { background.Text, surface.Text, accent.Text }.All(color =>
                color.Length == 7 && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit));
            try
            {
                UpdateWorkspace(workspace =>
                {
                    // While a hex colour is being typed, retain the last valid
                    // palette and still save all other independent preferences.
                    if (validColors)
                    {
                        workspace["appearanceMode"] = mode.SelectedIndex switch { 1 => "custom", 2 => "default", _ => "pet" };
                        workspace["themed"] = mode.SelectedIndex == 0;
                        if (mode.SelectedIndex == 1)
                            workspace["customTheme"] = new JsonObject { ["background"] = background.Text, ["surface"] = surface.Text, ["accent"] = accent.Text };
                    }
                    workspace["font"] = fonts.SelectedItem as string ?? "Segoe UI";
                    workspace["noteColorMode"] = noteColors.SelectedIndex == 1 ? "auto" : "pet";
                    workspace["petSmoothing"] = smoothing.IsChecked == true;
                    workspace["animationSpeed"] = speed.Value;
                    workspace["petTopmost"] = topmost.IsChecked == true;
                    workspace["windowGlass"] = glass.IsChecked == true;
                    workspace["windowTransparency"] = transparency.Value;
                    foreach (var pair in sliders) workspace[pair.Key] = pair.Value.Value;
                }, true);
                WindowMaterial.Configure(WorkspaceData());
                pending = false;
                previewing = false;
                status.Text = validColors ? "" : "Укажите цвета в формате #RRGGBB. Последняя палитра сохранена.";
            }
            catch (Exception error) { status.Text = "Не удалось сохранить настройки. " + error.Message; }
        }
        flushAppearance = PersistAppearance;
        autosave.Tick += (_, _) => PersistAppearance();
        void ScheduleSave(bool immediately = false)
        {
            pending = true;
            autosave.Stop();
            if (immediately) PersistAppearance(); else autosave.Start();
        }
        foreach (var select in new[] { mode, fonts, noteColors })
            select.SelectionChanged += (_, _) => ScheduleSave(true);
        foreach (var check in new[] { topmost, glass, smoothing })
        {
            check.Checked += (_, _) => ScheduleSave(true);
            check.Unchecked += (_, _) => ScheduleSave(true);
        }
        foreach (var slider in sliders.Values.Concat(new[] { speed, transparency }))
            slider.ValueChanged += (_, _) => ScheduleSave();
        foreach (var input in new[] { background, surface, accent })
            input.TextChanged += (_, _) => ScheduleSave();
        footer.Children.Add(Actions(IconAction("Сбросить", "M2 2V6H6 M2 6A6 6 0 1 1 2 11", () =>
        {
            autosave.Stop();
            pending = false;
            UpdateWorkspace(workspace =>
            {
                foreach (string key in sizes.Select(entry => entry.Key).Concat(new[] { "font", "customTheme", "appearanceMode", "petSmoothing", "petTopmost", "noteColorMode", "windowGlass", "windowTransparency", "animationSpeed" })) workspace.Remove(key);
                workspace["themed"] = true;
            }, true);
            previewing = false;
            OpenSection("theme");
        }), IconAction("Импорт и экспорт", "M3 1H10L14 5V15H3Z M10 1V5H14 M5 10H11 M8 7V13", ShowDataFiles)));
        footer.Children.Add(Text("Изменения сохраняются автоматически", "Muted", 11));
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
        editing = true;
        var root = Page(existing is null ? "Добавить в избранное" : "Изменить избранное",
            Button("Назад", () => OpenSection("favorites")), 18);
        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var page = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        string group = "FavoriteKind" + Guid.NewGuid().ToString("N");
        RadioButton Kind(string label, string icon)
        {
            var choice = new RadioButton { GroupName = group };
            choice.SetResourceReference(StyleProperty, "SputnikSegment");
            AutomationProperties.SetName(choice, label);
            var glyph = new System.Windows.Shapes.Path { Data = FavoriteIcons.Get(icon), Width = 15, Height = 15,
                Stretch = Stretch.Uniform, StrokeThickness = 1.6, Margin = new Thickness(0, 0, 7, 0),
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            glyph.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = choice });
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(glyph);
            content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            choice.Content = content;
            return choice;
        }
        var siteChoice = Kind("Сайт", "globe");
        var appChoice = Kind("Программа", "cpu");
        bool application = Value(existing, "kind") == "app";
        siteChoice.IsChecked = !application;
        appChoice.IsChecked = application;
        var kinds = new Grid();
        kinds.ColumnDefinitions.Add(new ColumnDefinition());
        kinds.ColumnDefinitions.Add(new ColumnDefinition());
        kinds.Children.Add(siteChoice);
        Grid.SetColumn(appChoice, 1);
        kinds.Children.Add(appChoice);
        var switcher = new Border { Child = kinds, CornerRadius = new CornerRadius(9), Padding = new Thickness(3),
            Height = 38, Margin = new Thickness(0, 0, 0, 12) };
        switcher.UseTheme(Border.BackgroundProperty, "Field");
        page.Children.Add(switcher);
        var title = Input("Название", Value(existing, "title"), 80);
        var target = Input("Адрес или путь", Value(existing, "target"));
        title.Height = target.Height = 34;
        Grid WithPlaceholder(TextBox input, string example, out TextBlock hint)
        {
            var placeholder = Text(example, "Muted", 12);
            placeholder.Margin = new Thickness(10, 0, 10, 0);
            placeholder.VerticalAlignment = VerticalAlignment.Center;
            placeholder.TextWrapping = TextWrapping.NoWrap;
            placeholder.TextTrimming = TextTrimming.CharacterEllipsis;
            placeholder.IsHitTestVisible = false;
            void Update() => placeholder.Visibility = input.Text.Length == 0 && !input.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;
            input.TextChanged += (_, _) => Update();
            input.IsKeyboardFocusWithinChanged += (_, _) => Update();
            Update();
            var field = new Grid();
            field.Children.Add(input);
            field.Children.Add(placeholder);
            hint = placeholder;
            return field;
        }
        Field(page, "Название", WithPlaceholder(title, "Как назвать в избранном", out _));
        var targetInput = WithPlaceholder(target, "example.com", out var targetHint);
        var browse = WorkspaceUi.IconButton("Выбрать приложение", "M1 4V14H15V4H7L5 2H1Z");
        browse.Height = 34;
        browse.Width = 34;
        browse.Margin = new Thickness(6, 0, 0, 0);
        browse.Click += (_, _) =>
        {
            if (Choose("Приложение Windows (*.exe)|*.exe") is not { } file) return;
            target.Text = file;
            if (string.IsNullOrWhiteSpace(title.Text)) title.Text = Path.GetFileNameWithoutExtension(file);
        };
        var address = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        address.Children.Add(browse);
        address.Children.Add(targetInput);
        var addressField = Labeled("Адрес сайта", address);
        var addressLabel = (TextBlock)addressField.Children[0];
        AutomationProperties.SetLabeledBy(target, addressLabel);
        page.Children.Add(addressField);
        const string automaticLabel = "Значок сайта автоматически";
        var icons = new ComboBox { SelectedValuePath = "Tag", MaxDropDownHeight = 270, IsTextSearchEnabled = true, Height = 32 };
        AutomationProperties.SetName(icons, "Значок");
        var iconGrid = new FrameworkElementFactory(typeof(WrapPanel));
        icons.ItemsPanel = new ItemsPanelTemplate(iconGrid);
        var automaticItem = new ComboBoxItem { Content = "Автоматически", Tag = automaticLabel,
            Height = 32, Padding = new Thickness(8, 4, 8, 4) };
        icons.SizeChanged += (_, _) => automaticItem.Width = Math.Max(44, icons.ActualWidth - 24);
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
        var monochrome = new CheckBox { Content = "Монохромный", FontSize = 12,
            IsChecked = existing?["monochrome"]?.GetValue<bool>() == true };
        AutomationProperties.SetName(monochrome, "Монохромный");
        var iconHeading = new DockPanel { Margin = new Thickness(0, 3, 0, 5) };
        DockPanel.SetDock(monochrome, Dock.Right);
        iconHeading.Children.Add(monochrome);
        var iconLabel = Text("Значок", "Muted", 12);
        iconLabel.FontWeight = FontWeights.SemiBold;
        iconLabel.VerticalAlignment = VerticalAlignment.Center;
        iconHeading.Children.Add(iconLabel);
        AutomationProperties.SetLabeledBy(icons, iconLabel);
        page.Children.Add(iconHeading);
        var preview = new Border { Width = 54, Height = 54, CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 10, 0), BorderThickness = new Thickness(1) };
        preview.UseTheme(Border.BackgroundProperty, "Field");
        preview.UseTheme(Border.BorderBrushProperty, "Line");
        AutomationProperties.SetName(preview, "Предпросмотр значка");
        var iconHint = Text("", "Muted", 11);
        iconHint.Margin = new Thickness(0, 4, 0, 0);
        var iconFields = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        iconFields.Children.Add(icons);
        iconFields.Children.Add(iconHint);
        var iconRow = new DockPanel();
        DockPanel.SetDock(preview, Dock.Left);
        iconRow.Children.Add(preview);
        iconRow.Children.Add(iconFields);
        page.Children.Add(iconRow);
        void UpdatePreview()
        {
            bool site = siteChoice.IsChecked == true;
            string selected = icons.SelectedValue as string ?? automaticLabel;
            bool automatic = selected == automaticLabel;
            var glyph = new System.Windows.Shapes.Path { Data = FavoriteIcons.Get(automatic ? site ? "globe" : "cpu" : selected),
                Width = 26, Height = 26, Stretch = Stretch.Uniform, StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            glyph.UseTheme(System.Windows.Shapes.Shape.StrokeProperty, "Accent");
            var cached = automatic && site && target.Text.Trim() == Value(existing, "target")
                ? DesktopControls.ReadSiteIcon(new DesktopControls.Favorite("site", "", target.Text, "globe", Value(existing, "siteIcon"), monochrome.IsChecked == true)) : null;
            preview.Child = cached ?? glyph;
            iconHint.Text = automatic ? site ? "Загрузится с сайта после сохранения" : "Стандартный значок программы" : "Значок из библиотеки";
        }
        void UpdateKind()
        {
            bool site = siteChoice.IsChecked == true;
            browse.Visibility = site ? Visibility.Collapsed : Visibility.Visible;
            addressLabel.Text = site ? "Адрес сайта" : "Путь к программе";
            targetHint.Text = site ? "example.com" : @"C:\Программы\Приложение.exe";
            automaticItem.Content = site ? "Автоматически" : "Значок программы";
            AutomationProperties.SetName(automaticItem, site ? "Значок сайта автоматически" : "Стандартный значок программы");
            UpdatePreview();
        }
        siteChoice.Checked += (_, _) => UpdateKind();
        appChoice.Checked += (_, _) => UpdateKind();
        icons.SelectionChanged += (_, _) => UpdatePreview();
        monochrome.Checked += (_, _) => UpdatePreview();
        monochrome.Unchecked += (_, _) => UpdatePreview();
        target.TextChanged += (_, _) => UpdatePreview();
        UpdateKind();
        var status = Status(11);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Assertive);
        footer.Children.Add(status);
        title.TextChanged += (_, _) => status.Text = "";
        target.TextChanged += (_, _) => status.Text = "";
        siteChoice.Checked += (_, _) => status.Text = "";
        appChoice.Checked += (_, _) => status.Text = "";
        string id = Value(existing, "id", Guid.NewGuid().ToString());
        var save = Button("Сохранить", () =>
        {
            string label = title.Text.Trim(), value = target.Text.Trim();
            if (label.Length == 0 || value.Length == 0)
            {
                status.Text = "Укажите название и адрес.";
                (label.Length == 0 ? title : target).Focus();
                return;
            }
            bool site = siteChoice.IsChecked == true;
            if (site)
            {
                value = WebsiteAddress(value);
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host))
                {
                    status.Text = "Укажите адрес сайта, например example.com.";
                    target.Focus();
                    return;
                }
            }
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
        }, true);
        save.Height = 34;
        save.IsDefault = true;
        AutomationProperties.SetName(save, "Сохранить");
        var actions = new Grid();
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.Children.Add(save);
        var secondary = existing is null ? Button("Отмена", () => OpenSection("favorites")) : Button("Удалить", () =>
        {
            UpdateWorkspace(state => { var items = Items(state, "favorites"); items.Remove(items.FirstOrDefault(item => Value(item, "id") == id)); }, true);
            OpenSection("favorites");
        });
        secondary.Height = 34;
        secondary.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(secondary, 1);
        actions.Children.Add(secondary);
        footer.Children.Add(actions);
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
