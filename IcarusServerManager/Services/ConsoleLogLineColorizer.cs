using System.Drawing;

namespace IcarusServerManager.Services;

/// <summary>
/// Maps a single console log line to an optional foreground color for the RichTextBox.
/// Manager lines use [ERROR]/[WARN]; game output is forwarded as [INFO] but often includes UE tokens like ": Error:".
/// </summary>
internal static class ConsoleLogLineColorizer
{
    public static Color? ResolveLineColor(string line, bool isDarkTheme)
    {
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        if (line.Contains("[ERROR]", StringComparison.Ordinal))
        {
            return isDarkTheme ? Color.FromArgb(255, 130, 130) : Color.FromArgb(175, 30, 30);
        }

        if (line.Contains("[WARN]", StringComparison.Ordinal))
        {
            return isDarkTheme ? Color.FromArgb(255, 205, 100) : Color.FromArgb(145, 95, 0);
        }

        if (ContainsToken(line, ": Fatal:") || ContainsToken(line, ": Error:"))
        {
            return isDarkTheme ? Color.FromArgb(255, 115, 115) : Color.FromArgb(165, 25, 25);
        }

        if (ContainsToken(line, ": Warning:"))
        {
            return isDarkTheme ? Color.FromArgb(255, 210, 115) : Color.FromArgb(155, 105, 15);
        }

        if (ConsoleLogFilter.IsImportantPhrase(line))
        {
            return isDarkTheme ? Color.FromArgb(115, 200, 255) : Color.FromArgb(0, 105, 150);
        }

        if (ConsoleLogFilter.IsLowPriorityUeVerbosity(line))
        {
            return isDarkTheme ? Color.FromArgb(140, 145, 158) : Color.FromArgb(95, 100, 110);
        }

        if (line.Equals("Initializing...", StringComparison.OrdinalIgnoreCase))
        {
            return isDarkTheme ? Color.FromArgb(185, 190, 205) : Color.FromArgb(95, 100, 110);
        }

        return null;
    }

    private static bool ContainsToken(string line, string token) =>
        line.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

}
