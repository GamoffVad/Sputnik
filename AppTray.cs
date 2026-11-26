using System.Windows;

namespace Sputnik;

public partial class App
{
    private DesktopTray? tray;
    private bool hiddenInTray;
    private readonly List<Window> trayWindows = [];

    internal void HideToTray()
    {
        if (exiting || hiddenInTray) return;
        if (tray?.EnsureVisible() != true)
        {
            MessageBox.Show("Не удалось добавить значок в системный трей. Повторите попытку, когда панель задач будет доступна.", "Спутник");
            return;
        }
        pet?.PrepareForTray();
        // Dismiss transient requests so a late model reply cannot reopen the hidden app.
        quickChat?.Dismiss();
        trayWindows.Clear();
        trayWindows.AddRange(Windows.Cast<Window>().Where(window => window.IsVisible));
        hiddenInTray = true;
        foreach (var window in trayWindows) window.Hide();
    }

    internal void RestoreFromTray()
    {
        if (exiting || !hiddenInTray) return;
        hiddenInTray = false;
        var alive = new HashSet<Window>(Windows.Cast<Window>());
        foreach (var window in trayWindows.Where(alive.Contains)) window.Show();
        trayWindows.Clear();
        pet?.ReturnFromTray();
    }
}
