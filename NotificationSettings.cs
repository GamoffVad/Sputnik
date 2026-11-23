using System.Text.Json.Nodes;

namespace Sputnik;

internal static class NotificationSettings
{
    internal static bool IsQuiet(string directory, DateTimeOffset now)
    {
        var preferences = FeatureFiles.Object(directory, "notification-settings.json");
        if (preferences["manual"]?.GetValue<bool>() == true &&
            (preferences["until"] is null || preferences["until"]!.GetValue<long>() > now.ToUnixTimeMilliseconds())) return true;
        if (preferences["schedule"]?.GetValue<bool>() != true) return false;
        int start = preferences["start"]?.GetValue<int>() ?? 1320, end = preferences["end"]?.GetValue<int>() ?? 540;
        if (start is < 0 or > 1439 || end is < 0 or > 1439) throw new InvalidOperationException("Неверное расписание тихих часов.");
        var days = (preferences["days"] as JsonArray)?.Select(v => v!.GetValue<int>()).ToArray() ?? [0,1,2,3,4,5,6];
        var local = now.LocalDateTime; int minute = local.Hour * 60 + local.Minute;
        if (start == end) return days.Contains((int)local.DayOfWeek);
        return start < end ? days.Contains((int)local.DayOfWeek) && minute >= start && minute < end
            : minute >= start && days.Contains((int)local.DayOfWeek) || minute < end && days.Contains((int)local.AddDays(-1).DayOfWeek);
    }
}

internal sealed class NotificationInbox(string directory)
{
    internal JsonArray Load() => FeatureFiles.Array(directory, "notifications.json");
    internal void Enqueue(string id, string title, string text, long due, string action = "", string target = "")
    {
        var all = Load(); if (all.Any(item => item?["id"]?.ToString() == id)) return;
        // Keep a full year of acknowledgements to avoid annual duplicates.
        long cutoff = DateTimeOffset.UtcNow.AddDays(-370).ToUnixTimeMilliseconds();
        foreach (var stale in all.Where(item => item?["done"]?.GetValue<bool>() == true && item["due"]!.GetValue<long>() < cutoff).ToArray()) all.Remove(stale);
        all.Add(new JsonObject { ["id"] = id, ["title"] = title, ["text"] = text, ["due"] = due, ["done"] = false, ["action"] = action, ["target"] = target });
        FeatureFiles.Write(directory, "notifications.json", all);
    }
    internal void Finish(string id, bool snooze = false)
    {
        var all = Load(); var item = all.FirstOrDefault(item => item?["id"]?.ToString() == id);
        if (item is null) return;
        if (snooze) item["due"] = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds(); else item["done"] = true;
        FeatureFiles.Write(directory, "notifications.json", all);
    }
    internal void RemovePrefix(string prefix)
    {
        var all = Load();
        foreach (var item in all.Where(item => item?["id"]?.ToString().StartsWith(prefix, StringComparison.Ordinal) == true).ToArray()) all.Remove(item);
        FeatureFiles.Write(directory, "notifications.json", all);
    }
}
