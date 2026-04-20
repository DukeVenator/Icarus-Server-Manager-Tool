namespace IcarusServerManager.Models;

internal sealed class ManagerOptions
{
    public string Theme { get; set; } = "Dark";
    public bool IntervalRestartEnabled { get; set; } = true;
    public int IntervalRestartMinutes { get; set; } = 60;
    public int IntervalWarningMinutes { get; set; } = 5;
    public bool PauseIntervalRestartWhenEmpty { get; set; }
    public bool IntervalRestartUseEmptyIdleTimer { get; set; }
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

    /// <summary>When true, Windows stop sequence tries console Ctrl+C before stdin quit/exit.</summary>
    public bool GracefulShutdownTryCtrlC { get; set; } = true;

    /// <summary>Max seconds to wait for graceful exit before force kill (clamped in UI).</summary>
    public int GracefulShutdownWaitSeconds { get; set; } = 120;

    public string UserDirOverride { get; set; } = string.Empty;
    public string SavedDirSuffix { get; set; } = string.Empty;
    public bool EnableDiscordWebhook { get; set; }
    public string DiscordWebhookUrl { get; set; } = string.Empty;

    /// <summary>Incremented when new option groups are added; used for one-time migration on load.</summary>
    public int OptionsSchemaVersion { get; set; } = 9;

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

    /// <summary>Prefix webhook titles with kind emoji (embed and plain modes).</summary>
    public bool DiscordWebhookUseTitleEmojis { get; set; } = true;

    /// <summary>Show embed author (manager name + store link) on rich cards.</summary>
    public bool DiscordWebhookShowEmbedAuthor { get; set; } = true;

    /// <summary>Include UTC timestamp on Discord embeds.</summary>
    public bool DiscordWebhookShowEmbedTimestamp { get; set; } = true;

    /// <summary>Add game/query port field on alert embeds when space allows.</summary>
    public bool DiscordWebhookShowPortsOnEmbeds { get; set; } = true;

    /// <summary>Include session name on operational / diagnostic embeds when set.</summary>
    public bool DiscordWebhookShowSessionOnEmbeds { get; set; } = true;

    /// <summary>Include prospect line on server-ready embed when known.</summary>
    public bool DiscordWebhookIncludeProspectOnStart { get; set; } = true;

    /// <summary>Include last policy / restart reason line on heartbeat embeds.</summary>
    public bool DiscordWebhookHeartbeatShowPolicyLine { get; set; } = true;

    /// <summary>Use short themed field labels (Beacon, Crew) instead of neutral (Server, Player).</summary>
    public bool DiscordWebhookUseThemedLabels { get; set; } = true;

    /// <summary>Strip common markdown emphasis from webhook descriptions before send.</summary>
    public bool DiscordWebhookPlainTextDescriptions { get; set; }

    /// <summary>When non-empty, replaces the default embed footer (max length enforced on save).</summary>
    public string DiscordWebhookCustomFooter { get; set; } = string.Empty;

    /// <summary>Max characters for webhook body text (clamped in UI and service).</summary>
    public int DiscordWebhookDescriptionMaxChars { get; set; } = 3500;

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
    public bool ManagerUpdateCheckEnabled { get; set; } = true;
    public int ManagerUpdateCheckIntervalHours { get; set; } = 6;
    public bool ManagerUpdateIncludePrerelease { get; set; }
    public bool ManagerUpdatePromptBeforeDownload { get; set; } = true;
    /// <summary>Optional -Log path (manager only; not in ServerSettings.ini).</summary>
    public string LaunchLogPath { get; set; } = string.Empty;
}
