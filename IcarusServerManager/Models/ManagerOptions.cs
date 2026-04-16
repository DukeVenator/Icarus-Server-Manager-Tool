namespace IcarusServerManager.Models;

internal sealed class ManagerOptions
{
    public string Theme { get; set; } = "Dark";
    public bool IntervalRestartEnabled { get; set; } = true;
    public int IntervalRestartMinutes { get; set; } = 60;
    public int IntervalWarningMinutes { get; set; } = 5;
    public bool CrashRestartEnabled { get; set; } = true;
    public int CrashRestartRetryDelaySeconds { get; set; } = 15;
    public int CrashRestartMaxAttempts { get; set; } = 5;
    public bool EmptyServerRestartEnabled { get; set; }
    public int EmptyServerRestartMinutes { get; set; } = 30;
    public int EmptyServerWarningMinutes { get; set; } = 5;
    public bool HighMemoryRestartEnabled { get; set; }
    public int HighMemoryMbThreshold { get; set; } = 8000;
    public int HighMemorySustainMinutes { get; set; } = 5;
    public int HighMemoryWarningMinutes { get; set; } = 2;
    public string UserDirOverride { get; set; } = string.Empty;
    public string SavedDirSuffix { get; set; } = string.Empty;
    public bool EnableDiscordWebhook { get; set; }
    public string DiscordWebhookUrl { get; set; } = string.Empty;

    /// <summary>Incremented when new option groups are added; used for one-time migration on load.</summary>
    public int OptionsSchemaVersion { get; set; } = 5;

    /// <summary>Game port for -Port (manager only; not written to ServerSettings.ini).</summary>
    public int LaunchGamePort { get; set; } = 17777;

    /// <summary>Steam query port for -QueryPort (manager only).</summary>
    public int LaunchQueryPort { get; set; } = 27015;

    public bool DiscordWebhookNotifyPlayerJoin { get; set; }
    public bool DiscordWebhookNotifyPlayerLeave { get; set; }
    public bool DiscordWebhookNotifyServerRestart { get; set; } = true;
    public bool DiscordWebhookNotifyServerStart { get; set; }
    public bool DiscordWebhookNotifyServerStop { get; set; }
    public bool DiscordWebhookNotifyChat { get; set; }
    public bool DiscordWebhookUseEmbeds { get; set; } = true;
    public string DiscordWebhookUsername { get; set; } = string.Empty;
    public string DiscordWebhookAvatarUrl { get; set; } = string.Empty;
    /// <summary>Minimum seconds between chat webhooks (0 = no limit).</summary>
    public int DiscordWebhookChatThrottleSeconds { get; set; } = 3;
    /// <summary>Minimum seconds between level-up / death webhooks (0 = no limit).</summary>
    public int DiscordWebhookGameplayThrottleSeconds { get; set; } = 5;

    public bool DiscordWebhookNotifyRestartWarning { get; set; }
    public bool DiscordWebhookNotifyScheduledUpdate { get; set; }
    public bool DiscordWebhookNotifySteamCmd { get; set; }
    public bool DiscordWebhookNotifyIniSaveFailed { get; set; }
    public bool DiscordWebhookNotifyIniValidationFailed { get; set; }
    public bool DiscordWebhookNotifyIniLoadFailed { get; set; }
    public bool DiscordWebhookNotifyLevelUp { get; set; }
    public bool DiscordWebhookNotifyPlayerDeath { get; set; }
    public bool DiscordWebhookNotifyUnexpectedExit { get; set; }
    public bool DiscordWebhookNotifyInstallPathIssue { get; set; }
    public bool DiscordWebhookNotifyRestartFailed { get; set; }
    /// <summary>Post a status summary on this interval (0 = disabled).</summary>
    public int DiscordWebhookHeartbeatIntervalHours { get; set; }

    /// <summary>Preset name: Minimal, Balanced, Verbose, QuietGame, or Custom.</summary>
    public string ConsoleLogPreset { get; set; } = "Balanced";

    public bool ConsoleShowManagerError { get; set; } = true;
    public bool ConsoleShowManagerWarn { get; set; } = true;
    public bool ConsoleShowManagerInfo { get; set; } = true;
    public bool ConsoleShowGameFatalError { get; set; } = true;
    public bool ConsoleShowGameWarning { get; set; } = true;
    public bool ConsoleShowGameImportant { get; set; } = true;
    public bool ConsoleShowGameVerbose { get; set; }
    public bool ConsoleShowGameGeneral { get; set; } = true;

    public bool AutoScrollConsole { get; set; } = true;
    public bool UpdateScheduleEnabled { get; set; }
    public string UpdateScheduleTime { get; set; } = "04:00";
    /// <summary>Optional -Log path (manager only; not in ServerSettings.ini).</summary>
    public string LaunchLogPath { get; set; } = string.Empty;
}
