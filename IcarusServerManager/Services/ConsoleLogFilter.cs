using IcarusServerManager.Models;

namespace IcarusServerManager.Services;

/// <summary>
/// Classifies log lines for the on-screen console and applies named visibility presets.
/// </summary>
internal static class ConsoleLogFilter
{
    public static readonly string[] PresetNames = ["Minimal", "Balanced", "Verbose", "QuietGame", "Custom"];

    public static ConsoleLogLineKind Classify(string formattedLine, bool isGameProcessOutput)
    {
        if (string.IsNullOrEmpty(formattedLine))
        {
            return ConsoleLogLineKind.GameGeneral;
        }

        if (!isGameProcessOutput)
        {
            if (formattedLine.Contains("[ERROR]", StringComparison.Ordinal))
            {
                return ConsoleLogLineKind.ManagerError;
            }

            if (formattedLine.Contains("[WARN]", StringComparison.Ordinal))
            {
                return ConsoleLogLineKind.ManagerWarn;
            }

            return ConsoleLogLineKind.ManagerInfo;
        }

        var payload = ExtractGamePayload(formattedLine);
        if (ContainsToken(payload, ": Fatal:") || ContainsToken(payload, ": Error:"))
        {
            return ConsoleLogLineKind.GameFatalOrError;
        }

        if (ContainsToken(payload, ": Warning:"))
        {
            return ConsoleLogLineKind.GameWarning;
        }

        if (IsImportantPhrase(payload))
        {
            return ConsoleLogLineKind.GameImportant;
        }

        if (IsLowPriorityUeVerbosity(payload))
        {
            return ConsoleLogLineKind.GameVerbose;
        }

        return ConsoleLogLineKind.GameGeneral;
    }

    public static bool ShouldDisplay(ConsoleLogLineKind kind, ManagerOptions o) =>
        kind switch
        {
            ConsoleLogLineKind.ManagerError => o.ConsoleShowManagerError,
            ConsoleLogLineKind.ManagerWarn => o.ConsoleShowManagerWarn,
            ConsoleLogLineKind.ManagerInfo => o.ConsoleShowManagerInfo,
            ConsoleLogLineKind.GameFatalOrError => o.ConsoleShowGameFatalError,
            ConsoleLogLineKind.GameWarning => o.ConsoleShowGameWarning,
            ConsoleLogLineKind.GameImportant => o.ConsoleShowGameImportant,
            ConsoleLogLineKind.GameVerbose => o.ConsoleShowGameVerbose,
            ConsoleLogLineKind.GameGeneral => o.ConsoleShowGameGeneral,
            _ => true
        };

    public static void ApplyPreset(ManagerOptions o, string presetName)
    {
        var key = presetName.Trim().ToLowerInvariant();
        switch (key)
        {
            case "minimal":
                o.ConsoleLogPreset = "Minimal";
                o.ConsoleShowManagerError = true;
                o.ConsoleShowManagerWarn = true;
                o.ConsoleShowManagerInfo = false;
                o.ConsoleShowGameFatalError = true;
                o.ConsoleShowGameWarning = true;
                o.ConsoleShowGameImportant = false;
                o.ConsoleShowGameVerbose = false;
                o.ConsoleShowGameGeneral = false;
                break;
            case "verbose":
                o.ConsoleLogPreset = "Verbose";
                o.ConsoleShowManagerError = true;
                o.ConsoleShowManagerWarn = true;
                o.ConsoleShowManagerInfo = true;
                o.ConsoleShowGameFatalError = true;
                o.ConsoleShowGameWarning = true;
                o.ConsoleShowGameImportant = true;
                o.ConsoleShowGameVerbose = true;
                o.ConsoleShowGameGeneral = true;
                break;
            case "quietgame":
                o.ConsoleLogPreset = "QuietGame";
                o.ConsoleShowManagerError = true;
                o.ConsoleShowManagerWarn = true;
                o.ConsoleShowManagerInfo = true;
                o.ConsoleShowGameFatalError = true;
                o.ConsoleShowGameWarning = true;
                o.ConsoleShowGameImportant = true;
                o.ConsoleShowGameVerbose = false;
                o.ConsoleShowGameGeneral = false;
                break;
            default:
                o.ConsoleLogPreset = "Balanced";
                o.ConsoleShowManagerError = true;
                o.ConsoleShowManagerWarn = true;
                o.ConsoleShowManagerInfo = true;
                o.ConsoleShowGameFatalError = true;
                o.ConsoleShowGameWarning = true;
                o.ConsoleShowGameImportant = true;
                o.ConsoleShowGameVerbose = false;
                o.ConsoleShowGameGeneral = true;
                break;
        }
    }

    /// <summary>Same markers as <see cref="ConsoleLogLineColorizer"/> for consistent filtering and coloring.</summary>
    public static bool IsLowPriorityUeVerbosity(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        var lower = line.ToLowerInvariant();
        if (lower.Contains("logicarusstaterecordercomponent", StringComparison.Ordinal)
            && lower.Contains("beginrecording", StringComparison.Ordinal))
        {
            return true;
        }

        if (lower.Contains("shadercodelibrarypakfilemountedcallback", StringComparison.Ordinal))
        {
            return true;
        }

        return ContainsToken(line, ": Display:")
            || ContainsToken(line, ": Verbose:")
            || ContainsToken(line, ": VeryVerbose:");
    }

    public static bool IsImportantPhrase(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        foreach (var phrase in ImportantPhrases)
        {
            if (line.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractGamePayload(string line)
    {
        const string marker = "] [INFO] ";
        var idx = line.IndexOf(marker, StringComparison.Ordinal);
        if (idx >= 0)
        {
            return line[(idx + marker.Length)..];
        }

        return line;
    }

    private static bool ContainsToken(string line, string token) =>
        line.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static readonly string[] ImportantPhrases =
    [
        "game server started",
        "game server stopped",
        "ini saved to",
        "ini loaded from",
        "manager options saved",
        "backed up ini",
        "starting game server",
        "stopping server",
        "server manager initialized",
        "installation found",
        "installation not found",
        "launch arguments",
        "preset saved",
        "preset loaded",
        "exported bundle",
        "imported bundle",
        "exported metrics csv",
        "install/update completed",
        "restart requested",
        "scheduled update window reached",
        "match state changed",
        "bound to port",
        "setactiveprospect",
        "updateactiveprospect",
        "resetactiveprospect",
        "leaving fengineloop::init",
        "game engine initialized",
        "starting game.",
        "engine is initialized",
        "addconnectedplayer",
        "removeconnectedplayer",
        "finaliseconnectedplayerinitialisation",
        "finalizeconnectedplayerinitialisation",
        "logconnectedplayers",
        "onconnectedplayerinitialised",
        "initialiseplayerforprospect",
        "initializeplayerforprospect",
        "readfromprospectsavestate complete",
        "match state changed from waitingtostart to inprogress",
    ];
}
