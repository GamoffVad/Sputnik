using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Sputnik;

internal sealed class FeatureRuntime : IDisposable
{
    private readonly string directory;
    private readonly Action<string, string> open;
    private readonly Action<Window> position;
    private readonly Func<bool> blocked;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private Window? notice;
    private string? noticeId;
    private readonly HashSet<string> dismissed = new(StringComparer.Ordinal);
    private DateTime lastError;
    private DateTime nextSync, nextWeather;
    private bool weatherBusy;
    internal Func<Task>? Synchronize;
    internal bool HasNotice => notice is not null;
    internal void CloseNotice() => notice?.Close();
    internal FeatureRuntime(string directory, Action<string, string> open, Action<Window> position, Func<bool> blocked)
    {
        this.directory = directory; this.open = open; this.position = position; this.blocked = blocked;
        timer.Tick += (_, _) => Tick(); timer.Start();
    }
    internal void Tick()
    {
        try
        {
            var now = DateTimeOffset.Now;
            new FocusClock(directory).Tick(now);
            if(DateTime.UtcNow>=nextSync && Synchronize is not null){nextSync=DateTime.UtcNow.AddMinutes(1);_ = Sync();}
            if(DateTime.UtcNow>=nextWeather && !weatherBusy){nextWeather=DateTime.UtcNow.AddMinutes(30);_ = Weather();}
            new PersonalDates(directory).Schedule(now);
            if(notice is not null && !new NotificationInbox(directory).Load().Any(i=>i?["id"]?.ToString()==noticeId))notice.Close();
            if (notice is not null || blocked() || NotificationSettings.IsQuiet(directory, now)) return;
            var item = new NotificationInbox(directory).Load().OfType<JsonObject>()
                .Where(item => !dismissed.Contains(item["id"]!.ToString()) && item["done"]?.GetValue<bool>() != true && item["due"]!.GetValue<long>() <= now.ToUnixTimeMilliseconds())
                .OrderBy(item => item["due"]!.GetValue<long>()).FirstOrDefault();
            if (item is not null) Show(item);
        }
        catch (Exception error)
        {
            if (DateTime.UtcNow - lastError > TimeSpan.FromMinutes(10)) { App.WriteDiagnostic("personal-tools", error.ToString()); lastError = DateTime.UtcNow; }
        }
    }
    private async Task Sync(){try{if(Synchronize is not null)await Synchronize();}catch(Exception error){App.WriteDiagnostic("folder-sync",error.Message);}}
    private async Task Weather(){weatherBusy=true;try{await new WeatherModule(directory).Read();}catch(Exception error){App.WriteDiagnostic("weather",error.Message);}finally{weatherBusy=false;}}
    private void Show(JsonObject item)
    {
        string id = item["id"]!.ToString();
        var window = new NotificationWindow(item["title"]?.ToString() ?? "Уведомление",
            item["text"]?.ToString() ?? "", null, command =>
            {
                if (command == "dismiss") dismissed.Add(id);
                else new NotificationInbox(directory).Finish(id, command == "snooze");
            }, item["action"]?.ToString() is { Length: > 0 } action ? () =>
            {
                open(action, item["target"]?.ToString() ?? "");
                new NotificationInbox(directory).Finish(id);
                notice?.Close();
            } : null, "Открыть");
        window.Closed += (_, _) => {notice = null;noticeId=null;};
        notice = window;noticeId=id; position(window); window.Show();
    }
    public void Dispose() { timer.Stop(); notice?.Close(); }
}
