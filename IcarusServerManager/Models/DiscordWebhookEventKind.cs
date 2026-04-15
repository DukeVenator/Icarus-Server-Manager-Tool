namespace IcarusServerManager.Models;

/// <summary>Categories used for Discord webhook toggles and embed styling.</summary>
internal enum DiscordWebhookEventKind
{
    PlayerJoin,
    PlayerLeave,
    ServerRestart,
    ServerRestartFailed,
    ServerStart,
    ServerStop,
    UnexpectedExit,
    Chat,
    RestartWarning,
    ScheduledUpdateWindow,
    SteamCmdFinished,
    IniSaveFailed,
    IniValidationFailed,
    IniLoadFailed,
    LevelUp,
    PlayerDeath,
    InstallPathIssue,
    ManagerHeartbeat
}
