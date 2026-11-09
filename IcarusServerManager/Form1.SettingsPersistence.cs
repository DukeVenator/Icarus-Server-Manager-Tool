using IcarusServerManager.Models;
using IcarusServerManager.Services;
using IcarusServerManager.UI;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace IcarusServerManager;

public partial class Form1 : Form
{
    private string? TryResolveProspectsDirectorySilent()
    {
        var userDir = GetString("UserDirOverride", string.Empty);
        var savedSuffix = GetString("SavedDirSuffix", string.Empty);
        return ProspectDirectoryResolver.TryResolveProspectsDirectory(serverLocationBox.Text, userDir, savedSuffix, iniService);
    }

    private string? TryGetLastProspectJsonPath()
    {
        var dir = TryResolveProspectsDirectorySilent();
        var name = GetString("LastProspectName", string.Empty).Trim();
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name))
        {
            return null;
        }

        return Path.Combine(dir, name + ".json");
    }

    private ProspectSummary? TryReadProspectSummaryForDiscord()
    {
        try
        {
            var path = TryGetLastProspectJsonPath();
            if (path == null || !File.Exists(path))
            {
                return null;
            }

            return ProspectSummaryReader.Read(path);
        }
        catch
        {
            return null;
        }
    }

    private void LoadPersistedData()
    {
        var optionsLoadFailed = false;
        try
        {
            managerOptions = optionsService.Load(
                () => (Properties.Settings.Default.gamePort, Properties.Settings.Default.queryPort));
        }
        catch (InvalidDataException ex)
        {
            // The malformed file is intentionally left untouched. Defaults apply to this session
            // in memory only; no automatic save is allowed to replace the user's file.
            optionsLoadFailed = true;
            logger.Error("Manager options could not be loaded.", ex);
            MessageBox.Show(
                this,
                $"{ex.Message}\n\nThe existing file was left unchanged. Default settings are in use for this session only and are not saved automatically.",
                "Manager settings",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            managerOptions = new ManagerOptions();
        }

        _optionsLoadFailed = optionsLoadFailed;

        NormalizeConsoleLogPresetName(managerOptions);
        EnsureLaunchArgumentsValidOrDefault();
        serverLocationBox.Text = Properties.Settings.Default.serverLocation;
        serverNameBox.Text = Properties.Settings.Default.serverName;
        queryPortBox.Text = Properties.Settings.Default.queryPort.ToString();
        gamePortBox.Text = Properties.Settings.Default.gamePort.ToString();
        SetRestartIntervalSelection(optionsLoadFailed);
        LoadManagerOptionsToUi();
        LoadIniToUi();
        SyncLegacyPortTextBoxesFromManagerOptions();
    }

    /// <summary>
    /// Selects the legacy restart interval without letting its change handler persist manager
    /// options. That handler is an automatic save; after a failed load it would overwrite the
    /// malformed file with in-memory defaults.
    /// </summary>
    private void SetRestartIntervalSelection(bool suppressAutoSave)
    {
        var index = Math.Clamp((Properties.Settings.Default.restartInterval / 60) - 1, 0, restartTimeBox.Items.Count - 1);
        if (!suppressAutoSave)
        {
            restartTimeBox.SelectedIndex = index;
            return;
        }

        // The designer handler is declared with a non-nullable sender; the compiler warns about the
        // delegate nullability difference when it is added/removed explicitly.
#pragma warning disable CS8622
        restartTimeBox.SelectedIndexChanged -= restartTimeBox_SelectedIndexChanged;
        try
        {
            restartTimeBox.SelectedIndex = index;
        }
        finally
        {
            restartTimeBox.SelectedIndexChanged += restartTimeBox_SelectedIndexChanged;
        }
#pragma warning restore CS8622
    }

    /// <summary>
    /// Ports and -Log were historically split between Application Settings and manager JSON; they are
    /// manager-owned (not ServerSettings.ini). The legacy copy runs inside the ordered migration
    /// pipeline in <see cref="ManagerOptionsService"/> before the schema-4 guard is applied.
    /// </summary>
    private static void NormalizeConsoleLogPresetName(ManagerOptions o)
    {
        foreach (var p in ConsoleLogFilter.PresetNames)
        {
            if (string.Equals(p, o.ConsoleLogPreset, StringComparison.OrdinalIgnoreCase))
            {
                o.ConsoleLogPreset = p;
                return;
            }
        }

        o.ConsoleLogPreset = "Balanced";
    }

    private static void EnsureLaunchArgumentsValidOrDefault(ManagerOptions o)
    {
        if (o.LaunchGamePort is < 1 or > 65535)
        {
            o.LaunchGamePort = 17777;
        }

        if (o.LaunchQueryPort is < 1 or > 65535)
        {
            o.LaunchQueryPort = 27015;
        }
    }

    private void EnsureLaunchArgumentsValidOrDefault() => EnsureLaunchArgumentsValidOrDefault(managerOptions);

    private void SyncLegacyPortTextBoxesFromManagerOptions()
    {
        gamePortBox.Text = managerOptions.LaunchGamePort.ToString();
        queryPortBox.Text = managerOptions.LaunchQueryPort.ToString();
        Properties.Settings.Default.gamePort = managerOptions.LaunchGamePort;
        Properties.Settings.Default.queryPort = managerOptions.LaunchQueryPort;
        Properties.Settings.Default.Save();
    }

    private void PersistLaunchArgumentsFromUi()
    {
        managerOptions.LaunchGamePort = Math.Clamp(GetInt("LaunchGamePort", managerOptions.LaunchGamePort), 1, 65535);
        managerOptions.LaunchQueryPort = Math.Clamp(GetInt("LaunchQueryPort", managerOptions.LaunchQueryPort), 1, 65535);
        managerOptions.LaunchLogPath = GetString("LaunchLogPath", managerOptions.LaunchLogPath);
        EnsureLaunchArgumentsValidOrDefault();
        PersistManagerOptions(userInitiated: false);
        SyncLegacyPortTextBoxesFromManagerOptions();
    }

    private void LoadManagerOptionsToUi()
    {
        SetControl("Theme", managerOptions.Theme);
        SetControl("IntervalRestartEnabled", managerOptions.IntervalRestartEnabled);
        SetControl("IntervalRestartMinutes", managerOptions.IntervalRestartMinutes);
        SetControl("IntervalWarningMinutes", managerOptions.IntervalWarningMinutes);
        SetControl("PauseIntervalRestartWhenEmpty", managerOptions.PauseIntervalRestartWhenEmpty);
        SetControl("IntervalRestartUseEmptyIdleTimer", managerOptions.IntervalRestartUseEmptyIdleTimer);
        SetControl("CrashRestartEnabled", managerOptions.CrashRestartEnabled);
        SetControl("CrashRestartRetryDelaySeconds", managerOptions.CrashRestartRetryDelaySeconds);
        SetControl("CrashRestartMaxAttempts", managerOptions.CrashRestartMaxAttempts);
        SetControl("EmptyServerRestartEnabled", managerOptions.EmptyServerRestartEnabled);
        SetControl("EmptyServerRestartMinutes", managerOptions.EmptyServerRestartMinutes);
        SetControl("EmptyServerWarningMinutes", managerOptions.EmptyServerWarningMinutes);
        SetControl("HighMemoryRestartEnabled", managerOptions.HighMemoryRestartEnabled);
        SetControl("HighMemoryMbThreshold", managerOptions.HighMemoryMbThreshold);
        SetControl("HighMemorySustainMinutes", managerOptions.HighMemorySustainMinutes);
        SetControl("HighMemoryWarningMinutes", managerOptions.HighMemoryWarningMinutes);
        SetControl("GracefulShutdownTryCtrlC", managerOptions.GracefulShutdownTryCtrlC);
        SetControl("GracefulShutdownWaitSeconds", managerOptions.GracefulShutdownWaitSeconds);
        SetControl("UserDirOverride", managerOptions.UserDirOverride);
        SetControl("SavedDirSuffix", managerOptions.SavedDirSuffix);
        SetControl("EnableDiscordWebhook", managerOptions.EnableDiscordWebhook);
        SetControl("DiscordWebhookUrl", managerOptions.DiscordWebhookUrl);
        SetControl("DiscordWebhookNotifyPlayerJoin", managerOptions.DiscordWebhookNotifyPlayerJoin);
        SetControl("DiscordWebhookNotifyPlayerLeave", managerOptions.DiscordWebhookNotifyPlayerLeave);
        SetControl("DiscordWebhookNotifyServerRestart", managerOptions.DiscordWebhookNotifyServerRestart);
        SetControl("DiscordWebhookNotifyServerStart", managerOptions.DiscordWebhookNotifyServerStart);
        SetControl("DiscordWebhookNotifyServerStop", managerOptions.DiscordWebhookNotifyServerStop);
        SetControl("DiscordWebhookNotifyUnexpectedExit", managerOptions.DiscordWebhookNotifyUnexpectedExit);
        SetControl("DiscordWebhookNotifyRestartWarning", managerOptions.DiscordWebhookNotifyRestartWarning);
        SetControl("DiscordWebhookNotifyScheduledUpdate", managerOptions.DiscordWebhookNotifyScheduledUpdate);
        SetControl("DiscordWebhookNotifySteamCmd", managerOptions.DiscordWebhookNotifySteamCmd);
        SetControl("DiscordWebhookNotifyIniSaveFailed", managerOptions.DiscordWebhookNotifyIniSaveFailed);
        SetControl("DiscordWebhookNotifyIniValidationFailed", managerOptions.DiscordWebhookNotifyIniValidationFailed);
        SetControl("DiscordWebhookNotifyIniLoadFailed", managerOptions.DiscordWebhookNotifyIniLoadFailed);
        SetControl("DiscordWebhookNotifyInstallPathIssue", managerOptions.DiscordWebhookNotifyInstallPathIssue);
        SetControl("DiscordWebhookNotifyRestartFailed", managerOptions.DiscordWebhookNotifyRestartFailed);
        SetControl("DiscordWebhookNotifyLevelUp", managerOptions.DiscordWebhookNotifyLevelUp);
        SetControl("DiscordWebhookNotifyPlayerDeath", managerOptions.DiscordWebhookNotifyPlayerDeath);
        SetControl("DiscordWebhookNotifyChat", managerOptions.DiscordWebhookNotifyChat);
        SetControl("DiscordWebhookChatThrottleSeconds", managerOptions.DiscordWebhookChatThrottleSeconds);
        SetControl("DiscordWebhookGameplayThrottleSeconds", managerOptions.DiscordWebhookGameplayThrottleSeconds);
        SetControl("DiscordWebhookHeartbeatIntervalHours", managerOptions.DiscordWebhookHeartbeatIntervalHours);
        SetControl("DiscordWebhookUseEmbeds", managerOptions.DiscordWebhookUseEmbeds);
        SetControl("DiscordWebhookUseTitleEmojis", managerOptions.DiscordWebhookUseTitleEmojis);
        SetControl("DiscordWebhookShowEmbedAuthor", managerOptions.DiscordWebhookShowEmbedAuthor);
        SetControl("DiscordWebhookShowEmbedTimestamp", managerOptions.DiscordWebhookShowEmbedTimestamp);
        SetControl("DiscordWebhookShowPortsOnEmbeds", managerOptions.DiscordWebhookShowPortsOnEmbeds);
        SetControl("DiscordWebhookShowSessionOnEmbeds", managerOptions.DiscordWebhookShowSessionOnEmbeds);
        SetControl("DiscordWebhookIncludeProspectOnStart", managerOptions.DiscordWebhookIncludeProspectOnStart);
        SetControl("DiscordWebhookHeartbeatShowPolicyLine", managerOptions.DiscordWebhookHeartbeatShowPolicyLine);
        SetControl("DiscordWebhookUseThemedLabels", managerOptions.DiscordWebhookUseThemedLabels);
        SetControl("DiscordWebhookPlainTextDescriptions", managerOptions.DiscordWebhookPlainTextDescriptions);
        SetControl("DiscordWebhookCustomFooter", managerOptions.DiscordWebhookCustomFooter);
        SetControl("DiscordWebhookDescriptionMaxChars", managerOptions.DiscordWebhookDescriptionMaxChars);
        SetControl("DiscordWebhookUsername", managerOptions.DiscordWebhookUsername);
        SetControl("DiscordWebhookAvatarUrl", managerOptions.DiscordWebhookAvatarUrl);
        SetControl("AutoScrollConsole", managerOptions.AutoScrollConsole);
        SetControl("UpdateScheduleEnabled", managerOptions.UpdateScheduleEnabled);
        SetControl("UpdateScheduleTime", managerOptions.UpdateScheduleTime);
        SetControl("ManagerUpdateCheckEnabled", managerOptions.ManagerUpdateCheckEnabled);
        SetControl("ManagerUpdateCheckIntervalHours", managerOptions.ManagerUpdateCheckIntervalHours);
        SetControl("ManagerUpdateIncludePrerelease", managerOptions.ManagerUpdateIncludePrerelease);
        SetControl("ManagerUpdatePromptBeforeDownload", managerOptions.ManagerUpdatePromptBeforeDownload);
        SetControl("LaunchGamePort", managerOptions.LaunchGamePort);
        SetControl("LaunchQueryPort", managerOptions.LaunchQueryPort);
        SetControl("LaunchLogPath", managerOptions.LaunchLogPath);
        ReflectConsoleLoggingControlsFromModel();
        UpdateConsoleAutoScrollButtonAppearance();
    }

    /// <summary>
    /// Releases the webhook HTTP client once the form is really closing. Safe to call more than once.
    /// </summary>
    private void DisposeAutomationServiceOnClose()
    {
        try
        {
            automationService.Dispose();
        }
        catch (Exception ex)
        {
            logger.Error("Could not dispose the automation service while closing.", ex);
        }
    }

    /// <summary>
    /// True when the options file existed but could not be parsed at startup. Automatic saves are
    /// suppressed in that state so the user's unreadable file is never silently replaced by
    /// in-memory defaults; an explicit settings save or an import may still replace it.
    /// </summary>
    private bool _optionsLoadFailed;

    /// <summary>
    /// Single write path for manager options. Returns false when the write was deliberately skipped.
    /// </summary>
    private bool PersistManagerOptions(bool userInitiated)
    {
        if (_optionsLoadFailed && !userInitiated)
        {
            logger.Warn("Manager options were not loaded at startup; automatic save skipped to preserve the existing file.");
            UpdateStatus("Settings not saved — the manager settings file could not be read.");
            return false;
        }

        if (_optionsLoadFailed)
        {
            _optionsLoadFailed = false;
            logger.Info("Replacing the unreadable manager options file after an explicit user save or import.");
        }

        optionsService.Save(managerOptions);
        return true;
    }

    private void SaveManagerOptionsFromUi(bool isExplicitSave)
    {
        managerOptions.Theme = GetString("Theme", "Dark");
        managerOptions.IntervalRestartEnabled = GetBool("IntervalRestartEnabled", true);
        managerOptions.IntervalRestartMinutes = GetInt("IntervalRestartMinutes", 60);
        managerOptions.IntervalWarningMinutes = GetInt("IntervalWarningMinutes", 5);
        managerOptions.PauseIntervalRestartWhenEmpty = GetBool("PauseIntervalRestartWhenEmpty", false);
        managerOptions.IntervalRestartUseEmptyIdleTimer = GetBool("IntervalRestartUseEmptyIdleTimer", false);
        managerOptions.CrashRestartEnabled = GetBool("CrashRestartEnabled", true);
        managerOptions.CrashRestartRetryDelaySeconds = GetInt("CrashRestartRetryDelaySeconds", 15);
        managerOptions.CrashRestartMaxAttempts = GetInt("CrashRestartMaxAttempts", 5);
        managerOptions.EmptyServerRestartEnabled = GetBool("EmptyServerRestartEnabled", false);
        managerOptions.EmptyServerRestartMinutes = GetInt("EmptyServerRestartMinutes", 30);
        managerOptions.EmptyServerWarningMinutes = GetInt("EmptyServerWarningMinutes", 5);
        managerOptions.HighMemoryRestartEnabled = GetBool("HighMemoryRestartEnabled", false);
        managerOptions.HighMemoryMbThreshold = GetInt("HighMemoryMbThreshold", 8000);
        managerOptions.HighMemorySustainMinutes = GetInt("HighMemorySustainMinutes", 5);
        managerOptions.HighMemoryWarningMinutes = GetInt("HighMemoryWarningMinutes", 2);
        managerOptions.GracefulShutdownTryCtrlC = GetBool("GracefulShutdownTryCtrlC", true);
        managerOptions.GracefulShutdownWaitSeconds = Math.Clamp(GetInt("GracefulShutdownWaitSeconds", 120), 10, 900);
        managerOptions.UserDirOverride = GetString("UserDirOverride", string.Empty);
        managerOptions.SavedDirSuffix = GetString("SavedDirSuffix", string.Empty);
        managerOptions.EnableDiscordWebhook = GetBool("EnableDiscordWebhook", false);
        managerOptions.DiscordWebhookUrl = GetString("DiscordWebhookUrl", string.Empty);
        managerOptions.DiscordWebhookNotifyPlayerJoin = GetBool("DiscordWebhookNotifyPlayerJoin", false);
        managerOptions.DiscordWebhookNotifyPlayerLeave = GetBool("DiscordWebhookNotifyPlayerLeave", false);
        managerOptions.DiscordWebhookNotifyServerRestart = GetBool("DiscordWebhookNotifyServerRestart", true);
        managerOptions.DiscordWebhookNotifyServerStart = GetBool("DiscordWebhookNotifyServerStart", false);
        managerOptions.DiscordWebhookNotifyServerStop = GetBool("DiscordWebhookNotifyServerStop", false);
        managerOptions.DiscordWebhookNotifyUnexpectedExit = GetBool("DiscordWebhookNotifyUnexpectedExit", false);
        managerOptions.DiscordWebhookNotifyRestartWarning = GetBool("DiscordWebhookNotifyRestartWarning", false);
        managerOptions.DiscordWebhookNotifyScheduledUpdate = GetBool("DiscordWebhookNotifyScheduledUpdate", false);
        managerOptions.DiscordWebhookNotifySteamCmd = GetBool("DiscordWebhookNotifySteamCmd", false);
        managerOptions.DiscordWebhookNotifyIniSaveFailed = GetBool("DiscordWebhookNotifyIniSaveFailed", false);
        managerOptions.DiscordWebhookNotifyIniValidationFailed = GetBool("DiscordWebhookNotifyIniValidationFailed", false);
        managerOptions.DiscordWebhookNotifyIniLoadFailed = GetBool("DiscordWebhookNotifyIniLoadFailed", false);
        managerOptions.DiscordWebhookNotifyInstallPathIssue = GetBool("DiscordWebhookNotifyInstallPathIssue", false);
        managerOptions.DiscordWebhookNotifyRestartFailed = GetBool("DiscordWebhookNotifyRestartFailed", false);
        managerOptions.DiscordWebhookNotifyLevelUp = GetBool("DiscordWebhookNotifyLevelUp", false);
        managerOptions.DiscordWebhookNotifyPlayerDeath = GetBool("DiscordWebhookNotifyPlayerDeath", false);
        managerOptions.DiscordWebhookNotifyChat = GetBool("DiscordWebhookNotifyChat", false);
        managerOptions.DiscordWebhookChatThrottleSeconds = Math.Clamp(GetInt("DiscordWebhookChatThrottleSeconds", 3), 0, 120);
        managerOptions.DiscordWebhookGameplayThrottleSeconds = Math.Clamp(GetInt("DiscordWebhookGameplayThrottleSeconds", 5), 0, 120);
        managerOptions.DiscordWebhookHeartbeatIntervalHours = Math.Clamp(GetInt("DiscordWebhookHeartbeatIntervalHours", 0), 0, 168);
        managerOptions.DiscordWebhookUseEmbeds = GetBool("DiscordWebhookUseEmbeds", true);
        managerOptions.DiscordWebhookUseTitleEmojis = GetBool("DiscordWebhookUseTitleEmojis", true);
        managerOptions.DiscordWebhookShowEmbedAuthor = GetBool("DiscordWebhookShowEmbedAuthor", true);
        managerOptions.DiscordWebhookShowEmbedTimestamp = GetBool("DiscordWebhookShowEmbedTimestamp", true);
        managerOptions.DiscordWebhookShowPortsOnEmbeds = GetBool("DiscordWebhookShowPortsOnEmbeds", true);
        managerOptions.DiscordWebhookShowSessionOnEmbeds = GetBool("DiscordWebhookShowSessionOnEmbeds", true);
        managerOptions.DiscordWebhookIncludeProspectOnStart = GetBool("DiscordWebhookIncludeProspectOnStart", true);
        managerOptions.DiscordWebhookHeartbeatShowPolicyLine = GetBool("DiscordWebhookHeartbeatShowPolicyLine", true);
        managerOptions.DiscordWebhookUseThemedLabels = GetBool("DiscordWebhookUseThemedLabels", true);
        managerOptions.DiscordWebhookPlainTextDescriptions = GetBool("DiscordWebhookPlainTextDescriptions", false);
        var footer = GetString("DiscordWebhookCustomFooter", string.Empty).Trim();
        managerOptions.DiscordWebhookCustomFooter = footer.Length > 2048 ? footer[..2048] : footer;
        managerOptions.DiscordWebhookDescriptionMaxChars = Math.Clamp(GetInt("DiscordWebhookDescriptionMaxChars", 3500), 800, 4096);
        managerOptions.DiscordWebhookUsername = GetString("DiscordWebhookUsername", string.Empty);
        managerOptions.DiscordWebhookAvatarUrl = GetString("DiscordWebhookAvatarUrl", string.Empty);
        managerOptions.AutoScrollConsole = GetBool("AutoScrollConsole", true);
        managerOptions.UpdateScheduleEnabled = GetBool("UpdateScheduleEnabled", false);
        var scheduleText = GetString("UpdateScheduleTime", "04:00").Trim();
        if (TimeSpan.TryParseExact(scheduleText, @"hh\:mm", CultureInfo.InvariantCulture, out var schedule) &&
            schedule >= TimeSpan.Zero &&
            schedule < TimeSpan.FromDays(1))
        {
            managerOptions.UpdateScheduleTime = schedule.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }
        else
        {
            // The scheduler skips an unparsable time, so reject it here instead of saving a value
            // that would silently never run.
            logger.Warn($"Invalid scheduled restart time '{scheduleText}'; keeping '{managerOptions.UpdateScheduleTime}'.");
            SetControl("UpdateScheduleTime", managerOptions.UpdateScheduleTime);
            UpdateStatus("Scheduled restart time must be HH:mm — the previous value was kept.");
        }
        managerOptions.ManagerUpdateCheckEnabled = GetBool("ManagerUpdateCheckEnabled", true);
        managerOptions.ManagerUpdateCheckIntervalHours = Math.Clamp(GetInt("ManagerUpdateCheckIntervalHours", 6), 1, 168);
        managerOptions.ManagerUpdateIncludePrerelease = GetBool("ManagerUpdateIncludePrerelease", false);
        managerOptions.ManagerUpdatePromptBeforeDownload = GetBool("ManagerUpdatePromptBeforeDownload", true);
        managerOptions.LaunchGamePort = Math.Clamp(GetInt("LaunchGamePort", managerOptions.LaunchGamePort), 1, 65535);
        managerOptions.LaunchQueryPort = Math.Clamp(GetInt("LaunchQueryPort", managerOptions.LaunchQueryPort), 1, 65535);
        managerOptions.LaunchLogPath = GetString("LaunchLogPath", string.Empty);
        SyncConsoleLoggingFromUiToModel();
        PersistManagerOptions(userInitiated: isExplicitSave);
        SyncLegacyPortTextBoxesFromManagerOptions();
        ApplyTheme();
        logger.Info("Manager options saved.");
        UpdateStatus("Manager options saved");
        if (isExplicitSave)
        {
            MessageBox.Show(
                "Manager options were saved.\n\nThis stores theme, restart policies, UserDir/SavedDir overrides, webhook, console behavior, update schedule, and launch arguments (-Port, -QueryPort, -Log).",
                "Saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        UpdateConsoleAutoScrollButtonAppearance();
    }

    private void ConfigureStaticTooltips()
    {
        toolTips.AutoPopDelay = 8000;
        toolTips.InitialDelay = 300;
        toolTips.ReshowDelay = 200;
        toolTips.ShowAlways = true;

        toolTips.SetToolTip(startServerButton, "Start or stop the dedicated server process.");
        toolTips.SetToolTip(forceKillServerButton, "Immediately terminate the server process. Skips graceful shutdown; saves may be lost.");
        toolTips.SetToolTip(installServerButton, "Install or update Icarus Dedicated Server using SteamCMD.");
        toolTips.SetToolTip(selectLocationButton, "Choose the dedicated server install folder (game root).");
        toolTips.SetToolTip(serverLocationBox, "Dedicated server install folder — used for the executable, ServerSettings.ini, and prospects.");
        toolTips.SetToolTip(restartTimeBox, "Legacy restart interval selector; policy settings are in Manager Settings.");
    }

    private string CurrentIniPath()
    {
        return iniService.ResolveIniPath(serverLocationBox.Text, managerOptions.UserDirOverride, managerOptions.SavedDirSuffix);
    }

    private void LoadIniToUi()
    {
        try
        {
            currentServerSettings = iniService.Load(CurrentIniPath());
            SetControl("SteamServerName", currentServerSettings.SteamServerName);
            SetControl("SessionName", currentServerSettings.SessionName);
            SetControl("JoinPassword", currentServerSettings.JoinPassword);
            SetControl("MaxPlayers", currentServerSettings.MaxPlayers);
            SetControl("ShutdownIfNotJoinedFor", (int)currentServerSettings.ShutdownIfNotJoinedFor);
            SetControl("ShutdownIfEmptyFor", (int)currentServerSettings.ShutdownIfEmptyFor);
            SetControl("AdminPassword", currentServerSettings.AdminPassword);
            SetControl("LoadProspect", currentServerSettings.LoadProspect);
            SetControl("CreateProspect", currentServerSettings.CreateProspect);
            SetControl("ResumeProspect", currentServerSettings.ResumeProspect);
            SetControl("LastProspectName", currentServerSettings.LastProspectName);
            SetControl("AllowNonAdminsToLaunchProspects", currentServerSettings.AllowNonAdminsToLaunchProspects);
            SetControl("AllowNonAdminsToDeleteProspects", currentServerSettings.AllowNonAdminsToDeleteProspects);
            SetControl("FiberFoliageRespawn", currentServerSettings.FiberFoliageRespawn);
            SetControl("LargeStonesRespawn", currentServerSettings.LargeStonesRespawn);
            SetControl("GameSaveFrequency", (int)currentServerSettings.GameSaveFrequency);
            SetControl("SaveGameOnExit", currentServerSettings.SaveGameOnExit);
            logger.Info($"INI loaded from {CurrentIniPath()}");
            RefreshLastWorldTab();
        }
        catch (Exception ex)
        {
            logger.Error("Failed to load INI.", ex);
            PostDiscordWebhook(
                DiscordWebhookEventKind.IniLoadFailed,
                "Could not read ServerSettings.ini",
                DiscordWebhookEmbedFactory.TruncateDescription(ex.ToString(), managerOptions),
                DiscordWebhookEmbedFactory.BuildDiagnosticExtras(managerOptions, currentServerSettings));
            MessageBox.Show("Unable to load ServerSettings.ini. Check location and permissions.");
        }
    }

    private void SaveUiToIni(bool createBackup)
    {
        try
        {
            currentServerSettings = new DedicatedServerSettingsModel
            {
                SteamServerName = GetString("SteamServerName", string.Empty),
                SessionName = GetString("SessionName", string.Empty),
                JoinPassword = GetString("JoinPassword", string.Empty),
                MaxPlayers = GetInt("MaxPlayers", 8),
                ShutdownIfNotJoinedFor = GetInt("ShutdownIfNotJoinedFor", 600),
                ShutdownIfEmptyFor = GetInt("ShutdownIfEmptyFor", 600),
                AdminPassword = GetString("AdminPassword", string.Empty),
                LoadProspect = GetString("LoadProspect", string.Empty),
                CreateProspect = GetString("CreateProspect", string.Empty),
                ResumeProspect = GetBool("ResumeProspect", true),
                LastProspectName = GetString("LastProspectName", string.Empty),
                AllowNonAdminsToLaunchProspects = GetBool("AllowNonAdminsToLaunchProspects", true),
                AllowNonAdminsToDeleteProspects = GetBool("AllowNonAdminsToDeleteProspects", false),
                FiberFoliageRespawn = GetBool("FiberFoliageRespawn", false),
                LargeStonesRespawn = GetBool("LargeStonesRespawn", false),
                GameSaveFrequency = GetInt("GameSaveFrequency", 10),
                SaveGameOnExit = GetBool("SaveGameOnExit", true)
            };

            var validation = iniService.Validate(currentServerSettings);
            if (!validation.IsValid)
            {
                PostDiscordWebhook(
                    DiscordWebhookEventKind.IniValidationFailed,
                    "ServerSettings.ini blocked",
                    DiscordWebhookEmbedFactory.TruncateDescription(string.Join("\n", validation.Errors), managerOptions),
                    DiscordWebhookEmbedFactory.BuildDiagnosticExtras(managerOptions, currentServerSettings));
                MessageBox.Show(string.Join(Environment.NewLine, validation.Errors), "Validation failed");
                return;
            }

            if (validation.Warnings.Count > 0)
            {
                logger.Warn(string.Join(" | ", validation.Warnings));
            }

            if (createBackup)
            {
                BackupIni();
            }
            iniService.Save(CurrentIniPath(), currentServerSettings);
            PersistLaunchArgumentsFromUi();
            logger.Info($"INI saved to {CurrentIniPath()}");
        }
        catch (Exception ex)
        {
            logger.Error("Failed saving INI.", ex);
            PostDiscordWebhook(
                DiscordWebhookEventKind.IniSaveFailed,
                "Could not write ServerSettings.ini",
                DiscordWebhookEmbedFactory.TruncateDescription(ex.ToString(), managerOptions),
                DiscordWebhookEmbedFactory.BuildDiagnosticExtras(managerOptions, currentServerSettings));
            MessageBox.Show("Unable to save ServerSettings.ini.");
        }
    }

    private void BackupIni()
    {
        var path = CurrentIniPath();
        if (!File.Exists(path))
        {
            return;
        }

        var backupFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
        Directory.CreateDirectory(backupFolder);
        var backupPath = Path.Combine(backupFolder, $"ServerSettings-{DateTime.Now:yyyyMMdd-HHmmss}.ini");
        File.Copy(path, backupPath, true);
        logger.Info($"Backed up INI to {backupPath}");
    }

    private string? ResolveProspectsDirectoryFromUiOrWarn()
    {
        var userDir = GetString("UserDirOverride", string.Empty);
        var savedSuffix = GetString("SavedDirSuffix", string.Empty);
        var dir = ProspectDirectoryResolver.TryResolveProspectsDirectory(serverLocationBox.Text, userDir, savedSuffix, iniService);
        if (dir == null)
        {
            MessageBox.Show("Set the server install location first.", "Prospects", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        return dir;
    }

    private void PickLastProspectNameFromDisk()
    {
        var dir = ResolveProspectsDirectoryFromUiOrWarn();
        if (dir == null)
        {
            return;
        }

        if (!Directory.Exists(dir))
        {
            MessageBox.Show(
                $"Prospects folder does not exist yet:\n{dir}\n\nStart the server once or create the folder.",
                "Prospects",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var summaries = ProspectWorldService.ListProspectSummaries(dir);
        if (summaries.Count == 0)
        {
            MessageBox.Show($"No prospect .json files found in:\n{dir}", "Prospects", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var picker = new ProspectPickerForm(summaries);
        if (picker.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(picker.SelectedName))
        {
            return;
        }

        SetControl("LastProspectName", picker.SelectedName);
        logger.Info($"Last prospect name set from disk list: {picker.SelectedName}");
        _prospectPlayersCacheKey = string.Empty;
        RefreshLastWorldTab();
        UpdateOnlinePlayersPanel();
    }

    private void BackupProspectWorldsZip()
    {
        var dir = ResolveProspectsDirectoryFromUiOrWarn();
        if (dir == null)
        {
            return;
        }

        if (!Directory.Exists(dir))
        {
            MessageBox.Show($"Prospects folder does not exist:\n{dir}", "Backup worlds", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var files = ProspectWorldService.GetFilesForWorldBackup(dir);
        if (files.Count == 0)
        {
            MessageBox.Show("No prospect files to back up.", "Backup worlds", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Filter = "Zip archive (*.zip)|*.zip",
            FileName = $"Icarus-Prospects-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            Title = "Save prospect world backup"
        };
        if (dlg.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        try
        {
            ProspectWorldService.ZipFiles(files, dlg.FileName);
            logger.Info($"Prospect world backup ({files.Count} files) saved to {dlg.FileName}");
            MessageBox.Show($"Saved {files.Count} file(s) to:\n{dlg.FileName}", "Backup complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            logger.Error("Prospect ZIP backup failed.", ex);
            MessageBox.Show("Could not create the ZIP file. See log for details.", "Backup worlds", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BackupProspectWorldsToFolder()
    {
        var dir = ResolveProspectsDirectoryFromUiOrWarn();
        if (dir == null)
        {
            return;
        }

        if (!Directory.Exists(dir))
        {
            MessageBox.Show($"Prospects folder does not exist:\n{dir}", "Backup worlds", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var files = ProspectWorldService.GetFilesForWorldBackup(dir);
        if (files.Count == 0)
        {
            MessageBox.Show("No prospect files to back up.", "Backup worlds", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var backupRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
        Directory.CreateDirectory(backupRoot);
        var dest = Path.Combine(backupRoot, $"Prospects-{DateTime.Now:yyyyMMdd-HHmmss}");
        try
        {
            ProspectWorldService.CopyFilesToDirectory(files, dest);
            logger.Info($"Prospect world backup ({files.Count} files) copied to {dest}");
            MessageBox.Show($"Copied {files.Count} file(s) to:\n{dest}", "Backup complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            logger.Error("Prospect folder backup failed.", ex);
            MessageBox.Show("Could not copy files. See log for details.", "Backup worlds", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportBundle()
    {
        using var dialog = new SaveFileDialog { Filter = "JSON files (*.json)|*.json", FileName = "icarus-config-bundle.json" };
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            automationService.ExportBundle(dialog.FileName, currentServerSettings, managerOptions);
            logger.Info($"Exported bundle: {dialog.FileName}");
        }
    }

    private void ImportBundle()
    {
        using var dialog = new OpenFileDialog { Filter = "JSON files (*.json)|*.json" };
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var imported = automationService.ImportBundle(dialog.FileName);
            if (imported == null)
            {
                MessageBox.Show("Invalid bundle file.");
                return;
            }

            currentServerSettings = imported.Value.Settings;
            managerOptions = imported.Value.Options;
            NormalizeConsoleLogPresetName(managerOptions);
            EnsureLaunchArgumentsValidOrDefault();
            PersistManagerOptions(userInitiated: true);
            LoadManagerOptionsToUi();
            LoadIniFromModel(currentServerSettings);
            ApplyTheme();
            logger.Info($"Imported bundle: {dialog.FileName}");
        }
    }

    private void SavePreset()
    {
        var name = Prompt("Preset name:", "Save Preset", "default");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        SaveManagerOptionsFromUi(false);
        SaveUiToIni(true);
        automationService.SaveProfile(name, currentServerSettings, managerOptions);
        logger.Info($"Preset saved: {name}");
    }

    private void LoadPreset()
    {
        var presets = automationService.GetProfiles().ToList();
        if (presets.Count == 0)
        {
            MessageBox.Show("No presets found.");
            return;
        }

        var name = Prompt($"Available: {string.Join(", ", presets)}\nPreset to load:", "Load Preset", presets[0]);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var loaded = automationService.LoadProfile(name);
        if (loaded == null)
        {
            MessageBox.Show("Preset not found.");
            return;
        }

        currentServerSettings = loaded.Value.Settings;
        managerOptions = loaded.Value.Options;
        NormalizeConsoleLogPresetName(managerOptions);
        EnsureLaunchArgumentsValidOrDefault();
        PersistManagerOptions(userInitiated: true);
        LoadManagerOptionsToUi();
        LoadIniFromModel(currentServerSettings);
        ApplyTheme();
        logger.Info($"Preset loaded: {name}");
    }

    private void LoadIniFromModel(DedicatedServerSettingsModel model)
    {
        SetControl("SteamServerName", model.SteamServerName);
        SetControl("LaunchGamePort", managerOptions.LaunchGamePort);
        SetControl("LaunchQueryPort", managerOptions.LaunchQueryPort);
        SetControl("LaunchLogPath", managerOptions.LaunchLogPath);
        SetControl("SessionName", model.SessionName);
        SetControl("JoinPassword", model.JoinPassword);
        SetControl("MaxPlayers", model.MaxPlayers);
        SetControl("ShutdownIfNotJoinedFor", (int)model.ShutdownIfNotJoinedFor);
        SetControl("ShutdownIfEmptyFor", (int)model.ShutdownIfEmptyFor);
        SetControl("AdminPassword", model.AdminPassword);
        SetControl("LoadProspect", model.LoadProspect);
        SetControl("CreateProspect", model.CreateProspect);
        SetControl("ResumeProspect", model.ResumeProspect);
        SetControl("LastProspectName", model.LastProspectName);
        SetControl("AllowNonAdminsToLaunchProspects", model.AllowNonAdminsToLaunchProspects);
        SetControl("AllowNonAdminsToDeleteProspects", model.AllowNonAdminsToDeleteProspects);
        SetControl("FiberFoliageRespawn", model.FiberFoliageRespawn);
        SetControl("LargeStonesRespawn", model.LargeStonesRespawn);
        SetControl("GameSaveFrequency", (int)model.GameSaveFrequency);
        SetControl("SaveGameOnExit", model.SaveGameOnExit);
        _prospectPlayersCacheKey = string.Empty;
        RefreshLastWorldTab();
    }

}
