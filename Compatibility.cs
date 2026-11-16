using System.IO;
#if NET48
using System.Diagnostics;
#endif

namespace Sputnik;

internal static class Compatibility
{
    internal static string? ProcessPath
    {
        get
        {
#if NET48
            using var process = Process.GetCurrentProcess();
            return process.MainModule?.FileName;
#else
            return Environment.ProcessPath;
#endif
        }
    }

    internal static void ReplaceFile(string source, string destination)
    {
#if NET48
        if (File.Exists(destination)) File.Replace(source, destination, null);
        else File.Move(source, destination);
#else
        File.Move(source, destination, overwrite: true);
#endif
    }

    internal static int ProcessId { get; } = GetProcessId();

    private static int GetProcessId()
    {
#if NET48
        using var process = Process.GetCurrentProcess();
        return process.Id;
#else
        return Environment.ProcessId;
#endif
    }

    internal static double Clamp(double value, double min, double max)
    {
#if NET48
        if (min > max) throw new ArgumentException("The minimum must not exceed the maximum.", nameof(min));
        if (value < min) return min;
        if (value > max) return max;
        return value;
#else
        return Math.Clamp(value, min, max);
#endif
    }

    internal static bool IsFullyQualifiedPath(string path)
    {
#if NET48
        static bool IsSeparator(char value) => value is '\\' or '/';
        return path.Length >= 3 &&
               ((path[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z' && path[1] == ':' && IsSeparator(path[2])) ||
                (IsSeparator(path[0]) && IsSeparator(path[1])));
#else
        return Path.IsPathFullyQualified(path);
#endif
    }

    internal static string NormalizeLineEndings(string text)
    {
#if NET48
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\f', '\n')
            .Replace('\u0085', '\n').Replace('\u2028', '\n').Replace('\u2029', '\n');
#else
        return text.ReplaceLineEndings("\n");
#endif
    }
}
