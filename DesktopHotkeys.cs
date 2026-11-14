using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows.Interop;

namespace Sputnik;

internal sealed class DesktopHotkeys(Action<string> open) : IDisposable
{
    private sealed record Binding(int Id, uint Modifiers, uint Key, string Section);
    private readonly Dictionary<string, Binding> bindings = [];
    private HwndSource? source;
    private int nextId = 200;

    public void Attach(IntPtr handle)
    {
        source = HwndSource.FromHwnd(handle);
        source?.AddHook(OnMessage);
    }

    public void Restore(JsonObject settings)
    {
        foreach (var item in settings)
        {
            if (item.Value is not JsonObject keys) continue;
            try { Set(item.Key, (uint)keys["modifiers"]!.GetValue<int>(), (uint)keys["key"]!.GetValue<int>()); }
            catch (Exception error) when (error is InvalidOperationException or NullReferenceException) { }
        }
    }

    public void Set(string section, uint modifiers, uint key)
    {
        if (!DesktopControls.Sections.Any(item => item.Action == section))
            throw new InvalidOperationException("Выберите пункт меню.");
        if (source is null) throw new InvalidOperationException("Горячие клавиши пока недоступны.");
        bindings.TryGetValue(section, out var existing);
        if (key == 0)
        {
            if (existing is not null)
            {
                UnregisterHotKey(source.Handle, existing.Id);
                bindings.Remove(section);
            }
            return;
        }
        if (modifiers is 0 or > 15 || key is not (>= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x87) || key == 0x7B)
            throw new InvalidOperationException("Выберите Ctrl, Alt, Shift или Win и букву, цифру или функциональную клавишу.");
        if (existing?.Modifiers == modifiers && existing.Key == key) return;
        if (bindings.Values.Any(item => item.Modifiers == modifiers && item.Key == key))
            throw new InvalidOperationException("Сочетание уже назначено другому пункту.");
        var replacement = new Binding(nextId++, modifiers, key, section);
        if (!RegisterHotKey(source.Handle, replacement.Id, modifiers | 0x4000, key))
            throw new InvalidOperationException("Это сочетание занято или зарезервировано Windows.");
        if (existing is not null) UnregisterHotKey(source.Handle, existing.Id);
        bindings[section] = replacement;
    }

    private IntPtr OnMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0312 || bindings.Values.FirstOrDefault(item => item.Id == wParam.ToInt32()) is not { } binding)
            return IntPtr.Zero;
        source?.Dispatcher.BeginInvoke(() => open(binding.Section));
        handled = true;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (source is not { IsDisposed: false }) return;
        foreach (var binding in bindings.Values) UnregisterHotKey(source.Handle, binding.Id);
        source.RemoveHook(OnMessage);
        bindings.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr handle, int id);
}
