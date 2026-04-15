namespace IcarusServerManager.Models;

internal readonly struct PlayerLogLineResult(PlayerLogHintKind kind, string? playerName)
{
    public PlayerLogHintKind Kind { get; } = kind;

    public string? PlayerName { get; } = playerName;

    public static PlayerLogLineResult None => new(PlayerLogHintKind.None, null);
}
