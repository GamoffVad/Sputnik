using System.Globalization;
using System.Text.RegularExpressions;

namespace Sputnik;

internal sealed record QuickCommand(string Kind, string Text, long Due = 0, int Minutes = 0)
{
    internal string Preview => Kind switch
    {
        "note" => "Создать заметку:\n" + Text,
        "reminder" => "Напомнить " + DateTimeOffset.FromUnixTimeMilliseconds(Due).LocalDateTime.ToString("dd.MM.yyyy в HH:mm") + ":\n" + Text,
        _ => $"Запустить фокус на {Minutes} мин."
    };
    internal static QuickCommand Parse(string text, DateTimeOffset now)
    {
        text = text.Trim();
        if (text.Length is < 1 or > 10000) throw new InvalidOperationException("Введите команду длиной до 10 000 символов.");
        var note = Regex.Match(text, @"^заметка\s*:?\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (note.Success) return new("note", note.Groups[1].Value.Trim());
        var timer = Regex.Match(text, @"^(?:таймер|фокус)(?:\s+на)?\s+(\d+)\s+(минут\w*|час\w*)$", RegexOptions.IgnoreCase);
        if (timer.Success) return new("focus", "", Minutes: Duration(timer.Groups[1].Value, timer.Groups[2].Value, 240));
        var relative = Regex.Match(text, @"^напомни(?:ть)?\s+через\s+(\d+)\s+(минут\w*|час\w*)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (relative.Success) return Reminder(relative.Groups[3].Value, now.AddMinutes(Duration(relative.Groups[1].Value, relative.Groups[2].Value, 525600)));
        var dated = Regex.Match(text, @"^напомни(?:ть)?\s+(сегодня|завтра|послезавтра|\d{1,2}\.\d{1,2}\.\d{4})\s+в\s+(\d{1,2})(?::(\d{2}))?\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (dated.Success)
        {
            string day = dated.Groups[1].Value.ToLowerInvariant();
            DateTime date = day switch { "сегодня" => now.LocalDateTime.Date, "завтра" => now.LocalDateTime.Date.AddDays(1), "послезавтра" => now.LocalDateTime.Date.AddDays(2),
                _ => DateTime.TryParseExact(day, new[]{"d.M.yyyy","dd.MM.yyyy","d.MM.yyyy","dd.M.yyyy"}, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : throw new InvalidOperationException("Такой даты нет.") };
            int hour = int.Parse(dated.Groups[2].Value), minute = dated.Groups[3].Success ? int.Parse(dated.Groups[3].Value) : 0;
            if (hour > 23 || minute > 59) throw new InvalidOperationException("Укажите время от 00:00 до 23:59.");
            var local = date.AddHours(hour).AddMinutes(minute);
            if (TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local)) throw new InvalidOperationException("Это время неоднозначно в текущем часовом поясе. Выберите другое время.");
            var due = new DateTimeOffset(local);
            if (due <= now) throw new InvalidOperationException("Это время уже прошло. Укажите будущую дату.");
            return Reminder(dated.Groups[4].Value, due);
        }
        throw new InvalidOperationException("Не удалось однозначно понять команду. Укажите дату и время, например: «напомни завтра в 10:00 позвонить».");
    }
    private static int Duration(string count, string unit, int maximum)
    {
        if (!int.TryParse(count, out int value) || value < 1 || value > maximum || (long)value * (unit.StartsWith("час", StringComparison.OrdinalIgnoreCase) ? 60 : 1) > maximum)
            throw new InvalidOperationException($"Продолжительность должна быть от 1 до {maximum} минут.");
        return value * (unit.StartsWith("час", StringComparison.OrdinalIgnoreCase) ? 60 : 1);
    }
    private static QuickCommand Reminder(string title, DateTimeOffset due)
    {
        title = title.Trim(); if (title.Length is < 1 or > 120) throw new InvalidOperationException("Название напоминания должно содержать от 1 до 120 символов.");
        return new("reminder", title, due.ToUnixTimeMilliseconds());
    }
}
