using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using IcarusServerManager.Models;

namespace IcarusServerManager.Services;

/// <summary>
/// Best-effort hints from dedicated server stdout/stderr (exact log lines vary by build).
/// Merged with <see cref="ProspectMemberInfo.IsCurrentlyPlaying"/> from the prospect JSON for the Stats panel.
/// </summary>
internal sealed class ServerOutputPlayerTracker
{
    private static readonly Regex SingleQuoted = new("'([^']{2,64})'", RegexOptions.Compiled);

    /// <summary>Icarus dedicated: authoritative join (once per connection).</summary>
    private static readonly Regex IcarusAddConnected = new(
        @"(?i)AddConnectedPlayer[^\n]*\bPlayerName:\s*(.+?)(?:\s*\||\s*$)",
        RegexOptions.Compiled);

    /// <summary>Icarus: sync hint if Finalise appears without a prior Add line in the same session buffer.</summary>
    private static readonly Regex IcarusFinalisePlayerName = new(
        @"(?i)FinaliseConnectedPlayerInitialisation[^\n]*\bPlayerName:\s*(.+?)(?:\s*\||\s*$)",
        RegexOptions.Compiled);

    private static readonly Regex IcarusRemoveConnected = new(
        @"(?i)RemoveConnectedPlayer[^\n]*\bPlayerName:\s*(.+?)(?:\s*\||\s*$)",
        RegexOptions.Compiled);

    private static readonly Regex ManagerLogPrefix = new(
        @"^(?:\[[^\]]+\]\s*){1,2}",
        RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, byte> _hints = new(StringComparer.OrdinalIgnoreCase);

    public void Clear() => _hints.Clear();

    public IReadOnlyCollection<string> HintNames => _hints.Keys.ToList();

    /// <summary>Updates join/leave hints and reports what changed for this line, if anything.</summary>
    public PlayerLogLineResult ProcessLogLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return PlayerLogLineResult.None;
        }

        var core = StripOptionalManagerPrefixes(line);

        // --- Icarus LogConnectedPlayers (preferred) ---
        var rm = IcarusRemoveConnected.Match(core);
        if (rm.Success)
        {
            var leftName = NormalizePlayerName(rm.Groups[1].Value);
            if (!string.IsNullOrEmpty(leftName))
            {
                _hints.TryRemove(leftName, out _);
                return new PlayerLogLineResult(PlayerLogHintKind.Left, leftName);
            }
        }

        var am = IcarusAddConnected.Match(core);
        if (am.Success)
        {
            var joinName = NormalizePlayerName(am.Groups[1].Value);
            if (!string.IsNullOrEmpty(joinName))
            {
                _hints[joinName] = 1;
                return new PlayerLogLineResult(PlayerLogHintKind.Joined, joinName);
            }
        }

        var fm = IcarusFinalisePlayerName.Match(core);
        if (fm.Success)
        {
            var n = NormalizePlayerName(fm.Groups[1].Value);
            if (!string.IsNullOrEmpty(n) && !_hints.ContainsKey(n))
            {
                _hints[n] = 1;
                return new PlayerLogLineResult(PlayerLogHintKind.Joined, n);
            }

            return PlayerLogLineResult.None;
        }

        // --- Generic leave (quoted / legacy wording) ---
        var lower = core.ToLowerInvariant();
        if (LooksLikeGenericLeave(lower))
        {
            var left = TryExtractName(core);
            if (!string.IsNullOrEmpty(left))
            {
                _hints.TryRemove(left, out _);
                return new PlayerLogLineResult(PlayerLogHintKind.Left, left);
            }

            return PlayerLogLineResult.None;
        }

        // --- Generic join (avoid substring traps like "Initialisation" / unrelated "connect") ---
        if (LooksLikeGenericJoin(lower) && !LooksLikeIcarusConnectedPlayersNoise(core, lower))
        {
            var name = TryExtractName(core);
            if (!string.IsNullOrWhiteSpace(name))
            {
                _hints[name] = 1;
                return new PlayerLogLineResult(PlayerLogHintKind.Joined, name);
            }
        }

