using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class Check
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Assembly Assembly = typeof(Sputnik.App).Assembly;
    static Type T(string name) => Assembly.GetType("Sputnik." + name)!;
    static object? Call(object target, string name, params object?[] args) =>
        (target as Type ?? target.GetType()).GetMethod(name, Flags)!.Invoke(target is Type ? null : target, args);
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static IEnumerable<TNode> Find<TNode>(DependencyObject root) where TNode : DependencyObject
    {
        if (root is TNode match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Find<TNode>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    static void Layout(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }
    static void Render(Window window, string path)
    {
        Layout(window);
        var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * 2),
            (int)Math.Ceiling(root.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
    static Button Button(Window window, string content) => Find<Button>(window).Single(b => Equals(b.Content, content));
    static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    [STAThread]
    static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        string data = Path.GetFullPath(args[0]); Directory.CreateDirectory(data);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Sputnik;component/Components/Controls.xaml") });
        Call(T("ThemePalette"), "Fill", app.Resources, Activator.CreateInstance(T("PetTheme"), "#CBAA83", "#27292B", "#1D1F21"));
        app.Resources["ThemeFont"] = new FontFamily("Segoe UI");
        var host = new Window { Left = 500, Top = 500, Width = 230, Height = 230,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        host.Show();
        var controls = Activator.CreateInstance(T("DesktopControls"), host, new Action<string>(_ => { }), new Action(() => { }))!;
        object Scheduler()
        {
            return Activator.CreateInstance(T("ReminderScheduler"), Flags, null,
                new object[] { data, new Action<Window>(w => Call(controls, "PositionPanel", w, "reminders")) }, null)!;
        }
        JsonObject Reminder(string title) => new() { ["id"] = "qa", ["title"] = title,
            ["due"] = DateTimeOffset.Now.AddMinutes(-1).ToUnixTimeMilliseconds(), ["repeat"] = "none", ["done"] = false };
        string store = Path.Combine(data, "workspace.json");
        void Write(JsonObject reminder) => File.WriteAllText(store, new JsonObject { ["reminders"] = new JsonArray(reminder.DeepClone()) }.ToJsonString());
        JsonNode Saved() => JsonNode.Parse(File.ReadAllText(store))!["reminders"]![0]!;
        Window Notice(object scheduler, JsonObject reminder)
        {
            Call(scheduler, "Show", reminder);
            var notice = (Window)T("ReminderScheduler").GetField("notice", Flags)!.GetValue(scheduler)!;
            Layout(notice);
            return notice;
        }
        try
        {
            using var scheduler = (IDisposable)Scheduler();
            var reminder = Reminder("7777"); Write(reminder);
            var shortNotice = Notice(scheduler, reminder);
            Assert(shortNotice.Width == 360 && shortNotice.Height is > 130 and < 230, "Notification expanded to settings size");
            foreach (var button in Find<Button>(shortNotice))
            {
                var bounds = button.TransformToAncestor(shortNotice).TransformBounds(new Rect(button.RenderSize));
                Assert(bounds.Top >= 0 && bounds.Bottom <= shortNotice.ActualHeight, "Action is clipped");
            }
            var closeButton = Find<Button>(shortNotice).Single(b => AutomationProperties.GetName(b).StartsWith("Закрыть уведомление"));
            var closeIcon = (FrameworkElement)closeButton.Content;
            Assert(closeButton.Padding == new Thickness(0) && closeIcon.Width == 12 && closeIcon.Height == 12,
                "Close glyph size or padding is wrong");
            var iconBounds = closeIcon.TransformToAncestor(closeButton).TransformBounds(new Rect(closeIcon.RenderSize));
            Assert(Math.Abs(iconBounds.Left + iconBounds.Width / 2 - closeButton.ActualWidth / 2) < 1
                && Math.Abs(iconBounds.Top + iconBounds.Height / 2 - closeButton.ActualHeight / 2) < 1, "Close glyph is not centered");
            Render(shortNotice, Path.Combine(data, "reminder-short.png"));
            Click(Button(shortNotice, "На 10 мин"));
            Assert(Saved()["due"]!.GetValue<long>() > DateTimeOffset.Now.AddMinutes(9).ToUnixTimeMilliseconds(), "Snooze did not save");
            reminder = Reminder("Позвонить в мастерскую и согласовать время доставки заказа на следующую неделю"); Write(reminder);
            var longNotice = Notice(scheduler, reminder);
            Assert(longNotice.Height > shortNotice.Height && longNotice.Height <= 360, "Long text sizing failed");
            Render(longNotice, Path.Combine(data, "reminder-long.png"));
            Click(Button(longNotice, "Готово"));
            Assert(Saved()["done"]!.GetValue<bool>(), "Done did not save");
            reminder = Reminder("Dismiss without completing"); Write(reminder);
            var dismissNotice = Notice(scheduler, reminder);
            Click(Find<Button>(dismissNotice).Single(b => AutomationProperties.GetName(b).StartsWith("Закрыть уведомление")));
            Assert(!Saved()["done"]!.GetValue<bool>(), "Dismiss completed the reminder");
            Call(scheduler, "Check");
            Assert(T("ReminderScheduler").GetField("notice", Flags)!.GetValue(scheduler) is null, "Dismissed reminder immediately reopened");
            int opened = 0;
            var overflow = (Window)Activator.CreateInstance(T("NotificationWindow"), Flags, null,
                new object?[] { "Напоминание", string.Join(" ", Enumerable.Repeat("Длинное напоминание", 80)), "23 ноября · 12:00",
                    new Action<string>(_ => throw new IOException("Не удалось сохранить. Повторите действие.")),
                    new Action(() => opened++), "Открыть заметку" }, null)!;
            Call(controls, "PositionPanel", overflow, "reminders"); overflow.Show(); Layout(overflow);
            Click(Button(overflow, "Открыть заметку")); Assert(opened == 1 && overflow.IsVisible, "Linked note action changed");
            Click(Button(overflow, "Готово")); Layout(overflow);
            Assert(overflow.IsVisible && Find<TextBlock>(overflow).Any(t => t.Text.StartsWith("Не удалось сохранить")), "Save failure was hidden");
            foreach (string caption in new[] { "Готово", "На 10 мин" })
            {
                var bounds = Button(overflow, caption).TransformToAncestor(overflow).TransformBounds(new Rect(Button(overflow, caption).RenderSize));
                Assert(bounds.Bottom <= overflow.ActualHeight && overflow.Height <= 360, "Overflow or error clipped action");
            }
            overflow.Close();
            Call(controls, "ShowMenu", "Праздничная ёлка", false);
            var menu = (Window)T("DesktopControls").GetField("menu", Flags)!.GetValue(controls)!;
            Layout(menu);
            string version = T("AppVersion").GetProperty("Current", Flags)!.GetValue(null)!.ToString()!;
            var versionLabel = Find<TextBlock>(menu).Single(t => t.Text == "Спутник · " + version);
            var parsedVersion = Version.Parse(version);
            Assert(new Version(parsedVersion.Major, parsedVersion.Minor, parsedVersion.Build, Math.Max(0, parsedVersion.Revision)) == Assembly.GetName().Version
                && versionLabel.FontSize == 11, "Menu version is missing or wrong");
            Assert(versionLabel.TransformToAncestor(menu).Transform(new Point()).Y < 30, "Version is not at the top");
            Render(menu, Path.Combine(data, "context-menu.png"));
            var besideMenu = Notice(scheduler, Reminder("Проверить напоминание рядом с открытым меню"));
            Assert(besideMenu.Width == 360 && besideMenu.Height < 280, "Menu placement expanded notification");
            besideMenu.Close();
            if (args.Length > 1)
            {
                var preview = (JsonObject)Call(T("PetPackages"), "Stage", Path.GetFullPath(args[1]),
                    Path.Combine(data, "pending-pets"), Path.Combine(data, "pets"))!;
                Assert(preview["id"]!.ToString() == "christmas-tree" && preview["animations"]!.AsArray().Count == 13,
                    "Large pet package import failed");
                Call(T("PetPackages"), "Install", preview["token"]!.ToString(), Path.Combine(data, "pending-pets"), Path.Combine(data, "pets"));
                Console.WriteLine("PASS large Christmas Tree package Stage/Install, 13 animations; process64=" + Environment.Is64BitProcess);
            }
            Console.WriteLine("PASS compact placement, long/overflow text, visible actions, snooze, done, dismiss, linked note, save error, menu version " + version);
        }
        finally { ((IDisposable)controls).Dispose(); host.Close(); app.Shutdown(); }
    }
}
