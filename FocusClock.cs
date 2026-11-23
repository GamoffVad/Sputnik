using System.Text.Json.Nodes;

namespace Sputnik;

internal sealed class FocusClock(string directory)
{
    internal JsonObject Load()
    {
        var value = FeatureFiles.Object(directory, "focus.json");
        value["phase"] ??= "work"; value["work"] ??= 25; value["short"] ??= 5; value["long"] ??= 15; value["every"] ??= 4;
        value["completed"] ??= 0; value["running"] ??= false; value["remaining"] ??= value["work"]!.GetValue<int>() * 60000L;
        foreach (string key in new[] { "work", "short", "long", "every" }) if (value[key]!.GetValue<int>() is < 1 or > 240) throw new InvalidOperationException("Неверная настройка таймера фокуса.");
        if (value["phase"]!.ToString() is not ("work" or "short" or "long")) throw new InvalidOperationException("Неверный этап таймера.");
        return value;
    }
    internal long Remaining(DateTimeOffset now)
    {
        var value = Load(); return Math.Max(0, value["running"]!.GetValue<bool>() ? value["due"]!.GetValue<long>() - now.ToUnixTimeMilliseconds() : value["remaining"]!.GetValue<long>());
    }
    internal void Start(DateTimeOffset now, int minutes = 0)
    {
        var value = Load();
        if (minutes != 0)
        {
            if (minutes is < 1 or > 240) throw new InvalidOperationException("Фокус — от 1 до 240 минут.");
            value["phase"] = "work"; value["remaining"] = minutes * 60000L;
        }
        else if (value["running"]!.GetValue<bool>()) return;
        value["due"] = now.ToUnixTimeMilliseconds() + Math.Max(1000, value["remaining"]!.GetValue<long>());
        value["cycle"] = Guid.NewGuid().ToString("N"); value["running"] = true;
        Save(value);
    }
    internal void Pause(DateTimeOffset now)
    {
        var value = Load(); if (!value["running"]!.GetValue<bool>()) return;
        value["remaining"] = Math.Max(0, value["due"]!.GetValue<long>() - now.ToUnixTimeMilliseconds()); value["running"] = false; Save(value);
    }
    internal void Reset()
    {
        var value = Load(); value["phase"] = "work"; value["running"] = false; value["completed"] = 0;
        value["remaining"] = value["work"]!.GetValue<int>() * 60000L; value.Remove("due"); Save(value);
    }
    internal void Configure(int work, int rest, int longer, int every, bool auto)
    {
        if (new[]{work,rest,longer,every}.Any(n => n is < 1 or > 240)) throw new InvalidOperationException("Укажите целые значения от 1 до 240.");
        var value = Load(); if (value["running"]!.GetValue<bool>()) throw new InvalidOperationException("Приостановите таймер перед изменением настроек.");
        value["work"] = work; value["short"] = rest; value["long"] = longer; value["every"] = every; value["auto"] = auto;
        value["remaining"] = value[value["phase"]!.ToString()]!.GetValue<int>() * 60000L; Save(value);
    }
    internal void Tick(DateTimeOffset now)
    {
        var value = Load(); if (!value["running"]!.GetValue<bool>() || value["due"]!.GetValue<long>() > now.ToUnixTimeMilliseconds()) return;
        string phase = value["phase"]!.ToString();
        // Inbox first: a crash between these writes retries the same stable notification ID.
        new NotificationInbox(directory).Enqueue("focus:" + value["cycle"], "Фокус", phase == "work" ? "Рабочий интервал завершён. Время отдохнуть." : "Перерыв завершён. Можно возвращаться к работе.", value["due"]!.GetValue<long>(), "focus");
        if (phase == "work") value["completed"] = value["completed"]!.GetValue<int>() + 1;
        string next = phase == "work" ? value["completed"]!.GetValue<int>() % value["every"]!.GetValue<int>() == 0 ? "long" : "short" : "work";
        value["phase"] = next; value["remaining"] = value[next]!.GetValue<int>() * 60000L;
        value["running"] = value["auto"]?.GetValue<bool>() == true;
        if (value["running"]!.GetValue<bool>()) { value["due"] = now.ToUnixTimeMilliseconds() + value["remaining"]!.GetValue<long>(); value["cycle"] = Guid.NewGuid().ToString("N"); }
        Save(value);
    }
    private void Save(JsonObject value) => FeatureFiles.Write(directory, "focus.json", value);
}
