using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace Sputnik;

public partial class MainWindow : Window
{
    private readonly string data = App.DataDirectory;
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
    private string Store => Path.Combine(data, "workspace.json");
    private string Settings => Path.Combine(data, "model.json");
    private string Pending => Path.Combine(data, "preview");
    private string Pets => Path.Combine(data, "pets");
    private string Favicons => Path.Combine(data, "site-icons");
    private string section = "";
    private bool sending;

    public MainWindow()
    {
        InitializeComponent();
        RoundedWindow.Apply(this, Frame);
        Closed += (_, _) => http.Dispose();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            var source = e.OriginalSource as FrameworkElement;
            if (source is ComboBox { IsDropDownOpen: true } or DatePicker { IsDropDownOpen: true } ||
                source?.TemplatedParent is ComboBox { IsDropDownOpen: true } or DatePicker { IsDropDownOpen: true }) return;
            e.Handled = true;
            if (CloseEditor()) return;
            Hide();
        };
    }

    internal void OpenSection(string value)
    {
        section = value;
        OpenNativeSection(value);
    }

    internal void FocusSection() =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
            new Action(() => PanelHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First))));

    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (!e.Handled) DragMove();
    }

    private void HidePanel(object sender, RoutedEventArgs e) => Hide();

    internal async Task<string> SendChat(string text)
    {
        if (text.Length is < 1 or > 10000) throw new InvalidOperationException("Введите сообщение длиной до 10 000 символов.");
        if (sending) throw new InvalidOperationException("Дождитесь ответа на предыдущее сообщение.");
        sending = true;
        chatSendState?.Invoke();
        try
        {
            JsonArray history = AppendMessage("user", text);
            string reply = await Send(null, new JsonArray(history.Skip(Math.Max(0, history.Count - 40)).Select(message =>
            {
                var copy = message?.DeepClone();
                if (copy?["content"]?.ToString() is { Length: > 20000 } longMessage) copy["content"] = longMessage[..20000];
                return copy;
            }).ToArray()), false);
            AppendMessage("assistant", reply);
            return reply;
        }
        finally { sending = false; chatSendState?.Invoke(); }
    }

    private JsonArray AppendMessage(string role, string text)
    {
        var workspace = Read(Store) as JsonObject ?? new JsonObject();
        if (workspace["messages"] is not JsonArray messages)
        {
            messages = new JsonArray();
            workspace["messages"] = messages;
        }
        messages.Add(new JsonObject { ["role"] = role, ["content"] = text });
        Save(Store, workspace.ToJsonString());
        RefreshChatHistory(messages);
        return messages;
    }

    private string? Choose(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private static JsonNode? Read(string path) => File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : null;

    private static void Save(string path, string content)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, content, Encoding.UTF8);
        Compatibility.ReplaceFile(temporary, path);
    }

    internal static void OpenTarget(string kind, string target)
    {
        if (kind == "site")
        {
            if (!Uri.TryCreate(WebsiteAddress(target), UriKind.Absolute, out var website) || website.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("Браузер не может открыть этот адрес.");
            target = website.AbsoluteUri;
        }
        else if (kind != "app" || !Compatibility.IsFullyQualifiedPath(target) || !File.Exists(target) ||
                 !Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Выберите существующее приложение Windows (.exe).");

        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    private static string WebsiteAddress(string address)
    {
        address = address.Trim();
        if (address.StartsWith("//", StringComparison.Ordinal)) return "https:" + address;
        return address.IndexOf("://", StringComparison.Ordinal) >= 0 ? address : "https://" + address;
    }

    private static Uri HttpUri(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Укажите полный адрес HTTP или HTTPS без учётных данных.");
        return uri;
    }

    private void SaveModel(JsonNode payload)
    {
        var (uri, model, key) = Model(payload);
        string? secret = string.IsNullOrEmpty(key) ? null :
            Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
        Save(Settings, JsonSerializer.Serialize(new { url = uri.AbsoluteUri, model, secret }));
    }

    private (Uri, string, string?) Model(JsonNode? draft)
    {
        var saved = Read(Settings);
        string model = (draft?["model"] ?? saved?["model"])?.ToString().Trim() ?? "";
        if (model.Length == 0)
            throw new InvalidOperationException("Укажите базовый URL и ID модели в настройках.");
        var (uri, key) = ModelConnection(draft, saved);
        return (uri, model, key);
    }

    private static (Uri, string?) ModelConnection(JsonNode? draft, JsonNode? saved)
    {
        string url = (draft?["url"] ?? saved?["url"])?.ToString().Trim() ?? "";
        var uri = HttpUri(url);
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Базовый URL должен быть без параметров и фрагмента.");
        string? key = draft?["key"]?.ToString().Trim();
        bool sameProvider = saved?["url"]?.ToString() == uri.AbsoluteUri;
        if (string.IsNullOrEmpty(key) && sameProvider && draft?["clearKey"]?.GetValue<bool>() != true && saved?["secret"] is JsonValue encrypted)
            key = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted.ToString()), null, DataProtectionScope.CurrentUser));
        return (uri, key);
    }

    private sealed record ModelCatalog(string[] Models, string? Url);

    private async Task<ModelCatalog> ListModels(JsonNode? draft)
    {
        var (uri, key) = ModelConnection(draft, Read(Settings));
        var bases = uri.AbsolutePath == "/" ? new[] { uri, new Uri(uri, "v1") } : new[] { uri };
        foreach (var baseUri in bases)
        {
            bool claude = baseUri.GetLeftPart(UriPartial.Authority) == "https://api.anthropic.com" && baseUri.AbsolutePath.TrimEnd('/') == "/v1";
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(baseUri.AbsoluteUri.TrimEnd('/') + (claude ? "/models?limit=1000" : "/models")));
            if (claude) request.Headers.Add("anthropic-version", "2026-10-21");
            if (!string.IsNullOrEmpty(key))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var result = await http.SendAsync(request);
            if (!result.IsSuccessStatusCode)
            {
                if (result.StatusCode == System.Net.HttpStatusCode.NotFound && baseUri != bases[bases.Length - 1])
                    continue;
                throw new InvalidOperationException($"Список моделей недоступен (HTTP {(int)result.StatusCode}). Проверьте URL и ключ или укажите ID вручную.");
            }
            JsonNode? response;
            try { response = await result.Content.ReadFromJsonAsync<JsonNode>(); }
            catch (JsonException) { response = null; }
            var models = response is JsonObject catalog ? catalog["data"] as JsonArray : response as JsonArray;
            if (models is null) continue;
            var ids = models.OfType<JsonObject>()
                .Select(model => model["id"] is JsonValue value && value.TryGetValue<string>(out var id) ? id : null)
                .OfType<string>().Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal).ToArray();
            return new ModelCatalog(ids, baseUri == uri ? null : baseUri.AbsoluteUri);
        }
        throw new InvalidOperationException("Сервер не вернул каталог моделей в формате API. Проверьте базовый URL (например, http://localhost:11434/v1) или укажите ID вручную.");
    }

    private async Task<string> Send(JsonNode? draft, JsonArray messages, bool testing)
    {
        var (uri, model, key) = Model(draft);
        if (messages.Count is < 1 or > 40 || messages.Any(message => message?["role"]?.ToString() is not ("user" or "assistant") ||
            message?["content"] is null || message["content"]!.ToString().Length > 20000))
            throw new InvalidOperationException("Сообщение или история слишком длинные.");
        var endpoint = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/chat/completions");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrEmpty(key))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = JsonContent.Create(new { model, messages, stream = false });
        using var result = await http.SendAsync(request);
        if (!result.IsSuccessStatusCode)
            throw new InvalidOperationException($"Модель вернула HTTP {(int)result.StatusCode}. Проверьте URL, модель и ключ.");
        JsonNode? response;
        try { response = await result.Content.ReadFromJsonAsync<JsonNode>(); }
        catch (JsonException) { throw new InvalidOperationException("Сервер вернул ответ в формате, отличном от JSON. Проверьте базовый URL API."); }
        string? reply = response?["choices"]?[0]?["message"]?["content"]?.ToString();
        if (string.IsNullOrWhiteSpace(reply))
            throw new InvalidOperationException("Модель вернула пустой ответ.");
        return testing ? "Подключение работает" : reply!;
    }
}
