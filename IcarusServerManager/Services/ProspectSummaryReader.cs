using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IcarusServerManager.Models;

namespace IcarusServerManager.Services;

/// <summary>
/// Reads only the beginning of large prospect JSON files to extract <c>ProspectInfo</c> fields without parsing the full document.
/// </summary>
internal static class ProspectSummaryReader
{
    private const int MaxHeaderBytes = 2_097_152;

    private static readonly Regex ProspectIdRe = new("\"ProspectID\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ProspectDtKeyRe = new("\"ProspectDTKey\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ProspectStateRe = new("\"ProspectState\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DifficultyRe = new("\"Difficulty\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LobbyNameRe = new("\"LobbyName\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FactionMissionDtKeyRe = new("\"FactionMissionDTKey\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ElapsedTimeRe = new("\"ElapsedTime\"\\s*:\\s*(-?\\d+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CostRe = new("\"Cost\"\\s*:\\s*(-?\\d+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RewardRe = new("\"Reward\"\\s*:\\s*(-?\\d+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex InsuranceRe = new("\"Insurance\"\\s*:\\s*(true|false)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NoRespawnsRe = new("\"NoRespawns\"\\s*:\\s*(true|false)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SelectedDropPointRe = new("\"SelectedDropPoint\"\\s*:\\s*(-?\\d+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>One member object inside AssociatedMembers (tolerates tab/space and extra fields between keys).</summary>
    private static readonly Regex MemberBlockRe = new(
        "\"AccountName\"\\s*:\\s*\"(?<an>[^\"]*)\"[\\s\\S]*?\"CharacterName\"\\s*:\\s*\"(?<cn>[^\"]*)\"[\\s\\S]*?\"UserID\"\\s*:\\s*\"(?<uid>[^\"]*)\"[\\s\\S]*?\"Experience\"\\s*:\\s*(?<xp>-?\\d+)[\\s\\S]*?\"Status\"\\s*:\\s*\"(?<st>[^\"]*)\"[\\s\\S]*?\"IsCurrentlyPlaying\"\\s*:\\s*(?<play>true|false)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static ProspectSummary Read(string absolutePath)
    {
        var fi = new FileInfo(absolutePath);
        var baseName = Path.GetFileNameWithoutExtension(absolutePath);
        if (!fi.Exists)
        {
            return new ProspectSummary
            {
                BaseName = baseName,
                FullPath = absolutePath,
                FileSizeBytes = 0,
                LastWriteTimeLocal = DateTime.MinValue
            };
        }

        var head = ReadUtf8Header(fi, MaxHeaderBytes);
        var members = ParseAssociatedMembers(head);
        return new ProspectSummary
        {
            BaseName = baseName,
            FullPath = absolutePath,
            ProspectId = Match(head, ProspectIdRe),
            ProspectDtKey = Match(head, ProspectDtKeyRe),
            ProspectState = Match(head, ProspectStateRe),
            Difficulty = Match(head, DifficultyRe),
            LobbyName = Match(head, LobbyNameRe),
            FactionMissionDtKey = Match(head, FactionMissionDtKeyRe),
            ElapsedGameMinutes = MatchInt(head, ElapsedTimeRe),
            Cost = MatchInt(head, CostRe),
            Reward = MatchInt(head, RewardRe),
            Insurance = MatchBool(head, InsuranceRe),
            NoRespawns = MatchBool(head, NoRespawnsRe),
            SelectedDropPoint = MatchInt(head, SelectedDropPointRe),
            Members = members,
            FileSizeBytes = fi.Length,
            LastWriteTimeLocal = fi.LastWriteTime
        };
    }

    private static IReadOnlyList<ProspectMemberInfo> ParseAssociatedMembers(string head)
    {
        var inner = ExtractBracketArrayAfterKey(head, "\"AssociatedMembers\"");
        if (string.IsNullOrEmpty(inner))
        {
            return Array.Empty<ProspectMemberInfo>();
        }

        var list = new List<ProspectMemberInfo>();
        foreach (Match m in MemberBlockRe.Matches(inner))
        {
            if (!m.Success)
            {
                continue;
            }

            var playing = m.Groups["play"].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
            long.TryParse(m.Groups["xp"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var xp);
            list.Add(new ProspectMemberInfo(
                m.Groups["an"].Value,
                m.Groups["cn"].Value,
                m.Groups["uid"].Value,
                playing,
                xp,
                m.Groups["st"].Success ? m.Groups["st"].Value : null));
        }

        return list;
    }

    /// <summary>Extracts inner text of the first [...] array after <paramref name="key"/> (balanced brackets).</summary>
    private static string? ExtractBracketArrayAfterKey(string text, string key)
    {
        var idx = text.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        var bracket = text.IndexOf('[', idx + key.Length);
        if (bracket < 0)
        {
            return null;
        }

        var depth = 0;
        for (var i = bracket; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return text.Substring(bracket + 1, i - bracket - 1);
                }
            }
        }

        return null;
    }

    private static string? Match(string text, Regex re)
    {
        var m = re.Match(text);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static int? MatchInt(string text, Regex re)
    {
        var m = re.Match(text);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
        {
            return null;
        }

        return v;
    }

    private static bool? MatchBool(string text, Regex re)
    {
        var m = re.Match(text);
        if (!m.Success)
        {
            return null;
        }

        return m.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadUtf8Header(FileInfo fi, int maxBytes)
    {
        if (fi.Length == 0)
        {
            return string.Empty;
        }

        var len = (int)Math.Min(maxBytes, fi.Length);
        var buffer = new byte[len];
        using (var fs = fi.OpenRead())
        {
            fs.ReadExactly(buffer);
        }

        return Encoding.UTF8.GetString(buffer);
    }
}
