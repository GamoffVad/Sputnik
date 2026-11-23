using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Sputnik;

internal static class AppVersion
{
    internal static string Current => typeof(App).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(App).Assembly.GetName().Version?.ToString() ?? "1.0";

    internal static TextBlock MenuLabel()
    {
        var label = new TextBlock
        {
            Text = "Спутник · " + Current, FontSize = 11,
            Margin = new Thickness(12, 3, 8, 4), IsHitTestVisible = false
        };
        label.UseTheme(TextBlock.ForegroundProperty, "Muted");
        return label;
    }
}
