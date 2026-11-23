using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Sputnik;

public partial class MainWindow
{
    private void ShowDataFiles()
    {
        editing = true;
        var root = Page("Импорт и экспорт", Button("Назад", () => OpenSection(section)), 18);
        var status = Status(12);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Assertive);
        var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        footer.Children.Add(status);
        footer.Children.Add(Button("Открыть папку данных", () =>
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + data + "\"") { UseShellExecute = true })));
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var page = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var hint = Text("Файлы JSON сохраняются автоматически. Импорт добавляет записи и обновляет совпадающие по ID, сохраняя резервную копию.", "Muted", 12);
        hint.Margin = new Thickness(0, 0, 0, 10);
        page.Children.Add(hint);
        foreach (var entry in new[] { ("notes", "Заметки"), ("settings", "Настройки"), ("reminders", "Напоминания"), ("dates", "Памятные даты") })
        {
            var export = Button("Экспорт", () =>
            {
                try
                {
                    Desktop?.FlushNotes();
                    var picker = new SaveFileDialog { FileName = DataTransfer.FileName(entry.Item1), DefaultExt = ".json",
                        Filter = entry.Item2 + " Спутника|*.sputnik-" + entry.Item1 + ".json", AddExtension = true, OverwritePrompt = true };
                    if (picker.ShowDialog(this) != true) return;
                    new DataTransfer(data).Export(entry.Item1, picker.FileName);
                    status.Text = "Сохранено: " + Path.GetFileName(picker.FileName);
                }
                catch (Exception error) { status.Text = error.Message; }
            });
            var import = Button("Импорт", () =>
            {
                try
                {
                    var picker = new OpenFileDialog { Filter = entry.Item2 + " Спутника|*.sputnik-" + entry.Item1 + ".json|JSON|*.json", CheckFileExists = true };
                    if (picker.ShowDialog(this) == true) status.Text = ImportDataFile(entry.Item1, picker.FileName);
                }
                catch (Exception error) { status.Text = error.Message; }
            });
            AutomationProperties.SetName(export, "Экспорт: " + entry.Item2);
            AutomationProperties.SetName(import, "Импорт: " + entry.Item2);
            Entry(page, entry.Item2, ".sputnik-" + entry.Item1 + ".json", export, import);
        }
        var credentials = Text("Настройки переносятся без API-ключа. На другом компьютере укажите его заново в разделе «Модель и API».", "Muted", 11);
        credentials.Margin = new Thickness(0, 7, 0, 0);
        page.Children.Add(credentials);
    }

    private string ImportDataFile(string kind, string path)
    {
        var transfer = new DataTransfer(data);
        var package = transfer.Read(kind, path);
        Desktop?.FlushNotes();
        bool reopenNotes = kind == "notes" && Desktop?.PrepareNotesImport() == true;
        DataTransfer.Result? result = null;
        try
        {
            result = transfer.Import(package);
            if (kind == "settings") Desktop?.ApplyImportedSettings(package["data"]);
            if (kind == "reminders") Desktop?.ReloadImportedReminders(package["data"]!.AsArray());
            return kind == "settings" ? "Настройки импортированы и применены." : $"Импортировано записей: {result.Count}.";
        }
        catch
        {
            if (result is not null)
            {
                transfer.Restore(result.Backup);
                if (kind == "settings") Desktop?.ApplyImportedSettings(null);
            }
            throw;
        }
        finally { if (kind == "notes") Desktop?.FinishNotesImport(reopenNotes); }
    }
}
