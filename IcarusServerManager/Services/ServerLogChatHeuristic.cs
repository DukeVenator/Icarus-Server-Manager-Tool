using System.Text.RegularExpressions;
using IcarusServerManager.Models;

namespace IcarusServerManager.Services;

/// <summary>
/// Best-effort detection of chat-like lines in Unreal-style server logs (varies by game build).
/// </summary>
internal static class ServerLogChatHeuristic
{
    /// <param name="playerLine">Result from <see cref="ServerOutputPlayerTracker.ProcessLogLine"/> for the same line.</param>
    public static bool LooksLikeChatLine(string line, PlayerLogLineResult playerLine)
    {
        if (playerLine.Kind != PlayerLogHintKind.None)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var lowerAll = line.ToLowerInvariant();
        if (lowerAll.Contains("logconnectedplayers", StringComparison.Ordinal)
            || lowerAll.Contains("addconnectedplayer", StringComparison.Ordinal)
            || lowerAll.Contains("removeconnectedplayer", StringComparison.Ordinal))
        {
            return false;
        }

        if (line.Length < 6 || line.Length > 600)
        {
            return false;
        }

        var lower = line.ToLowerInvariant();
        if (lower.Contains("error", StringComparison.Ordinal) && lower.Contains("fatal", StringComparison.Ordinal))
        {
            return false;
        }

        if (LooksLikeJoinOrLeaveNoise(lower))
        {
            return false;
        }

        if (lower.Contains("logchat", StringComparison.Ordinal)
            || lower.Contains("globalchat", StringComparison.Ordinal)
            || lower.Contains("localchat", StringComparison.Ordinal)
            || lower.Contains("replicated chat", StringComparison.Ordinal)
            || lower.Contains("chat message", StringComparison.Ordinal))
        {
            return true;
        }

        if (lower.Contains("say ", StringComparison.Ordinal) && (line.Contains('\'') || line.Contains('"')))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeJoinOrLeaveNoise(string lower)
    {
        if (lower.Contains("joined", StringComparison.Ordinal) || lower.Contains("joining", StringComparison.Ordinal))
        {
            return true;
        }

        if (lower.Contains("left", StringComparison.Ordinal) || lower.Contains("disconnect", StringComparison.Ordinal) || lower.Contains("logout", StringComparison.Ordinal))
        {
            return true;
        }

        if (Regex.IsMatch(lower, @"\blogin\b") && Regex.IsMatch(lower, @"\bconnect(ed)?\b"))
        {
            return true;
        }

        return false;
    }
}
