using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
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
        var page = PageFields("Напоминания", Button("Добавить", () => EditReminder(null), true));
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

    private void EditReminder(JsonObject? existing)
    {
        var page = Editor(existing is null ? "Новое напоминание" : "Изменить напоминание");
        var initial = ReminderScheduler.AsLocalTime(ReminderScheduler.Due(existing)) ?? DateTime.Now.AddMinutes(10);
        var title = Input("Название", Value(existing, "title"), 120);
        var date = new DatePicker
        {
            SelectedDate = initial.Date, FirstDayOfWeek = DayOfWeek.Monday,
            Language = System.Windows.Markup.XmlLanguage.GetLanguage("ru-RU")
        };
        var time = Input("Время (ЧЧ:ММ)", initial.ToString("HH:mm", CultureInfo.InvariantCulture), 5);
        var repeat = new ComboBox { MaxDropDownHeight = 325, ItemsSource = RepeatOptions.Select(option => option.Label).ToArray() };
        repeat.SelectedIndex = Math.Max(0, Array.FindIndex(RepeatOptions, option => option.Id == Value(existing, "repeat")));
        var minutes = Input("Интервал в минутах", Value(existing, "repeatMinutes", "10"), 9);
        var custom = new StackPanel();
        Field(custom, "Интервал в минутах", minutes);
        void UpdateRepeat() => custom.Visibility = repeat.SelectedIndex == RepeatOptions.Length - 1 ? Visibility.Visible : Visibility.Collapsed;
        repeat.SelectionChanged += (_, _) => UpdateRepeat();
        UpdateRepeat();
        Field(page, "Название", title);
        Row(page, Labeled("Дата", date), Labeled("Время (ЧЧ:ММ)", time));
        Field(page, "Частота повтора", repeat);
        page.Children.Add(custom);

        string id = Value(existing, "id", Guid.NewGuid().ToString());
        var buttons = Actions(Button("Сохранить", () =>
        {
            string label = title.Text.Trim();
            if (label.Length == 0 || date.SelectedDate is not { } day ||
                !TimeSpan.TryParseExact(time.Text, @"hh\:mm", CultureInfo.InvariantCulture, out var hhmm) || hhmm >= TimeSpan.FromDays(1))
                throw new InvalidOperationException("Укажите название, дату и время в формате ЧЧ:ММ.");
            DateTime local = DateTime.SpecifyKind(day.Date + hhmm, DateTimeKind.Unspecified);
            if (TimeZoneInfo.Local.IsInvalidTime(local)) throw new InvalidOperationException("Это время недоступно в текущем часовом поясе.");
            var due = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
            string interval = RepeatOptions[repeat.SelectedIndex].Id;
            var reminder = new JsonObject { ["id"] = id, ["title"] = label, ["due"] = due.ToUnixTimeMilliseconds(), ["repeat"] = interval, ["done"] = false };
            if (interval == "custom")
            {
                if (!int.TryParse(minutes.Text, out int count) || count < 1 || count > (DateTimeOffset.MaxValue - due).TotalMinutes)
                    throw new InvalidOperationException("Укажите допустимый интервал целым числом минут.");
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
        }, true));
        if (existing is not null) buttons.Children.Add(Button("Удалить", () =>
        {
            UpdateWorkspace(state => { var items = Items(state, "reminders"); items.Remove(items.FirstOrDefault(item => Value(item, "id") == id)); });
            Desktop?.ResetReminder(id);
            OpenSection("reminders");
        }));
        page.Children.Add(buttons);
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
            if (notice is not null) return;
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
        var top = new StackPanel { Margin = new Thickness(12) };
        var heading = new TextBlock { Text = "Напоминание", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        heading.UseTheme(TextBlock.ForegroundProperty, "Accent");
        top.Children.Add(heading);
        var message = new TextBlock { Text = item["title"]?.ToString(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        message.UseTheme(TextBlock.ForegroundProperty, "Ink");
        top.Children.Add(message);
        var row = new WrapPanel();
        top.Children.Add(row);
        var frame = new Border { Child = new ScrollViewer { Content = top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, BorderThickness = new Thickness(1) };
        frame.UseTheme(Border.BackgroundProperty, "Surface");
        frame.UseTheme(Border.BorderBrushProperty, "Edge");
        var window = new Window
        {
            Content = frame, Title = "Напоминание Спутника", Width = 330, Height = 134, ShowInTaskbar = false, ShowActivated = false,
            MinWidth = 280, MinHeight = 134,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true
        };
        window.UseTheme(Window.FontFamilyProperty, "Font");
        RoundedWindow.Apply(window, frame);
        foreach (var choice in new[] { ("Готово", "done"), ("На 10 мин", "snooze"), ("Закрыть", "dismiss") })
        {
            var button = new Button { Content = choice.Item1, Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(0, 0, 5, 0) };
            button.Click += (_, _) =>
            {
                try
                {
                    if (choice.Item2 == "dismiss") dismissed.Add(id);
                    else Update(store, id, choice.Item2);
                    window.Close();
                }
                catch (Exception error) { MessageBox.Show(window, error.Message, "Напоминание"); }
            };
            row.Children.Add(button);
        }
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
