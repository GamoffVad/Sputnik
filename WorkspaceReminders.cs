using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Sputnik;

public partial class MainWindow
{
    private static readonly (string Id, string Label)[] RepeatOptions =
    [
        ("none", "Без повтора"), ("5", "Каждые 5 минут"), ("10", "Каждые 10 минут"),
        ("15", "Каждые 15 минут"), ("20", "Каждые 20 минут"), ("30", "Каждые 30 минут"),
        ("60", "Каждый час"), ("120", "Каждые 2 часа"), ("180", "Каждые 3 часа"),
        ("daily", "Каждый день"), ("weekly", "Каждую неделю"), ("custom", "Свой интервал"),
    ];

    private void ShowReminders()
    {
        var page = PageFields("Напоминания", Button("Добавить", () => EditReminder(null), true), 18);
        var reminders = WorkspaceData()["reminders"] as JsonArray ?? new JsonArray();
        foreach (var reminder in reminders.OfType<JsonObject>().OrderBy(ReminderScheduler.IsDone).ThenBy(ReminderScheduler.Due))
        {
            long timestamp = ReminderScheduler.Due(reminder);
            string detail = ReminderScheduler.AsLocalTime(timestamp)?.ToString("g") ?? "Укажите дату";
            if (ReminderScheduler.IsDone(reminder))
                Entry(page, Value(reminder, "title"), detail + " · Выполнено", Button("Изменить", () => EditReminder(reminder)));
            else
                Entry(page, Value(reminder, "title"), detail,
                    Button("Изменить", () => EditReminder(reminder)), Button("Готово", () =>
                    {
                        ReminderScheduler.Update(Store, Value(reminder, "id"), "done");
                        OpenSection("reminders");
                    }));
        }
        if (reminders.Count == 0) page.Children.Add(Text("Добавьте напоминание, чтобы получить его в нужное время.", "Muted"));
    }

