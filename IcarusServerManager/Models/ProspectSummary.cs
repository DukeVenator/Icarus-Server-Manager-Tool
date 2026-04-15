namespace IcarusServerManager.Models;

/// <summary>
/// Lightweight metadata read from the start of a dedicated-server prospect JSON (ProspectInfo block).
/// </summary>
internal sealed class ProspectSummary
{
    public required string BaseName { get; init; }

    public required string FullPath { get; init; }

    public string? ProspectId { get; init; }

    /// <summary>Unreal data-table / map key (e.g. Outpost006_Olympus, OpenWorld_Elysium).</summary>
    public string? ProspectDtKey { get; init; }

    public string? ProspectState { get; init; }

    public string? Difficulty { get; init; }

    public string? LobbyName { get; init; }

    public string? FactionMissionDtKey { get; init; }

    /// <summary>Server-side elapsed minutes (integer in JSON).</summary>
    public int? ElapsedGameMinutes { get; init; }

    public int? Cost { get; init; }

    public int? Reward { get; init; }

    public bool? Insurance { get; init; }

    public bool? NoRespawns { get; init; }

    public int? SelectedDropPoint { get; init; }

    public IReadOnlyList<ProspectMemberInfo> Members { get; init; } = Array.Empty<ProspectMemberInfo>();

    public long FileSizeBytes { get; init; }

    public DateTime LastWriteTimeLocal { get; init; }

    public int OnlineMemberCount => Members.Count(m => m.IsCurrentlyPlaying);

    public override string ToString()
    {
        var map = ProspectDtKey ?? "?";
        var diff = Difficulty ?? "?";
        var state = ProspectState ?? "?";
        var online = OnlineMemberCount;
        return $"{BaseName} — {map} · {diff} · {state} · online {online}";
    }

    public string BuildDetailsText()
    {
        var lines = new List<string>
        {
            $"File: {Path.GetFileName(FullPath)}",
            $"Size on disk: {FormatBytes(FileSizeBytes)}",
            $"Last modified: {LastWriteTimeLocal:g}",
            string.Empty,
            $"ProspectID: {ProspectId ?? "—"}",
            $"ProspectDTKey (map): {ProspectDtKey ?? "—"}",
            $"ProspectState: {ProspectState ?? "—"}",
            $"Difficulty: {Difficulty ?? "—"}",
            $"LobbyName: {LobbyName ?? "—"}",
            $"FactionMissionDTKey: {FactionMissionDtKey ?? "—"}",
            $"ElapsedTime (min): {(ElapsedGameMinutes?.ToString() ?? "—")}",
            $"Cost / Reward: {(Cost?.ToString() ?? "—")} / {(Reward?.ToString() ?? "—")}",
            $"Insurance: {(Insurance?.ToString() ?? "—")}  NoRespawns: {(NoRespawns?.ToString() ?? "—")}",
            $"SelectedDropPoint: {(SelectedDropPoint?.ToString() ?? "—")}",
            string.Empty,
            $"Associated members: {Members.Count} (IsCurrentlyPlaying=true: {OnlineMemberCount})"
        };

        if (Members.Count > 0)
        {
            lines.Add(string.Empty);
            foreach (var m in Members)
            {
                var flag = m.IsCurrentlyPlaying ? "●" : "○";
                lines.Add($"{flag} {m.CharacterName} ({m.AccountName})  Steam:{m.UserId}  XP:{m.Experience}  {m.Status ?? ""}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        const double kb = 1024;
        if (bytes < kb * kb)
        {
            return $"{bytes / kb:0.#} KB";
        }

        if (bytes < kb * kb * kb)
        {
            return $"{bytes / (kb * kb):0.#} MB";
        }

        return $"{bytes / (kb * kb * kb):0.##} GB";
    }
}
