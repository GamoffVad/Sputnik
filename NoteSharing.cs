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
    private static readonly Channel[] Defaults =
    [
        new("Почта", "mailto:?subject={title}&body={text}"),
        new("Telegram", "https://t.me/share/url?url=&text={text}"),
        new("MAX", "https://max.ru/:share?text={text}"),
    ];

    public Channel[] Load() => File.Exists(FilePath)
        ? JsonSerializer.Deserialize<Channel[]>(File.ReadAllText(FilePath)) ?? []
        : Defaults;

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

    public static void Open(Channel channel, FlowDocument document)
    {
        string text = Text(document);
        if (text.Length == 0) throw new InvalidOperationException("Добавьте текст в заметку.");
        string target = BuildUri(channel, "Заметка из «Спутника»", text);
        var uri = new Uri(target);
        if (uri.Host.Equals("t.me", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.TrimEnd('/') == "/share/url")
        {
            // Telegram Desktop rejects an empty url parameter before opening the recipient picker.
            string query = uri.Query.Replace("url=&", "url=%E2%80%8B&");
            try
            {
                Launch("tg://msg_url" + query);
                return;
            }
            catch (Win32Exception error) when (error.NativeErrorCode is 1155 or 2 or 3) { }
            target = uri.GetLeftPart(UriPartial.Path) + query;
        }
        Launch(target);
    }

    private static void Launch(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    public static string Text(FlowDocument document)
    {
        var lines = new List<string>();
        void Read(BlockCollection blocks, string prefix)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case Paragraph paragraph:
                        string check = paragraph.Inlines.FirstInline is InlineUIContainer { Child: System.Windows.Controls.CheckBox box }
                            ? box.IsChecked == true ? "[x] " : "[ ] " : "";
                        lines.Add(prefix + check + new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Replace("\uFFFC", ""));
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
