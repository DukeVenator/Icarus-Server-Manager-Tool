namespace IcarusProspectEditor.Services;

/// <summary>
/// Parses the optional prospect path passed on the command line, so the editor can be launched
/// straight into a file (drag-and-drop onto the exe, or UI automation that must skip the Open dialog).
///
/// Accepted forms:
///   IcarusProspectEditor.exe "C:\path\prospect.json"
///   IcarusProspectEditor.exe --open "C:\path\prospect.json"
///   IcarusProspectEditor.exe -o "C:\path\prospect.json"
/// </summary>
internal static class StartupArguments
{
    private static readonly string[] OpenSwitches = { "--open", "-o", "/open" };

    /// <summary>
    /// Returns the prospect path to open, or null when no path was supplied. A switch without a
    /// value, or a path that does not exist, yields null rather than a dialog or a crash.
    /// </summary>
    public static string? GetProspectPath(IReadOnlyList<string> args)
    {
        if (args is null || args.Count == 0)
        {
            return null;
        }

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            if (IsOpenSwitch(argument))
            {
                // The value must follow the switch; an empty or missing value means "open nothing".
                return index + 1 < args.Count ? Normalize(args[index + 1]) : null;
            }

            if (!argument.StartsWith('-') && !argument.StartsWith('/'))
            {
                return Normalize(argument);
            }
        }

        return null;
    }

    private static bool IsOpenSwitch(string argument) =>
        OpenSwitches.Contains(argument, StringComparer.OrdinalIgnoreCase);

    private static string? Normalize(string candidate)
    {
        var trimmed = candidate.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(trimmed);
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