        return PlayerLogLineResult.None;
    }

    private static string StripOptionalManagerPrefixes(string line)
    {
        var s = line.Trim();
        for (var i = 0; i < 4; i++)
        {
            var m = ManagerLogPrefix.Match(s);
            if (!m.Success)
            {
                break;
            }

            s = s[m.Length..].TrimStart();
        }

        return s;
    }

    private static string NormalizePlayerName(string raw)
    {
        var s = raw.Trim();
        if (s.Length is < 2 or > 64)
        {
            return string.Empty;
        }

        return s;
    }

    private static bool LooksLikeGenericLeave(string lower) =>
        ContainsWord(lower, "disconnected")
        || ContainsWord(lower, "disconnect")
        || ContainsWord(lower, "logout")
        || (ContainsWord(lower, "left") && (lower.Contains("player", StringComparison.Ordinal) || lower.Contains("session", StringComparison.Ordinal) || lower.Contains("game", StringComparison.Ordinal)));

    private static bool LooksLikeGenericJoin(string lower) =>
        ContainsWord(lower, "joined")
        || ContainsWord(lower, "joining")
        || (ContainsWord(lower, "connected") && (lower.Contains("player", StringComparison.Ordinal) || lower.Contains("login", StringComparison.Ordinal) || lower.Contains("client", StringComparison.Ordinal)))
        || (ContainsWord(lower, "login") && lower.Contains("success", StringComparison.Ordinal));

    private static bool LooksLikeIcarusConnectedPlayersNoise(string core, string lower)
    {
        if (!lower.Contains("logconnectedplayers", StringComparison.Ordinal))
        {
            return false;
        }

        // Intermediate init lines; join/leave handled by explicit regexes above.
        if (lower.Contains("servertrycompleteplayerinitialisation", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsWord(string haystack, string word)
    {
        var i = 0;
        while (i < haystack.Length)
        {
            i = haystack.IndexOf(word, i, StringComparison.Ordinal);
            if (i < 0)
            {
                return false;
            }

            var end = i + word.Length;
            var beforeOk = i == 0 || !char.IsLetterOrDigit(haystack[i - 1]);
            var afterOk = end >= haystack.Length || !char.IsLetterOrDigit(haystack[end]);
            if (beforeOk && afterOk)
            {
                return true;
            }

            i++;
        }

        return false;
    }

    private static string? TryExtractName(string line)
    {
        var pipeName = TryExtractPlayerNameColon(line);
        if (!string.IsNullOrEmpty(pipeName))
        {
            return pipeName;
        }

        var m = SingleQuoted.Match(line);
        if (m.Success)
        {
            return m.Groups[1].Value.Trim();
        }

        var q1 = line.LastIndexOf('"');
        if (q1 > 0)
        {
            var q0 = line.LastIndexOf('"', q1 - 1);
            if (q0 >= 0 && q1 - q0 > 1)
            {
                var inner = line.Substring(q0 + 1, q1 - q0 - 1).Trim();
                if (inner.Length is >= 2 and <= 64 && !inner.Contains('\\', StringComparison.Ordinal) && !inner.Contains(':', StringComparison.Ordinal))
                {
                    return inner;
                }
            }
        }

        return null;
    }

    /// <summary>Player: Name or PlayerName: Name (Icarus-style tail fields).</summary>
    private static string? TryExtractPlayerNameColon(string line)
    {
        var lower = line.ToLowerInvariant();
        var idx = lower.LastIndexOf("playername:", StringComparison.Ordinal);
        if (idx < 0)
        {
            idx = lower.LastIndexOf("player:", StringComparison.Ordinal);
        }

        if (idx < 0)
        {
            return null;
        }

        var start = line.IndexOf(':', idx) + 1;
        if (start <= 0 || start >= line.Length)
        {
            return null;
        }

        var tail = line[start..].Trim();
        var pipe = tail.IndexOf('|');
        if (pipe >= 0)
        {
            tail = tail[..pipe].Trim();
        }

        tail = NormalizePlayerName(tail);
        return tail.Length is >= 2 and <= 64 ? tail : null;
    }
}
