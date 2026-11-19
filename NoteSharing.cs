using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Documents;

namespace Sputnik;

internal sealed class NoteSharing(string directory)
{
    internal sealed record Channel(string Name, string Template);

    private string FilePath => Path.Combine(directory, "note-channels.json");
    private const string Telegram = "https://t.me/share/url?url=%E2%80%8B&text={text}";
    private const string Max = "https://web.max.ru/:share?text={text}";
    private static readonly Channel[] Defaults =
    [
        new("Почта", "mailto:?subject={title}&body={text}"),
        new("Telegram", Telegram),
        new("MAX", Max),
    ];

    public Channel[] Load() => File.Exists(FilePath)
        ? (JsonSerializer.Deserialize<Channel[]>(File.ReadAllText(FilePath)) ?? []).Select(CurrentTemplate).ToArray()
        : Defaults;

    private static Channel CurrentTemplate(Channel channel) => channel.Template switch
    {
        "https://t.me/share/url?url=&text={text}" => channel with { Template = Telegram },
        "https://max.ru/:share?text={text}" => channel with { Template = Max },
        _ => channel,
    };

    public void Save(Channel[] channels)
    {
        if (channels.Length > 30) throw new InvalidOperationException("Можно сохранить до 30 каналов.");
        foreach (var channel in channels)
        {
            if (channel.Name.Length is < 1 or > 40 || channel.Template.Length > 1800)
                throw new InvalidOperationException("Проверьте название и адрес канала.");
            BuildUri(channel, "Заметка", "Текст");
        }
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(channels));
        Compatibility.ReplaceFile(temporary, FilePath);
    }

    public static string? Open(Channel channel, FlowDocument document)
    {
        string text = Text(document);
        byte[][] images = NoteAttachments.Images(document);
        if (images.Length > 0)
        {
            string[] files = NoteAttachments.Export(images, Path.Combine(App.DataDirectory, "shared-notes"));
            var uri = new Uri(BuildUri(CurrentTemplate(channel), "Заметка из «Спутника»", text));
            bool telegram = uri.Host.Equals("t.me", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.TrimEnd('/') == "/share/url";
            if (telegram && NoteAttachments.TryTelegram(files))
            {
                if (text.Length > 0) System.Windows.Clipboard.SetText(text);
                return text.Length == 0 ? "Фото переданы в Telegram. Выберите получателя и подтвердите отправку." :
                    "Фото переданы в Telegram. Выберите получателя. Текст заметки скопирован — вставьте его в подпись через Ctrl+V.";
            }
            // A web share link cannot carry local files. Keep the images available
            // for paste/attachment instead of silently dropping them.
            System.Windows.Clipboard.SetDataObject(NoteAttachments.PhotoClipboard(files), true);
            var (photoTarget, photoFallback) = Targets(channel, text);
            try { Launch(photoTarget); }
            catch (Win32Exception error) when (photoFallback is not null && error.NativeErrorCode is (1155 or 2 or 3)) { Launch(photoFallback); }
            return "Фото скопированы. Выберите чат и нажмите Ctrl+V. Если вставка недоступна, прикрепите PNG из «Файлы фото».";
        }
        if (text.Length == 0) throw new InvalidOperationException("Добавьте текст в заметку.");
        var (target, fallback) = Targets(channel, text);
        try { Launch(target); }
        catch (Win32Exception error) when (fallback is not null && error.NativeErrorCode is (1155 or 2 or 3))
        {
            Launch(fallback);
        }
        return null;
    }

    internal static (string Primary, string? Fallback) Targets(Channel channel, string text)
    {
        string target = BuildUri(CurrentTemplate(channel), "Заметка из «Спутника»", text);
        var uri = new Uri(target);
        if (uri.Host.Equals("t.me", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.TrimEnd('/') == "/share/url")
        {
            // Telegram Desktop rejects an empty url parameter before opening the recipient picker.
            string query = uri.Query.Replace("url=&", "url=%E2%80%8B&");
            return ("tg://msg_url" + query, uri.GetLeftPart(UriPartial.Path) + query);
        }
        return (target, null);
    }

    private static void Launch(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    public static string Text(FlowDocument document)
    {
        var lines = new List<string>();
        // TextRange on a paragraph inside a WPF List includes the generated
        // marker. Read its inlines so our explicit list prefix appears once.
        string InlineText(InlineCollection inlines) => string.Concat(inlines.Select(inline => inline switch
        {
            Run run => run.Text,
            Span span => InlineText(span.Inlines),
            LineBreak => Environment.NewLine,
            _ => "",
        }));
        void Read(BlockCollection blocks, string prefix)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case Paragraph paragraph:
                        string check = paragraph.Inlines.FirstInline is InlineUIContainer { Child: System.Windows.Controls.CheckBox box }
                            ? box.IsChecked == true ? "[x] " : "[ ] " : "";
                        lines.Add(prefix + check + InlineText(paragraph.Inlines));
                        break;
                    case List list:
                        int index = 1;
                        foreach (var item in list.ListItems)
                            Read(item.Blocks, list.MarkerStyle == System.Windows.TextMarkerStyle.Decimal ? $"{index++}. " : "• ");
                        break;
                    case Section section:
                        Read(section.Blocks, prefix);
                        break;
                }
            }
        }
        Read(document.Blocks, "");
        return string.Join(Environment.NewLine, lines).Trim();
    }

    internal static string BuildUri(Channel channel, string title, string text)
    {
        if (!channel.Template.Contains("{text}"))
            throw new InvalidOperationException("Добавьте {text} в параметр текста ссылки.");
        string target = channel.Template.Replace("{title}", Uri.EscapeDataString(title))
            .Replace("{text}", Uri.EscapeDataString(text));
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "mailto") || uri.UserInfo.Length != 0 ||
            (uri.Scheme == "https" && channel.Template.IndexOf('?') is var query && (query < 0 || !channel.Template[(query + 1)..].Contains("{text}"))))
            throw new InvalidOperationException("Используйте ссылку HTTPS с параметром текста или адрес mailto.");
        return target;
    }
}
