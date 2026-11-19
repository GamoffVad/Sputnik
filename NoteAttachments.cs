using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Imaging;

namespace Sputnik;

internal static class NoteAttachments
{
    internal static byte[][] Images(FlowDocument document)
    {
        var images = new List<byte[]>();
        void Read(IEnumerable<StickyNoteContent.BlockData> blocks)
        {
            foreach (var block in blocks)
            {
                foreach (var inline in block.Inlines ?? [])
                    if (inline.Kind == "image" && inline.Png is { Length: > 0 } png) images.Add(png);
                foreach (var item in block.Items ?? []) Read(item);
            }
        }
        Read(StickyNoteContent.Read(document.Blocks));
        return images.ToArray();
    }

    internal static string[] Export(byte[][] images, string root)
    {
        string folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var files = new List<string>();
        try
        {
            for (int i = 0; i < images.Length; i++)
            {
                string file = Path.Combine(folder, $"Фото-{i + 1:00}.png");
                File.WriteAllBytes(file, images[i]);
                files.Add(file);
            }
            return files.ToArray();
        }
        catch { Directory.Delete(folder, true); throw; }
    }

    internal static DataObject PhotoClipboard(string[] files)
    {
        var data = new DataObject();
        var paths = new StringCollection(); paths.AddRange(files);
        data.SetFileDropList(paths);
        if (files.Length == 1)
        {
            using var stream = File.OpenRead(files[0]);
            var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            image.Freeze(); data.SetImage(image);
            data.SetData("PNG", new MemoryStream(File.ReadAllBytes(files[0]), writable: false));
        }
        return data;
    }

    internal static ProcessStartInfo TelegramCommand(string executable, string arguments, string[] files)
    {
        // Use the installed client's Send To switch, which changed from -sendpath
        // to -- in newer Telegram versions. Never guess it or pass a shell string.
        arguments = arguments.Trim();
        if (!string.Equals(Path.GetFileName(executable), "Telegram.exe", StringComparison.OrdinalIgnoreCase) ||
            arguments is not ("--" or "-sendpath") || files.Length == 0 || files.Any(path => path.IndexOf('"') >= 0))
            throw new InvalidOperationException("Не найден способ передачи фото в Telegram Desktop.");
        return new ProcessStartInfo(executable, arguments + " " + string.Join(" ", files.Select(path => "\"" + Path.GetFullPath(path) + "\"")))
        { UseShellExecute = false };
    }

    internal static bool TryTelegram(string[] files)
    {
        string sendTo = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
        if (!Directory.Exists(sendTo)) return false;
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type is null) return false;
        object? shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(type)!;
            foreach (string shortcut in Directory.EnumerateFiles(sendTo, "*Telegram*.lnk"))
            {
                link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [shortcut]);
                if (link is null) continue;
                string Read(string property) => link.GetType().InvokeMember(property, BindingFlags.GetProperty, null, link, null)?.ToString() ?? "";
                string executable = Read("TargetPath"), arguments = Read("Arguments").Trim();
                if (File.Exists(executable) && string.Equals(Path.GetFileName(executable), "Telegram.exe", StringComparison.OrdinalIgnoreCase) && arguments is "--" or "-sendpath")
                {
                    Process.Start(TelegramCommand(executable, arguments, files));
                    return true;
                }
                Marshal.FinalReleaseComObject(link); link = null;
            }
        }
        catch (Exception error) when (error is COMException or System.ComponentModel.Win32Exception or TargetInvocationException) { }
        finally
        {
            if (link is not null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
        return false;
    }
}
