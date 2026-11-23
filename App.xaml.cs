using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Sputnik;

public partial class App : Application
{
    private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Preference = "desktop-only-autostart-off";
    private Mutex? instance;
    private EventWaitHandle? showRequest;
    private RegisteredWaitHandle? showWait;
    private EventWaitHandle? updateRequest;
    private RegisteredWaitHandle? updateWait;
    private MainWindow? workspace;
    private DesktopPetWindow? pet;
    private StickyNotes? notes;
    private QuickChatWindow? quickChat;
    private DesktopHotkeys? hotkeys;
    private ReminderScheduler? reminders;
    private FeatureRuntime? features;
    private bool syncing;
    private static string HotkeysFile => Path.Combine(DataDirectory, "hotkeys.json");
    private bool exiting;

    internal static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sputnik");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool desktopOnly = e.Args.Contains("--desktop-only");
        showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Sputnik.OpenWorkspace");
        instance = new Mutex(true, @"Local\Sputnik.Desktop", out bool first);
        if (!first)
        {
            if (!desktopOnly) showRequest.Set();
            Shutdown();
            return;
        }

        // Keep a local stack trace for any future unexpected failure; do not
        // suppress exceptions or continue with a potentially inconsistent UI.
        DispatcherUnhandledException += (_, error) => WriteDiagnostic("unhandled-ui", error.Exception.ToString());
        AppDomain.CurrentDomain.UnhandledException += (_, error) => WriteDiagnostic("unhandled", error.ExceptionObject?.ToString() ?? "Unknown exception");

        updateRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Sputnik.CloseForUpdate");
        updateWait = ThreadPool.RegisterWaitForSingleObject(updateRequest, (_, _) =>
            Dispatcher.BeginInvoke(ExitApp), null, Timeout.Infinite, true);
        try
        {
            StartDesktop();
            showWait = ThreadPool.RegisterWaitForSingleObject(showRequest, (_, _) =>
                Dispatcher.BeginInvoke(() => ShowWorkspace("quick")), null, Timeout.Infinite, false);
        }
        catch (Exception error)
        {
            string message = "Не удалось запустить «Спутник». " + error.Message;
            string? log = WriteDiagnostic("startup", error.ToString());
            if (log is not null) message += $"\n\nЖурнал: {log}";
            MessageBox.Show(message, "Спутник", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    internal static string? WriteDiagnostic(string stage, string details)
    {
        try
        {
            var folder = Path.Combine(DataDirectory, "logs");
            Directory.CreateDirectory(folder);
            var log = Path.Combine(folder, $"{stage}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
            File.WriteAllText(log, $"{DateTimeOffset.Now:O}{Environment.NewLine}{details}");
            return log;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void StartDesktop()
    {
        Directory.CreateDirectory(DataDirectory);
        new ChatHistory(DataDirectory).Load();
        DataTransfer.SyncAll(DataDirectory);
        RefreshWindowMaterial();
        pet = new DesktopPetWindow(ShowWorkspace, ExitApp);
        pet.UpdateFromStore();
        pet.Show();
        _ = RefreshSiteIcons();
        reminders = new ReminderScheduler(DataDirectory, window => pet?.PositionPanel(window, "reminders"));
        reminders.OpenNote = id => Notes().OpenById(id);
        reminders.Blocked = () => features?.HasNotice == true;
        features = new FeatureRuntime(DataDirectory, (section, id) =>
        {
            if (section == "note") Notes().OpenById(id); else ShowWorkspace(section);
        }, window => pet?.PositionPanel(window, "reminders"), () => reminders.HasNotice);
        features.Synchronize = async () => { await SyncNow(); };
        hotkeys = new DesktopHotkeys(ShowWorkspace);
        hotkeys.Attach(new WindowInteropHelper(pet).Handle);
        hotkeys.Restore(ReadHotkeys());
        if (!File.Exists(Path.Combine(DataDirectory, Preference)))
        {
            try { SetAutoStart(true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                MessageBox.Show("Не удалось настроить запуск Спутника при входе в Windows. " + error.Message, "Спутник");
            }
        }
    }

    private async Task RefreshSiteIcons()
    {
        try
        {
            string path = Path.Combine(DataDirectory, "workspace.json");
            if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path))?["favorites"] is not JsonArray favorites) return;
            var icons = new SiteIcons(Path.Combine(DataDirectory, "site-icons"));
            foreach (var favorite in favorites.OfType<JsonObject>().Where(item => item["kind"]?.ToString() == "site" &&
                item["iconSource"]?.ToString() == "site" && SiteIcons.IsCachedName(item["siteIcon"]?.ToString())))
            {
                if (exiting) return;
                await icons.Refresh(favorite["target"]?.ToString() ?? "");
            }
            if (!exiting) pet?.RefreshFavoriteIcons();
        }
        catch (Exception error) when (error is IOException or System.Net.Http.HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
        { WriteDiagnostic("favorite-icon-refresh", error.GetType().Name); }
    }

    internal void ShowWorkspace(string section)
    {
        if (exiting) return;
        if (section == "notes")
        {
            workspace?.Hide();
            var manager = Notes();
            manager.Toggle();
            pet?.SetNotesChecked(manager.IsVisible);
            return;
        }
        if (section == "quick")
        {
            workspace?.Hide();
            if (quickChat is null)
            {
                quickChat = new QuickChatWindow(window => pet?.PositionBubble(window),
                    () => ShowWorkspace("chat"), text => Workspace().SendChat(text));
                pet?.WatchQuickChat(quickChat);
            }
            quickChat.Compose();
            return;
        }

        try
        {
            var panel = Workspace();
            pet?.PositionPanel(panel, section);
            panel.OpenSection(section);
            panel.Show();
            if (panel.WindowState == WindowState.Minimized) panel.WindowState = WindowState.Normal;
            panel.Activate();
            panel.FocusSection();
        }
        catch (Exception error)
        {
            string message = "Не удалось открыть окно «Спутника». " + error.Message;
            string? log = WriteDiagnostic("window", $"{section}{Environment.NewLine}{error}");
            if (log is not null) message += $"\n\nЖурнал: {log}";
            MessageBox.Show(message, "Спутник", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private MainWindow Workspace()
    {
        if (workspace is null)
        {
            workspace = new MainWindow();
            MainWindow = workspace;
            workspace.Closing += (_, args) =>
            {
                if (exiting) return;
                args.Cancel = true;
                workspace.Hide();
            };
        }

        return workspace;
    }

    internal void FollowQuickChat()
    {
        quickChat?.FollowPosition();
    }

    internal void RefreshPet() { pet?.UpdateFromStore(); RefreshWindowMaterial(); }
    internal void PreviewPetTopmost(bool enabled) => pet?.ApplyTopmost(enabled);
    internal void PreviewPlaybackSpeed(double speed) => pet?.ApplyPlaybackSpeed(speed);
    private static void RefreshWindowMaterial()
    {
        string path = Path.Combine(DataDirectory, "workspace.json");
        WindowMaterial.Configure(File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null);
    }
    internal void ResetReminder(string id) => reminders?.Forget(id);
    internal void PreviewFavoriteSpheres() => pet?.ShowFavoriteSpheres();
    internal void PreviewAppearanceSizes(double petScale, double sphereScale, double spread, double offset, bool smoothing) =>
        pet?.ApplyAppearanceSizes(petScale, sphereScale, spread, offset, smoothing);

    internal static JsonObject ReadHotkeys() => File.Exists(HotkeysFile)
        ? JsonNode.Parse(File.ReadAllText(HotkeysFile)) as JsonObject ?? new()
        : new();

    internal void SetHotkey(string section, uint modifiers, uint key)
    {
        var settings = ReadHotkeys();
        hotkeys?.Set(section, modifiers, key);
        if (key == 0) settings.Remove(section);
        else settings[section] = new JsonObject { ["modifiers"] = modifiers, ["key"] = key };
        try
        {
            string temp = HotkeysFile + ".tmp";
            File.WriteAllText(temp, settings.ToJsonString());
            Compatibility.ReplaceFile(temp, HotkeysFile);
            DataTransfer.SyncFor(HotkeysFile);
        }
        catch
        {
            var old = ReadHotkeys()[section];
            hotkeys?.Set(section, old?["modifiers"]?.GetValue<uint>() ?? 0, old?["key"]?.GetValue<uint>() ?? 0);
            throw;
        }
    }
    internal bool NotesVisible => notes?.IsVisible == true;
    internal event Action? NotesChanged;
    private StickyNotes Notes()
    {
        if (notes is not null) return notes;
        notes = new StickyNotes(DataDirectory, window => pet?.PositionPanel(window, "sticky"), () => ShowWorkspace("sharing"));
        notes.Changed += () => NotesChanged?.Invoke();
        notes.Remind = id =>
        {
            ShowWorkspace("reminders");
            Workspace().EditReminder(new JsonObject { ["title"] = StickyNotes.Text(notes.All.First(n => n.Id == id)).Split('\n')[0] }, id);
        };
        notes.Deleted += id => reminders?.NoteDeleted(id);
        return notes;
    }
    internal void OpenNote(string id) => Notes().OpenById(id);
    internal void DeleteNote(string id) => Notes().RequestDelete(id);
    internal string CreateNote(string text) => Notes().CreateText(text);
    internal void SwitchProfile(string id)
    {
        FlushNotes(); bool visible = PrepareNotesImport();
        var names = PersonalProfiles.Files.Select(f => f.File).Concat(new[]{"workspace.json","profiles.json"});
        var before = names.ToDictionary(n => n, n => File.Exists(Path.Combine(DataDirectory,n)) ? File.ReadAllText(Path.Combine(DataDirectory,n)) : null);
        try
        {
            new PersonalProfiles(DataDirectory).Switch(id);
            features?.CloseNotice();
            ApplyImportedSettings(null); reminders?.Dispose();
            reminders = new ReminderScheduler(DataDirectory, window => pet?.PositionPanel(window,"reminders")) { OpenNote = OpenNote, Blocked = () => features?.HasNotice == true };
        }
        catch
        {
            FeatureFiles.Commit(DataDirectory,before); DataTransfer.SyncAll(DataDirectory); ApplyImportedSettings(null); throw;
        }
        finally { FinishNotesImport(visible); }
    }
    internal async Task<string> SyncNow()
    {
        if(syncing)return "Синхронизация уже выполняется.";
        var sync=new FolderSync(DataDirectory);var config=sync.Configuration;
        if(config["enabled"]?.GetValue<bool>()!=true)return "Синхронизация выключена.";
        syncing=true;string profile=sync.Active,folder=config["folder"]!.ToString();
        try
        {
            var peers=await Task.Run(()=>FolderSync.ReadPeers(folder));
            if(exiting || sync.Active!=profile || FolderSync.Canonical(config)!=FolderSync.Canonical(sync.Configuration))return "Настройки изменились. Повторите синхронизацию.";
            FlushNotes();var local=sync.Collect();var merged=sync.Merge(profile,local,peers);
            var incoming=new JsonObject();foreach(var entry in merged["records"]!.AsObject())if(entry.Value!["value"] is {} value)incoming[entry.Key]=value.DeepClone();
            bool change=FolderSync.Canonical(local)!=FolderSync.Canonical(incoming),visible=false;
            if(change) {visible=PrepareNotesImport();merged=sync.Merge(profile,sync.Collect(),peers);}
            try {sync.Apply(profile,merged);}
            finally {if(change)FinishNotesImport(visible);}
            if(change){RefreshPet();reminders?.Reload((FeatureFiles.Object(DataDirectory,"workspace.json")["reminders"] as JsonArray??new()).Select(i=>i!["id"]!.ToString()));}
            await Task.Run(()=>FolderSync.Publish(folder,merged));
            string message="Синхронизировано: "+DateTime.Now.ToString("dd.MM HH:mm");
            var statuses=FeatureFiles.Object(DataDirectory,"sync-status.json");statuses[profile]=message;FeatureFiles.Write(DataDirectory,"sync-status.json",statuses);return message;
        }
        catch(Exception error)
        {
            var statuses=FeatureFiles.Object(DataDirectory,"sync-status.json");statuses[profile]="Ошибка: "+error.Message;FeatureFiles.Write(DataDirectory,"sync-status.json",statuses);throw;
        }
        finally{syncing=false;}
    }
    internal void FlushNotes() => notes?.Flush();
    internal void ArrangeNotes() => Notes().Arrange();
    internal bool PrepareNotesImport()
    {
        bool visible = NotesVisible;
        notes?.CloseWindows();
        notes = null;
        return visible;
    }
    internal void FinishNotesImport(bool visible)
    {
        if (visible)
        {
            Notes().Show();
        }
        RefreshNotesCheck();
    }
    internal void ReloadImportedReminders(JsonArray incoming) => reminders?.Reload(incoming.Select(item => item!["id"]!.GetValue<string>()));
    internal void ApplyImportedSettings(JsonNode? settings)
    {
        bool previousAutoStart = AutoStartEnabled();
        try
        {
            hotkeys?.Replace(ReadHotkeys());
            if (settings?["autoStart"] is JsonValue autoStart) SetAutoStart(autoStart.GetValue<bool>());
            RefreshPet();
        }
        catch
        {
            if (AutoStartEnabled() != previousAutoStart) SetAutoStart(previousAutoStart);
            throw;
        }
    }
    internal void RefreshNotesCheck() => pet?.SetNotesChecked(NotesVisible);

    internal void SetPlayback(string command, int index) => pet?.SetPlayback(command, index);

    internal (string Pet, (string Name, bool Enabled)[] Clips) MotionSettings() => pet?.MotionSettings() ?? ("", []);

    internal static bool AutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupKey);
        return key?.GetValue("Sputnik") is string;
    }

    internal static void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupKey);
        string preference = Path.Combine(DataDirectory, Preference);
        if (enabled)
        {
            string executable = Compatibility.ProcessPath ?? throw new IOException("Не найден путь к исполняемому файлу.");
            string command = $"\"{executable}\" --desktop-only";
            if (!Equals(key.GetValue("Sputnik"), command)) key.SetValue("Sputnik", command);
            File.Delete(preference);
        }
        else
        {
            File.WriteAllText(preference, "");
            key.DeleteValue("Sputnik", throwOnMissingValue: false);
        }
        DataTransfer.SyncFor(preference);
    }

    private void ExitApp()
    {
        exiting = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { StickyNoteContent.ClearImageCache(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { WriteDiagnostic("image-cache-cleanup", error.Message); }
        hotkeys?.Dispose();
        reminders?.Dispose();
        features?.Dispose();
        updateWait?.Unregister(null);
        updateRequest?.Dispose();
        showWait?.Unregister(null);
        showRequest?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
