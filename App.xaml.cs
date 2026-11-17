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
        RefreshWindowMaterial();
        pet = new DesktopPetWindow(ShowWorkspace, ExitApp);
        pet.UpdateFromStore();
        pet.Show();
        reminders = new ReminderScheduler(DataDirectory, window => pet?.PositionPanel(window, "reminders"));
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

    internal void ShowWorkspace(string section)
    {
        if (exiting) return;
        if (section == "notes")
        {
            workspace?.Hide();
            notes ??= new StickyNotes(DataDirectory, window => pet?.PositionPanel(window, "sticky"), () => ShowWorkspace("sharing"));
            notes.Toggle(reposition: pet?.HasContextMenu == true);
            pet?.SetNotesChecked(notes.IsVisible);
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
        if (quickChat is { IsVisible: true }) pet?.PositionBubble(quickChat);
    }

    internal void RefreshPet() { pet?.UpdateFromStore(); RefreshWindowMaterial(); }
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
        }
        catch
        {
            var old = ReadHotkeys()[section];
            hotkeys?.Set(section, old?["modifiers"]?.GetValue<uint>() ?? 0, old?["key"]?.GetValue<uint>() ?? 0);
            throw;
        }
    }
    internal bool NotesVisible => notes?.IsVisible == true;
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
    }

    private void ExitApp()
    {
        exiting = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        hotkeys?.Dispose();
        reminders?.Dispose();
        updateWait?.Unregister(null);
        updateRequest?.Dispose();
        showWait?.Unregister(null);
        showRequest?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