    internal void EditReminder(JsonObject? existing, string? noteId = null)
    {
        editing = true;
        var root = Page(existing is null ? "Новое напоминание" : "Изменить напоминание",
            Button("Назад", () => OpenSection("reminders")), 18);
        var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var page = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var initial = ReminderScheduler.AsLocalTime(ReminderScheduler.Due(existing)) ?? DateTime.Now.AddMinutes(10);
        var title = Input("Название", Value(existing, "title"), 120);
        title.Height = 32;
        var date = new DatePicker
        {
            SelectedDate = initial.Date, FirstDayOfWeek = DayOfWeek.Monday,
            Language = System.Windows.Markup.XmlLanguage.GetLanguage("ru-RU"), Height = 36,
            SelectedDateFormat = DatePickerFormat.Short
        };
        AutomationProperties.SetName(date, "Дата");
        var time = new TimePicker { Value = new TimeSpan(initial.Hour, initial.Minute, 0) };
        var repeat = new ComboBox { MaxDropDownHeight = 325, Height = 32,
            ItemsSource = RepeatOptions.Select(option => option.Label).ToArray() };
        AutomationProperties.SetName(repeat, "Повтор");
        repeat.SelectedIndex = Math.Max(0, Array.FindIndex(RepeatOptions, option => option.Id == Value(existing, "repeat")));
        var minutes = Input("Интервал в минутах", Value(existing, "repeatMinutes", "10"), 9);
        minutes.Height = 32;
        var custom = Labeled("Минут", minutes);
        custom.Margin = new Thickness(8, 0, 0, 0);
        var repeatRow = new Grid();
        repeatRow.ColumnDefinitions.Add(new ColumnDefinition());
        var intervalColumn = new ColumnDefinition { Width = new GridLength(0) };
        repeatRow.ColumnDefinitions.Add(intervalColumn);
        var repeatField = Labeled("Повтор", repeat);
        repeatField.Margin = new Thickness(0);
        repeatRow.Children.Add(repeatField);
        Grid.SetColumn(custom, 1);
        repeatRow.Children.Add(custom);
        void UpdateRepeat()
        {
            bool show = repeat.SelectedIndex == RepeatOptions.Length - 1;
            custom.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            intervalColumn.Width = new GridLength(show ? 100 : 0);
        }
        repeat.SelectionChanged += (_, _) => UpdateRepeat();
        UpdateRepeat();
        Field(page, "Название", title);
        var schedule = new Grid();
        schedule.ColumnDefinitions.Add(new ColumnDefinition());
        schedule.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 142 });
        var dateField = Labeled("Дата", date);
        dateField.Margin = new Thickness(0, 0, 8, 7);
        schedule.Children.Add(dateField);
        var timeField = Labeled("Время", time);
        Grid.SetColumn(timeField, 1);
        schedule.Children.Add(timeField);
        page.Children.Add(schedule);

        var shortcuts = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
        var hint = Text("Через", "Muted", 11);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(0, 0, 6, 0);
        shortcuts.Children.Add(hint);
        foreach (var option in new[] { (10, "10 мин"), (30, "30 мин"), (60, "1 час") })
        {
            var quick = Button(option.Item2, () =>
            {
                var next = DateTime.Now.AddMinutes(option.Item1);
                date.SelectedDate = next.Date;
                time.Value = new TimeSpan(next.Hour, next.Minute, 0);
            });
            quick.MinHeight = 24;
            quick.FontSize = 11;
            quick.Padding = new Thickness(8, 1, 8, 1);
            quick.Margin = new Thickness(0, 0, 5, 0);
            AutomationProperties.SetName(quick, "Напомнить через " + option.Item2);
            shortcuts.Children.Add(quick);
        }
        page.Children.Add(shortcuts);
        page.Children.Add(repeatRow);

        var status = Status(11);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Assertive);
        footer.Children.Add(status);
        void Invalid(string message, UIElement input)
        {
            status.Text = message;
            input.Focus();
        }
        title.TextChanged += (_, _) => status.Text = "";
        date.SelectedDateChanged += (_, _) => status.Text = "";
        time.ValueChanged += (_, _) => status.Text = "";
        minutes.TextChanged += (_, _) => status.Text = "";
        repeat.SelectionChanged += (_, _) => status.Text = "";
        date.DateValidationError += (_, e) => { e.ThrowException = false; status.Text = "Укажите существующую дату."; };

        string id = Value(existing, "id", Guid.NewGuid().ToString());
        var save = Button("Сохранить", () =>
        {
            string label = title.Text.Trim();
            if (label.Length == 0) { Invalid("Введите название напоминания.", title); return; }
            if (date.SelectedDate is not { } day) { Invalid("Выберите дату напоминания.", date); return; }
            if (!time.TryGetValue(out var hhmm)) { Invalid("Укажите часы от 00 до 23 и минуты от 00 до 59.", time.Hours); return; }
            DateTime local = DateTime.SpecifyKind(day.Date + hhmm, DateTimeKind.Unspecified);
            if (TimeZoneInfo.Local.IsInvalidTime(local)) { Invalid("Это время недоступно в текущем часовом поясе.", time.Hours); return; }
            var due = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
            string interval = RepeatOptions[repeat.SelectedIndex].Id;
            var reminder = new JsonObject { ["id"] = id, ["title"] = label, ["due"] = due.ToUnixTimeMilliseconds(), ["repeat"] = interval, ["done"] = false };
            string linked = noteId ?? Value(existing, "noteId");
            if (linked.Length > 0) reminder["noteId"] = linked;
            if (interval == "custom")
            {
                if (!int.TryParse(minutes.Text, out int count) || count < 1 || count > (DateTimeOffset.MaxValue - due).TotalMinutes)
                { Invalid("Укажите интервал целым числом минут, от 1.", minutes); return; }
                reminder["repeatMinutes"] = count;
            }
            UpdateWorkspace(state =>
            {
                var items = Items(state, "reminders");
                var previous = items.FirstOrDefault(item => Value(item, "id") == id);
                if (previous is null) items.Add(reminder); else items[items.IndexOf(previous)] = reminder;
            });
            Desktop?.ResetReminder(id);
            OpenSection("reminders");
        }, true);
        save.Height = 34;
        save.IsDefault = true;
        AutomationProperties.SetName(save, "Сохранить");
        var buttons = new Grid();
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.Children.Add(save);
        var secondary = existing is null ? Button("Отмена", () => OpenSection("reminders")) : Button("Удалить", () =>
        {
            UpdateWorkspace(state => { var items = Items(state, "reminders"); items.Remove(items.FirstOrDefault(item => Value(item, "id") == id)); });
            Desktop?.ResetReminder(id);
            OpenSection("reminders");
        });
        secondary.Height = 34;
        secondary.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(secondary, 1);
        buttons.Children.Add(secondary);
        footer.Children.Add(buttons);
    }
}

internal sealed class ReminderScheduler : IDisposable
{
    private readonly string store;
    private readonly Action<Window> position;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly HashSet<string> dismissed = new(StringComparer.Ordinal);
    private Window? notice;
    private string? noticeId;
    private DateTime lastWrite;
    private JsonArray items = new();
    internal Func<bool>? Blocked;
    internal Action<string>? OpenNote;
    internal bool HasNotice => notice is not null;
    internal void NoteDeleted(string id)
    {
        if (items.FirstOrDefault(item => item?["id"]?.ToString() == noticeId)?["noteId"]?.ToString() == id) notice?.Close();
        lastWrite = DateTime.MinValue;
    }

