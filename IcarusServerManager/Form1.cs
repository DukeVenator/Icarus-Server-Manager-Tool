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
    private readonly ManagerUpdateService managerUpdateService = new();
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
    private const string ManagerMainExeName = "IcarusServerManager.exe";
    private DateTime _lastDiscordHeartbeatUtc = DateTime.MinValue;
    private DateTime _lastManagerUpdateCheckUtc = DateTime.MinValue;
    private bool _managerUpdateCheckInProgress;
    private bool _managerUpdatePromptShownThisRun;
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
        _ = CheckForManagerUpdateAsync(userInitiated: false);
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
