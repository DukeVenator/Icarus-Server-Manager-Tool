using IcarusServerManager.Models;
using IcarusServerManager.Services;
using IcarusServerManager.UI;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace IcarusServerManager;

public partial class Form1 : Form
{
    private void BuildEnhancedUi()
    {
        PreserveLegacyDesignerControlsBeforeTabRebuild();
        HideLegacyTabLayout();
        BuildServerSettingsUi();
        BuildManagerSettingsUi();
        BuildStatsUi();
        BuildLastWorldTab();
        BuildConsoleTabLayout();
    }

    private void BuildLastWorldTab()
    {
        _lastWorldTab = new TabPage { Name = "lastWorldTab", Text = "Last world" };
        _lastWorldDetails = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point),
            DetectUrls = false
        };
        _lastWorldTab.Controls.Add(_lastWorldDetails);
        var insertAt = Math.Min(2, settingsTabControl.TabPages.Count);
        settingsTabControl.TabPages.Insert(insertAt, _lastWorldTab);
    }

    private void BuildConsoleTabLayout()
    {
        consoleTab.SuspendLayout();
        consoleTab.Controls.Remove(consoleTextbox);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            Padding = new Padding(8, 6, 8, 4)
        };

        top.Controls.Add(new Label { Text = "Logging preset", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });

        var presetCombo = new ComboBox
        {
            Width = 130,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Tag = "ConsoleLogPreset"
        };
        foreach (var name in ConsoleLogFilter.PresetNames)
        {
            presetCombo.Items.Add(name);
        }

        managerControls["ConsoleLogPreset"] = presetCombo;
        presetCombo.SelectedIndexChanged += ConsoleLogPresetComboOnSelectedIndexChanged;
        top.Controls.Add(presetCombo);

        var saveLogBtn = new Button { Text = "Save logging settings", AutoSize = true, Margin = new Padding(12, 4, 0, 0) };
        saveLogBtn.Click += (_, _) => SaveConsoleLoggingSettings();
        toolTips.SetToolTip(
            saveLogBtn,
            "Writes console visibility and preset to manager-options.json (same file as Manager Settings → Save).");
        top.Controls.Add(saveLogBtn);

        _consoleAutoScrollButton = new Button
        {
            Text = "Follow log: On",
            AutoSize = true,
            Margin = new Padding(8, 4, 0, 0),
            Padding = new Padding(10, 0, 10, 0)
        };
        _consoleAutoScrollButton.Click += ConsoleAutoScrollButtonOnClick;
        UpdateConsoleAutoScrollButtonAppearance();
        top.Controls.Add(_consoleAutoScrollButton);

        var hint = new Label
        {
            Text = "Filters apply to this tab only; disk log files are unchanged.",
            AutoSize = true,
            Margin = new Padding(8, 0, 8, 4),
            MaximumSize = new Size(900, 0)
        };

        var checksHost = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            Padding = new Padding(8, 0, 8, 6)
        };

        void AddConsoleCheck(string text, string key, bool defaultChecked)
        {
            var check = new DarkThemedCheckBox
            {
                Text = text,
                Tag = key,
                AutoSize = true,
                Margin = new Padding(0, 2, 16, 2),
                MinimumSize = new Size(180, 22),
                Checked = defaultChecked
            };
            check.CheckedChanged += ConsoleLoggingFilterCheckOnCheckedChanged;
            managerControls[key] = check;
            checksHost.Controls.Add(check);
        }

        AddConsoleCheck("Manager: errors", "ConsoleShowManagerError", true);
        AddConsoleCheck("Manager: warnings", "ConsoleShowManagerWarn", true);
        AddConsoleCheck("Manager: info", "ConsoleShowManagerInfo", true);
        AddConsoleCheck("Game: fatal / error", "ConsoleShowGameFatalError", true);
        AddConsoleCheck("Game: warnings", "ConsoleShowGameWarning", true);
        AddConsoleCheck("Game: important lines", "ConsoleShowGameImportant", true);
        AddConsoleCheck("Game: low-priority UE (Display / Verbose)", "ConsoleShowGameVerbose", false);
        AddConsoleCheck("Game: general", "ConsoleShowGameGeneral", true);

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true
        };
        stack.Controls.Add(top);
        stack.Controls.Add(hint);
        stack.Controls.Add(checksHost);

        root.Controls.Add(stack, 0, 0);
        consoleTextbox.Dock = DockStyle.Fill;
        root.Controls.Add(consoleTextbox, 0, 1);
        consoleTab.Controls.Add(root);
        consoleTab.ResumeLayout();
    }

    private void ConsoleLogPresetComboOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressConsoleLoggingEvents)
        {
            return;
        }

        var name = GetString("ConsoleLogPreset", "Balanced");
        if (string.Equals(name, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            managerOptions.ConsoleLogPreset = "Custom";
            return;
        }

        ConsoleLogFilter.ApplyPreset(managerOptions, name);
        ReflectConsoleLoggingControlsFromModel();
    }

    private void ConsoleLoggingFilterCheckOnCheckedChanged(object? sender, EventArgs e)
    {
        if (_suppressConsoleLoggingEvents)
        {
            return;
        }

        SyncConsoleLoggingFromUiToModel();
        managerOptions.ConsoleLogPreset = "Custom";
        _suppressConsoleLoggingEvents = true;
        try
        {
            SetControl("ConsoleLogPreset", "Custom");
        }
        finally
        {
            _suppressConsoleLoggingEvents = false;
        }
    }

    private void SyncConsoleLoggingFromUiToModel()
    {
        managerOptions.ConsoleLogPreset = GetString("ConsoleLogPreset", "Balanced");
        managerOptions.ConsoleShowManagerError = GetBool("ConsoleShowManagerError", true);
        managerOptions.ConsoleShowManagerWarn = GetBool("ConsoleShowManagerWarn", true);
        managerOptions.ConsoleShowManagerInfo = GetBool("ConsoleShowManagerInfo", true);
        managerOptions.ConsoleShowGameFatalError = GetBool("ConsoleShowGameFatalError", true);
        managerOptions.ConsoleShowGameWarning = GetBool("ConsoleShowGameWarning", true);
        managerOptions.ConsoleShowGameImportant = GetBool("ConsoleShowGameImportant", true);
        managerOptions.ConsoleShowGameVerbose = GetBool("ConsoleShowGameVerbose", false);
        managerOptions.ConsoleShowGameGeneral = GetBool("ConsoleShowGameGeneral", true);
    }

    private void ReflectConsoleLoggingControlsFromModel()
    {
        _suppressConsoleLoggingEvents = true;
        try
        {
            SetControl("ConsoleShowManagerError", managerOptions.ConsoleShowManagerError);
            SetControl("ConsoleShowManagerWarn", managerOptions.ConsoleShowManagerWarn);
            SetControl("ConsoleShowManagerInfo", managerOptions.ConsoleShowManagerInfo);
            SetControl("ConsoleShowGameFatalError", managerOptions.ConsoleShowGameFatalError);
            SetControl("ConsoleShowGameWarning", managerOptions.ConsoleShowGameWarning);
            SetControl("ConsoleShowGameImportant", managerOptions.ConsoleShowGameImportant);
            SetControl("ConsoleShowGameVerbose", managerOptions.ConsoleShowGameVerbose);
            SetControl("ConsoleShowGameGeneral", managerOptions.ConsoleShowGameGeneral);
            SetControl("ConsoleLogPreset", managerOptions.ConsoleLogPreset);
        }
        finally
        {
            _suppressConsoleLoggingEvents = false;
        }
    }

    private void SaveConsoleLoggingSettings()
    {
        SyncConsoleLoggingFromUiToModel();
        optionsService.Save(managerOptions);
        logger.Info("Console logging settings saved.");
    }

    private void ConsoleAutoScrollButtonOnClick(object? sender, EventArgs e)
    {
        managerOptions.AutoScrollConsole = !managerOptions.AutoScrollConsole;
        SetControl("AutoScrollConsole", managerOptions.AutoScrollConsole);
        optionsService.Save(managerOptions);
        UpdateConsoleAutoScrollButtonAppearance();
        logger.Info(
            managerOptions.AutoScrollConsole
                ? "Console log will follow new output (auto-scroll on)."
                : "Console log auto-scroll off; view stays put until you turn follow back on.");
    }

    private void UpdateConsoleAutoScrollButtonAppearance()
    {
        if (_consoleAutoScrollButton == null)
        {
            return;
        }

        _consoleAutoScrollButton.Text = managerOptions.AutoScrollConsole ? "Follow log: On" : "Follow log: Off";
        toolTips.SetToolTip(
            _consoleAutoScrollButton,
            "Toggles the same setting as Manager Settings → Auto Scroll Console. " +
            "When On, new lines jump to the bottom unless you select text or move the caret away from the end (then the view is held so you can read). " +
            "When Off, the log never scrolls for new lines.");
    }

    private void SettingsTabControlOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (settingsTabControl.SelectedTab == _lastWorldTab)
        {
            RefreshLastWorldTab();
        }
    }

    private void RefreshLastWorldTab()
    {
        if (_lastWorldDetails == null)
        {
            return;
        }

        var path = TryGetLastProspectJsonPath();
        if (path == null)
        {
            _lastWorldDetails.Text = "Set the server install location, then set Last Prospect Name (or use the … picker).";
            return;
        }

        if (!File.Exists(path))
        {
            _lastWorldDetails.Text = "Prospect file not found:\n" + path;
            return;
        }

        try
        {
            var s = ProspectSummaryReader.Read(path);
            _lastWorldDetails.Text = s.BuildDetailsText();
        }
        catch (Exception ex)
        {
            _lastWorldDetails.Text = "Could not read prospect JSON.\n\n" + ex.Message;
        }
    }

    private void PreserveLegacyDesignerControlsBeforeTabRebuild()
    {
        if (_legacyDesignerControlsHost == null)
        {
            _legacyDesignerControlsHost = new Panel
            {
                Name = "legacyDesignerControlsHost",
                Visible = false,
                Size = new Size(1, 1),
                Location = new Point(-4000, -4000),
                TabStop = false
            };
            Controls.Add(_legacyDesignerControlsHost);
        }

        foreach (var legacy in new Control[] { panel3, panel4, panel1, panel2, severSettingsPanel })
        {
            legacy.Parent?.Controls.Remove(legacy);
            if (!_legacyDesignerControlsHost.Controls.Contains(legacy))
            {
                _legacyDesignerControlsHost.Controls.Add(legacy);
            }
        }
    }

    private void HideLegacyTabLayout()
    {
        panel3.Visible = false;
        panel4.Visible = false;
        severSettingsPanel.Visible = false;
        panel1.Visible = false;
        panel2.Visible = false;
    }

    private void BuildServerSettingsUi()
    {
        serverSettingsTab.SuspendLayout();
        serverSettingsTab.Controls.Clear();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var editPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(8),
            Margin = new Padding(0)
        };

        AddTextField(editPanel, "Steam Server Name (-SteamServerName)", "SteamServerName");
        AddNumericField(editPanel, "Game Port (-Port)", "LaunchGamePort", 1, 65535, 17777);
        AddNumericField(editPanel, "Query Port (-QueryPort)", "LaunchQueryPort", 1, 65535, 27015);
        AddTextField(editPanel, "Log Path (-Log)", "LaunchLogPath");
        toolTips.SetToolTip(
            iniControls["LaunchGamePort"],
            "-Port, -QueryPort, and -Log are stored in manager-options.json (not ServerSettings.ini). They are saved when you use Save to INI, Save Manager Options, or Start Server.");
        toolTips.SetToolTip(iniControls["LaunchQueryPort"], toolTips.GetToolTip(iniControls["LaunchGamePort"]));
        toolTips.SetToolTip(iniControls["LaunchLogPath"], toolTips.GetToolTip(iniControls["LaunchGamePort"]));
        AddTextField(editPanel, "Session Name (ini)", "SessionName");
        AddTextField(editPanel, "Join Password", "JoinPassword");
        AddNumericField(editPanel, "Max Players (1-8)", "MaxPlayers", 1, 8, 8);
        AddTextField(editPanel, "Admin Password", "AdminPassword");
        AddNumericField(editPanel, "Shutdown If Not Joined For (sec)", "ShutdownIfNotJoinedFor", -1, 86400, 600);
        AddNumericField(editPanel, "Shutdown If Empty For (sec)", "ShutdownIfEmptyFor", -1, 86400, 600);
        AddTextField(editPanel, "Load Prospect", "LoadProspect");
        AddTextField(editPanel, "Create Prospect", "CreateProspect");
        AddCheckField(editPanel, "Resume Prospect", "ResumeProspect", true);
        AddLastProspectNameRow(editPanel);
        AddCheckField(editPanel, "Allow Non-Admins Launch", "AllowNonAdminsToLaunchProspects", true);
        AddCheckField(editPanel, "Allow Non-Admins Delete", "AllowNonAdminsToDeleteProspects", false);
        AddCheckField(editPanel, "Fiber Foliage Respawn", "FiberFoliageRespawn", false);
        AddCheckField(editPanel, "Large Stones Respawn", "LargeStonesRespawn", false);
        AddNumericField(editPanel, "Game Save Frequency", "GameSaveFrequency", 1, 120, 10);
        AddCheckField(editPanel, "Save Game On Exit", "SaveGameOnExit", true);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 10),
            Margin = new Padding(0)
        };
        var reloadBtn = new Button { Text = "Reload from INI", Width = 160, Height = 40 };
        var saveBtn = new Button { Text = "Save to INI", Width = 160, Height = 40 };
        var backupBtn = new Button { Text = "Backup INI", Width = 120, Height = 40 };
        var exportBtn = new Button { Text = "Export Bundle", Width = 130, Height = 40 };
        var importBtn = new Button { Text = "Import Bundle", Width = 130, Height = 40 };
        var saveProfileBtn = new Button { Text = "Save Preset", Width = 120, Height = 40 };
        var loadProfileBtn = new Button { Text = "Load Preset", Width = 120, Height = 40 };
        var worldZipBtn = new Button { Text = "Backup Worlds (ZIP)", Width = 170, Height = 40 };
        var worldFolderBtn = new Button { Text = "Backup Worlds (folder)", Width = 190, Height = 40 };
        reloadBtn.Click += (_, _) => LoadIniToUi();
        saveBtn.Click += (_, _) => SaveUiToIni(true);
        backupBtn.Click += (_, _) => BackupIni();
        exportBtn.Click += (_, _) => ExportBundle();
        importBtn.Click += (_, _) => ImportBundle();
        saveProfileBtn.Click += (_, _) => SavePreset();
        loadProfileBtn.Click += (_, _) => LoadPreset();
        worldZipBtn.Click += (_, _) => BackupProspectWorldsZip();
        worldFolderBtn.Click += (_, _) => BackupProspectWorldsToFolder();
        toolTips.SetToolTip(reloadBtn, "Reload DedicatedServerSettings values from ServerSettings.ini.");
        toolTips.SetToolTip(saveBtn, "Validate and save current server settings to ServerSettings.ini.");
        toolTips.SetToolTip(backupBtn, "Create a timestamped backup of ServerSettings.ini.");
        toolTips.SetToolTip(exportBtn, "Export manager options + server settings to one JSON bundle.");
        toolTips.SetToolTip(importBtn, "Import manager options + server settings from a JSON bundle.");
        toolTips.SetToolTip(saveProfileBtn, "Save current settings as a named preset profile.");
        toolTips.SetToolTip(loadProfileBtn, "Load an existing preset profile into the UI.");
        toolTips.SetToolTip(worldZipBtn, "Zip main prospect .json files plus the game's .json.backup rotation files from Saved/…/Prospects.");
        toolTips.SetToolTip(worldFolderBtn, "Copy those prospect files into the manager backups folder (next to INI backups).");
        buttonPanel.Controls.AddRange(new Control[] { reloadBtn, saveBtn, backupBtn, exportBtn, importBtn, saveProfileBtn, loadProfileBtn, worldZipBtn, worldFolderBtn });

        var tip = new Label
        {
            // Dock.Fill + AutoSize breaks word wrap on Labels; anchor horizontally and cap width via MaximumSize.
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            AutoSize = true,
            Padding = new Padding(8, 4, 8, 6),
            Text = "Prospect startup precedence: LoadProspect -> ResumeProspect -> CreateProspect. SessionName may not affect browser listing; use Steam Server Name launch arg. Game/Query ports and log path are manager-only (see tooltips on those fields)."
        };
        _serverSettingsTipLabel = tip;
        _serverSettingsRoot = root;
        void UpdateServerSettingsTipWrap()
        {
            if (_serverSettingsTipLabel == null || _serverSettingsRoot == null || _serverSettingsTipLabel.IsDisposed || _serverSettingsRoot.IsDisposed)
            {
                return;
            }

            var pad = _serverSettingsTipLabel.Padding.Horizontal + _serverSettingsRoot.Padding.Horizontal + 16;
            var w = Math.Max(200, _serverSettingsRoot.ClientSize.Width - pad);
            _serverSettingsTipLabel.MaximumSize = new Size(w, 0);
        }

        root.SizeChanged += (_, _) => UpdateServerSettingsTipWrap();
        serverSettingsTab.SizeChanged += (_, _) => UpdateServerSettingsTipWrap();

        root.Controls.Add(editPanel, 0, 0);
        root.Controls.Add(tip, 0, 1);
        root.Controls.Add(buttonPanel, 0, 2);
        serverSettingsTab.Controls.Add(root);
        serverSettingsTab.ResumeLayout();
        root.PerformLayout();
        UpdateServerSettingsTipWrap();
    }

    private void BuildManagerSettingsUi()
    {
        managerSettingsTab.SuspendLayout();
        managerSettingsTab.Controls.Clear();

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8),
            Margin = new Padding(0)
        };

        var installHeading = new Label
        {
            Text = "Server install folder",
            AutoSize = true,
            Font = new Font(panel.Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4)
        };
        panel.Controls.Add(installHeading);

        var installHint = new Label
        {
            Text =
                "Folder that contains (or will receive) Icarus\\Binaries\\Win64\\IcarusServer-Win64-Shipping.exe. " +
                "INI and prospects resolve under this path unless you override UserDir / SavedDir below.",
            AutoSize = true,
            MaximumSize = new Size(880, 0),
            Margin = new Padding(0, 0, 0, 6)
        };
        panel.Controls.Add(installHint);

        serverLocationBox.Parent?.Controls.Remove(serverLocationBox);
        selectLocationButton.Parent?.Controls.Remove(selectLocationButton);
        serverLocationBox.ReadOnly = true;
        serverLocationBox.Width = 540;
        serverLocationBox.MinimumSize = new Size(320, 24);
        serverLocationBox.Margin = new Padding(0, 2, 8, 8);
        selectLocationButton.Text = "Browse…";
        selectLocationButton.AutoSize = true;
        selectLocationButton.Margin = new Padding(0, 2, 8, 8);
        var setupWizardButton = new Button
        {
            Text = "Setup wizard…",
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 8)
        };
        setupWizardButton.Click += (_, _) => OpenSetupWizard();
        toolTips.SetToolTip(setupWizardButton, "Open a short wizard that explains the folder layout and lets you pick the path.");

        var installRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 12)
        };
        installRow.Controls.Add(serverLocationBox);
        installRow.Controls.Add(selectLocationButton);
        installRow.Controls.Add(setupWizardButton);
        panel.Controls.Add(installRow);

        AddThemeField(panel, "Theme", "Theme");
        AddCheckField(panel, "Interval Restart Enabled", "IntervalRestartEnabled", true);
        AddNumericField(panel, "Interval Restart Minutes", "IntervalRestartMinutes", 5, 1440, 60);
        AddNumericField(panel, "Interval Warning Minutes", "IntervalWarningMinutes", 0, 120, 5);
        AddCheckField(panel, "Pause interval timer when empty", "PauseIntervalRestartWhenEmpty", false);
        AddCheckField(panel, "When paused (empty), use empty-server restart timer", "IntervalRestartUseEmptyIdleTimer", false);
        AddCheckField(panel, "Crash Restart Enabled", "CrashRestartEnabled", true);
        AddNumericField(panel, "Crash Retry Delay (sec)", "CrashRestartRetryDelaySeconds", 1, 300, 15);
        AddNumericField(panel, "Crash Max Attempts", "CrashRestartMaxAttempts", 1, 50, 5);
        AddCheckField(panel, "Empty Server Restart Enabled", "EmptyServerRestartEnabled", false);
        AddNumericField(panel, "Empty Server Restart Minutes", "EmptyServerRestartMinutes", 1, 1440, 30);
        AddNumericField(panel, "Empty Server Warning Minutes", "EmptyServerWarningMinutes", 0, 120, 5);
        AddCheckField(panel, "High Memory Restart Enabled", "HighMemoryRestartEnabled", false);
        AddNumericField(panel, "High Memory Threshold (MB)", "HighMemoryMbThreshold", 256, 65536, 8000);
        AddNumericField(panel, "High Memory Sustain Minutes", "HighMemorySustainMinutes", 1, 240, 5);
        AddNumericField(panel, "High Memory Warning Minutes", "HighMemoryWarningMinutes", 0, 120, 2);
        AddCheckField(panel, "Try Ctrl+C on stop (Windows)", "GracefulShutdownTryCtrlC", true);
        AddNumericField(panel, "Graceful shutdown max wait (sec)", "GracefulShutdownWaitSeconds", 10, 900, 120);
        AddTextField(panel, "UserDir Override", "UserDirOverride");
        AddTextField(panel, "SavedDirSuffix", "SavedDirSuffix");
        AddCheckField(panel, "Enable Discord Webhook", "EnableDiscordWebhook", false);
        AddTextField(panel, "Discord Webhook URL", "DiscordWebhookUrl");
        var discordSection = new Label
        {
            Text = "Discord webhook events (requires master switch + URL)",
            AutoSize = true,
            Font = new Font(panel.Font, FontStyle.Bold)
        };
        panel.Controls.Add(discordSection);
        AddCheckField(panel, "Notify: player joined (from server log)", "DiscordWebhookNotifyPlayerJoin", false);
        AddCheckField(panel, "Notify: player left (from server log)", "DiscordWebhookNotifyPlayerLeave", false);
        AddCheckField(panel, "Notify: automated server restart", "DiscordWebhookNotifyServerRestart", true);
        AddCheckField(panel, "Notify: server started", "DiscordWebhookNotifyServerStart", false);
        AddCheckField(panel, "Notify: server stopped (managed stop only)", "DiscordWebhookNotifyServerStop", false);
        AddCheckField(panel, "Notify: unexpected exit / crash (process died)", "DiscordWebhookNotifyUnexpectedExit", false);
        AddCheckField(panel, "Notify: restart warning (policy countdown)", "DiscordWebhookNotifyRestartWarning", false);
        AddCheckField(panel, "Notify: scheduled update window reached", "DiscordWebhookNotifyScheduledUpdate", false);
        AddCheckField(panel, "Notify: SteamCMD install/update finished", "DiscordWebhookNotifySteamCmd", false);
        AddCheckField(panel, "Notify: INI save failed (exception)", "DiscordWebhookNotifyIniSaveFailed", false);
        AddCheckField(panel, "Notify: INI validation failed (blocked save)", "DiscordWebhookNotifyIniValidationFailed", false);
        AddCheckField(panel, "Notify: INI load failed", "DiscordWebhookNotifyIniLoadFailed", false);
        AddCheckField(panel, "Notify: install path missing executable", "DiscordWebhookNotifyInstallPathIssue", false);
        AddCheckField(panel, "Notify: automated restart did not start server", "DiscordWebhookNotifyRestartFailed", false);
        AddCheckField(panel, "Notify: possible level-up / XP (heuristic)", "DiscordWebhookNotifyLevelUp", false);
        AddCheckField(panel, "Notify: possible player death (heuristic)", "DiscordWebhookNotifyPlayerDeath", false);
        AddCheckField(panel, "Notify: possible chat lines (heuristic)", "DiscordWebhookNotifyChat", false);
        AddNumericField(panel, "Chat webhook min interval (sec, 0=off)", "DiscordWebhookChatThrottleSeconds", 0, 120, 3);
        AddNumericField(panel, "Gameplay webhook min interval (sec, 0=off)", "DiscordWebhookGameplayThrottleSeconds", 0, 120, 5);
        AddNumericField(panel, "Heartbeat webhook interval (hours, 0=off)", "DiscordWebhookHeartbeatIntervalHours", 0, 168, 0);
        AddCheckField(panel, "Discord: use embeds (richer cards)", "DiscordWebhookUseEmbeds", true);
        var discordBehaviorHeading = new Label
        {
            Text = "Discord message & embed behavior",
            AutoSize = true,
            Font = new Font(panel.Font, FontStyle.Bold),
            Margin = new Padding(0, 10, 0, 0)
        };
        panel.Controls.Add(discordBehaviorHeading);
        AddCheckField(panel, "Discord: title emojis", "DiscordWebhookUseTitleEmojis", true);
        AddCheckField(panel, "Discord: embed author (name + store link)", "DiscordWebhookShowEmbedAuthor", true);
        AddCheckField(panel, "Discord: embed UTC timestamp", "DiscordWebhookShowEmbedTimestamp", true);
        AddCheckField(panel, "Discord: show game/query ports on cards", "DiscordWebhookShowPortsOnEmbeds", true);
        AddCheckField(panel, "Discord: show session name when set", "DiscordWebhookShowSessionOnEmbeds", true);
        AddCheckField(panel, "Discord: prospect line on server-ready", "DiscordWebhookIncludeProspectOnStart", true);
        AddCheckField(panel, "Discord: policy line on heartbeat", "DiscordWebhookHeartbeatShowPolicyLine", true);
        AddCheckField(panel, "Discord: themed field names (Beacon / Crew)", "DiscordWebhookUseThemedLabels", true);
        AddCheckField(panel, "Discord: plain descriptions (strip emphasis)", "DiscordWebhookPlainTextDescriptions", false);
        AddTextField(panel, "Discord custom footer (empty = default)", "DiscordWebhookCustomFooter");
        AddNumericField(panel, "Discord description max length", "DiscordWebhookDescriptionMaxChars", 800, 4096, 3500);
        AddTextField(panel, "Discord override username (optional)", "DiscordWebhookUsername");
        AddTextField(panel, "Discord override avatar URL (optional)", "DiscordWebhookAvatarUrl");
        AddCheckField(panel, "Auto Scroll Console", "AutoScrollConsole", true);
        AddCheckField(panel, "Scheduled Update Enabled", "UpdateScheduleEnabled", false);
        AddTextField(panel, "Update Time HH:mm", "UpdateScheduleTime");
        AddCheckField(panel, "Manager auto-update checks", "ManagerUpdateCheckEnabled", true);
        AddNumericField(panel, "Manager update check interval (hours)", "ManagerUpdateCheckIntervalHours", 1, 168, 6);
        AddCheckField(panel, "Manager updates: include prerelease tags", "ManagerUpdateIncludePrerelease", false);
        AddCheckField(panel, "Manager updates: confirm before download/install", "ManagerUpdatePromptBeforeDownload", true);
        var checkNowButton = new Button { Text = "Check manager updates now", Width = 220 };
        checkNowButton.Click += async (_, _) => await CheckForManagerUpdateAsync(userInitiated: true).ConfigureAwait(true);
        panel.Controls.Add(checkNowButton);

        var saveButton = new Button { Text = "Save Manager Options", Width = 180 };
        saveButton.Click += (_, _) => SaveManagerOptionsFromUi(true);
        toolTips.SetToolTip(saveButton, "Save manager-only settings (theme, restart policies, webhook, schedule) and apply them immediately.");
        panel.Controls.Add(saveButton);
        managerSettingsTab.Controls.Add(panel);
        managerSettingsTab.ResumeLayout();

        foreach (Control c in panel.Controls)
        {
            if (c.Tag is string key)
            {
                managerControls[key] = c;
            }
        }

        if (managerControls.TryGetValue("GracefulShutdownTryCtrlC", out var ctrlCField) && ctrlCField is CheckBox ctrlCBox)
        {
            toolTips.SetToolTip(
                ctrlCBox,
                "When enabled, the manager tries Windows console Ctrl+C before sending quit/exit on stdin. Disable to send quit/exit immediately (faster when Ctrl+C never works).");
        }

        if (managerControls.TryGetValue("GracefulShutdownWaitSeconds", out var waitField) && waitField is NumericUpDown waitNum)
        {
            toolTips.SetToolTip(
                waitNum,
                "Maximum time to wait for the server process to exit after stop before forcing termination.");
        }
    }

    private void BuildStatsUi()
    {
        var statsTab = new TabPage { Name = "statsTab", Text = "Stats" };
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 62f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 38f));

        metricsGraph = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White
        };
        metricsGraph.Paint += (_, e) => RenderMetricsGraph(e);

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8)
        };

        CreateStatLabel(panel, "Uptime");
        CreateStatLabel(panel, "RestartCountdown");
        CreateStatLabel(panel, "LastRestartReason");
        CreateStatLabel(panel, "CrashCount");
        CreateStatLabel(panel, "CpuPercent");
        CreateStatLabel(panel, "MemoryMb");
        CreateStatLabel(panel, "Health");
        CreateStatLabel(panel, "RestartHistory");

        var playersHeading = new Label
        {
            Width = 650,
            Height = 22,
            Margin = new Padding(0, 10, 0, 0),
            Text = "Players online — from Last Prospect save (IsCurrentlyPlaying) plus join/leave hints parsed from server output."
        };
        panel.Controls.Add(playersHeading);
        _onlinePlayersList = new ListBox
        {
            Width = 650,
            Height = 120,
            IntegralHeight = false,
            HorizontalScrollbar = true
        };
        panel.Controls.Add(_onlinePlayersList);

        var saveHistoryBtn = new Button { Text = "Export Metrics CSV", Width = 160 };
        saveHistoryBtn.Click += (_, _) => ExportMetricsCsv();
        toolTips.SetToolTip(saveHistoryBtn, "Export the current metrics history to a CSV file.");
        panel.Controls.Add(saveHistoryBtn);

        root.Controls.Add(metricsGraph, 0, 0);
        root.Controls.Add(panel, 0, 1);
        statsTab.Controls.Add(root);
        settingsTabControl.TabPages.Add(statsTab);
    }

    private void CreateStatLabel(Control parent, string key)
    {
        var label = new Label { Width = 650, Height = 24, Text = $"{key}: -" };
        statsLabels[key] = label;
        parent.Controls.Add(label);
    }

    private void AddTextField(Control parent, string label, string key)
    {
        parent.Controls.Add(new Label { Text = label, Width = 500, Height = 18 });
        var box = new TextBox { Width = 500, Tag = key };
        parent.Controls.Add(box);
        iniControls.TryAdd(key, box);
        managerControls.TryAdd(key, box);
    }

    private void AddLastProspectNameRow(Control parent)
    {
        parent.Controls.Add(new Label { Text = "Last Prospect Name", Width = 500, Height = 18 });
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = 500,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        var box = new TextBox { Width = 420, Tag = "LastProspectName" };
        var pick = new Button { Text = "…", Width = 40, AutoSize = false };
        toolTips.SetToolTip(pick, "Open a wizard listing prospect JSON files under …\\PlayerData\\DedicatedServer\\Prospects. Table shows map, difficulty, state, elapsed time, member counts, and online flags from each file header; sets Last Prospect Name (no .json suffix).");
        pick.Click += (_, _) => PickLastProspectNameFromDisk();
        row.Controls.Add(box);
        row.Controls.Add(pick);
        parent.Controls.Add(row);
        iniControls.Remove("LastProspectName");
        managerControls.Remove("LastProspectName");
        iniControls.Add("LastProspectName", box);
        managerControls.Add("LastProspectName", box);
    }

    private void AddThemeField(Control parent, string label, string key)
    {
        parent.Controls.Add(new Label { Text = label, Width = 500, Height = 18 });
        var combo = new ComboBox
        {
            Width = 500,
            Tag = key,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        combo.Items.Add("Light");
        combo.Items.Add("Dark");
        combo.SelectedIndex = 0;
        parent.Controls.Add(combo);
        managerControls[key] = combo;
    }

    private void AddNumericField(Control parent, string label, string key, decimal min, decimal max, decimal value)
    {
        parent.Controls.Add(new Label { Text = label, Width = 500, Height = 18 });
        var num = new NumericUpDown
        {
            Width = 500,
            Minimum = min,
            Maximum = max,
            DecimalPlaces = 0,
            Value = value,
            Tag = key
        };
        parent.Controls.Add(num);
        iniControls.TryAdd(key, num);
        managerControls.TryAdd(key, num);
    }

    private void AddCheckField(Control parent, string label, string key, bool value)
    {
        var check = new DarkThemedCheckBox { Text = label, Checked = value, Width = 500, Height = 26, Tag = key };
        parent.Controls.Add(check);
        iniControls.TryAdd(key, check);
        managerControls.TryAdd(key, check);
    }

}