    internal ReminderScheduler(string data, Action<Window> position)
    {
        store = Path.Combine(data, "workspace.json");
        this.position = position;
        timer.Tick += (_, _) => Check();
        timer.Start();
    }

    internal static long Due(JsonNode? node) => node?["due"] is JsonValue value && value.TryGetValue<long>(out long due) ? due : -1;
    internal static bool IsDone(JsonNode? node) => node?["done"]?.GetValue<bool>() == true;
    internal static DateTime? AsLocalTime(long timestamp) =>
        timestamp < -62135596800000L || timestamp > 253402300799999L || timestamp == -1
            ? null : DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;

    internal void Forget(string id)
    {
        dismissed.Remove(id);
        if (noticeId != id) return;
        notice?.Close();
    }

    internal void Reload(IEnumerable<string> changed)
    {
        foreach (string id in changed) Forget(id);
        lastWrite = DateTime.MinValue;
    }

    private void Check()
    {
        try
        {
            var write = File.GetLastWriteTimeUtc(store);
            if (write != lastWrite)
            {
                lastWrite = write;
                items = (File.Exists(store) ? JsonNode.Parse(File.ReadAllText(store))?["reminders"] as JsonArray : null) ?? new JsonArray();
            }
            if (notice is not null || Blocked?.Invoke() == true || NotificationSettings.IsQuiet(Path.GetDirectoryName(store)!, DateTimeOffset.Now)) return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var due = items.OfType<JsonObject>().Where(item => Due(item) >= 0 && Due(item) <= now && !IsDone(item) &&
                !dismissed.Contains(item["id"]?.ToString() ?? "")).OrderBy(Due).FirstOrDefault();
            if (due is not null) Show(due);
        }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidOperationException)
        { App.WriteDiagnostic("reminders", error.ToString()); }
    }

    private void Show(JsonObject item)
    {
        string id = item["id"]?.ToString() ?? "";
        if (id.Length == 0) return;
        var window = new NotificationWindow("Напоминание", item["title"]?.ToString() ?? "",
            AsLocalTime(Due(item))?.ToString("d MMMM · HH:mm"), action =>
            {
                if (action == "dismiss") dismissed.Add(id);
                else Update(store, id, action);
            },
            item["noteId"]?.ToString() is { Length: > 0 } linked ? () => OpenNote?.Invoke(linked) : null);
        window.Closed += (_, _) => { notice = null; noticeId = null; };
        notice = window;
        noticeId = id;
        position(window);
        window.Show();
    }

    internal static void Update(string path, string id, string action)
    {
        if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject workspace ||
            workspace["reminders"] is not JsonArray reminders ||
            reminders.OfType<JsonObject>().FirstOrDefault(item => item["id"]?.ToString() == id) is not { } target) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (action == "snooze")
        {
            target["scheduled"] ??= Due(target);
            target["due"] = now + 600_000;
        }
        else if (Next(target, now) is { } next)
        {
            target.Remove("scheduled");
            target["due"] = next;
        }
        else target["done"] = true;
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, workspace.ToJsonString());
        Compatibility.ReplaceFile(temporary, path);
        DataTransfer.SyncFor(path);
    }

    private static long? Next(JsonObject reminder, long now)
    {
        string? repeat = reminder["repeat"]?.ToString();
        long scheduled = reminder["scheduled"]?.GetValue<long>() ?? Due(reminder);
        if (repeat is "daily" or "weekly" && AsLocalTime(scheduled) is { } initial)
        {
            int days = repeat == "daily" ? 1 : 7;
            int steps = Math.Max(1, (DateTime.Now.Date - initial.Date).Days / days);
            DateTime next = initial.AddDays(steps * days);
            while (next <= DateTime.Now) next = next.AddDays(days);
            if (TimeZoneInfo.Local.IsInvalidTime(next)) next = next.AddHours(1);
            return new DateTimeOffset(next).ToUnixTimeMilliseconds();
        }
        string? value = repeat == "custom" ? reminder["repeatMinutes"]?.ToString() : repeat;
        if (!int.TryParse(value, out int minutes) || minutes < 1) return null;
        long interval = minutes * 60_000L;
        long nextUtc = scheduled + interval * Math.Max(1, (now - scheduled) / interval + 1);
        return AsLocalTime(nextUtc) is null ? null : nextUtc;
    }

    public void Dispose()
    {
        timer.Stop();
        notice?.Close();
    }
}
