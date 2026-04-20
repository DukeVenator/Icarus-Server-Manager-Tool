using IcarusServerManager.Models;
using IcarusServerManager.Services;
using IcarusServerManager.UI;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace IcarusServerManager;

    public partial class Form1 : Form
    {
    public Process? serverProc;
    private readonly object _serverProcDisposeLock = new();
    private bool serverStarted;
    private bool restartInProgress;
    private int _serverReadyAnnounced;
    private bool possibleServerEmpty = true;
    private DateTime startTime;
    private DateTime lastRestartAt = DateTime.MinValue;
    private string lastRestartReason = "N/A";
    private int crashCount;
    private bool crashDetected;
    private readonly List<MetricsSample> metricsHistory = new();
    private readonly List<string> restartHistory = new();

    private readonly FolderBrowserDialog folderBrowser = new();
    private readonly ServerSettingsIniService iniService = new();
    private readonly ManagerOptionsService optionsService = new();
    private readonly RestartPolicyService restartPolicyService = new();
    private readonly MetricsSampler metricsSampler = new();
    private readonly ThemeManager themeManager = new();
    private readonly Logger logger = new();
    private readonly AutomationService automationService = new();
    private readonly ServerOutputPlayerTracker playerTracker = new();

    private ManagerOptions managerOptions = new();
    private DedicatedServerSettingsModel currentServerSettings = new();
    private System.Windows.Forms.Timer policyTimer = new();
    private System.Windows.Forms.Timer metricsTimer = new();
    private System.Windows.Forms.Timer updateTimer = new();
    private readonly System.Windows.Forms.Timer heartbeatTimer = new() { Interval = 60_000 };
    private bool _discordPolicyWarningActive;
    private DateTime? _scheduledUpdateWebhookBucketUtc;
    private const string ReadyForPlayersLogMarker = "IcarusOSSLog: Error: OnResUserTicket : No player found";
    private const string DiscordIcarusStoreUrl = "https://store.steampowered.com/app/949230/ICARUS/";
    private const string DiscordManagerBrand = "Icarus Server Manager";
    private DateTime _lastDiscordHeartbeatUtc = DateTime.MinValue;
    private bool _installPathPreviouslyValid = true;
    private bool _suppressConsoleLoggingEvents;

    private readonly Dictionary<string, Control> iniControls = new();
    private readonly Dictionary<string, Control> managerControls = new();
    private readonly Dictionary<string, Label> statsLabels = new();
    private readonly ToolTip toolTips = new();
    private PictureBox? metricsGraph;
    private Label? _serverSettingsTipLabel;
    private TableLayoutPanel? _serverSettingsRoot;
    private TabPage? _lastWorldTab;
    private RichTextBox? _lastWorldDetails;
    private ListBox? _onlinePlayersList;
    private ProspectSummary? _prospectPlayersCache;
    private string _prospectPlayersCacheKey = string.Empty;
    private DateTime _prospectPlayersCacheUtc = DateTime.MinValue;
    /// <summary>
    /// Designer legacy panels (location, ports, restart) must not live under tab pages that call
    /// <see cref="Control.Controls.Clear"/> — that disposes children and breaks <see cref="LoadPersistedData"/>.
    /// </summary>
    private Panel? _legacyDesignerControlsHost;
    private Button? _consoleAutoScrollButton;

        public Form1()
        {
            InitializeComponent();
        logger.OnLog += n => WriteToConsole(n.Line, n.IsGameProcessOutput);
        Text = "Icarus Server Manager";
        MinimumSize = new Size(980, 700);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
            consoleTextbox.Text = "Initializing...";
        FormClosing += Form1_FormClosing;
        FormClosed += KillProcess;
    }

    private void Form1_Load(object sender, EventArgs e)
    {
        BuildEnhancedUi();
        settingsTabControl.SelectedIndexChanged += SettingsTabControlOnSelectedIndexChanged;
        ConfigureStaticTooltips();
        LoadPersistedData();
        SetupTimers();
        ApplyTheme();
        PrimeInstallPathWebhookState();
        UpdateServerAvailability();
        RunSetupWizardIfNeeded();
        logger.Info("Server manager initialized.");
    }

    public void WriteToConsole(string text, bool isGameProcessOutput = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (consoleTextbox.InvokeRequired)
        {
            consoleTextbox.BeginInvoke(() => WriteToConsole(text, isGameProcessOutput));
            return;
        }

        var dark = managerOptions.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase);
        var defaultColor = consoleTextbox.ForeColor;
        var lengthBefore = consoleTextbox.TextLength;
        var savedStart = consoleTextbox.SelectionStart;
        var savedLength = consoleTextbox.SelectionLength;
        var preserveView = savedLength > 0
            || (savedLength == 0 && savedStart < lengthBefore);

        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw;
            if (line.Length == 0)
            {
                continue;
            }

            if (!line.Equals("Initializing...", StringComparison.OrdinalIgnoreCase))
            {
                var kind = ConsoleLogFilter.Classify(line, isGameProcessOutput);
                if (!ConsoleLogFilter.ShouldDisplay(kind, managerOptions))
                {
                    continue;
                }
            }

            var color = ConsoleLogLineColorizer.ResolveLineColor(line, dark) ?? defaultColor;
            consoleTextbox.SelectionStart = consoleTextbox.TextLength;
            consoleTextbox.SelectionLength = 0;
            consoleTextbox.SelectionColor = color;
            consoleTextbox.AppendText(line + Environment.NewLine);
        }

        if (preserveView)
        {
            consoleTextbox.Select(savedStart, savedLength);
            return;
        }

        consoleTextbox.SelectionStart = consoleTextbox.TextLength;
        consoleTextbox.SelectionLength = 0;
        consoleTextbox.SelectionColor = defaultColor;
        if (managerOptions.AutoScrollConsole)
        {
            consoleTextbox.ScrollToCaret();
        }
    }

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

    private string? TryResolveProspectsDirectorySilent()
    {
        if (string.IsNullOrWhiteSpace(serverLocationBox.Text))
        {
            return null;
        }

        var userDir = GetString("UserDirOverride", string.Empty);
        var savedSuffix = GetString("SavedDirSuffix", string.Empty);
        return iniService.ResolveProspectsDirectory(serverLocationBox.Text.Trim(), userDir, savedSuffix);
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

    private string DiscordSteamServerLabel()
    {
        var steam = currentServerSettings.SteamServerName.Trim();
        return string.IsNullOrWhiteSpace(steam) ? "Dedicated server" : steam;
    }

    private string DiscordFieldServerLabel() =>
        managerOptions.DiscordWebhookUseThemedLabels ? "Beacon" : "Server";

    private string DiscordFieldPlayerLabel() =>
        managerOptions.DiscordWebhookUseThemedLabels ? "Crew" : "Player";

    private (string? Name, string? Url) DiscordEmbedAuthorFromOptions() =>
        managerOptions.DiscordWebhookShowEmbedAuthor
            ? (DiscordManagerBrand, DiscordIcarusStoreUrl)
            : ((string?)null, (string?)null);

    private string DiscordCardFooter(string defaultSuffix)
    {
        var custom = (managerOptions.DiscordWebhookCustomFooter ?? string.Empty).Trim();
        if (custom.Length > 0)
        {
            return custom.Length > 2048 ? custom[..2048] : custom;
        }

        return $"{DiscordManagerBrand} · {defaultSuffix}";
    }

    /// <summary>Minimal card for stops, exits, and manager shutdown — no long field grids.</summary>
    private DiscordWebhookExtras BuildDiscordLifecycleExtras()
    {
        var label = DiscordSteamServerLabel();
        var fields = new List<DiscordEmbedField>
        {
            new DiscordEmbedField(DiscordFieldServerLabel(), label, false)
        };
        if (managerOptions.DiscordWebhookShowPortsOnEmbeds)
        {
            fields.Add(new DiscordEmbedField("Ports", $"{managerOptions.LaunchGamePort} · {managerOptions.LaunchQueryPort}", true));
        }

        var author = DiscordEmbedAuthorFromOptions();
        return new DiscordWebhookExtras(
            fields,
            FooterText: DiscordCardFooter($"{DateTime.Now:MMM d, HH:mm} local"),
            AuthorName: author.Name,
            AuthorUrl: author.Url);
    }

    /// <summary>Short context for policy / install / SteamCMD style alerts.</summary>
    private DiscordWebhookExtras BuildDiscordOperationalExtras()
    {
        var label = DiscordSteamServerLabel();
        var fields = new List<DiscordEmbedField>
        {
            new DiscordEmbedField(DiscordFieldServerLabel(), label, false)
        };
        if (managerOptions.DiscordWebhookShowPortsOnEmbeds)
        {
            fields.Add(new DiscordEmbedField("Ports", $"{managerOptions.LaunchGamePort} · {managerOptions.LaunchQueryPort}", true));
        }

        if (managerOptions.DiscordWebhookShowSessionOnEmbeds &&
            !string.IsNullOrWhiteSpace(currentServerSettings.SessionName))
        {
            fields.Add(new DiscordEmbedField("Session", currentServerSettings.SessionName.Trim(), true));
        }

        var author = DiscordEmbedAuthorFromOptions();
        return new DiscordWebhookExtras(
            fields,
            FooterText: DiscordCardFooter("ops feed"),
            AuthorName: author.Name,
            AuthorUrl: author.Url);
    }

    /// <summary>INI and other diagnostics: compact facts, leave stack traces in the message body.</summary>
    private DiscordWebhookExtras BuildDiscordDiagnosticExtras()
    {
        var label = DiscordSteamServerLabel();
        var fields = new List<DiscordEmbedField>
        {
            new DiscordEmbedField(DiscordFieldServerLabel(), label, false)
        };
        if (managerOptions.DiscordWebhookShowPortsOnEmbeds)
        {
            fields.Add(new DiscordEmbedField("Ports", $"{managerOptions.LaunchGamePort} · {managerOptions.LaunchQueryPort}", true));
        }

        if (managerOptions.DiscordWebhookShowSessionOnEmbeds &&
            !string.IsNullOrWhiteSpace(currentServerSettings.SessionName))
        {
            fields.Add(new DiscordEmbedField("Session", currentServerSettings.SessionName.Trim(), true));
        }

        var author = DiscordEmbedAuthorFromOptions();
        return new DiscordWebhookExtras(
            fields,
            FooterText: DiscordCardFooter("diagnostic"),
            AuthorName: author.Name,
            AuthorUrl: author.Url);
    }

    private DiscordWebhookExtras BuildDiscordHeartbeatExtras()
    {
        var label = DiscordSteamServerLabel();
        var uptime = serverStarted ? (DateTime.Now - startTime).ToString(@"d\.hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture) : "—";
        var reason = string.IsNullOrWhiteSpace(lastRestartReason)
            ? "—"
            : (lastRestartReason.Trim().Length > 220 ? lastRestartReason.Trim()[..220] + "…" : lastRestartReason.Trim());
        var fields = new List<DiscordEmbedField>
        {
            new DiscordEmbedField("Game process", serverStarted ? "● Online" : "○ Idle", true),
            new DiscordEmbedField("Uptime", uptime, true),
            new DiscordEmbedField(DiscordFieldServerLabel(), label, false)
        };
        if (managerOptions.DiscordWebhookHeartbeatShowPolicyLine)
        {
            fields.Add(new DiscordEmbedField("Last policy note", reason, false));
        }

        if (managerOptions.DiscordWebhookShowPortsOnEmbeds)
        {
            fields.Add(new DiscordEmbedField("Ports", $"{managerOptions.LaunchGamePort} · {managerOptions.LaunchQueryPort}", true));
        }

        var author = DiscordEmbedAuthorFromOptions();
        return new DiscordWebhookExtras(
            fields,
            FooterText: DiscordCardFooter("heartbeat"),
            AuthorName: author.Name,
            AuthorUrl: author.Url);
    }

    private DiscordWebhookExtras BuildDiscordLaunchExtras()
    {
        var label = DiscordSteamServerLabel();
        var sum = TryReadProspectSummaryForDiscord();
        var fields = new List<DiscordEmbedField>
        {
            new DiscordEmbedField(DiscordFieldServerLabel(), label, false)
        };
        if (managerOptions.DiscordWebhookShowPortsOnEmbeds)
        {
            fields.Add(new DiscordEmbedField("Ports", $"{managerOptions.LaunchGamePort} · {managerOptions.LaunchQueryPort}", true));
        }

        if (managerOptions.DiscordWebhookIncludeProspectOnStart)
        {
            if (sum != null)
            {
                var world = string.IsNullOrWhiteSpace(sum.ProspectId) ? sum.BaseName : sum.ProspectId.Trim();
                fields.Add(new DiscordEmbedField("Prospect", world, true));
            }
            else if (!string.IsNullOrWhiteSpace(currentServerSettings.LastProspectName.Trim()))
            {
                fields.Add(new DiscordEmbedField("Prospect", currentServerSettings.LastProspectName.Trim(), true));
            }
        }

        var author = DiscordEmbedAuthorFromOptions();
        return new DiscordWebhookExtras(
            fields,
            FooterText: DiscordCardFooter("live"),
            AuthorName: author.Name,
            AuthorUrl: author.Url);
    }

    private DiscordWebhookExtras BuildDiscordMinimalServerTag(string steamServerName)
    {
        var steam = string.IsNullOrWhiteSpace(steamServerName) ? "—" : steamServerName.Trim();
        var fields = new List<DiscordEmbedField>
        {
            new DiscordEmbedField(DiscordFieldServerLabel(), steam, true)
        };
        if (managerOptions.DiscordWebhookShowPortsOnEmbeds)
        {
            fields.Add(new DiscordEmbedField("Ports", $"{managerOptions.LaunchGamePort} · {managerOptions.LaunchQueryPort}", true));
        }

        var author = DiscordEmbedAuthorFromOptions();
        return new DiscordWebhookExtras(
            fields,
            FooterText: DiscordCardFooter("log hint"),
            AuthorName: author.Name,
            AuthorUrl: author.Url);
    }

    private DiscordWebhookExtras BuildDiscordPlayerEventExtras(string playerName)
    {
        var label = DiscordSteamServerLabel();
        var fields = new List<DiscordEmbedField>
        {
            new DiscordEmbedField(DiscordFieldPlayerLabel(), playerName.Trim(), false),
            new DiscordEmbedField(DiscordFieldServerLabel(), label, false)
        };
        if (managerOptions.DiscordWebhookShowPortsOnEmbeds)
        {
            fields.Add(new DiscordEmbedField("Ports", $"{managerOptions.LaunchGamePort} · {managerOptions.LaunchQueryPort}", true));
        }

        var author = DiscordEmbedAuthorFromOptions();
        return new DiscordWebhookExtras(
            fields,
            FooterText: DiscordCardFooter("roster"),
            AuthorName: author.Name,
            AuthorUrl: author.Url);
    }

    private string TruncateDiscordDescription(string s, int? hardCap = null)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        var cap = Math.Clamp(managerOptions.DiscordWebhookDescriptionMaxChars, 800, 4096);
        if (hardCap is int h)
        {
            cap = Math.Min(cap, h);
        }

        s = s.Replace("\r\n", "\n", StringComparison.Ordinal);
        return s.Length <= cap ? s : s[..cap] + "…";
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

    private void SetupTimers()
    {
        policyTimer = new System.Windows.Forms.Timer { Interval = 10000 };
        metricsTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        updateTimer = new System.Windows.Forms.Timer { Interval = 30000 };
        policyTimer.Tick += (_, _) => EvaluatePolicies();
        metricsTimer.Tick += (_, _) => UpdateStats();
        updateTimer.Tick += (_, _) => CheckScheduledUpdate();
        heartbeatTimer.Tick += (_, _) => HeartbeatDiscordTick();
        policyTimer.Start();
        metricsTimer.Start();
        updateTimer.Start();
        heartbeatTimer.Start();
    }

    private void PrimeInstallPathWebhookState()
    {
        if (string.IsNullOrWhiteSpace(serverLocationBox.Text))
        {
            _installPathPreviouslyValid = false;
            return;
        }

        var exe = Path.Combine(serverLocationBox.Text.Trim(), "Icarus", "Binaries", "Win64", "IcarusServer-Win64-Shipping.exe");
        _installPathPreviouslyValid = File.Exists(exe);
    }

    private void HeartbeatDiscordTick()
    {
        if (managerOptions.DiscordWebhookHeartbeatIntervalHours <= 0)
        {
            return;
        }

        var hours = managerOptions.DiscordWebhookHeartbeatIntervalHours;
        var now = DateTime.UtcNow;
        if (_lastDiscordHeartbeatUtc == DateTime.MinValue)
        {
            _lastDiscordHeartbeatUtc = now;
            return;
        }

        if ((now - _lastDiscordHeartbeatUtc).TotalHours < hours)
        {
            return;
        }

        _lastDiscordHeartbeatUtc = now;
            PostDiscordWebhook(
                DiscordWebhookEventKind.ManagerHeartbeat,
                "Still here",
                "_Quiet pulse from the manager — game state is in the fields below._",
                BuildDiscordHeartbeatExtras());
    }

    private void LoadPersistedData()
    {
        managerOptions = optionsService.Load();
        NormalizeConsoleLogPresetName(managerOptions);
        MigrateLaunchArgumentsFromLegacySettingsIfNeeded();
        EnsureLaunchArgumentsValidOrDefault();
        serverLocationBox.Text = Properties.Settings.Default.serverLocation;
        serverNameBox.Text = Properties.Settings.Default.serverName;
        queryPortBox.Text = Properties.Settings.Default.queryPort.ToString();
        gamePortBox.Text = Properties.Settings.Default.gamePort.ToString();
        restartTimeBox.SelectedIndex = Math.Clamp((Properties.Settings.Default.restartInterval / 60) - 1, 0, restartTimeBox.Items.Count - 1);
        LoadManagerOptionsToUi();
        LoadIniToUi();
        SyncLegacyPortTextBoxesFromManagerOptions();
    }

    /// <summary>
    /// Ports and -Log were historically split between Application Settings and manager JSON; they are
    /// manager-owned (not ServerSettings.ini). One-time copy from legacy settings when options file predates schema 4.
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

    private void MigrateLaunchArgumentsFromLegacySettingsIfNeeded()
    {
        if (managerOptions.OptionsSchemaVersion >= 4)
        {
            return;
        }

        managerOptions.LaunchGamePort = Properties.Settings.Default.gamePort;
        managerOptions.LaunchQueryPort = Properties.Settings.Default.queryPort;
        managerOptions.OptionsSchemaVersion = 4;
        optionsService.Save(managerOptions);
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
        optionsService.Save(managerOptions);
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
        SetControl("LaunchGamePort", managerOptions.LaunchGamePort);
        SetControl("LaunchQueryPort", managerOptions.LaunchQueryPort);
        SetControl("LaunchLogPath", managerOptions.LaunchLogPath);
        ReflectConsoleLoggingControlsFromModel();
        UpdateConsoleAutoScrollButtonAppearance();
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
        managerOptions.UpdateScheduleTime = GetString("UpdateScheduleTime", "04:00");
        managerOptions.LaunchGamePort = Math.Clamp(GetInt("LaunchGamePort", managerOptions.LaunchGamePort), 1, 65535);
        managerOptions.LaunchQueryPort = Math.Clamp(GetInt("LaunchQueryPort", managerOptions.LaunchQueryPort), 1, 65535);
        managerOptions.LaunchLogPath = GetString("LaunchLogPath", string.Empty);
        SyncConsoleLoggingFromUiToModel();
        optionsService.Save(managerOptions);
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
                TruncateDiscordDescription(ex.ToString()),
                BuildDiscordDiagnosticExtras());
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
                    TruncateDiscordDescription(string.Join("\n", validation.Errors)),
                    BuildDiscordDiagnosticExtras());
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
                TruncateDiscordDescription(ex.ToString()),
                BuildDiscordDiagnosticExtras());
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
        if (string.IsNullOrWhiteSpace(serverLocationBox.Text))
        {
            MessageBox.Show("Set the server install location first.", "Prospects", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        var userDir = GetString("UserDirOverride", string.Empty);
        var savedSuffix = GetString("SavedDirSuffix", string.Empty);
        return iniService.ResolveProspectsDirectory(serverLocationBox.Text.Trim(), userDir, savedSuffix);
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
            optionsService.Save(managerOptions);
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
        optionsService.Save(managerOptions);
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

    private void StartProcess()
    {
        if (serverStarted)
        {
            return;
        }

        SaveManagerOptionsFromUi(false);
        SaveUiToIni(false);
        logger.Info("Starting game server...");
        var exe = Path.Combine(serverLocationBox.Text, "Icarus", "Binaries", "Win64", "IcarusServer-Win64-Shipping.exe");
        if (!File.Exists(exe))
        {
            logger.Error($"Executable not found: {exe}");
            MessageBox.Show("Game server executable was not found.");
            return;
        }

        var gamePort = GetInt("LaunchGamePort", managerOptions.LaunchGamePort);
        var queryPort = GetInt("LaunchQueryPort", managerOptions.LaunchQueryPort);
        var logPath = GetString("LaunchLogPath", managerOptions.LaunchLogPath);
        var args = iniService.BuildLaunchArguments(currentServerSettings, gamePort, queryPort, logPath);
        logger.Info($"Launch arguments: {args}");
        var procStartInfo = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        serverProc = new Process { StartInfo = procStartInfo, EnableRaisingEvents = true };
        serverProc.OutputDataReceived += HandleExeOutput;
        serverProc.ErrorDataReceived += HandleExeOutput;
        serverProc.Exited += (_, _) => OnServerExited();

        serverProc.Start();
        serverProc.BeginOutputReadLine();
        serverProc.BeginErrorReadLine();
        playerTracker.Clear();
        startTime = DateTime.Now;
        serverStarted = true;
        _serverReadyAnnounced = 0;
        restartInProgress = false;
        crashDetected = false;
        restartPolicyService.ResetCrashAttempts();
        UpdateStatus("Starting...");
        startServerButton.BackColor = Color.Green;
        ChangeStartButton("Stop Server");
        forceKillServerButton.Enabled = true;
        ApplyTheme();
        logger.Info("Game server started.");
    }

    private void OnServerExited()
    {
        var wasRunning = serverStarted;
        serverStarted = false;
        _serverReadyAnnounced = 0;
        playerTracker.Clear();
        UpdateStatus("Idle");
        ChangeStartButton("Start Server");
        startServerButton.BackColor = Color.Maroon;
        forceKillServerButton.Enabled = false;
        ApplyTheme();
        if (wasRunning && !restartInProgress)
        {
            crashDetected = true;
            crashCount++;
            logger.Warn("Server process exited unexpectedly.");
            PostDiscordWebhook(
                DiscordWebhookEventKind.UnexpectedExit,
                "Server dropped offline",
                "*The dedicated server exited on its own.* Check the host and logs when you can.",
                BuildDiscordLifecycleExtras());
        }
    }

    private async Task StopProcessAsync(bool isRestartOperation = false)
    {
        if (!serverStarted || serverProc == null)
        {
            return;
        }

        var proc = serverProc;
        try
        {
            logger.Info("Stopping server...");
            restartInProgress = true;
            var waitSeconds = Math.Clamp(managerOptions.GracefulShutdownWaitSeconds, 10, 900);
            var exitedGracefully = await RequestGracefulShutdownAsync(
                    proc,
                    TimeSpan.FromSeconds(waitSeconds),
                    managerOptions.GracefulShutdownTryCtrlC)
                .ConfigureAwait(true);
            if (!exitedGracefully && !SafeProcessHasExited(proc))
            {
                logger.Warn("Server did not exit gracefully in time; forcing termination.");
                try
                {
                    proc.Kill(true);
                    proc.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    logger.Warn($"Force termination step failed: {ex.Message}");
                }
            }

            lock (_serverProcDisposeLock)
            {
                if (ReferenceEquals(serverProc, proc))
                {
                    serverProc = null;
                    try
                    {
                        proc.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"Process dispose failed: {ex.Message}");
                    }
                }
            }

            serverStarted = false;
            _serverReadyAnnounced = 0;
            playerTracker.Clear();
            UpdateStatus("Idle");
            startServerButton.BackColor = Color.Maroon;
            ChangeStartButton("Start Server");
            forceKillServerButton.Enabled = false;
            ApplyTheme();
            logger.Info("Game server stopped.");
            if (!isRestartOperation)
            {
                PostDiscordWebhook(
                    DiscordWebhookEventKind.ServerStop,
                    "Session offline",
                    "*The manager stopped the dedicated server.*",
                    BuildDiscordLifecycleExtras());
            }
        }
        catch (Exception ex)
        {
            logger.Error("Failed to stop server.", ex);
        }
        finally
        {
            restartInProgress = false;
        }
    }

    private static bool SafeProcessHasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            // Includes ObjectDisposedException on some runtimes where it derives from InvalidOperationException.
            return true;
        }
    }

    private async Task<bool> RequestGracefulShutdownAsync(Process process, TimeSpan totalTimeout, bool tryCtrlC)
    {
        try
        {
            if (SafeProcessHasExited(process))
            {
                return true;
            }

            var deadline = DateTime.UtcNow + totalTimeout;

            if (tryCtrlC && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    if (WindowsConsoleShutdown.TrySendCtrlC(process.Id))
                    {
                        logger.Info("Sent Ctrl+C to the server process (graceful shutdown).");
                    }
                    else
                    {
                        logger.Warn("Ctrl+C could not be delivered to the server process; stdin fallback may be used.");
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn($"Ctrl+C delivery threw: {ex.Message}");
                }
            }
            else if (!tryCtrlC)
            {
                logger.Info("Ctrl+C skipped by manager option; using stdin quit/exit when available.");
            }

            // After Ctrl+C (when enabled), allow time to flush saves before stdin. When Ctrl+C is disabled, go to stdin immediately.
            DateTime firstPhaseEnd;
            if (tryCtrlC)
            {
                firstPhaseEnd = DateTime.UtcNow + TimeSpan.FromSeconds(45);
                if (firstPhaseEnd > deadline)
                {
                    firstPhaseEnd = deadline;
                }
            }
            else
            {
                firstPhaseEnd = DateTime.UtcNow;
            }

            while (DateTime.UtcNow < firstPhaseEnd)
            {
                if (SafeProcessHasExited(process))
                {
                    return true;
                }

                await Task.Delay(250).ConfigureAwait(true);
            }

            if (!SafeProcessHasExited(process) && process.StartInfo.RedirectStandardInput)
            {
                try
                {
                    await process.StandardInput.WriteLineAsync("quit").ConfigureAwait(true);
                    await process.StandardInput.WriteLineAsync("exit").ConfigureAwait(true);
                    await process.StandardInput.FlushAsync().ConfigureAwait(true);
                    logger.Info("Sent quit/exit on stdin (secondary shutdown path).");
                }
                catch (Exception ex)
                {
                    logger.Warn($"Stdin shutdown commands failed: {ex.Message}");
                }
            }

            while (DateTime.UtcNow < deadline)
            {
                if (SafeProcessHasExited(process))
                {
                    return true;
                }

                await Task.Delay(250).ConfigureAwait(true);
            }

            return SafeProcessHasExited(process);
        }
        catch (Exception ex)
        {
            logger.Warn($"Graceful shutdown request failed; will force stop. {ex.Message}");
            return false;
        }
    }

    private async Task ForceKillServerAsync()
    {
        var proc = serverProc;
        if (proc == null || SafeProcessHasExited(proc))
        {
            return;
        }

        var confirm = MessageBox.Show(
            "Force kill ends the server immediately. Unsaved progress may be lost.\n\nContinue?",
            "Force kill server",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        restartInProgress = true;
        try
        {
            logger.Warn("Force killing server process (user requested).");
            if (!SafeProcessHasExited(proc))
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    logger.Error("Force kill could not terminate the process.", ex);
                    return;
                }

                await Task.Run(() =>
                {
                    try
                    {
                        proc.WaitForExit(8000);
                    }
                    catch
                    {
                        // best-effort
                    }
                }).ConfigureAwait(true);
            }

            lock (_serverProcDisposeLock)
            {
                if (ReferenceEquals(serverProc, proc))
                {
                    serverProc = null;
                    try
                    {
                        proc.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"Process dispose after force kill failed: {ex.Message}");
                    }
                }
            }

            serverStarted = false;
            playerTracker.Clear();
            UpdateStatus("Idle");
            startServerButton.BackColor = Color.Maroon;
            ChangeStartButton("Start Server");
            forceKillServerButton.Enabled = false;
            ApplyTheme();
            PostDiscordWebhook(
                DiscordWebhookEventKind.ServerStop,
                "Hard stop",
                "*The server process was killed immediately* — saves may not have flushed.",
                BuildDiscordLifecycleExtras());
        }
        finally
        {
            restartInProgress = false;
        }
    }

    private async void forceKillServerButton_Click(object sender, EventArgs e)
    {
        await ForceKillServerAsync().ConfigureAwait(true);
    }

    private void PostDiscordWebhook(
        DiscordWebhookEventKind kind,
        string title,
        string? description = null,
        DiscordWebhookExtras? extras = null)
    {
        if (!managerOptions.EnableDiscordWebhook)
        {
            return;
        }

        var snapshot = managerOptions;
        var extrasSnapshot = extras;
        _ = Task.Run(async () =>
        {
            try
            {
                await automationService.SendWebhookEventAsync(snapshot, kind, title, description, extrasSnapshot)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Service already suppresses errors; extra guard for Task.Run.
            }
        });
    }

    private async Task RestartServerAsync(string reason)
    {
        if (restartInProgress)
        {
            return;
        }

        restartInProgress = true;
        lastRestartReason = reason;
        restartHistory.Add($"{DateTime.Now:HH:mm:ss} - {reason}");
        logger.Warn($"Restart requested: {reason}");
        await automationService.SendWebhookEventAsync(
            managerOptions,
            DiscordWebhookEventKind.ServerRestart,
            "Restart cycle",
            TruncateDiscordDescription(reason),
            BuildDiscordOperationalExtras()).ConfigureAwait(true);
        await StopProcessAsync(isRestartOperation: true).ConfigureAwait(true);
        await Task.Delay(1500).ConfigureAwait(true);
        StartProcess();
        lastRestartAt = DateTime.Now;
        if (!serverStarted)
        {
            PostDiscordWebhook(
                DiscordWebhookEventKind.ServerRestartFailed,
                "Restart did not come back online",
                TruncateDiscordDescription(reason),
                BuildDiscordOperationalExtras());
        }
    }

    private async void EvaluatePolicies()
    {
        var sample = metricsSampler.Sample(serverProc, startTime);
        var decision = restartPolicyService.Evaluate(
            managerOptions,
            serverStarted,
            startTime,
            DateTime.Now,
            crashed: crashDetected,
            memoryMb: sample.MemoryMb,
            maybeEmptyFromLogs: possibleServerEmpty);

        if (decision.ShouldWarn)
        {
            logger.Warn(decision.Reason);
            if (!_discordPolicyWarningActive)
            {
                _discordPolicyWarningActive = true;
                PostDiscordWebhook(
                    DiscordWebhookEventKind.RestartWarning,
                    "Heads-up",
                    TruncateDiscordDescription(decision.Reason),
                    BuildDiscordOperationalExtras());
            }
        }
        else
        {
            _discordPolicyWarningActive = false;
        }

        if (decision.ShouldRestart)
        {
            await RestartServerAsync(decision.Reason);
        }
    }

    private void UpdateStats()
    {
        var sample = metricsSampler.Sample(serverProc, startTime);
        metricsHistory.Add(sample);
        if (metricsHistory.Count > 1000)
        {
            metricsHistory.RemoveRange(0, 200);
        }

        var restartCountdown = startTime.AddMinutes(managerOptions.IntervalRestartMinutes) - DateTime.Now;
        var health = "Green";
        if (sample.MemoryMb > managerOptions.HighMemoryMbThreshold * 0.9)
        {
            health = "Yellow";
        }

        if (!serverStarted)
        {
            health = "Red";
        }

        SetStat("Uptime", sample.Uptime.ToString(@"dd\.hh\:mm\:ss"));
        SetStat("RestartCountdown", restartCountdown.TotalSeconds > 0 ? restartCountdown.ToString(@"hh\:mm\:ss") : "due");
        SetStat("LastRestartReason", lastRestartReason);
        SetStat("CrashCount", crashCount.ToString());
        SetStat("CpuPercent", $"{sample.CpuPercent:F1}%");
        SetStat("MemoryMb", $"{sample.MemoryMb:F0} MB");
        SetStat("Health", health);
        SetStat("RestartHistory", restartHistory.Count == 0 ? "-" : restartHistory[^1]);
        UpdateOnlinePlayersPanel();
        UpdateMetricsChart();
    }

    private void UpdateOnlinePlayersPanel()
    {
        if (_onlinePlayersList == null)
        {
            return;
        }

        _onlinePlayersList.Items.Clear();
        var prospectsDir = TryResolveProspectsDirectorySilent();
        var lastName = GetString("LastProspectName", string.Empty).Trim();
        if (string.IsNullOrEmpty(prospectsDir) || string.IsNullOrEmpty(lastName))
        {
            _onlinePlayersList.Items.Add("(Set install location and Last Prospect Name.)");
            return;
        }

        var fullPath = Path.Combine(prospectsDir, lastName + ".json");
        if (!File.Exists(fullPath))
        {
            _onlinePlayersList.Items.Add($"(No file: {lastName}.json)");
            return;
        }

        var mtime = File.GetLastWriteTimeUtc(fullPath);
        var cacheKey = $"{fullPath}|{mtime.Ticks}";
        if (_prospectPlayersCacheKey != cacheKey || (DateTime.UtcNow - _prospectPlayersCacheUtc).TotalSeconds > 8)
        {
            try
            {
                _prospectPlayersCache = ProspectSummaryReader.Read(fullPath);
                _prospectPlayersCacheKey = cacheKey;
                _prospectPlayersCacheUtc = DateTime.UtcNow;
            }
            catch
            {
                _prospectPlayersCache = null;
            }
        }

        var summary = _prospectPlayersCache;
        if (summary == null)
        {
            _onlinePlayersList.Items.Add("(Could not read prospect header.)");
            return;
        }

        var fromJson = summary.Members.Where(m => m.IsCurrentlyPlaying)
            .Select(m => $"{m.CharacterName} ({m.AccountName})  {m.UserId}")
            .ToList();
        foreach (var line in fromJson)
        {
            _onlinePlayersList.Items.Add("Save: " + line);
        }

        foreach (var hint in playerTracker.HintNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (fromJson.Any(j => j.Contains(hint, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _onlinePlayersList.Items.Add("Output: " + hint);
        }

        if (_onlinePlayersList.Items.Count == 0)
        {
            _onlinePlayersList.Items.Add("(Nobody flagged IsCurrentlyPlaying; no join hints in log.)");
        }
    }

    private void UpdateMetricsChart()
    {
        if (metricsGraph == null)
        {
            return;
        }

        metricsGraph.Invalidate();
    }

    private void CheckScheduledUpdate()
    {
        if (!automationService.IsUpdateDue(managerOptions, DateTime.Now))
        {
            return;
        }

        if (TimeSpan.TryParse(managerOptions.UpdateScheduleTime, out var schedule))
        {
            var bucket = DateTime.Now.Date.Add(schedule);
            if (_scheduledUpdateWebhookBucketUtc != bucket)
            {
                _scheduledUpdateWebhookBucketUtc = bucket;
                PostDiscordWebhook(
                    DiscordWebhookEventKind.ScheduledUpdateWindow,
                    "Update window",
                    serverStarted
                        ? "*Scheduled time hit* — a restart will run if your policies allow it."
                        : "*Scheduled time hit* — the server was already idle.",
                    BuildDiscordOperationalExtras());
            }
        }

        logger.Info("Scheduled update window reached.");
        if (serverStarted)
        {
            _ = RestartServerAsync("Scheduled update window");
        }
    }

    private void SetStat(string key, string value)
    {
        if (!statsLabels.TryGetValue(key, out var label))
        {
            return;
        }

        label.Text = $"{key}: {value}";
    }

    private void ApplyTheme()
    {
        themeManager.ApplyTheme(this, managerOptions.Theme);
        ApplyChartTheme();
        if (serverStarted)
        {
            startServerButton.BackColor = Color.Green;
            startServerButton.ForeColor = Color.White;
        }
        else
        {
            startServerButton.BackColor = Color.Maroon;
            startServerButton.ForeColor = Color.White;
        }

        var dark = managerOptions.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase);
        if (forceKillServerButton.Enabled)
        {
            forceKillServerButton.BackColor = dark ? Color.FromArgb(180, 90, 0) : Color.DarkOrange;
            forceKillServerButton.ForeColor = Color.White;
        }
        else
        {
            forceKillServerButton.BackColor = dark ? Color.FromArgb(53, 56, 66) : SystemColors.ControlDark;
            forceKillServerButton.ForeColor = dark ? Color.FromArgb(200, 200, 205) : Color.Black;
        }
    }

    private void ApplyChartTheme()
    {
        if (metricsGraph == null)
        {
            return;
        }

        var dark = managerOptions.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase);
        metricsGraph.BackColor = dark ? Color.FromArgb(45, 45, 48) : Color.White;
        metricsGraph.Invalidate();
    }

    private void RenderMetricsGraph(PaintEventArgs e)
    {
        if (metricsGraph == null)
        {
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var dark = managerOptions.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase);
        var gridColor = dark ? Color.FromArgb(80, 80, 80) : Color.Gainsboro;
        var textColor = dark ? Color.Gainsboro : Color.Black;
        var cpuColor = dark ? Color.DeepSkyBlue : Color.Blue;
        var memColor = dark ? Color.Orange : Color.DarkOrange;

        var rect = new Rectangle(50, 20, Math.Max(1, metricsGraph.Width - 80), Math.Max(1, metricsGraph.Height - 60));
        using var gridPen = new Pen(gridColor, 1);
        using var axisPen = new Pen(textColor, 1);
        using var font = new Font("Segoe UI", 8f);
        using var cpuPen = new Pen(cpuColor, 2);
        using var memPen = new Pen(memColor, 2);
        using var textBrush = new SolidBrush(textColor);

        for (var i = 0; i <= 5; i++)
        {
            var y = rect.Top + (i * rect.Height / 5f);
            g.DrawLine(gridPen, rect.Left, y, rect.Right, y);
        }

        g.DrawRectangle(axisPen, rect);
        g.DrawString("CPU % / Memory MB (last 120 samples)", font, textBrush, rect.Left, 2);
        g.DrawString("CPU", font, new SolidBrush(cpuColor), rect.Right - 90, 2);
        g.DrawString("MEM", font, new SolidBrush(memColor), rect.Right - 50, 2);

        var window = metricsHistory.TakeLast(120).ToList();
        if (window.Count < 2)
        {
            return;
        }

        var maxMem = Math.Max(100, window.Max(x => x.MemoryMb));
        var cpuPoints = new PointF[window.Count];
        var memPoints = new PointF[window.Count];
        for (var i = 0; i < window.Count; i++)
        {
            var x = rect.Left + (i / (float)(window.Count - 1)) * rect.Width;
            var cpuY = rect.Bottom - (float)(Math.Clamp(window[i].CpuPercent, 0, 100) / 100d) * rect.Height;
            var memY = rect.Bottom - (float)(window[i].MemoryMb / maxMem) * rect.Height;
            cpuPoints[i] = new PointF(x, cpuY);
            memPoints[i] = new PointF(x, memY);
        }

        g.DrawLines(cpuPen, cpuPoints);
        g.DrawLines(memPen, memPoints);
    }

    private void RunSetupWizardIfNeeded()
    {
        if (!string.IsNullOrWhiteSpace(serverLocationBox.Text.Trim()))
        {
            return;
        }

        var r = MessageBox.Show(
            this,
            "No dedicated server install folder is set yet.\n\nOpen the setup wizard to choose the folder (the directory that will contain Icarus\\Binaries\\Win64\\IcarusServer-Win64-Shipping.exe)?",
            "Welcome",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);
        if (r == DialogResult.Yes)
        {
            OpenSetupWizard();
        }
    }

    private void OpenSetupWizard()
    {
        using var wizard = new SetupWizardForm(serverLocationBox.Text);
        if (wizard.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(wizard.SelectedPath))
        {
            ApplyServerInstallPath(wizard.SelectedPath);
        }
    }

    private void ApplyServerInstallPath(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        folder = folder.Trim();
        Properties.Settings.Default.serverLocation = folder;
        Properties.Settings.Default.Save();
        serverLocationBox.Text = folder;
        PrimeInstallPathWebhookState();
        UpdateServerAvailability();
        LoadIniToUi();
    }

    private void ExportMetricsCsv()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "icarus-metrics.csv" };
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,CpuPercent,MemoryMb,UptimeSeconds");
        foreach (var row in metricsHistory)
        {
            sb.AppendLine($"{row.Timestamp:O},{row.CpuPercent:F2},{row.MemoryMb:F2},{row.Uptime.TotalSeconds:F0}");
        }

        File.WriteAllText(dialog.FileName, sb.ToString());
        logger.Info($"Exported metrics CSV: {dialog.FileName}");
    }

        private void selectLocationButton_Click(object sender, EventArgs e)
    {
        folderBrowser.Description = "Select the dedicated server install folder (game root).";
        if (folderBrowser.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        ApplyServerInstallPath(folderBrowser.SelectedPath);
    }

    private void UpdateServerAvailability()
    {
        var exe = Path.Combine(serverLocationBox.Text.Trim(), "Icarus", "Binaries", "Win64", "IcarusServer-Win64-Shipping.exe");
        var valid = File.Exists(exe);
        if (!valid && _installPathPreviouslyValid)
        {
            PostDiscordWebhook(
                DiscordWebhookEventKind.InstallPathIssue,
                "Game binary missing",
                TruncateDiscordDescription(exe),
                BuildDiscordOperationalExtras());
        }

        _installPathPreviouslyValid = valid;
        if (!valid)
        {
            logger.Warn("Icarus installation not found at selected location.");
            startServerButton.Enabled = false;
            UpdateStatus("Waiting for install");
        }
        else
        {
            logger.Info("Icarus installation found.");
            startServerButton.Enabled = true;
            UpdateStatus("Idle");
        }
    }

        private void installServerButton_Click(object sender, EventArgs e)
    {
        try
        {
            UpdateStatus("Installing...");
            var steamcmd = Path.Combine(Environment.CurrentDirectory, "steamcmd", "steamcmd.exe");
            var args = $"/b /w /high +login anonymous +force_install_dir \"{Properties.Settings.Default.serverLocation}\" +app_update 2089300 validate +quit";
            var procStartInfo = new ProcessStartInfo(steamcmd, args) { UseShellExecute = false };
            using var installer = new Process { StartInfo = procStartInfo };
            installer.Start();
            installer.WaitForExit();
            var exitCode = installer.ExitCode;
            PostDiscordWebhook(
                DiscordWebhookEventKind.SteamCmdFinished,
                exitCode == 0 ? "SteamCMD finished" : "SteamCMD reported failure",
                exitCode == 0 ? "*Install or update step completed.*" : $"*Exit code* `{exitCode}`",
                BuildDiscordOperationalExtras());
            installServerButton.Enabled = false;
            startServerButton.Enabled = true;
            UpdateStatus("Idle");
            Properties.Settings.Default.IsInit = false;
            Properties.Settings.Default.Save();
            logger.Info("Install/update completed.");
        }
        catch (Exception ex)
        {
            logger.Error("Install/update failed.", ex);
            PostDiscordWebhook(
                DiscordWebhookEventKind.SteamCmdFinished,
                "SteamCMD install/update failed",
                TruncateDiscordDescription(ex.ToString()),
                BuildDiscordDiagnosticExtras());
            MessageBox.Show("Install/update failed. See logs for details.");
        }
        }

        private async void startServerButton_Click(object sender, EventArgs e)
        {
            if (serverStarted)
            {
                await StopProcessAsync().ConfigureAwait(true);
            }
            else
            {
                StartProcess();
            }
        }

    private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
    {
        var proc = serverProc;
        if (!serverStarted || proc == null || SafeProcessHasExited(proc))
        {
            return;
        }

        var interactiveClose = e.CloseReason is CloseReason.UserClosing
            or CloseReason.ApplicationExitCall
            or CloseReason.MdiFormClosing
            or CloseReason.FormOwnerClosing;

        if (interactiveClose)
        {
            var confirm = MessageBox.Show(
                "The dedicated server is still running. Closing the manager will stop the server immediately (not a graceful shutdown). Unsaved progress may be lost.\n\nClose the manager anyway?",
                "Server is running",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        SynchronouslyShutdownServerForAppClose(notifyDiscord: interactiveClose);
    }

    /// <summary>
    /// Terminates the game process without graceful shutdown. Used when exiting the app; avoids blocking on <see cref="StopProcessAsync"/>.
    /// </summary>
    private void SynchronouslyShutdownServerForAppClose(bool notifyDiscord)
    {
        var proc = serverProc;
        if (proc == null)
        {
            return;
        }

        try
        {
            if (!SafeProcessHasExited(proc))
            {
                logger.Warn("Terminating server process because the manager is closing.");
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    logger.Error("Could not kill server process on manager exit.", ex);
                }

                try
                {
                    proc.WaitForExit(8000);
                }
                catch
                {
                    // best-effort
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error("Shutdown cleanup failed while closing manager.", ex);
        }

        lock (_serverProcDisposeLock)
        {
            if (ReferenceEquals(serverProc, proc))
            {
                serverProc = null;
                try
                {
                    proc.Dispose();
                }
                catch (Exception ex)
                {
                    logger.Warn($"Process dispose on manager exit failed: {ex.Message}");
                }
            }
        }

        serverStarted = false;
        _serverReadyAnnounced = 0;
        playerTracker.Clear();

        if (notifyDiscord)
        {
            PostDiscordWebhook(
                DiscordWebhookEventKind.ServerStop,
                "Session offline",
                "*The manager closed and the server was stopped immediately.*",
                BuildDiscordLifecycleExtras());
        }
    }

    private void KillProcess(object? o, EventArgs e)
    {
        try
        {
            var proc = serverProc;
            if (proc == null || SafeProcessHasExited(proc))
            {
                return;
            }

            logger.Warn("Server process still running after close; applying best-effort kill.");
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                logger.Warn($"Final kill on FormClosed failed: {ex.Message}");
            }

            try
            {
                proc.WaitForExit(3000);
            }
            catch
            {
                // best-effort
            }

            lock (_serverProcDisposeLock)
            {
                if (ReferenceEquals(serverProc, proc))
                {
                    serverProc = null;
                    try
                    {
                        proc.Dispose();
                    }
                    catch
                    {
                        // best-effort
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error("FormClosed process cleanup failed.", ex);
        }
    }

        private void HandleExeOutput(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data))
        {
            return;
        }

        var line = e.Data;
        if (line.Contains(ReadyForPlayersLogMarker, StringComparison.OrdinalIgnoreCase) &&
            Interlocked.Exchange(ref _serverReadyAnnounced, 1) == 0)
        {
            logger.Info("Detected readiness marker in server output. Server is ready for players.");
            UpdateStatus("Running (Ready for players)");
            PostDiscordWebhook(
                DiscordWebhookEventKind.ServerStart,
                "Drop zone is live",
                "**Players can join** — the server reported readiness in its log.",
                BuildDiscordLaunchExtras());
        }

        var playerLine = playerTracker.ProcessLogLine(line);
        switch (playerLine.Kind)
        {
            case PlayerLogHintKind.Joined when !string.IsNullOrWhiteSpace(playerLine.PlayerName):
                PostDiscordWebhook(
                    DiscordWebhookEventKind.PlayerJoin,
                    "Crew inbound",
                    $"**{playerLine.PlayerName}** *is on approach (log hint).*",
                    BuildDiscordPlayerEventExtras(playerLine.PlayerName));
                break;
            case PlayerLogHintKind.Left when !string.IsNullOrWhiteSpace(playerLine.PlayerName):
                PostDiscordWebhook(
                    DiscordWebhookEventKind.PlayerLeave,
                    "Crew outbound",
                    $"**{playerLine.PlayerName}** *signed off (log hint).*",
                    BuildDiscordPlayerEventExtras(playerLine.PlayerName));
                break;
        }

        if (ServerLogChatHeuristic.LooksLikeChatLine(line, playerLine))
        {
            PostDiscordWebhook(
                DiscordWebhookEventKind.Chat,
                "Comms intercept",
                TruncateDiscordDescription(line, 2000),
                BuildDiscordMinimalServerTag(currentServerSettings.SteamServerName));
        }
        else if (ServerLogGameplayHeuristic.LooksLikeLevelUp(line, playerLine))
        {
            PostDiscordWebhook(
                DiscordWebhookEventKind.LevelUp,
                "Progress ping",
                TruncateDiscordDescription(line, 2000),
                BuildDiscordMinimalServerTag(currentServerSettings.SteamServerName));
        }
        else if (ServerLogGameplayHeuristic.LooksLikePlayerDeath(line, playerLine))
        {
            PostDiscordWebhook(
                DiscordWebhookEventKind.PlayerDeath,
                "Hazard ping",
                TruncateDiscordDescription(line, 2000),
                BuildDiscordMinimalServerTag(currentServerSettings.SteamServerName));
        }

        if (playerLine.Kind == PlayerLogHintKind.Joined)
        {
            possibleServerEmpty = false;
        }
        else if (playerLine.Kind == PlayerLogHintKind.Left)
        {
            possibleServerEmpty = true;
        }

        logger.Info(line, isGameProcessOutput: true);
        }

        private void UpdateStatus(string text)
        {
            if (serverStatusBox.InvokeRequired)
            {
            serverStatusBox.Invoke(new InvokeConsoleWrite(UpdateStatus), text);
            }
            else
            {
                serverStatusBox.Text = text;
            }
        }

        private void ChangeStartButton(string text)
        {
            if (startServerButton.InvokeRequired)
            {
            startServerButton.Invoke(new InvokeConsoleWrite(ChangeStartButton), text);
            }
            else
            {
                startServerButton.Text = text;
            }
        }

        private void serverNameBox_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.serverName = serverNameBox.Text;
            Properties.Settings.Default.Save();
        }

        private void queryPortBox_TextChanged(object sender, EventArgs e)
        {
        if (!int.TryParse(queryPortBox.Text, out var port))
        {
            return;
        }

        Properties.Settings.Default.queryPort = port;
        Properties.Settings.Default.Save();
        }

        private void gamePortBox_TextChanged(object sender, EventArgs e)
    {
        if (!int.TryParse(gamePortBox.Text, out var port))
        {
            return;
        }

        Properties.Settings.Default.gamePort = port;
        Properties.Settings.Default.Save();
        }

        private void restartTimeBox_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (!int.TryParse(restartTimeBox.Text.Split(' ')[0], out var hours))
        {
            return;
        }

        var time = hours * 60;
            Properties.Settings.Default.restartInterval = time;
            Properties.Settings.Default.Save();
        managerOptions.IntervalRestartMinutes = time;
        optionsService.Save(managerOptions);
    }

    private void SetControl(string key, object value)
    {
        if (!iniControls.TryGetValue(key, out var control) && !managerControls.TryGetValue(key, out control))
        {
            return;
        }

        switch (control)
        {
            case TextBox text:
                text.Text = Convert.ToString(value) ?? string.Empty;
                break;
            case ComboBox combo:
                var stringValue = Convert.ToString(value) ?? string.Empty;
                var idx = combo.FindStringExact(stringValue);
                combo.SelectedIndex = idx >= 0 ? idx : 0;
                break;
            case CheckBox check:
                check.Checked = Convert.ToBoolean(value);
                break;
            case NumericUpDown num:
                var dec = Convert.ToDecimal(value);
                num.Value = Math.Clamp(dec, num.Minimum, num.Maximum);
                break;
        }
    }

    private string GetString(string key, string fallback = "")
    {
        if (!managerControls.TryGetValue(key, out var control) && !iniControls.TryGetValue(key, out control))
        {
            return fallback;
        }

        return control switch
        {
            TextBox text => text.Text,
            ComboBox combo when combo.SelectedItem != null => combo.SelectedItem.ToString() ?? fallback,
            _ => fallback
        };
    }

    private bool GetBool(string key, bool fallback = false)
    {
        if (!managerControls.TryGetValue(key, out var control) && !iniControls.TryGetValue(key, out control))
        {
            return fallback;
        }

        return control switch
        {
            CheckBox check => check.Checked,
            _ => fallback
        };
    }

    private int GetInt(string key, int fallback = 0)
    {
        if (!managerControls.TryGetValue(key, out var control) && !iniControls.TryGetValue(key, out control))
        {
            return fallback;
        }

        return control switch
        {
            NumericUpDown num => (int)num.Value,
            TextBox text when int.TryParse(text.Text, out var n) => n,
            _ => fallback
        };
        }

        public delegate void InvokeConsoleWrite(string text);

    private static string Prompt(string text, string caption, string defaultValue)
    {
        using var form = new Form
        {
            Width = 520,
            Height = 160,
            Text = caption,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false
        };
        var label = new Label { Left = 10, Top = 12, Width = 480, Text = text };
        var input = new TextBox { Left = 10, Top = 38, Width = 480, Text = defaultValue };
        var ok = new Button { Text = "OK", Left = 330, Width = 75, Top = 72, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 415, Width = 75, Top = 72, DialogResult = DialogResult.Cancel };
        form.Controls.AddRange(new Control[] { label, input, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog() == DialogResult.OK ? input.Text : string.Empty;
    }
}
