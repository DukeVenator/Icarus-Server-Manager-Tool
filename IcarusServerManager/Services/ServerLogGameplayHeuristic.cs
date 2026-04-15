using IcarusServerManager.Models;

namespace IcarusServerManager.Services;

/// <summary>Best-effort detection of level-up and death lines in server logs (varies by build).</summary>
internal static class ServerLogGameplayHeuristic
{
    public static bool LooksLikeLevelUp(string line, PlayerLogLineResult playerLine)
    {
        if (playerLine.Kind != PlayerLogHintKind.None || string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        if (line.Length < 8 || line.Length > 500)
        {
            return false;
        }

        var lower = line.ToLowerInvariant();
        if (ServerLogChatHeuristic.LooksLikeChatLine(line, playerLine))
        {
            return false;
        }

        if (lower.Contains("death", StringComparison.Ordinal) || lower.Contains("died", StringComparison.Ordinal))
        {
            return false;
        }

        return lower.Contains("level up", StringComparison.Ordinal)
            || lower.Contains("level-up", StringComparison.Ordinal)
            || lower.Contains("leveled up", StringComparison.Ordinal)
            || lower.Contains("reached level", StringComparison.Ordinal)
            || lower.Contains("gained level", StringComparison.Ordinal)
            || lower.Contains("rank up", StringComparison.Ordinal)
            || lower.Contains("xp reward", StringComparison.Ordinal)
            || (lower.Contains("experience") && lower.Contains("gain", StringComparison.Ordinal));
    }

    public static bool LooksLikePlayerDeath(string line, PlayerLogLineResult playerLine)
    {
        if (playerLine.Kind != PlayerLogHintKind.None || string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        if (line.Length < 6 || line.Length > 500)
        {
            return false;
        }

        var lower = line.ToLowerInvariant();
        if (ServerLogChatHeuristic.LooksLikeChatLine(line, playerLine))
        {
            return false;
        }

        if (LooksLikeLevelUp(line, playerLine))
        {
            return false;
        }

        return lower.Contains("player death", StringComparison.Ordinal)
            || lower.Contains("has died", StringComparison.Ordinal)
            || lower.Contains("was killed", StringComparison.Ordinal)
            || lower.Contains("eliminated", StringComparison.Ordinal)
            || (lower.Contains("died", StringComparison.Ordinal) && (lower.Contains("player", StringComparison.Ordinal) || lower.Contains("character", StringComparison.Ordinal)));
    }
}
