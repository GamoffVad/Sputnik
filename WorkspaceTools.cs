using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Sputnik;

public partial class MainWindow
{
    private static readonly (string Id, string Title, string Detail)[] ToolPages =
    [
        ("note-list", "Все заметки", "Поиск, открытие и полное удаление"),
        ("dates", "Дни рождения и даты", "Ежегодные события и предварительные уведомления"),
        ("calendar", "Календарь", "Сегодня, неделя и месяц"),
        ("notifications", "Не беспокоить", "Тихие часы и отложенные уведомления"),
        ("commands", "Быстрые команды", "Заметки, напоминания и таймер обычным текстом"),
        ("focus", "Фокус", "Рабочие интервалы и перерывы"),
        ("profiles", "Профили", "Отдельные заметки, избранное и сочетания клавиш"),
        ("sync", "Синхронизация", "Общая папка между компьютерами"),
        ("modules", "Модули", "Погода, ближайшие события и задачи")
    ];
    private void NavigateTool(string id) { if (Desktop is { } app) app.ShowWorkspace(id); else OpenSection(id); }
    private StackPanel ToolPage(string title) => PageFields(title, Button("Назад", () => NavigateTool("tools")), 19);
    private void ShowTools()
    {
        var page = PageFields("Инструменты", titleSize: 20);
        foreach (var tool in ToolPages) Entry(page, tool.Title, tool.Detail, Button("Открыть", () => NavigateTool(tool.Id)));
    }
    private void ShowPersonalDates()
    {
        var page = ToolPage("Дни рождения и даты");
        page.Children.Add(Button("Добавить дату", () => EditPersonalDate(null), true));
        var dates = new PersonalDates(data).Load().OrderBy(item => item.Next(DateTime.Today)).ToArray();
        foreach (var date in dates)
            Entry(page, date.Name, date.Next(DateTime.Today).ToString("dd MMMM", CultureInfo.GetCultureInfo("ru-RU")) + (date.Year is { } year ? $" · с {year} г." : ""),
                Button("Изменить", () => EditPersonalDate(date)));
        if (dates.Length == 0) page.Children.Add(Text("Добавьте день рождения, годовщину или другую ежегодную дату. Год указывать необязательно.", "Muted"));
    }
    private void EditPersonalDate(PersonalDate? existing)
    {
        editing = true;
        var page = PageFields(existing is null ? "Новая дата" : "Изменить дату", Button("Назад", ShowPersonalDates), 19);
        var name = Input("Имя или название", existing?.Name ?? "", 120);
        var kinds = new[] { "birthday", "anniversary", "date" };
        var kind = new ComboBox { ItemsSource = new[] { "День рождения", "Годовщина", "Памятная дата" }, SelectedIndex = Array.IndexOf(kinds, existing?.Kind ?? "birthday") };
        var day = Input("День", (existing?.Day ?? DateTime.Today.Day).ToString(), 2);
        var month = Input("Месяц", (existing?.Month ?? DateTime.Today.Month).ToString(), 2);
        var year = Input("Год (необязательно)", existing?.Year?.ToString() ?? "", 4);
        var time = new TimePicker { Value = TimeSpan.FromMinutes(existing?.Minute ?? 540) };
        var leap = new ComboBox { ItemsSource = new[] { "28 февраля", "1 марта" }, SelectedIndex = existing?.LeapRule == "mar1" ? 1 : 0 };
        Field(page, "Имя или название", name); Field(page, "Тип", kind);
        Row(page, Labeled("День", day), Labeled("Месяц", month), Labeled("Год", year));
        Field(page, "Время уведомления", time);
        var leads = new[] { (0, "В день события"), (1, "За день"), (7, "За неделю") };
        var checks = leads.Select(item => new CheckBox { Content = item.Item2, IsChecked = existing?.LeadDays.Contains(item.Item1) ?? item.Item1 == 0, Margin = new Thickness(0, 3, 0, 3) }).ToArray();
        foreach (var check in checks) page.Children.Add(check);
        Field(page, "29 февраля в обычном году", leap);
        var error = Status(); page.Children.Add(error);
        page.Children.Add(Actions(Button("Сохранить", () =>
        {
            try
            {
                if (!int.TryParse(day.Text, out int d) || !int.TryParse(month.Text, out int m) ||
                    year.Text.Length > 0 && !int.TryParse(year.Text, out _) || !time.TryGetValue(out var t)) throw new InvalidOperationException("Проверьте день, месяц, год и время.");
                new PersonalDates(data).Save(new(existing?.Id ?? Guid.NewGuid().ToString("N"), name.Text.Trim(), kinds[kind.SelectedIndex], m, d,
                    year.Text.Length == 0 ? null : int.Parse(year.Text), (int)t.TotalMinutes, leads.Where((_, i) => checks[i].IsChecked == true).Select(item => item.Item1).ToArray(), leap.SelectedIndex == 0 ? "feb28" : "mar1"));
                ShowPersonalDates();
            }
            catch (Exception ex) { error.Text = ex.Message; }
        }, true), Button(existing is null ? "Отмена" : "Удалить", () =>
        {
            if (existing is not null) new PersonalDates(data).Delete(existing.Id); ShowPersonalDates();
        })));
    }
    private DateTime calendarDay = DateTime.Today;
    private string calendarMode = "day";
    private void ShowPersonalCalendar()
    {
        var page = ToolPage("Календарь");
        var picker = new DatePicker { SelectedDate = calendarDay, FirstDayOfWeek = DayOfWeek.Monday, Language = System.Windows.Markup.XmlLanguage.GetLanguage("ru-RU") };
        AutomationProperties.SetName(picker, "Дата календаря");
        picker.SelectedDateChanged += (_, _) => { if (picker.SelectedDate is { } day) { calendarDay = day; ShowPersonalCalendar(); } };
        page.Children.Add(picker);
        page.Children.Add(Actions(Button("Сегодня", () => { calendarDay = DateTime.Today; calendarMode = "day"; ShowPersonalCalendar(); }),
            Button("Неделя", () => { calendarMode = "week"; ShowPersonalCalendar(); }), Button("Месяц", () => { calendarMode = "month"; ShowPersonalCalendar(); })));
        DateTime start = calendarMode == "month" ? new DateTime(calendarDay.Year, calendarDay.Month, 1) : calendarMode == "week" ? calendarDay.AddDays(-((int)calendarDay.DayOfWeek + 6) % 7) : calendarDay;
        int count = calendarMode == "month" ? DateTime.DaysInMonth(start.Year, start.Month) : calendarMode == "week" ? 7 : 1;
        var reminders = (WorkspaceData()["reminders"] as JsonArray ?? new()).OfType<JsonObject>().Where(item => !ReminderScheduler.IsDone(item)).ToArray();
        var dates = new PersonalDates(data).Load(); int total = 0;
        for (int i = 0; i < count; i++)
        {
            DateTime day = start.AddDays(i);
            var due = reminders.Where(item => ReminderScheduler.AsLocalTime(ReminderScheduler.Due(item))?.Date == day.Date).OrderBy(ReminderScheduler.Due).ToArray();
            var annual = dates.Where(item => item.InYear(day.Year) == day.Date).ToArray();
            if (due.Length + annual.Length == 0 && count > 1) continue;
            page.Children.Add(Text(day.ToString("dddd, dd MMMM", CultureInfo.GetCultureInfo("ru-RU")), "Accent", 13));
            foreach (var item in due) Entry(page, Value(item, "title"), ReminderScheduler.AsLocalTime(ReminderScheduler.Due(item))?.ToString("HH:mm") ?? "", Button("Открыть", () => EditReminder(item)));
            foreach (var item in annual) Entry(page, item.Name, item.Kind == "birthday" ? "День рождения" : "Памятная дата", Button("Открыть", () => EditPersonalDate(item)));
            total += due.Length + annual.Length;
        }
        if (total == 0) page.Children.Add(Text("На выбранный период событий нет.", "Muted"));
        page.Children.Add(Button("Добавить напоминание", () => EditReminder(new JsonObject { ["due"] = new DateTimeOffset(calendarDay.AddHours(9)).ToUnixTimeMilliseconds() })));
    }
    private void ShowNotificationSettings()
    {
        var page = ToolPage("Не беспокоить"); var settings = FeatureFiles.Object(data, "notification-settings.json");
        var manual = new CheckBox { Content = "Не беспокоить сейчас", IsChecked = settings["manual"]?.GetValue<bool>() == true && (settings["until"] is null || settings["until"]!.GetValue<long>() > DateTimeOffset.Now.ToUnixTimeMilliseconds()) };
        var duration = new ComboBox { ItemsSource = new[] { "До выключения вручную", "На 1 час", "На 2 часа" }, SelectedIndex = 0 };
        var schedule = new CheckBox { Content = "Тихие часы по расписанию", IsChecked = settings["schedule"]?.GetValue<bool>() == true };
        var start = new TimePicker { Value = TimeSpan.FromMinutes(settings["start"]?.GetValue<int>() ?? 1320) };
        var end = new TimePicker { Value = TimeSpan.FromMinutes(settings["end"]?.GetValue<int>() ?? 540) };
        page.Children.Add(Text(NotificationSettings.IsQuiet(data, DateTimeOffset.Now) ? "Уведомления отложены до окончания тихих часов." : "Уведомления включены.", "Muted"));
        page.Children.Add(manual); Field(page, "Продолжительность", duration); page.Children.Add(schedule);
        Row(page, Labeled("С", start), Labeled("До", end));
        var labels = new[] { "Вс", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" };
        var days = Enumerable.Range(0, 7).Select(i => new CheckBox { Content = labels[i], IsChecked = (settings["days"] as JsonArray)?.Any(v => v!.GetValue<int>() == i) ?? true, Margin = new Thickness(0, 0, 9, 4) }).ToArray();
        page.Children.Add(Actions(days.Cast<UIElement>().ToArray()));
        page.Children.Add(Text("Для ночного интервала день относится ко времени его начала. Отложенные напоминания появятся после тихих часов.", "Muted", 12));
        var status = Status(); page.Children.Add(status);
        page.Children.Add(Button("Сохранить", () =>
        {
            if (!start.TryGetValue(out var s) || !end.TryGetValue(out var e)) { status.Text = "Проверьте время."; return; }
            FeatureFiles.Write(data, "notification-settings.json", new JsonObject { ["manual"] = manual.IsChecked == true,
                ["until"] = duration.SelectedIndex == 0 ? null : JsonValue.Create(DateTimeOffset.Now.AddHours(duration.SelectedIndex).ToUnixTimeMilliseconds()),
                ["schedule"] = schedule.IsChecked == true, ["start"] = (int)s.TotalMinutes, ["end"] = (int)e.TotalMinutes,
                ["days"] = new JsonArray(Enumerable.Range(0, 7).Where(i => days[i].IsChecked == true).Select(i => (JsonNode?)JsonValue.Create(i)).ToArray()) });
            ShowNotificationSettings();
        }, true));
    }
}
