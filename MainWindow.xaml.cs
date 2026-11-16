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
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace Sputnik;

public partial class MainWindow : Window
{
    private const string Origin = "https://sputnik.local";
    private readonly string data = App.DataDirectory;
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
    private string Store => Path.Combine(data, "workspace.json");
    private string Settings => Path.Combine(data, "model.json");
    private string Pending => Path.Combine(data, "preview");
    private string Pets => Path.Combine(data, "pets");
    private string Favicons => Path.Combine(data, "site-icons");
    private string section = "quick";
    private bool ready;
    private bool sending;

    public MainWindow()
    {
        InitializeComponent();
        RoundedWindow.Apply(this, Frame);
        Loaded += InitializeBrowser;
        Closed += (_, _) => { View.Dispose(); http.Dispose(); };
    }

    internal void OpenSection(string value)
    {
        section = value;
        if (ready) View.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { section }));
    }

    internal void FocusSection()
    {
        FocusManager.SetFocusedElement(this, View);
        View.Focus();
    }

    private void DragHeader(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!e.Handled) DragMove();
    }

    private void HidePanel(object sender, RoutedEventArgs e) => Hide();

    private async void InitializeBrowser(object sender, RoutedEventArgs args)
    {
        Loaded -= InitializeBrowser;
        try
        {
            Directory.CreateDirectory(Pending);
            Directory.CreateDirectory(Pets);
            Directory.CreateDirectory(Favicons);
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(data, "webview"));
            await View.EnsureCoreWebView2Async(env);
            var browser = View.CoreWebView2;
            browser.SetVirtualHostNameToFolderMapping("sputnik.local", Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.DenyCors);
            browser.SetVirtualHostNameToFolderMapping("pets.sputnik.local", Pets, CoreWebView2HostResourceAccessKind.DenyCors);
            browser.SetVirtualHostNameToFolderMapping("preview.sputnik.local", Pending, CoreWebView2HostResourceAccessKind.DenyCors);
            // CSS masks load images using CORS. This mapping contains only cached PNG icons.
            browser.SetVirtualHostNameToFolderMapping("icons.sputnik.local", Favicons, CoreWebView2HostResourceAccessKind.Allow);
            browser.Settings.AreDefaultContextMenusEnabled = false;
            browser.Settings.AreDevToolsEnabled = false;
            browser.NavigationStarting += (_, e) => e.Cancel = !e.Uri.StartsWith(Origin + "/", StringComparison.Ordinal);
            browser.NewWindowRequested += (_, e) => e.Handled = true;
            browser.WebMessageReceived += OnMessage;
            browser.Navigate(Origin + "/index.html");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "Питомец и заметки продолжают работать. Для настроек и полного чата нужен WebView2. Запустите установщик Спутника повторно для восстановления WebView2.",
                "Спутник");
            Hide();
            Loaded += InitializeBrowser;
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Не удалось запустить Спутник");
            Hide();
            Loaded += InitializeBrowser;
        }
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (args.Source != Origin + "/index.html")
            return;

        string? id = null;
        try
        {
            var message = JsonNode.Parse(args.WebMessageAsJson)!;
            id = message["id"]?.GetValue<string>();
            var response = await Execute(message["action"]!.GetValue<string>(), message["payload"]);
            View.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok = true, value = response }));
        }
        catch (Exception error)
        {
            View.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok = false, error = error.Message }));
        }
    }

    private async Task<object?> Execute(string action, JsonNode? payload)
    {
        switch (action)
        {
            case "ready": ready = true; return section;
            case "openSection": (Application.Current as App)?.ShowWorkspace(payload!.GetValue<string>()); return null;
            case "desktopPlayback":
                (Application.Current as App)?.SetPlayback(payload!["command"]!.GetValue<string>(), payload["index"]?.GetValue<int>() ?? 0);
                return null;
            case "desktopMotions": return (Application.Current as App)?.AnimationNames();
            case "autoStart": return App.AutoStartEnabled();
            case "setAutoStart": App.SetAutoStart(payload!.GetValue<bool>()); return null;
            case "load": return Read(Store);
            case "siteIcon": return await new SiteIcons(Favicons).Find(payload?.ToString() ?? "");
            case "siteIconMask": return SiteIconMask.Ensure(Favicons, payload?.ToString() ?? "");
            case "appearanceFonts": return ThemePalette.InstalledFonts;
            case "hotkeys": return new
            {
                sections = DesktopControls.Sections.Select(section => new { title = section.Title, action = section.Action }),
                bindings = App.ReadHotkeys(),
            };
            case "setHotkey":
                (Application.Current as App)?.SetHotkey(payload!["section"]!.GetValue<string>(),
                    payload["modifiers"]!.GetValue<uint>(), payload["key"]!.GetValue<uint>());
                return null;
            case "shareChannels":
                return new NoteSharing(data).Load().Select(channel => new { name = channel.Name, template = channel.Template }).ToArray();
            case "saveShareChannels":
                new NoteSharing(data).Save(payload!.AsArray().Select(item => new NoteSharing.Channel(
                    item?["name"]?.ToString().Trim() ?? "", item?["template"]?.ToString().Trim() ?? "")).ToArray());
                return null;
            case "pets": return PetPackages.Installed(Pets);
            case "save":
                var workspace = payload!.AsObject();
                if (workspace["font"]?.ToString() is { } font &&
                    !ThemePalette.InstalledFonts.Contains(font, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Выберите установленный шрифт.");
                if (workspace["appearanceMode"]?.ToString() == "custom")
                {
                    var colors = workspace["customTheme"] as JsonObject;
                    foreach (string key in new[] { "accent", "background", "surface" })
                    {
                        string color = colors?[key]?.ToString() ?? "";
                        if (color.Length != 7 || color[0] != '#' || !color.Skip(1).All(Uri.IsHexDigit))
                            throw new InvalidOperationException("Укажите цвета в формате #RRGGBB.");
                    }
                }
                workspace["messages"] = Read(Store)?["messages"]?.DeepClone() ?? new JsonArray();
                string json = workspace.ToJsonString();
                if (json.Length > 8_000_000) throw new InvalidOperationException("Локальные данные слишком велики.");
                Save(Store, json);
                (Application.Current as App)?.RefreshPet();
                return null;
            case "selectPet":
                var archive = Choose("Пакет питомца (*.pet)|*.pet");
                return archive is null ? null : await Task.Run(() => PetPackages.Stage(archive, Pending, Pets));
            case "installPet": return PetPackages.Install(payload!.GetValue<string>(), Pending, Pets);
            case "pickApp": return Choose("Приложение Windows (*.exe)|*.exe");
            case "open":
                OpenTarget(payload?["kind"]?.ToString() ?? "", payload?["target"]?.ToString() ?? "");
                return null;
            case "getModel":
                var saved = Read(Settings);
                return new { url = saved?["url"]?.ToString(), model = saved?["model"]?.ToString(), hasKey = saved?["secret"] is not null };
            case "saveModel":
                SaveModel(payload!);
                return null;
            case "testModel": return await Send(payload, new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Ответь одним словом: OK." }), true);
            case "chat": return await SendChat(payload!.GetValue<string>());
            default: throw new InvalidOperationException("Неизвестное действие.");
        }
    }

    internal async Task<string> SendChat(string text)
    {
        if (text.Length is < 1 or > 10000) throw new InvalidOperationException("Введите сообщение длиной до 10 000 символов.");
        if (sending) throw new InvalidOperationException("Дождитесь ответа на предыдущее сообщение.");
        sending = true;
        try
        {
            JsonArray history = AppendMessage("user", text);
            string reply = await Send(null, new JsonArray(history.TakeLast(40).Select(message =>
            {
                var copy = message?.DeepClone();
                if (copy?["content"]?.ToString() is { Length: > 20000 } longMessage) copy["content"] = longMessage[..20000];
                return copy;
            }).ToArray()), false);
            AppendMessage("assistant", reply);
            return reply;
        }
        finally { sending = false; }
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
        if (ready) View.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { history = messages }));
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
        File.Move(temporary, path, true);
    }

    internal static void OpenTarget(string kind, string target)
    {
        if (kind == "site")
            HttpUri(target);
        else if (kind != "app" || !Path.IsPathFullyQualified(target) || !File.Exists(target) ||
                 !Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Выберите существующее приложение Windows (.exe).");

        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
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
        string url = (draft?["url"] ?? saved?["url"])?.ToString().Trim() ?? "";
        string model = (draft?["model"] ?? saved?["model"])?.ToString().Trim() ?? "";
        if (url.Length == 0 || model.Length == 0)
            throw new InvalidOperationException("Укажите базовый URL и ID модели в настройках.");
        var uri = HttpUri(url);
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Базовый URL должен быть без параметров и фрагмента.");
        string? key = draft?["key"]?.ToString().Trim();
        bool sameProvider = saved?["url"]?.ToString() == uri.AbsoluteUri;
        if (string.IsNullOrEmpty(key) && sameProvider && draft?["clearKey"]?.GetValue<bool>() != true && saved?["secret"] is JsonValue encrypted)
            key = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted.ToString()), null, DataProtectionScope.CurrentUser));
        return (uri, model, key);
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
        var response = await result.Content.ReadFromJsonAsync<JsonNode>();
        string? reply = response?["choices"]?[0]?["message"]?["content"]?.ToString();
        if (string.IsNullOrWhiteSpace(reply))
            throw new InvalidOperationException("Модель вернула пустой ответ.");
        return testing ? "Подключение работает" : reply;
    }
}
