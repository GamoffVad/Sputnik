using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sputnik;

internal sealed record PersonalDate(string Id, string Name, string Kind, int Month, int Day, int? Year,
    int Minute, int[] LeadDays, string LeapRule = "feb28")
{
    internal DateTime InYear(int year)
    {
        if (Month == 2 && Day == 29 && !DateTime.IsLeapYear(year))
            return LeapRule == "mar1" ? new DateTime(year, 3, 1) : new DateTime(year, 2, 28);
        return new DateTime(year, Month, Day);
    }
    internal DateTime Next(DateTime from)
    {
        var next = InYear(from.Year);
        return next.Date < from.Date ? InYear(from.Year + 1) : next;
    }
    internal int? Age(int year) => Year is { } start ? year - start : null;
}

internal sealed class PersonalDates(string directory)
{
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal PersonalDate[] Load()
    {
        var data = FeatureFiles.Array(directory, "dates.json"); Validate(data);
        return data.Deserialize<PersonalDate[]>(Json) ?? [];
    }
    internal void Save(PersonalDate value)
    {
        var all = Load().Where(item => item.Id != value.Id).Append(value).ToArray();
        var node = JsonSerializer.SerializeToNode(all, Json)!.AsArray(); Validate(node);
        var inbox=new NotificationInbox(directory).Load();
        foreach(var item in inbox.Where(item=>item?["id"]?.ToString().StartsWith("date:"+value.Id+":",StringComparison.Ordinal)==true && item["done"]?.GetValue<bool>()!=true).ToArray())inbox.Remove(item);
        FeatureFiles.Commit(directory,new Dictionary<string,string?>{["dates.json"]=node.ToJsonString(),["notifications.json"]=inbox.ToJsonString()});
        DataTransfer.SyncFor(System.IO.Path.Combine(directory, "dates.json"));
    }
    internal void Delete(string id)
    {
        var inbox=new NotificationInbox(directory).Load();
        foreach(var item in inbox.Where(item=>item?["id"]?.ToString().StartsWith("date:"+id+":",StringComparison.Ordinal)==true).ToArray())inbox.Remove(item);
        FeatureFiles.Commit(directory,new Dictionary<string,string?>{["dates.json"]=JsonSerializer.Serialize(Load().Where(item=>item.Id!=id),Json),["notifications.json"]=inbox.ToJsonString()});
        DataTransfer.SyncFor(System.IO.Path.Combine(directory, "dates.json"));
    }
    internal static void Validate(JsonArray data)
    {
        if (data.Count > 2000) throw new InvalidOperationException("Можно сохранить до 2000 памятных дат.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in data)
        {
            PersonalDate value;
            try { value = node!.Deserialize<PersonalDate>(Json)!; }
            catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
            { throw new InvalidOperationException("Некорректная памятная дата.", error); }
            if (value is null || string.IsNullOrWhiteSpace(value.Id) || value.Id.Length > 80 || !ids.Add(value.Id) ||
                string.IsNullOrWhiteSpace(value.Name) || value.Name.Length > 120 || value.Kind is not ("birthday" or "anniversary" or "date") ||
                value.Month is < 1 or > 12 || value.Day < 1 || value.Day > DateTime.DaysInMonth(2000, value.Month) ||
                value.Year is { } year && (year < 1800 || year > DateTime.Now.Year) || value.Minute is < 0 or > 1439 ||
                value.LeadDays is null || value.LeadDays.Length is < 1 or > 8 || value.LeadDays.Any(day => day is < 0 or > 366) ||
                value.LeapRule is not ("feb28" or "mar1")) throw new InvalidOperationException("Проверьте название, дату, год и время уведомления.");
        }
    }
    internal void Schedule(DateTimeOffset now)
    {
        var inbox = new NotificationInbox(directory);
        DateTime today = now.LocalDateTime.Date;
        foreach (var date in Load())
        foreach (int year in new[] { today.Year, today.Year + 1, today.Year + 2 })
        foreach (int lead in date.LeadDays.Distinct())
        {
            DateTime occurrence = date.InYear(year);
            DateTime local = occurrence.AddMinutes(date.Minute).AddDays(-lead);
            if (local.Date != today || local > now.LocalDateTime) continue;
            if (TimeZoneInfo.Local.IsInvalidTime(local)) local = local.AddHours(1);
            string label = date.Kind switch { "birthday" => "День рождения", "anniversary" => "Годовщина", _ => "Памятная дата" };
            string age = date.Age(year) is { } years ? $" · {years} лет" : "";
            string message = $"{date.Name}{age}" + (lead == 0 ? " · сегодня" : $" · через {lead} дн., {occurrence:dd.MM}");
            inbox.Enqueue($"date:{date.Id}:{year}:{lead}", label, message, new DateTimeOffset(local).ToUnixTimeMilliseconds(), "dates", date.Id);
        }
    }
}
