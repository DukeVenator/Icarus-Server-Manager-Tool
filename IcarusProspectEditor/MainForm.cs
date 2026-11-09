using System.Collections;
using System.ComponentModel;
using System.Reflection;
using IcarusProspectEditor.Controls;
using IcarusProspectEditor.Mapping;
using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using IcarusSaveLib;
using Newtonsoft.Json;

namespace IcarusProspectEditor;

internal sealed partial class MainForm : Form
{
    private const int NarrowLayoutThreshold = 900;

    private readonly BindingList<MemberRow> _members = [];
    private readonly BindingList<CustomSettingRow> _customSettings = [];
    private readonly BindingList<RecorderRow> _characters = [];
    private readonly BindingList<RecorderRow> _missions = [];
    private readonly BindingList<RecorderRow> _prebuilts = [];
    private readonly BindingList<RecorderRow> _allRecorders = [];
    private readonly BindingList<MountRow> _mounts = [];
    private readonly ProspectProjectionCache _projectionCache = new();

    private readonly MenuStrip _menu = new() { Dock = DockStyle.Top };
    private readonly Label _documentTitle = new() { AutoSize = true, Font = new Font("Segoe UI", 12.5f, FontStyle.Bold) };
    private readonly Label _documentPath = new() { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _unsavedLabel = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly Button _saveButton = new() { Text = "Save", AutoSize = true, Padding = new Padding(14, 4, 14, 4) };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 26, Text = "Ready.", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
    private readonly ThemedTabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly ToolTip _toolTip = new();
    private readonly TableLayoutPanel _metadataRoot = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, AutoScroll = true, Padding = new Padding(24, 16, 24, 16) };
    private readonly FlowLayoutPanel _identitySection = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 24, 24) };
    private readonly FlowLayoutPanel _ownershipSection = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 24, 24) };
    private readonly FlowLayoutPanel _gameplaySection = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 24, 24) };
    private readonly FlowLayoutPanel _timingSection = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 24, 24) };

    private readonly TextBox _lobbyName = new() { Dock = DockStyle.Fill };
    private readonly TextBox _prospectId = new() { Dock = DockStyle.Fill };
    private readonly TextBox _difficulty = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _dropPoint = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 9999 };
    private readonly CheckBox _noRespawns = new() { Text = "No respawns (hardcore)", AutoSize = true };
    private readonly CheckBox _insurance = new() { Text = "Insurance enabled", AutoSize = true };
    private readonly TextBox _claimedAccountId = new() { Dock = DockStyle.Fill };
    private readonly Button _pickClaimedAccountButton = new() { Text = "Choose player…", AutoSize = true };
    private readonly NumericUpDown _claimedCharacterSlot = new() { Dock = DockStyle.Fill, Minimum = -1, Maximum = 100 };
    private readonly TextBox _prospectState = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _elapsedTime = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = int.MaxValue };
    private readonly NumericUpDown _cost = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = int.MaxValue };
    private readonly NumericUpDown _reward = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = int.MaxValue };
    private readonly TextBox _prospectDtKey = new() { Dock = DockStyle.Fill };
    private readonly TextBox _factionMissionDtKey = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _neverExpires = new() { Text = "Never expires", AutoSize = true };
    private readonly Label _expiryPlaceholder = new() { Text = "No expiry date", AutoSize = true, Visible = false };
    private readonly DateTimePicker _expireTime = new() { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss" };
    private readonly Panel _advancedPanel = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Visible = false };
    private readonly Button _advancedToggle = new()
    {
        Text = "Advanced identifiers ▸",
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(0),
        Margin = new Padding(0),
        AccessibleName = "Advanced identifiers"
    };

    private ProspectDocument? _document;
    private readonly HashSet<string> _dangerNotes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inspectorEdits = new(StringComparer.Ordinal);
    private readonly Dictionary<int, List<TalentRow>> _mountTalentsOverride = [];
    private readonly Dictionary<Guid, string> _membersBaseline = [];
    private readonly Dictionary<Guid, string> _customSettingsBaseline = [];
    private readonly Dictionary<Guid, string> _mountsBaseline = [];
    private MetadataSnapshot _metadataBaseline = MetadataSnapshot.Empty;
    private byte[]? _savedSnapshotBytes;
    private string _fullDocumentPath = string.Empty;
    private int _revision;
    private int _savedRevision;
    private int _suppressDirtyDepth;
    private bool _isDarkTheme;
    private DataGridView? _membersGrid;
    private DataGridView? _customSettingsGrid;
    private DataGridView? _mountsGrid;
    private readonly Dictionary<Control, string> _lastLoggedMetadataValues = new();
    private readonly Dictionary<string, string> _cellEditBeforeValues = new(StringComparer.Ordinal);
    // Paths whose recorder value came from the raw/Advanced inspector (or a mount merge conflict
    // where the raw value won). A curated mount replay must never overwrite these on save (E02).
    private readonly Dictionary<int, HashSet<string>> _advancedRecorderFieldEdits = [];
    private DataGridView? _charactersGrid;
    private DataGridView? _missionsGrid;
    private DataGridView? _prebuiltsGrid;
    private DataGridView? _allRecordersGrid;

    private readonly ProspectEditorUpdateService _editorUpdateService = new();
    private ProspectEditorUpdateSettings _updateSettings = ProspectEditorUpdateSettings.Load();
    private readonly System.Windows.Forms.Timer _editorUpdateTimer = new() { Interval = 30000 };
    private DateTime _lastEditorUpdateCheckUtc = DateTime.MinValue;
    private bool _editorUpdateCheckInProgress;
    private bool _editorUpdatePromptShownThisRun;
    private readonly string? _startupProspectPath;

    // Single revision-based mutation marker. Metadata/grid edits, recorder inspector edits and
    // recorder removal all call MarkMutated, so every edit class takes part in close/save prompts.
    private bool IsDirty => _document is not null && _revision != _savedRevision;

    public MainForm()
        : this(startupProspectPath: null)
    {
    }

    /// <param name="startupProspectPath">
    /// Prospect supplied on the command line. It is opened once the window is shown, so drag-and-drop
    /// onto the executable and UI automation can skip the Open dialog.
    /// </param>
    internal MainForm(string? startupProspectPath)
    {
        _startupProspectPath = startupProspectPath;
        Text = "Icarus Prospect Editor";
        Width = 1440;
        Height = 900;
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        BuildLayout();
        _tabs.AccessibleName = "Prospect sections";
        _saveButton.AccessibleName = "Save prospect";
        // The title, path and status texts are their own accessible names: a constant label would
        // hide the actual value from assistive technology.
        _insurance.AccessibleName = "Insurance enabled";
        _noRespawns.AccessibleName = "No respawns (hardcore)";
        _neverExpires.AccessibleName = "Never expires";
        _expireTime.AccessibleName = "Expiry date and time";
        BindTables();
        InitializeTheme();
        SetupEditorUpdateFlow();
        FormClosing += MainFormOnFormClosing;
        KeyDown += MainFormOnKeyDown;
        Shown += (_, _) =>
        {
            _toolTip.SetToolTip(_documentPath, _documentPath.Text);
            if (_startupProspectPath is not null)
            {
                TryOpenStartupProspect(_startupProspectPath);
            }
            else if (_document is null)
            {
                OpenProspect();
            }
        };
    }

    private void TryOpenStartupProspect(string path)
    {
        try
        {
            LoadDocument(path);
            AppLogService.UserAction($"Opened prospect from the command line: {path}");
            AppLogService.Info($"Prospect loaded from the command line: {path}");
        }
        catch (Exception ex)
        {
            // A bad path argument must not leave an empty, unresponsive window behind.
            AppLogService.Error($"Failed to load the prospect supplied on the command line: {path}", ex);
            MessageBox.Show(
                this,
                $"Could not open the prospect passed on the command line.\n\n{ex.Message}",
                "Load failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            OpenProspect();
        }
    }

    // ---------------------------------------------------------------- layout

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        // Without an explicit 100% column the default column sizes to its content, so the tab strip
        // kept its creation-time width and the whole shell was clipped when the window was resized.
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(BuildMenu(), 0, 0);
        root.Controls.Add(BuildHeader(), 0, 1);
        root.Controls.Add(_tabs, 0, 2);
        root.Controls.Add(_status, 0, 3);

        _tabs.TabPages.Add(BuildMetadataTab());
        _tabs.TabPages.Add(BuildGridTab("Members", _members));
        _tabs.TabPages.Add(BuildGridTab("Custom Settings", _customSettings));
        _tabs.TabPages.Add(BuildRecorderTab("Characters", _characters));
        _tabs.TabPages.Add(BuildRecorderTab("Missions", _missions));
        _tabs.TabPages.Add(BuildRecorderTab("Prebuilts", _prebuilts));
        _tabs.TabPages.Add(BuildMountsTab());
        _tabs.TabPages.Add(BuildRecorderTab("Diagnostics", _allRecorders));
        MainMenuStrip = _menu;
    }

    private MenuStrip BuildMenu()
    {
        _menu.Items.Add(BuildMenuItem("&File",
            ("&Open prospect…", "Ctrl+O", (_, _) => OpenProspect()),
            ("&Save", "Ctrl+S", (_, _) => SaveDocument()),
            ("Save &as…", "Ctrl+Shift+S", (_, _) => SaveAs()),
            ("&Export decoded JSON…", "Ctrl+E", async (_, _) => await ExportDecodedAsync()),
            ("E&xit", "Alt+F4", (_, _) => Close())));

        _menu.Items.Add(BuildMenuItem("&Edit",
            ("&Revert all unsaved changes…", "Ctrl+R", (_, _) => RevertAllUnsavedChanges()),
            ("Revert &selected rows", "Ctrl+Shift+R", (_, _) => RevertSelectedOnActiveTab()),
            ("Choose claimed &player…", null, (_, _) => PickClaimedAccountFromKnownPlayers())));

        var view = BuildMenuItem("&View", ("&Refresh view", "F5", (_, _) => RefreshTabs()));
        var theme = new ToolStripMenuItem("&Theme");
        _darkThemeMenuItem = new ToolStripMenuItem("&Dark") { Checked = true };
        _lightThemeMenuItem = new ToolStripMenuItem("&Light");
        _darkThemeMenuItem.Click += (_, _) => SetTheme("Dark");
        _lightThemeMenuItem.Click += (_, _) => SetTheme("Light");
        theme.DropDownItems.Add(_darkThemeMenuItem);
        theme.DropDownItems.Add(_lightThemeMenuItem);
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(theme);
        _menu.Items.Add(view);

        _menu.Items.Add(BuildMenuItem("&Tools",
            ("&Check for updates…", null, async (_, _) => await CheckForEditorUpdateAsync(userInitiated: true)),
            ("Update &settings…", null, (_, _) => ShowEditorUpdateSettings()),
            ("Open &log folder", null, (_, _) => AppLogService.OpenLogFolder())));

        _menu.Items.Add(BuildMenuItem("&Help",
            ("&About Icarus Prospect Editor", null, (_, _) => ShowAbout())));

        return _menu;
    }

    private static ToolStripMenuItem BuildMenuItem(string text, params (string Text, string? Shortcut, EventHandler Click)[] items)
    {
        var menu = new ToolStripMenuItem(text);
        foreach (var (itemText, shortcut, click) in items)
        {
            var item = new ToolStripMenuItem(itemText);
            if (!string.IsNullOrEmpty(shortcut))
            {
                // Bind the key: a display string alone advertised a shortcut that never fired.
                if (TryParseShortcut(shortcut, out var keys))
                {
                    item.ShortcutKeys = keys;
                }
                else
                {
                    item.ShortcutKeyDisplayString = shortcut;
                }
            }

            item.Click += click;
            menu.DropDownItems.Add(item);
        }

        return menu;
    }

    private static bool TryParseShortcut(string shortcut, out Keys keys)
    {
        keys = Keys.None;
        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var modifiers = Keys.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= Keys.Control;
                    break;
                case "SHIFT":
                    modifiers |= Keys.Shift;
                    break;
                case "ALT":
                    modifiers |= Keys.Alt;
                    break;
                default:
                    return false;
            }
        }

        var keyText = parts[^1].ToUpperInvariant();
        if (keyText.StartsWith('F') &&
            int.TryParse(keyText.AsSpan(1), out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            keys = modifiers | (Keys)((int)Keys.F1 + functionKey - 1);
            return true;
        }

        if (keyText.Length == 1 && char.IsLetterOrDigit(keyText[0]))
        {
            keys = modifiers | (Keys)char.ToUpperInvariant(keyText[0]);
            return true;
        }

        return false;
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(24, 12, 24, 8)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var identity = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        identity.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        identity.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _documentTitle.Text = "No prospect open";
        var pathRow = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 0) };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _documentPath.Text = "Open a prospect to begin.";
        _documentPath.MinimumSize = new Size(300, 20);
        _documentPath.Resize += (_, _) => ApplyDocumentPathEllipsis();
        var copyPath = new Button { Text = "Copy path", AutoSize = true, FlatStyle = FlatStyle.Flat };
        copyPath.Click += (_, _) => CopyDocumentPath();
        pathRow.Controls.Add(_documentPath, 0, 0);
        pathRow.Controls.Add(copyPath, 1, 0);
        identity.Controls.Add(_documentTitle, 0, 0);
        identity.Controls.Add(pathRow, 0, 1);

        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = new Padding(12, 4, 0, 0) };
        var open = new Button { Text = "Open", AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
        open.Click += (_, _) => OpenProspect();
        _saveButton.Click += (_, _) =>
        {
            AppLogService.UserAction("Save clicked.");
            SaveDocument();
        };
        var openFolder = new Button { Text = "Open folder", AutoSize = true };
        openFolder.Click += (_, _) => OpenDocumentFolder();
        actions.Controls.Add(open);
        actions.Controls.Add(_saveButton);
        actions.Controls.Add(openFolder);

        header.Controls.Add(identity, 0, 0);
        header.Controls.Add(actions, 1, 0);
        header.Controls.Add(_unsavedLabel, 2, 0);
        header.SetColumnSpan(identity, 1);
        return header;
    }

    private TabPage BuildMetadataTab()
    {
        var page = new TabPage("Metadata") { Padding = new Padding(0) };
        _metadataRoot.ColumnStyles.Clear();
        _metadataRoot.RowStyles.Clear();
        _metadataRoot.Resize += (_, _) => ReflowMetadata();

        BuildIdentitySection();
        BuildOwnershipSection();
        BuildGameplaySection();
        BuildTimingSection();
        ReflowMetadata();

        page.Controls.Add(_metadataRoot);
        return page;
    }

    private void BuildIdentitySection()
    {
        _identitySection.Controls.Add(SectionHeading("Identity", "Name, identifier and world state."));
        _identitySection.Controls.Add(BuildField("Lobby name", _lobbyName, 320));
        _identitySection.Controls.Add(BuildField("Prospect ID", _prospectId, 320));
        _identitySection.Controls.Add(BuildField("Difficulty", _difficulty, 220));
        _identitySection.Controls.Add(BuildField("Prospect state", _prospectState, 220));

        _advancedPanel.Controls.Add(BuildField("Prospect data-table key (internal)", _prospectDtKey, 320));
        _advancedPanel.Controls.Add(BuildField("Faction mission data-table key (internal)", _factionMissionDtKey, 320));
        _advancedToggle.Click += (_, _) =>
        {
            _advancedPanel.Visible = !_advancedPanel.Visible;
            _advancedToggle.Text = _advancedPanel.Visible ? "Advanced identifiers ▾" : "Advanced identifiers ▸";
        };
        var advancedHost = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        advancedHost.Controls.Add(_advancedToggle);
        advancedHost.Controls.Add(_advancedPanel);
        _identitySection.Controls.Add(advancedHost);

        RegisterMetadataFieldEvents();
    }

    private void BuildOwnershipSection()
    {
        _ownershipSection.Controls.Add(SectionHeading("Ownership", "Which account and character own this prospect."));
        var accountRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
        var accountField = BuildField("Account ID", _claimedAccountId, 260);
        accountRow.Controls.Add(accountField);
        accountRow.Controls.Add(new Panel { Width = 8, Height = 1 });
        _pickClaimedAccountButton.Margin = new Padding(0, 22, 0, 0);
        accountRow.Controls.Add(_pickClaimedAccountButton);
        _ownershipSection.Controls.Add(accountRow);
        _ownershipSection.Controls.Add(BuildField("Character slot", _claimedCharacterSlot, 120));
    }

    private void BuildGameplaySection()
    {
        _gameplaySection.Controls.Add(SectionHeading("Gameplay", "Drop rules and difficulty modifiers."));
        _gameplaySection.Controls.Add(BuildField("Drop point", _dropPoint, 120));
        _gameplaySection.Controls.Add(WithMargin(_insurance));
        _gameplaySection.Controls.Add(WithMargin(_noRespawns));
    }

    private void BuildTimingSection()
    {
        _timingSection.Controls.Add(SectionHeading("Timing & rewards", "Elapsed time, payout and expiry."));
        _timingSection.Controls.Add(BuildField("Elapsed time (seconds)", _elapsedTime, 160));
        _timingSection.Controls.Add(BuildField("Cost", _cost, 120));
        _timingSection.Controls.Add(BuildField("Reward", _reward, 120));
        var expiryHost = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        expiryHost.Controls.Add(new Label { Text = "Expiry", AutoSize = true });
        expiryHost.Controls.Add(_neverExpires);
        expiryHost.Controls.Add(_expiryPlaceholder);
        expiryHost.Controls.Add(_expireTime);
        _expireTime.Width = 200;
        _timingSection.Controls.Add(expiryHost);
    }

    private static Control SectionHeading(string title, string subtitle)
    {
        var host = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        host.Controls.Add(new Label { Text = title, AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI", 11f, FontStyle.Bold) });
        host.Controls.Add(new Label { Text = subtitle, AutoSize = true, UseMnemonic = false, ForeColor = Color.Gray, Margin = new Padding(0, 0, 0, 4) });
        return host;
    }

    private static Control BuildField(string label, Control control, int width)
    {
        var host = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 12)
        };
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.Controls.Add(new Label { Text = label, AutoSize = true, UseMnemonic = false, ForeColor = Color.Gray }, 0, 0);
        control.Width = width;
        control.Anchor = AnchorStyles.Left;
        // Screen readers otherwise announce only the value, with no indication of which field it is.
        if (string.IsNullOrEmpty(control.AccessibleName))
        {
            control.AccessibleName = label;
        }

        host.Controls.Add(control, 0, 1);
        return host;
    }

    private static Control WithMargin(Control control)
    {
        var host = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        host.Controls.Add(control);
        return host;
    }

    private void RegisterMetadataFieldEvents()
    {
        foreach (var control in new Control[]
        {
            _lobbyName, _prospectId, _difficulty, _dropPoint, _insurance, _noRespawns,
            _claimedAccountId, _claimedCharacterSlot, _prospectState, _elapsedTime, _cost, _reward,
            _prospectDtKey, _factionMissionDtKey, _expireTime, _neverExpires
        })
        {
            switch (control)
            {
                case TextBox box:
                    box.TextChanged += (_, _) => MarkMutated("Edited metadata");
                    box.Leave += (_, _) =>
                    {
                        LogMetadataEdit(box, box.Text);
                        TryClearDirtyIfUnchanged();
                    };
                    break;
                case NumericUpDown numeric:
                    numeric.ValueChanged += (_, _) => MarkMutated("Edited metadata");
                    numeric.Leave += (_, _) =>
                    {
                        LogMetadataEdit(numeric, numeric.Value.ToString());
                        TryClearDirtyIfUnchanged();
                    };
                    break;
                case CheckBox check:
                    check.CheckedChanged += (_, _) =>
                    {
                        MarkMutated("Edited metadata");
                        LogMetadataEdit(check, check.Checked ? "true" : "false");
                    };
                    break;
                case DateTimePicker picker:
                    picker.ValueChanged += (_, _) => MarkMutated("Edited metadata");
                    picker.Leave += (_, _) =>
                    {
                        LogMetadataEdit(picker, picker.Value.ToString("yyyy-MM-dd HH:mm:ss"));
                        TryClearDirtyIfUnchanged();
                    };
                    break;
            }
        }

        _neverExpires.CheckedChanged += (_, _) =>
        {
            UpdateExpiryVisibility();
            MarkMutated("Edited expiry");
            LogMetadataEdit(_neverExpires, _neverExpires.Checked ? "true" : "false");
        };

        _pickClaimedAccountButton.Click += (_, _) => PickClaimedAccountFromKnownPlayers();
    }

    private void ReflowMetadata()
    {
        // The breakpoint follows the text scale: at 200 % text a two-column layout is cramped long
        // before the window itself is narrow, so stack earlier in that case.
        var fontScale = Math.Max(1f, _metadataRoot.Font.Size / 9f);
        var threshold = NarrowLayoutThreshold * fontScale;
        var narrow = _metadataRoot.ClientSize.Width > 0 && _metadataRoot.ClientSize.Width < threshold;
        var columns = narrow ? 1 : 2;
        var rows = narrow ? 4 : 2;
        if (_metadataRoot.ColumnCount == columns && _metadataRoot.RowCount == rows && _metadataRoot.Controls.Count == 4)
        {
            return;
        }

        _metadataRoot.SuspendLayout();
        try
        {
            _metadataRoot.Controls.Clear();
            _metadataRoot.ColumnStyles.Clear();
            _metadataRoot.RowStyles.Clear();
            _metadataRoot.ColumnCount = columns;
            _metadataRoot.RowCount = rows;
            for (var c = 0; c < columns; c++)
            {
                _metadataRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            }

            for (var r = 0; r < rows; r++)
            {
                _metadataRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            _metadataRoot.Controls.Add(_identitySection, 0, 0);
            if (narrow)
            {
                _metadataRoot.Controls.Add(_ownershipSection, 0, 1);
                _metadataRoot.Controls.Add(_gameplaySection, 0, 2);
                _metadataRoot.Controls.Add(_timingSection, 0, 3);
            }
            else
            {
                _metadataRoot.Controls.Add(_ownershipSection, 1, 0);
                _metadataRoot.Controls.Add(_gameplaySection, 0, 1);
                _metadataRoot.Controls.Add(_timingSection, 1, 1);
            }
        }
        finally
        {
            _metadataRoot.ResumeLayout();
        }
    }

    private TabPage BuildGridTab<T>(string title, BindingList<T> source) where T : class
    {
        var page = new TabPage(title);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = true,
            DataSource = source,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AccessibleName = $"{title} table"
        };
        StyleGrid(grid);
        grid.CellBeginEdit += (_, e) => CaptureCellBeforeValue(grid, e.RowIndex, e.ColumnIndex);
        grid.CellValueChanged += (_, e) =>
        {
            MarkMutated($"Edited {title}");
            LogGridCellChange(title, grid, e.RowIndex, e.ColumnIndex);
        };
        grid.RowValidated += (_, _) => TryClearDirtyIfUnchanged();
        grid.UserAddedRow += (_, e) =>
        {
            var key = e.Row?.DataBoundItem?.ToString() ?? $"rowIndex={e.Row?.Index}";
            AppLogService.UserAction($"Added {title} row ({key}).");
        };
        grid.UserDeletedRow += (_, _) =>
        {
            if (title == "Mounts")
            {
                _dangerNotes.Add("Mount entries were removed. This can break active mount ownership and state.");
            }
            AppLogService.UserAction($"Removed {title} row.");
            MarkMutated($"Removed {title} row");
        };
        var revertSelected = new Button { Text = "Revert selected rows", AutoSize = true };
        revertSelected.Click += (_, _) => RevertSelectedRows(title, grid);
        var actions = ActionBar(out var actionButtons, title == "Members"
            ? "Edits apply to the prospect when you save."
            : "Custom settings override the dedicated server settings for this prospect.");
        actionButtons.Controls.Add(revertSelected);
        panel.Controls.Add(grid, 0, 0);
        panel.Controls.Add(actions, 0, 1);
        page.Controls.Add(panel);
        if (title == "Members")
        {
            _membersGrid = grid;
        }
        else if (title == "Custom Settings")
        {
            _customSettingsGrid = grid;
        }
        return page;
    }

    private TabPage BuildMountsTab()
    {
        var page = new TabPage("Mounts");
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = true,
            DataSource = _mounts,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AccessibleName = "Mounts table"
        };
        StyleGrid(grid, readOnly: false);
        grid.CellBeginEdit += (_, e) => CaptureCellBeforeValue(grid, e.RowIndex, e.ColumnIndex);
        grid.CellValueChanged += (_, e) =>
        {
            MarkMutated("Edited mount row");
            LogGridCellChange("Mounts", grid, e.RowIndex, e.ColumnIndex);
        };
        grid.RowValidated += (_, _) => TryClearDirtyIfUnchanged();
        _mountsGrid = grid;

        var editSelected = new Button { Text = "Edit selected mount…", AutoSize = true };
        var inspectSelected = new Button { Text = "Inspect raw recorder…", AutoSize = true };
        editSelected.Click += (_, _) => EditSelectedMount(grid);
        inspectSelected.Click += (_, _) => InspectSelectedMount(grid);

        var actions = ActionBar(out var actionButtons, "Edit opens the curated mount editor. Inspect raw shows every stored field.");
        actionButtons.Controls.Add(editSelected);
        actionButtons.Controls.Add(inspectSelected);
        panel.Controls.Add(grid, 0, 0);
        panel.Controls.Add(actions, 0, 1);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildRecorderTab(string title, BindingList<RecorderRow> source)
    {
        var page = new TabPage(title);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = true,
            DataSource = source,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AccessibleName = $"{title} table"
        };
        StyleGrid(grid, readOnly: true);

        var removeSelected = new Button { Text = "Remove selected recorders…", AutoSize = true };
        var inspectSelected = new Button { Text = "Inspect / edit selected…", AutoSize = true };

        inspectSelected.Click += (_, _) => InspectSelectedRecorder(title, grid);
        removeSelected.Click += (_, _) => RemoveSelectedRecorders(title, grid);

        var actions = ActionBar(out var actionButtons, "Recorder removal is permanent for this edit and can break active game state.");
        actionButtons.Controls.Add(inspectSelected);
        actionButtons.Controls.Add(removeSelected);

        panel.Controls.Add(grid, 0, 0);
        panel.Controls.Add(actions, 0, 1);
        page.Controls.Add(panel);
        switch (title)
        {
            case "Characters":
                _charactersGrid = grid;
                break;
            case "Missions":
                _missionsGrid = grid;
                break;
            case "Prebuilts":
                _prebuiltsGrid = grid;
                break;
            case "Diagnostics":
                _allRecordersGrid = grid;
                break;
        }

        return page;
    }

    private static TableLayoutPanel ActionBar(out FlowLayoutPanel buttons, string hint)
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12, 8, 12, 8)
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        host.Controls.Add(buttons, 0, 0);
        host.Controls.Add(new Label
        {
            Text = hint,
            AutoSize = true,
            ForeColor = Color.Gray,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(16, 6, 0, 0)
        }, 1, 0);
        return host;
    }

    private void StyleGrid(DataGridView grid, bool readOnly = false)
    {
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.ColumnHeadersHeight = 32;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = !readOnly;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.EnableHeadersVisualStyles = false;
        // A Transparent alternating-row background is not supported by DataGridView: the row is not
        // erased before painting, which lets the previous row's text bleed through (overlapping rows
        // when scrolling or at higher DPI). The theme supplies a real alternating colour instead.
        grid.AlternatingRowsDefaultCellStyle.BackColor = SystemColors.Window;
        grid.ReadOnly = readOnly;
        EnableGridDoubleBuffering(grid);
        EnableGridSorting(grid);
    }

    private readonly Dictionary<DataGridView, (string Property, ListSortDirection Direction)> _gridSortState = [];

    /// <summary>
    /// BindingList does not implement sorting, so clicking a header did nothing. Columns are sorted
    /// programmatically here, keeping the same row objects (and therefore their session RowId and any
    /// pending edits) and without marking the document dirty.
    /// </summary>
    private void EnableGridSorting(DataGridView grid)
    {
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.Programmatic;
        }

        grid.ColumnAdded += (_, e) => e.Column.SortMode = DataGridViewColumnSortMode.Programmatic;
        grid.ColumnHeaderMouseClick += GridOnColumnHeaderMouseClick;
    }

    private void GridOnColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (sender is not DataGridView grid || e.ColumnIndex < 0 || e.ColumnIndex >= grid.Columns.Count)
        {
            return;
        }

        SortGridByColumn(grid, grid.Columns[e.ColumnIndex]);
    }

    private void SortGridByColumn(DataGridView grid, DataGridViewColumn column)
    {
        if (grid.DataSource is not IBindingList list || string.IsNullOrEmpty(column.DataPropertyName))
        {
            return;
        }

        var direction = ListSortDirection.Ascending;
        if (_gridSortState.TryGetValue(grid, out var state) &&
            state.Property == column.DataPropertyName &&
            state.Direction == ListSortDirection.Ascending)
        {
            direction = ListSortDirection.Descending;
        }

        _gridSortState[grid] = (column.DataPropertyName, direction);

        var items = list.Cast<object>().ToList();
        var property = items.FirstOrDefault()?.GetType().GetProperty(column.DataPropertyName);
        if (property is null)
        {
            return;
        }

        var selected = grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.DataBoundItem)
            .Where(item => item is not null)
            .ToHashSet();

        var comparer = Comparer<object?>.Create(CompareSortValues);
        var ordered = direction == ListSortDirection.Ascending
            ? items.OrderBy(item => property.GetValue(item), comparer).ToList()
            : items.OrderByDescending(item => property.GetValue(item), comparer).ToList();

        // BindingList<T> has the batching API; IBindingList itself does not.
        var listType = list.GetType();
        var raiseProperty = listType.GetProperty("RaiseListChangedEvents");
        var resetMethod = listType.GetMethod("ResetBindings", Type.EmptyTypes);

        raiseProperty?.SetValue(list, false);
        try
        {
            if (list is IList editable)
            {
                editable.Clear();
                foreach (var item in ordered)
                {
                    editable.Add(item);
                }
            }
        }
        finally
        {
            raiseProperty?.SetValue(list, true);
            resetMethod?.Invoke(list, null);
        }

        foreach (DataGridViewColumn gridColumn in grid.Columns)
        {
            gridColumn.HeaderCell.SortGlyphDirection = SortOrder.None;
        }

        column.HeaderCell.SortGlyphDirection = direction == ListSortDirection.Ascending
            ? SortOrder.Ascending
            : SortOrder.Descending;

        grid.ClearSelection();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.DataBoundItem is not null && selected.Contains(row.DataBoundItem))
            {
                row.Selected = true;
            }
        }
    }

    private static int CompareSortValues(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        if (left is string leftText && right is string rightText)
        {
            return string.Compare(leftText, rightText, StringComparison.OrdinalIgnoreCase);
        }

        if (left is IComparable comparable)
        {
            try
            {
                return comparable.CompareTo(right);
            }
            catch (ArgumentException)
            {
                // Mixed value types fall back to a text comparison.
            }
        }

        return string.Compare(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reduces flicker and partial-row repaints while scrolling large recorder lists.
    /// </summary>
    private static void EnableGridDoubleBuffering(DataGridView grid)
    {
        typeof(DataGridView)
            .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(grid, true);
    }

    private void BindTables()
    {
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedTab is { } tab)
            {
                AppLogService.UserAction($"Tab switched: {tab.Text}");
            }

            RefreshStatusLine();
        };
    }

    // ---------------------------------------------------------------- document lifecycle

    private void OpenProspect()
    {
        if (!TryResolveUnsavedChanges("open another prospect"))
        {
            return;
        }

        var selectedPath = SelectProspectPath();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        try
        {
            LoadDocument(selectedPath);
            AppLogService.UserAction($"Opened prospect: {selectedPath}");
            AppLogService.Info($"Prospect loaded: {selectedPath}");
        }
        catch (Exception ex)
        {
            AppLogService.Error($"Failed to load prospect: {selectedPath}", ex);
            MessageBox.Show(this, $"Failed to load prospect.\n{ex.Message}", "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadDocument(string path)
    {
        _document = ProspectLoadService.Load(path);
        _revision = 0;
        _savedRevision = 0;
        _projectionCache.Invalidate();
        _mountTalentsOverride.Clear();
        _dangerNotes.Clear();
        _inspectorEdits.Clear();
        _advancedRecorderFieldEdits.Clear();
        RefreshTabs(rebindDocumentState: true);
        CaptureBaselines();
        ApplyUnsavedIndicators();
        _status.Text = "Prospect loaded.";
    }

    private string? SelectProspectPath()
    {
        if (_document is null)
        {
            using var wizard = new FreshProspectLoadWizardForm();
            wizard.ApplyTheme(_isDarkTheme);
            return wizard.ShowDialog(this) == DialogResult.OK ? wizard.SelectedPath : null;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "Icarus Prospect (*.json)|*.json|All files (*.*)|*.*",
            Title = "Open prospect JSON"
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private bool SaveDocument()
    {
        if (_document is null)
        {
            MessageBox.Show(this, "Open a prospect first.", "Save", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        if (!ValidateBeforeSave(out var validationError))
        {
            MessageBox.Show(this, validationError, "Validation failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var candidate = TryBuildSaveCandidate(out var applyError, out var mountConflicts, out _);
        if (candidate is null)
        {
            MessageBox.Show(this, applyError, "Save blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _status.Text = "Save blocked — a field value could not be applied.";
            return false;
        }

        AddMountConflictNotes(mountConflicts);
        if (!ShowDangerWarningsIfNeeded())
        {
            return false;
        }

        var summary = BuildChangeSummary();
        var result = MessageBox.Show(
            this,
            $"{summary}\n\nA backup of the existing file is always created before it is replaced.",
            "Confirm save",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);
        if (result != DialogResult.OK)
        {
            AppLogService.UserAction("Save cancelled at confirmation dialog.");
            return false;
        }

        var saveResult = ProspectSaveService.SaveDocument(candidate);
        if (!saveResult.Succeeded)
        {
            AppLogService.Error(
                $"Save failed: {candidate.ProspectPath}",
                new InvalidOperationException(saveResult.Error ?? "Unknown save failure."));
            MessageBox.Show(
                this,
                $"Save failed. The original file and your edits are unchanged.\n\n{saveResult.Error}",
                "Save failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            _status.Text = "Save failed — original file unchanged.";
            return false;
        }

        // The file is safely published, so the drafts become the editable document.
        _document.Prospect = candidate.Prospect;
        _projectionCache.Invalidate();
        _savedRevision = _revision;
        _advancedRecorderFieldEdits.Clear();
        _dangerNotes.Clear();
        _inspectorEdits.Clear();
        RefreshMountRowsAfterConflict(mountConflicts);
        CaptureBaselines();
        ApplyUnsavedIndicators();
        var savedText = saveResult.BackupPath is null
            ? "Saved."
            : $"Saved. Backup: {Path.GetFileName(saveResult.BackupPath)}";
        _status.Text = mountConflicts.Count == 0
            ? savedText
            : $"{savedText} {mountConflicts.Count} mount conflict(s): raw Advanced values kept.";
        AppLogService.UserAction($"Saved prospect: {_document.ProspectPath}");
        AppLogService.Info($"Prospect saved: {_document.ProspectPath}");
        return true;
    }

    private bool SaveAs()
    {
        if (_document is null)
        {
            return false;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "Icarus Prospect (*.json)|*.json",
            FileName = Path.GetFileName(_document.ProspectPath),
            Title = "Save prospect as"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return false;
        }

        if (!ValidateBeforeSave(out var validationError))
        {
            MessageBox.Show(this, validationError, "Validation failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var candidate = TryBuildSaveCandidate(out var applyError, out var mountConflicts, out _);
        if (candidate is null)
        {
            MessageBox.Show(this, applyError, "Save As blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _status.Text = "Save As blocked — a field value could not be applied.";
            return false;
        }

        AddMountConflictNotes(mountConflicts);
        if (!ShowDangerWarningsIfNeeded())
        {
            return false;
        }

        var target = dialog.FileName;
        var saveResult = ProspectSaveService.SaveProspect(candidate.Prospect, target);
        if (!saveResult.Succeeded)
        {
            MessageBox.Show(
                this,
                $"Save As failed. The document is still '{_document.ProspectPath}'.\n\n{saveResult.Error}",
                "Save As failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }

        // Path, document, baseline and clean state are committed only after the new file was published.
        _document.Prospect = candidate.Prospect;
        _document.ProspectPath = target;
        _projectionCache.Invalidate();
        _savedRevision = _revision;
        _advancedRecorderFieldEdits.Clear();
        _dangerNotes.Clear();
        _inspectorEdits.Clear();
        UpdateDocumentHeader();
        RefreshMountRowsAfterConflict(mountConflicts);
        CaptureBaselines();
        ApplyUnsavedIndicators();
        _status.Text = mountConflicts.Count == 0
            ? $"Saved as {Path.GetFileName(target)}."
            : $"Saved as {Path.GetFileName(target)}. {mountConflicts.Count} mount conflict(s): raw Advanced values kept.";
        AppLogService.UserAction($"Save As: now editing {target}");
        return true;
    }

    private async Task ExportDecodedAsync()
    {
        if (_document is null)
        {
            MessageBox.Show(this, "Open a prospect first.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "JSON (*.json)|*.json",
            FileName = $"{Path.GetFileNameWithoutExtension(_document.ProspectPath)}.decoded.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            // Validate before asking for a mode so the source save can never be targeted.
            DecodedExportService.EnsureDestinationIsNotSource(_document.ProspectPath, dialog.FileName);

            var modeChoice = MessageBox.Show(
                this,
                "Choose export mode:\n\nYes = Raw decoded export (fastest)\nNo = Enriched export with metadata + mounts (slower)\nCancel = abort",
                "Decoded export mode",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            if (modeChoice == DialogResult.Cancel)
            {
                return;
            }

            var mode = modeChoice == DialogResult.Yes ? DecodedExportMode.Raw : DecodedExportMode.Enriched;

            // Commit pending form drafts into an independent snapshot on the UI thread. The live
            // document is never touched by export, and the worker cannot observe later edits (E04).
            var exportSnapshot = ProspectSaveService.CreateSnapshot(_document);
            if (!ApplyCurrentFormStateTo(exportSnapshot, out var applyError, out var mountConflicts, out _))
            {
                MessageBox.Show(this, applyError, "Export blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _status.Text = "Decoded export blocked — a field value could not be applied.";
                return;
            }

            AddMountConflictNotes(mountConflicts);

            using var progressForm = new ExportProgressForm();
            progressForm.ApplyTheme(_isDarkTheme);
            progressForm.UpdateProgress("Preparing export...", 0);
            progressForm.Show(this);

            var progress = new Progress<DecodedExportProgress>(p =>
            {
                progressForm.UpdateProgress(p.Stage, p.Percent);
                _status.Text = p.Stage;
            });

            AppLogService.UserAction($"Decoded export started ({mode}) -> {dialog.FileName}");
            // The snapshot already exists, so pass it (and the source path it came from) directly:
            // the 4-argument overload would take another full round-trip snapshot inside the worker.
            var exportSourcePath = exportSnapshot.ProspectPath;
            var exportTargetPath = dialog.FileName;
            await Task.Run(() => DecodedExportService.Export(exportSnapshot, exportSourcePath, exportTargetPath, mode, progress));
            progressForm.Close();

            MessageBox.Show(this, $"Decoded export written:\n{dialog.FileName}", "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _status.Text = mountConflicts.Count == 0
                ? "Decoded export complete."
                : $"Decoded export complete. {mountConflicts.Count} mount conflict(s): raw Advanced values kept.";
            AppLogService.Info($"Decoded export completed ({mode}): {dialog.FileName}");
        }
        catch (OperationCanceledException)
        {
            AppLogService.UserAction("Decoded export cancelled.");
            MessageBox.Show(
                this,
                "The decoded export was interrupted before it finished. Any existing destination file was left unchanged.",
                "Export cancelled",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            _status.Text = "Decoded export cancelled.";
        }
        catch (Exception ex)
        {
            AppLogService.Error("Decoded export failed.", ex);
            MessageBox.Show(this, $"Failed to export decoded JSON.\n{ex.Message}", "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "Decoded export failed.";
        }
    }

    private void TryResolveUnsavedChangesOnClose(FormClosingEventArgs e)
    {
        if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing or CloseReason.MdiFormClosing)
        {
            return;
        }

        if (IsDirty && !TryResolveUnsavedChanges("close the editor"))
        {
            e.Cancel = true;
        }
    }

    private void MainFormOnFormClosing(object? sender, FormClosingEventArgs e) => TryResolveUnsavedChangesOnClose(e);

    private void MainFormOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.S)
        {
            e.SuppressKeyPress = true;
            if (e.Shift)
            {
                SaveAs();
            }
            else
            {
                SaveDocument();
            }
        }
        else if (e.Control && e.KeyCode == Keys.O)
        {
            e.SuppressKeyPress = true;
            OpenProspect();
        }
        else if (e.KeyCode == Keys.F5)
        {
            e.SuppressKeyPress = true;
            RefreshTabs();
        }
    }

    // ---------------------------------------------------------------- refresh / projections

    /// <summary>
    /// Re-projects the document. <paramref name="rebindDocumentState"/> is only true after load,
    /// revert, or save: ordinary refresh must not reload controls from the model, or pending edits
    /// would be replaced by older values (E01).
    /// </summary>
    private void RefreshTabs(bool rebindDocumentState = false)
    {
        if (_document is null)
        {
            return;
        }

        if (rebindDocumentState)
        {
            using var suspension = SuspendDirtyTracking();
            var info = _document.Prospect.ProspectInfo;
            _lobbyName.Text = info.LobbyName ?? string.Empty;
            _prospectId.Text = info.ProspectID ?? string.Empty;
            _difficulty.Text = info.Difficulty ?? string.Empty;
            _dropPoint.Value = Math.Clamp(info.SelectedDropPoint, 0, (int)_dropPoint.Maximum);
            _insurance.Checked = info.Insurance;
            _noRespawns.Checked = info.NoRespawns;
            _claimedAccountId.Text = info.ClaimedAccountID ?? string.Empty;
            _claimedCharacterSlot.Value = Math.Clamp(info.ClaimedAccountCharacter, (int)_claimedCharacterSlot.Minimum, (int)_claimedCharacterSlot.Maximum);
            _prospectState.Text = info.ProspectState ?? string.Empty;
            _elapsedTime.Value = Math.Clamp(info.ElapsedTime, 0, int.MaxValue);
            _cost.Value = Math.Clamp(info.Cost, 0, int.MaxValue);
            _reward.Value = Math.Clamp(info.Reward, 0, int.MaxValue);
            _prospectDtKey.Text = info.ProspectDTKey ?? string.Empty;
            _factionMissionDtKey.Text = info.FactionMissionDTKey ?? string.Empty;
            ProspectExpireTimeUi.ApplyPersistedToControl(info.ExpireTime, _neverExpires, _expireTime);
            UpdateExpiryVisibility();
            UpdateDocumentHeader();

            ResetRows(_members, ProspectModelMapper.ReadMembers(_document.Prospect));
            ResetRows(_customSettings, ProspectModelMapper.ReadCustomSettings(_document.Prospect));
            ResetRows(_mounts, _projectionCache.GetMounts(_document.Prospect, _revision).Select(CloneMountRow));
        }

        // Recorder tabs are read-only projections and are always rebuilt from the current revision.
        var allRecorders = _projectionCache.GetRecorderRows(_document.Prospect, _revision);
        ResetRows(_characters, allRecorders.Where(r => r.Category == RecorderCategory.Character));
        ResetRows(_missions, allRecorders.Where(r => r.Category == RecorderCategory.Mission));
        ResetRows(_prebuilts, allRecorders.Where(r => r.Category == RecorderCategory.Prebuilt));
        ResetRows(_allRecorders, allRecorders);

        ApplyUnsavedIndicators();
    }

    private static MountRow CloneMountRow(MountRow row) => row.Clone();

    /// <summary>
    /// Builds a save candidate from an independent snapshot, so a failed save leaves the editable
    /// document exactly as it was. The live document only adopts the candidate after the file has
    /// been published, which is what makes the failure message literally true.
    ///
    /// A curated mount row is replayed only when its committed values differ from the baseline
    /// snapshot captured on load/save. An unchanged row must never overwrite a recorder the user
    /// edited through the raw Advanced inspector; a changed curated field that collides with an
    /// Advanced edit keeps the raw value and is reported instead of silently overwritten (E02).
    /// </summary>
    private ProspectDocument? TryBuildSaveCandidate(
        out string error,
        out IReadOnlyList<string> mountConflicts,
        out bool recorderChanged)
    {
        error = string.Empty;
        mountConflicts = Array.Empty<string>();
        recorderChanged = false;
        if (_document is null)
        {
            error = "No prospect loaded.";
            return null;
        }

        ProspectDocument candidate;
        try
        {
            candidate = ProspectSaveService.CreateSnapshot(_document);
        }
        catch (Exception ex)
        {
            error = $"The document could not be prepared for saving.\n{ex.Message}";
            return null;
        }

        return ApplyCurrentFormStateTo(candidate, out error, out mountConflicts, out recorderChanged)
            ? candidate
            : null;
    }

    /// <summary>
    /// Applies the current drafts to the live document (save) or to an independent snapshot
    /// (export). Export must not mutate the editable document on the UI thread.
    /// </summary>
    private bool ApplyCurrentFormStateTo(
        ProspectDocument document,
        out string error,
        out IReadOnlyList<string> mountConflicts,
        out bool recorderChanged)
    {
        error = string.Empty;
        mountConflicts = Array.Empty<string>();
        recorderChanged = false;

        var prospect = document.Prospect;
        var info = prospect.ProspectInfo;
        info.LobbyName = _lobbyName.Text.Trim();
        info.ProspectID = _prospectId.Text.Trim();
        info.Difficulty = _difficulty.Text.Trim();
        info.SelectedDropPoint = (int)_dropPoint.Value;
        info.NoRespawns = _noRespawns.Checked;
        info.Insurance = _insurance.Checked;
        info.ClaimedAccountID = _claimedAccountId.Text.Trim();
        info.ClaimedAccountCharacter = (int)_claimedCharacterSlot.Value;
        info.ProspectState = _prospectState.Text.Trim();
        info.ElapsedTime = (int)_elapsedTime.Value;
        info.Cost = (int)_cost.Value;
        info.Reward = (int)_reward.Value;
        info.ProspectDTKey = _prospectDtKey.Text.Trim();
        info.FactionMissionDTKey = _factionMissionDtKey.Text.Trim();
        info.ExpireTime = ProspectExpireTimeUi.ToPersistedSeconds(_neverExpires.Checked, _expireTime.Value);
        prospect.ProspectInfo = info;

        ProspectModelMapper.ApplyMembers(prospect, _members.Where(m => !string.IsNullOrWhiteSpace(m.UserID)));
        ProspectModelMapper.ApplyCustomSettings(prospect, _customSettings.Where(s => !string.IsNullOrWhiteSpace(s.SettingRowName)));

        var conflicts = new List<string>();
        var failures = new List<string>();
        foreach (var mount in _mounts)
        {
            _mountTalentsOverride.TryGetValue(mount.RecorderIndex, out var talents);
            if (!TryApplyMountRowToDocument(prospect, mount, talents, onlyChangedSinceBaseline: true, conflicts, out var mountError, out var mountChanged))
            {
                failures.Add(mountError);
            }

            recorderChanged |= mountChanged;
        }

        mountConflicts = conflicts;
        if (failures.Count > 0)
        {
            error = "These mount values could not be applied:\n\n" + string.Join("\n", failures);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Applies one curated mount row. <paramref name="onlyChangedSinceBaseline"/> is the save/export
    /// rule (skip unchanged rows); revert-selected passes false because it intentionally restores
    /// baseline values into a recorder that a mount-editor apply already changed.
    /// </summary>
    private bool TryApplyMountRowToDocument(
        ProspectSave prospect,
        MountRow mount,
        IReadOnlyList<TalentRow>? talents,
        bool onlyChangedSinceBaseline,
        ICollection<string> conflicts,
        out string error,
        out bool recorderChanged)
    {
        error = string.Empty;
        recorderChanged = false;

        MountRow? baselineRow = null;
        if (_mountsBaseline.TryGetValue(mount.RowId, out var baselineJson))
        {
            baselineRow = JsonConvert.DeserializeObject<MountRow>(baselineJson);
        }

        var rowUnchanged = baselineRow is not null && MountRowsHaveSameCommittedValues(mount, baselineRow);
        if (onlyChangedSinceBaseline && rowUnchanged)
        {
            // A talent override was already merged and applied by the mount editor; re-applying it is
            // the only remaining work for an otherwise unchanged curated row.
            if (talents is null)
            {
                return true;
            }

            var talentResult = ProspectModelMapper.ApplyTalentRows(prospect, mount.RecorderIndex, talents);
            if (!talentResult.Succeeded)
            {
                error = $"Mount '{mount.MountName}' (recorder {mount.RecorderIndex}): {talentResult.Describe()}";
                return false;
            }

            recorderChanged = talentResult.Changed;
            return true;
        }

        var fields = ProspectModelMapper.ReadRecorderFields(prospect, mount.RecorderIndex);
        if (fields.Count == 0)
        {
            error = $"Mount '{mount.MountName}' points at recorder {mount.RecorderIndex}, which no longer exists.";
            return false;
        }

        var liveValues = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            liveValues[field.Path] = field.Value;
        }

        var currentValues = ProspectModelMapper.BuildMountFieldValues(mount, fields);
        IEnumerable<string> pathsToApply = currentValues.Keys;
        if (onlyChangedSinceBaseline && baselineRow is not null)
        {
            var baselineValues = ProspectModelMapper.BuildMountFieldValues(baselineRow, fields);
            pathsToApply = currentValues
                .Where(kv => !baselineValues.TryGetValue(kv.Key, out var baselineValue) ||
                             !string.Equals(kv.Value, baselineValue, StringComparison.Ordinal))
                .Select(kv => kv.Key)
                .ToList();
        }

        _advancedRecorderFieldEdits.TryGetValue(mount.RecorderIndex, out var advancedPaths);
        var applyPaths = new List<string>();
        var conflictPaths = new List<string>();
        foreach (var path in pathsToApply.Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal))
        {
            if (advancedPaths?.Contains(path) == true)
            {
                liveValues.TryGetValue(path, out var liveValue);
                if (!string.Equals(liveValue, currentValues[path], StringComparison.Ordinal))
                {
                    conflictPaths.Add(path);
                    continue;
                }
            }

            applyPaths.Add(path);
        }

        if (applyPaths.Count > 0 || talents is not null)
        {
            var editRows = applyPaths.Select(path => new RecorderFieldRow
            {
                Path = path,
                PropertyType = string.Empty,
                Value = currentValues[path],
                Editable = true
            });
            var result = ProspectModelMapper.ApplyMountEdits(prospect, mount.RecorderIndex, editRows, talents);
            if (!result.Succeeded)
            {
                error = $"Mount '{mount.MountName}' (recorder {mount.RecorderIndex}): {result.Describe()}";
                return false;
            }

            recorderChanged = result.Changed;
        }

        if (conflictPaths.Count > 0)
        {
            conflicts.Add(
                $"Mount '{mount.MountName}' (recorder {mount.RecorderIndex}): kept the raw Advanced value for " +
                $"{string.Join(", ", conflictPaths)}; the curated Overview edit for those field(s) was not saved.");
        }

        return true;
    }

    private static bool MountRowsHaveSameCommittedValues(MountRow current, MountRow baseline) =>
        string.Equals(MountCommittedValueSnapshot(current), MountCommittedValueSnapshot(baseline), StringComparison.Ordinal);

    /// <summary>
    /// Session identity, recorder pointer and recorder class are projection bookkeeping, not edited
    /// save values. They are excluded so a recorder-index remap after removal cannot look like a
    /// curated edit (E01).
    /// </summary>
    private static string MountCommittedValueSnapshot(MountRow row)
    {
        var copy = row.Clone();
        copy.RowId = Guid.Empty;
        copy.RecorderIndex = 0;
        copy.RecorderPath = string.Empty;
        return JsonConvert.SerializeObject(copy);
    }

    private void AddMountConflictNotes(IReadOnlyList<string> mountConflicts)
    {
        foreach (var conflict in mountConflicts)
        {
            _dangerNotes.Add(conflict);
            AppLogService.UserAction($"Mount conflict: {conflict}");
        }
    }

    /// <summary>
    /// When a raw Advanced value was kept, the curated row still holds the value that was
    /// deliberately not applied. Re-project the mount rows from the just-saved document so the grid
    /// reflects the file instead of a change that was reported but not saved.
    /// </summary>
    private void RefreshMountRowsAfterConflict(IReadOnlyList<string> mountConflicts)
    {
        if (_document is null || mountConflicts.Count == 0)
        {
            return;
        }

        using (SuspendDirtyTracking())
        {
            _projectionCache.Invalidate();
            ResetRows(_mounts, _projectionCache.GetMounts(_document.Prospect, _revision).Select(CloneMountRow));
        }
    }

    private void UpdateDocumentHeader()
    {
        if (_document is null)
        {
            _documentTitle.Text = "No prospect open";
            _documentPath.Text = "Open a prospect to begin.";
            _fullDocumentPath = string.Empty;
            _toolTip.SetToolTip(_documentPath, _documentPath.Text);
            return;
        }

        var info = _document.Prospect.ProspectInfo;
        // Prefer the human name, then the prospect id, and only fall back to the file name.
        _documentTitle.Text = !string.IsNullOrWhiteSpace(info.LobbyName)
            ? info.LobbyName.Trim()
            : !string.IsNullOrWhiteSpace(info.ProspectID)
                ? info.ProspectID.Trim()
                : Path.GetFileNameWithoutExtension(_document.ProspectPath);
        _fullDocumentPath = _document.ProspectPath;
        ApplyDocumentPathEllipsis();
        _toolTip.SetToolTip(_documentPath, _fullDocumentPath);
    }

    /// <summary>
    /// Shows the path on one line with a middle ellipsis, keeping the drive and file name visible.
    /// The full path stays available through the tooltip and the copy-path action.
    /// </summary>
    private void ApplyDocumentPathEllipsis()
    {
        if (string.IsNullOrEmpty(_fullDocumentPath))
        {
            return;
        }

        var available = _documentPath.ClientSize.Width - 4;
        _documentPath.Text = available <= 0
            ? _fullDocumentPath
            : MiddleEllipsis(_fullDocumentPath, _documentPath.Font, available);
    }

    private static string MiddleEllipsis(string text, Font font, int maxWidth)
    {
        if (maxWidth <= 0 || TextRenderer.MeasureText(text, font).Width <= maxWidth)
        {
            return text;
        }

        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (TextRenderer.MeasureText(BuildCandidate(middle), font).Width <= maxWidth)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return BuildCandidate(low);

        string BuildCandidate(int keep)
        {
            if (keep <= 0)
            {
                return "…";
            }

            var head = (keep + 1) / 2;
            var tail = keep - head;
            return tail == 0
                ? string.Concat(text.AsSpan(0, head), "…")
                : string.Concat(text.AsSpan(0, head), "…", text.AsSpan(text.Length - tail));
        }
    }

    // ---------------------------------------------------------------- validation and edit state

    private bool ValidateBeforeSave(out string error)
    {
        error = string.Empty;
        if (_document is null)
        {
            error = "No prospect loaded.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_lobbyName.Text))
        {
            error = "Lobby name is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(_difficulty.Text))
        {
            error = "Difficulty is required.";
            return false;
        }
        if (_members.Any(m => string.IsNullOrWhiteSpace(m.UserID)))
        {
            error = "Each member row must have a UserID.";
            return false;
        }
        if (_mounts.Any(m => string.IsNullOrWhiteSpace(m.MountName)))
        {
            error = "Each mount row must have a mount name.";
            return false;
        }
        foreach (var mount in _mounts)
        {
            var issues = MountSpeciesMetadataService.ValidateMount(mount);
            if (issues.Count > 0)
            {
                error = $"Mount '{mount.MountName}' failed validation: {string.Join(" | ", issues)}";
                return false;
            }
        }
        return true;
    }

    private bool ShowDangerWarningsIfNeeded()
    {
        var message = DangerWarningService.BuildWarningMessage(_inspectorEdits, _dangerNotes);
        if (string.IsNullOrWhiteSpace(message))
        {
            return true;
        }

        return MessageBox.Show(this, message, "Danger warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private string BuildChangeSummary() =>
        "About to save:\n" +
        $"- Lobby: {_lobbyName.Text}\n" +
        $"- Difficulty: {_difficulty.Text}\n" +
        $"- Prospect state: {_prospectState.Text}\n" +
        $"- Members: {_members.Count}\n" +
        $"- Custom settings: {_customSettings.Count}\n" +
        $"- Character recorders: {_characters.Count}\n" +
        $"- Mission recorders: {_missions.Count}\n" +
        $"- Prebuilt recorders: {_prebuilts.Count}\n" +
        $"- Mount entries: {_mounts.Count}";

    private bool TryResolveUnsavedChanges(string action)
    {
        if (!IsDirty)
        {
            return true;
        }

        var answer = MessageBox.Show(
            this,
            $"Save unsaved changes before you {action}?\n\nYes = save first\nNo = discard changes\nCancel = stay here",
            "Unsaved changes",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Warning);
        return answer switch
        {
            DialogResult.Yes => SaveDocument(),
            DialogResult.No => true,
            _ => false
        };
    }

    private void MarkMutated(string what)
    {
        if (_suppressDirtyDepth > 0 || _document is null)
        {
            return;
        }

        _revision++;
        _projectionCache.Invalidate();
        _status.Text = $"{what} — unsaved changes.";
        ApplyUnsavedIndicators();
    }

    private IDisposable SuspendDirtyTracking() => new DirtyTrackingSuspension(this);

    private sealed class DirtyTrackingSuspension : IDisposable
    {
        private readonly MainForm _form;
        private bool _disposed;

        public DirtyTrackingSuspension(MainForm form)
        {
            _form = form;
            _form._suppressDirtyDepth++;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _form._suppressDirtyDepth = Math.Max(0, _form._suppressDirtyDepth - 1);
        }
    }

    /// <summary>
    /// After a revert or a value retyped to its original text, recompute whether the document still
    /// differs from the last saved snapshot. This is what lets a revert actually clear dirty state.
    /// </summary>
    private void TryClearDirtyIfUnchanged()
    {
        if (_document is null || !IsDirty || _savedSnapshotBytes is null)
        {
            return;
        }

        if (HasPendingFormEdits())
        {
            // Drafts are committed only on save/export. Applying them here would make an undo of a
            // grid edit unable to restore the recorder, and would hide the pending change.
            return;
        }

        var current = ProspectSaveService.Serialize(_document.Prospect);
        if (current.AsSpan().SequenceEqual(_savedSnapshotBytes))
        {
            _savedRevision = _revision;
            _advancedRecorderFieldEdits.Clear();
            ApplyUnsavedIndicators();
        }
    }

    private bool HasPendingFormEdits() =>
        MetadataSnapshot.Capture(this) != _metadataBaseline ||
        RowsDifferFromBaseline(_members, _membersBaseline, row => row.RowId) ||
        RowsDifferFromBaseline(_customSettings, _customSettingsBaseline, row => row.RowId) ||
        RowsDifferFromBaseline(_mounts, _mountsBaseline, row => row.RowId);

    private static bool RowsDifferFromBaseline<T>(
        IEnumerable<T> rows,
        IDictionary<Guid, string> baseline,
        Func<T, Guid> rowIdSelector) where T : class
    {
        var currentIds = new HashSet<Guid>();
        foreach (var row in rows)
        {
            var rowId = rowIdSelector(row);
            currentIds.Add(rowId);
            var snapshot = JsonConvert.SerializeObject(row);
            if (!baseline.TryGetValue(rowId, out var baselineSnapshot) ||
                !string.Equals(snapshot, baselineSnapshot, StringComparison.Ordinal))
            {
                return true;
            }
        }

        // A deleted row leaves no current row to compare, so its baseline identity proves the draft.
        return baseline.Keys.Any(rowId => !currentIds.Contains(rowId));
    }

    private void PickClaimedAccountFromKnownPlayers()
    {
        AppLogService.UserAction("Player picker opened.");
        var ids = _members
            .Select(m => m.UserID)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0)
        {
            MessageBox.Show(this, "No player IDs are available in this prospect yet.", "Player picker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var picker = new Form
        {
            Text = "Select claimed account ID",
            Width = 520,
            Height = 420,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false
        };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(16) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var list = new ListBox { Dock = DockStyle.Fill };
        list.Items.AddRange(ids.Cast<object>().ToArray());
        if (!string.IsNullOrWhiteSpace(_claimedAccountId.Text))
        {
            var index = ids.FindIndex(id => string.Equals(id, _claimedAccountId.Text, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                list.SelectedIndex = index;
            }
        }

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var apply = new Button { Text = "Use selected", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(apply);
        buttons.Controls.Add(cancel);
        root.Controls.Add(list, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        picker.Controls.Add(root);
        picker.AcceptButton = apply;
        picker.CancelButton = cancel;
        UiThemeService.ApplyTheme(picker, _isDarkTheme);

        if (picker.ShowDialog(this) != DialogResult.OK || list.SelectedItem is not string selected)
        {
            AppLogService.UserAction("Player picker cancelled.");
            return;
        }

        _claimedAccountId.Text = selected;
        AppLogService.UserAction($"Player picker: set claimed account ID to {selected}.");
    }

    private void CopyDocumentPath()
    {
        if (_document is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(_document.ProspectPath);
            _status.Text = "Path copied to clipboard.";
        }
        catch (Exception ex)
        {
            _status.Text = "Could not copy the path.";
            AppLogService.Error("Copy path failed.", ex);
        }
    }

    private void OpenDocumentFolder()
    {
        if (_document is null)
        {
            return;
        }

        try
        {
            var folder = Path.GetDirectoryName(_document.ProspectPath);
            if (!string.IsNullOrEmpty(folder))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            AppLogService.Error("Open folder failed.", ex);
        }
    }

    private void ShowAbout()
    {
        var version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        MessageBox.Show(
            this,
            $"Icarus Prospect Editor\nVersion {version}\n\nEdits encoded Icarus prospect saves. A backup is created before an existing save is replaced.",
            "About",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    // ---------------------------------------------------------------- theme

    private void InitializeTheme()
    {
        var theme = ThemePreferenceService.LoadTheme();
        _isDarkTheme = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);
        UiThemeService.ApplyTheme(this, _isDarkTheme);
        ApplyThemeMenuChecks();
        ApplyAccentButton();
        ApplyUnsavedIndicators();
    }

    private void SetTheme(string theme)
    {
        AppLogService.UserAction($"Theme changed to {theme}.");
        _isDarkTheme = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);
        ThemePreferenceService.SaveTheme(theme);
        UiThemeService.ApplyTheme(this, _isDarkTheme);
        ApplyThemeMenuChecks();
        ApplyAccentButton();
        ApplyUnsavedIndicators();
    }

    private ToolStripMenuItem? _darkThemeMenuItem;
    private ToolStripMenuItem? _lightThemeMenuItem;

    private void ApplyThemeMenuChecks()
    {
        if (_darkThemeMenuItem is not null)
        {
            _darkThemeMenuItem.Checked = _isDarkTheme;
        }

        if (_lightThemeMenuItem is not null)
        {
            _lightThemeMenuItem.Checked = !_isDarkTheme;
        }
    }

    private void ApplyAccentButton()
    {
        _saveButton.BackColor = _isDarkTheme ? Color.FromArgb(46, 111, 178) : Color.FromArgb(32, 100, 180);
        _saveButton.ForeColor = Color.White;
        _saveButton.FlatStyle = FlatStyle.Flat;
        _saveButton.FlatAppearance.BorderSize = 0;

        // The Advanced toggle is a real button so keyboard and screen readers can activate it; style
        // it as a link so it still reads as a disclosure control.
        _advancedToggle.BackColor = _isDarkTheme ? Color.FromArgb(24, 24, 28) : Color.FromArgb(246, 248, 251);
        _advancedToggle.ForeColor = _isDarkTheme ? Color.FromArgb(130, 180, 240) : Color.FromArgb(32, 100, 180);
        _advancedToggle.FlatAppearance.BorderSize = 0;
        _advancedToggle.FlatAppearance.MouseOverBackColor = _advancedToggle.BackColor;
        _advancedToggle.FlatAppearance.MouseDownBackColor = _advancedToggle.BackColor;
    }

    // ---------------------------------------------------------------- baselines and highlighting

    private void CaptureBaselines()
    {
        if (_document is null)
        {
            return;
        }

        _metadataBaseline = MetadataSnapshot.Capture(this);
        _membersBaseline.Clear();
        foreach (var row in _members)
        {
            _membersBaseline[row.RowId] = JsonConvert.SerializeObject(row);
        }

        _customSettingsBaseline.Clear();
        foreach (var row in _customSettings)
        {
            _customSettingsBaseline[row.RowId] = JsonConvert.SerializeObject(row);
        }

        _mountsBaseline.Clear();
        foreach (var row in _mounts)
        {
            _mountsBaseline[row.RowId] = JsonConvert.SerializeObject(row);
        }

        _savedSnapshotBytes = ProspectSaveService.Serialize(_document.Prospect);
    }

    private void ApplyUnsavedIndicators()
    {
        HighlightMetadataField(_lobbyName, _lobbyName.Text.Trim() != _metadataBaseline.LobbyName);
        HighlightMetadataField(_prospectId, _prospectId.Text.Trim() != _metadataBaseline.ProspectId);
        HighlightMetadataField(_difficulty, _difficulty.Text.Trim() != _metadataBaseline.Difficulty);
        HighlightMetadataField(_claimedAccountId, _claimedAccountId.Text.Trim() != _metadataBaseline.ClaimedAccountId);
        HighlightMetadataField(_prospectState, _prospectState.Text.Trim() != _metadataBaseline.ProspectState);
        HighlightMetadataField(_prospectDtKey, _prospectDtKey.Text.Trim() != _metadataBaseline.ProspectDtKey);
        HighlightMetadataField(_factionMissionDtKey, _factionMissionDtKey.Text.Trim() != _metadataBaseline.FactionMissionDtKey);
        HighlightMetadataField(_dropPoint, (int)_dropPoint.Value != _metadataBaseline.DropPoint);
        HighlightMetadataField(_claimedCharacterSlot, (int)_claimedCharacterSlot.Value != _metadataBaseline.ClaimedCharacterSlot);
        HighlightMetadataField(_elapsedTime, (int)_elapsedTime.Value != _metadataBaseline.ElapsedTime);
        HighlightMetadataField(_cost, (int)_cost.Value != _metadataBaseline.Cost);
        HighlightMetadataField(_reward, (int)_reward.Value != _metadataBaseline.Reward);
        HighlightMetadataField(_insurance, _insurance.Checked != _metadataBaseline.Insurance);
        HighlightMetadataField(_noRespawns, _noRespawns.Checked != _metadataBaseline.NoRespawns);
        var expirePersistedNow = ProspectExpireTimeUi.ToPersistedSeconds(_neverExpires.Checked, _expireTime.Value);
        var expireDirty = expirePersistedNow != _metadataBaseline.ExpirePersisted;
        HighlightMetadataField(_neverExpires, expireDirty);
        HighlightMetadataField(_expireTime, expireDirty);

        HighlightGridRows(_membersGrid, _members.Cast<MemberRow>().ToList(), _membersBaseline, r => r.RowId);
        HighlightGridRows(_customSettingsGrid, _customSettings.Cast<CustomSettingRow>().ToList(), _customSettingsBaseline, r => r.RowId);
        HighlightGridRows(_mountsGrid, _mounts.Cast<MountRow>().ToList(), _mountsBaseline, r => r.RowId);

        _unsavedLabel.Text = IsDirty ? "● Unsaved changes" : _document is null ? string.Empty : "Saved";
        _unsavedLabel.ForeColor = IsDirty
            ? (_isDarkTheme ? Color.FromArgb(255, 205, 120) : Color.FromArgb(150, 95, 0))
            : (_isDarkTheme ? Color.FromArgb(150, 200, 160) : Color.FromArgb(40, 110, 60));
        RefreshStatusLine();
    }

    private void RefreshStatusLine()
    {
        if (_document is null)
        {
            _status.Text = "Ready — open a prospect.";
            return;
        }

        _status.Text = IsDirty ? "Unsaved changes." : "Loaded.";
    }

    private void UpdateExpiryVisibility()
    {
        var never = _neverExpires.Checked;
        _expireTime.Visible = !never;
        _expiryPlaceholder.Visible = never;
    }

    private void RevertAllUnsavedChanges()
    {
        if (_document is null)
        {
            return;
        }

        if (!IsDirty)
        {
            MessageBox.Show(this, "No unsaved changes to revert.", "Revert all", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            "Revert all unsaved changes and reload the document from disk?",
            "Revert all",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            AppLogService.UserAction("Revert all cancelled at confirmation dialog.");
            return;
        }

        try
        {
            LoadDocument(_document.ProspectPath);
            _status.Text = "Unsaved changes reverted.";
            AppLogService.UserAction($"Revert all: reloaded from disk {_document.ProspectPath}");
        }
        catch (Exception ex)
        {
            AppLogService.Error("Revert all failed.", ex);
            MessageBox.Show(this, $"Could not reload the document.\n{ex.Message}", "Revert failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RevertSelectedOnActiveTab()
    {
        switch (_tabs.SelectedTab?.Text)
        {
            case "Members" when _membersGrid is not null:
                RevertSelectedRows("Members", _membersGrid);
                break;
            case "Custom Settings" when _customSettingsGrid is not null:
                RevertSelectedRows("Custom Settings", _customSettingsGrid);
                break;
            case "Mounts" when _mountsGrid is not null:
                RevertSelectedRows("Mounts", _mountsGrid);
                break;
            default:
                MessageBox.Show(this, "Select the Members, Custom Settings, or Mounts tab to revert rows.", "Revert selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
        }
    }

    private void RevertSelectedRows(string title, DataGridView grid)
    {
        if (grid.SelectedRows.Count == 0)
        {
            MessageBox.Show(this, "Select one or more rows to revert.", "Revert selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var revertConflicts = new List<string>();
        var revertErrors = new List<string>();
        using (SuspendDirtyTracking())
        {
            switch (title)
            {
                case "Members":
                    RevertSelectedRowsInternal(_members, _membersBaseline, grid);
                    break;
                case "Custom Settings":
                    RevertSelectedRowsInternal(_customSettings, _customSettingsBaseline, grid);
                    break;
                case "Mounts":
                    RevertSelectedMountRows(grid, revertConflicts, revertErrors);
                    break;
            }
        }

        foreach (var conflict in revertConflicts)
        {
            _dangerNotes.Add(conflict);
            AppLogService.UserAction($"Mount revert conflict: {conflict}");
        }

        if (revertErrors.Count > 0)
        {
            MessageBox.Show(
                this,
                "Some reverted mount rows could not be applied:\n\n" + string.Join("\n", revertErrors),
                "Revert selected",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        AppLogService.UserAction($"Revert selected rows in {title} ({grid.SelectedRows.Count} row(s)).");
        ApplyUnsavedIndicators();
        TryClearDirtyIfUnchanged();
    }

    /// <summary>
    /// A mount-editor apply writes straight into the recorder, so restoring the curated row alone is
    /// not enough: the restored baseline values must also be pushed back into the document, while
    /// fields explicitly edited in Advanced keep their raw value.
    /// </summary>
    private void RevertSelectedMountRows(DataGridView grid, ICollection<string> conflicts, ICollection<string> errors)
    {
        if (_document is null)
        {
            return;
        }

        var selectedRows = grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Where(r => r.DataBoundItem is MountRow)
            .Select(r => ((MountRow)r.DataBoundItem!).Clone())
            .ToList();
        var selectedRowIds = selectedRows.Select(row => row.RowId).ToList();

        RevertSelectedRowsInternal(_mounts, _mountsBaseline, grid);

        var recorderChanged = false;
        foreach (var rowId in selectedRowIds)
        {
            var restored = _mounts.FirstOrDefault(row => row.RowId == rowId);
            if (restored is null)
            {
                continue;
            }

            var beforeRevert = selectedRows.FirstOrDefault(row => row.RowId == rowId);
            if (beforeRevert is not null && MountRowsHaveSameCommittedValues(beforeRevert, restored))
            {
                // Nothing was changed in the session row; do not re-clamp untouched recorder data.
                continue;
            }

            _mountTalentsOverride.TryGetValue(restored.RecorderIndex, out var talents);
            if (!TryApplyMountRowToDocument(_document.Prospect, restored, talents, onlyChangedSinceBaseline: false, conflicts, out var error, out var changed))
            {
                errors.Add(error);
            }

            recorderChanged |= changed;
        }

        if (recorderChanged)
        {
            _projectionCache.Invalidate();
        }
    }

    /// <summary>
    /// Reverts by session-local identity, not by editable key. Renaming a member or setting no
    /// longer makes revert delete the row it was meant to restore.
    /// </summary>
    private static void RevertSelectedRowsInternal<T>(
        BindingList<T> source,
        IDictionary<Guid, string> baseline,
        DataGridView grid) where T : class
    {
        var selected = grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Where(r => r.DataBoundItem is T)
            .Select(r => (Item: (T)r.DataBoundItem!, Index: r.Index))
            .OrderByDescending(r => r.Index)
            .ToList();

        foreach (var (item, index) in selected)
        {
            if (index < 0 || index >= source.Count)
            {
                continue;
            }

            var rowId = RowIdOf(item);
            if (baseline.TryGetValue(rowId, out var snapshot))
            {
                var restored = JsonConvert.DeserializeObject<T>(snapshot);
                if (restored is not null)
                {
                    source[index] = restored;
                }
            }
            else
            {
                source.RemoveAt(index);
            }
        }
    }

    private static Guid RowIdOf<T>(T row) => row switch
    {
        MemberRow member => member.RowId,
        CustomSettingRow setting => setting.RowId,
        MountRow mount => mount.RowId,
        _ => Guid.Empty
    };

    private void HighlightMetadataField(Control control, bool changed)
    {
        var defaultBack = _isDarkTheme
            ? (control is TextBox or NumericUpDown or DateTimePicker ? Color.FromArgb(37, 39, 46) : Color.FromArgb(24, 24, 28))
            : (control is TextBox or NumericUpDown or DateTimePicker ? Color.White : Color.FromArgb(246, 248, 251));
        control.BackColor = changed ? ThemeHighlightColors.ChangedFieldBack(_isDarkTheme) : defaultBack;
    }

    private void HighlightGridRows<T>(
        DataGridView? grid,
        IReadOnlyList<T> rows,
        IDictionary<Guid, string> baseline,
        Func<T, Guid> rowIdSelector) where T : class
    {
        if (grid is null)
        {
            return;
        }

        var defaultBack = _isDarkTheme ? Color.FromArgb(37, 39, 46) : Color.White;
        var defaultFore = _isDarkTheme ? Color.FromArgb(230, 230, 235) : Color.FromArgb(28, 30, 33);
        var changedBack = ThemeHighlightColors.ChangedGridBack(_isDarkTheme);
        var changedFore = ThemeHighlightColors.ChangedGridFore(_isDarkTheme);
        for (var i = 0; i < grid.Rows.Count && i < rows.Count; i++)
        {
            var row = rows[i];
            var snapshot = JsonConvert.SerializeObject(row);
            var changed = !baseline.TryGetValue(rowIdSelector(row), out var baseSnapshot) ||
                          !string.Equals(snapshot, baseSnapshot, StringComparison.Ordinal);
            grid.Rows[i].DefaultCellStyle.BackColor = changed ? changedBack : defaultBack;
            grid.Rows[i].DefaultCellStyle.ForeColor = changed ? changedFore : defaultFore;
        }
    }

    private void LogMetadataEdit(Control control, string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var key = GetMetadataFieldKey(control);

        if (_lastLoggedMetadataValues.TryGetValue(control, out var previous) &&
            string.Equals(previous, normalized, StringComparison.Ordinal))
        {
            return;
        }

        AppLogService.UserAction($"Edited metadata.{key}: '{previous ?? "<unset>"}' -> '{normalized}'.");
        _lastLoggedMetadataValues[control] = normalized;
    }

    private string GetMetadataFieldKey(Control control)
    {
        if (ReferenceEquals(control, _lobbyName)) return "LobbyName";
        if (ReferenceEquals(control, _prospectId)) return "ProspectId";
        if (ReferenceEquals(control, _difficulty)) return "Difficulty";
        if (ReferenceEquals(control, _dropPoint)) return "DropPoint";
        if (ReferenceEquals(control, _insurance)) return "Insurance";
        if (ReferenceEquals(control, _noRespawns)) return "NoRespawns";
        if (ReferenceEquals(control, _claimedAccountId)) return "ClaimedAccountId";
        if (ReferenceEquals(control, _claimedCharacterSlot)) return "ClaimedCharacterSlot";
        if (ReferenceEquals(control, _prospectState)) return "ProspectState";
        if (ReferenceEquals(control, _elapsedTime)) return "ElapsedTime";
        if (ReferenceEquals(control, _cost)) return "Cost";
        if (ReferenceEquals(control, _reward)) return "Reward";
        if (ReferenceEquals(control, _prospectDtKey)) return "ProspectDtKey";
        if (ReferenceEquals(control, _factionMissionDtKey)) return "FactionMissionDtKey";
        if (ReferenceEquals(control, _neverExpires)) return "NeverExpires";
        if (ReferenceEquals(control, _expireTime)) return "ExpireTime";
        return control.GetType().Name;
    }

    private void CaptureCellBeforeValue(DataGridView grid, int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0 || rowIndex >= grid.Rows.Count || columnIndex >= grid.Columns.Count)
        {
            return;
        }

        var key = $"{grid.GetHashCode()}:{rowIndex}:{columnIndex}";
        var value = grid.Rows[rowIndex].Cells[columnIndex].Value?.ToString() ?? string.Empty;
        _cellEditBeforeValues[key] = value;
    }

    private void LogGridCellChange(string title, DataGridView grid, int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0 || rowIndex >= grid.Rows.Count || columnIndex >= grid.Columns.Count)
        {
            return;
        }

        var key = $"{grid.GetHashCode()}:{rowIndex}:{columnIndex}";
        var before = _cellEditBeforeValues.TryGetValue(key, out var from) ? from : "<unknown>";
        var after = grid.Rows[rowIndex].Cells[columnIndex].Value?.ToString() ?? string.Empty;
        var column = grid.Columns[columnIndex].DataPropertyName;
        if (string.IsNullOrWhiteSpace(column))
        {
            column = grid.Columns[columnIndex].Name;
        }

        AppLogService.UserAction($"Edited {title} row={rowIndex} col={column}: '{before}' -> '{after}'.");
        _cellEditBeforeValues.Remove(key);
    }

    // ---------------------------------------------------------------- inspectors and mount editing

    private void InspectSelectedRecorder(string title, DataGridView grid)
    {
        AppLogService.UserAction($"{title}: inspect clicked ({grid.SelectedRows.Count} selected).");
        if (_document is null || grid.SelectedRows.Count != 1)
        {
            MessageBox.Show(this, "Select exactly one recorder to inspect.", "Inspector", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (grid.SelectedRows[0].DataBoundItem is not RecorderRow selected)
        {
            return;
        }

        var fields = ProspectModelMapper.ReadRecorderFields(_document.Prospect, selected.Index);
        if (fields.Count == 0)
        {
            MessageBox.Show(this, "No editable internal fields were found for this recorder.", "Inspector", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var originalFieldValues = SnapshotRecorderFieldValues(fields);
        using var inspector = new RecorderInspectorForm(
            $"Recorder inspector — {selected.ComponentClass} [{selected.Index}]",
            fields,
            _members);
        inspector.ApplyTheme(_isDarkTheme);
        if (inspector.ShowDialog(this) != DialogResult.OK)
        {
            AppLogService.UserAction($"Recorder inspector cancelled: {selected.ComponentClass} [{selected.Index}].");
            return;
        }

        var advancedPaths = CollectChangedRecorderPaths(originalFieldValues, inspector.EditedRows);
        var result = ProspectModelMapper.ApplyRecorderFieldEdits(_document.Prospect, selected.Index, inspector.EditedRows);
        if (!result.Succeeded)
        {
            MessageBox.Show(this, result.Describe(), "Recorder edit not applied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!result.Changed)
        {
            _status.Text = "Recorder editor closed with no changes.";
            return;
        }

        AppLogService.UserAction($"Recorder inspector applied: {selected.ComponentClass} [{selected.Index}].");
        RecordAdvancedRecorderEdits(selected.Index, advancedPaths);
        _inspectorEdits.Add($"{selected.ComponentClass} [{selected.Index}]");
        MarkMutated("Edited recorder");
        RefreshTabs();
        _status.Text = result.Describe();
    }

    private void RemoveSelectedRecorders(string title, DataGridView grid)
    {
        AppLogService.UserAction($"{title}: remove clicked ({grid.SelectedRows.Count} selected).");
        if (_document is null || grid.SelectedRows.Count == 0)
        {
            return;
        }

        var indexes = grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(r => (r.DataBoundItem as RecorderRow)?.Index ?? -1)
            .Where(i => i >= 0)
            .ToList();
        if (indexes.Count == 0)
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Remove {indexes.Count} {title.ToLowerInvariant()} recorder(s) from this prospect?\n\nThis can break active game state and cannot be undone except by reverting.",
            "Remove recorders",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        if (!ProspectModelMapper.RemoveRecordersByIndex(_document.Prospect, indexes))
        {
            MessageBox.Show(this, "Could not remove the selected recorders from the prospect data.", "Remove failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        AppLogService.UserAction($"{title}: removed {indexes.Count} recorder(s): {string.Join(",", indexes)}.");
        _dangerNotes.Add($"{title} recorder removal performed. This may break active game state.");
        MarkMutated("Removed recorders");

        // Recorder indexes shifted. Remap the session projections in place instead of reloading them
        // from the model: pending metadata/member/settings/mount drafts and the saved baseline stay
        // intact, and no mount row can point at a different recorder after the removal (E01).
        using (SuspendDirtyTracking())
        {
            RemapRecorderIndexesAfterRemoval(indexes);
        }

        RefreshTabs();
        ClearRecorderGridSelections();
        _status.Text = $"Removed {indexes.Count} recorder(s).";
    }

    /// <summary>
    /// Keeps every recorder-index-based session projection aligned after recorders are removed.
    /// Rows that pointed at removed recorders are dropped; the rest keep their RowId, pending values
    /// and captured baseline, with only the recorder pointer adjusted.
    /// </summary>
    private void RemapRecorderIndexesAfterRemoval(IReadOnlyCollection<int> removedIndexes)
    {
        var removed = removedIndexes as HashSet<int> ?? removedIndexes.ToHashSet();
        for (var i = _mounts.Count - 1; i >= 0; i--)
        {
            var mount = _mounts[i];
            if (removed.Contains(mount.RecorderIndex))
            {
                _mountsBaseline.Remove(mount.RowId);
                _mounts.RemoveAt(i);
                continue;
            }

            var newIndex = RemapRecorderIndex(mount.RecorderIndex, removed);
            if (newIndex == mount.RecorderIndex)
            {
                continue;
            }

            if (_mountsBaseline.TryGetValue(mount.RowId, out var baselineJson))
            {
                var baselineRow = JsonConvert.DeserializeObject<MountRow>(baselineJson);
                if (baselineRow is not null)
                {
                    baselineRow.RecorderIndex = newIndex;
                    _mountsBaseline[mount.RowId] = JsonConvert.SerializeObject(baselineRow);
                }
            }

            mount.RecorderIndex = newIndex;
        }

        RemapIndexedValues(_mountTalentsOverride, removed);
        RemapIndexedValues(_advancedRecorderFieldEdits, removed);
        _mounts.ResetBindings();
    }

    private static int RemapRecorderIndex(int current, IReadOnlySet<int> removedIndexes)
    {
        var removedBefore = 0;
        foreach (var index in removedIndexes)
        {
            if (index < current)
            {
                removedBefore++;
            }
        }

        return current - removedBefore;
    }

    private static void RemapIndexedValues<T>(Dictionary<int, T> values, IReadOnlySet<int> removedIndexes)
    {
        if (values.Count == 0)
        {
            return;
        }

        var remapped = new Dictionary<int, T>();
        foreach (var (index, value) in values)
        {
            if (!removedIndexes.Contains(index))
            {
                remapped[RemapRecorderIndex(index, removedIndexes)] = value;
            }
        }

        values.Clear();
        foreach (var (index, value) in remapped)
        {
            values[index] = value;
        }
    }

    private void ClearRecorderGridSelections()
    {
        _charactersGrid?.ClearSelection();
        _missionsGrid?.ClearSelection();
        _prebuiltsGrid?.ClearSelection();
        _allRecordersGrid?.ClearSelection();
        _mountsGrid?.ClearSelection();
    }

    private void EditSelectedMount(DataGridView grid)
    {
        if (_document is null || grid.SelectedRows.Count != 1)
        {
            MessageBox.Show(this, "Select exactly one mount row.", "Mounts", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (grid.SelectedRows[0].DataBoundItem is not MountRow row)
        {
            return;
        }

        var originalFields = ProspectModelMapper.ReadRecorderFields(_document.Prospect, row.RecorderIndex);
        var originalFieldValues = SnapshotRecorderFieldValues(originalFields);
        var talents = ProspectModelMapper.ReadTalentRows(_document.Prospect, row.RecorderIndex);
        using var form = new MountEditorForm(row, talents, originalFields, _members);
        form.ApplyTheme(_isDarkTheme);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            AppLogService.UserAction($"Mount editor closed without apply: '{row.MountName}' (recorder {row.RecorderIndex}).");
            return;
        }

        AppLogService.UserAction($"Mount editor applied: '{row.MountName}' (recorder {row.RecorderIndex}).");
        var merge = MountEditMerge.Apply(
            _document.Prospect,
            row.RecorderIndex,
            originalFields,
            form.UpdatedMount,
            form.UpdatedAdvancedFields,
            form.UpdatedTalents);
        if (!merge.Edit.Succeeded)
        {
            MessageBox.Show(this, merge.Edit.Describe(), "Mount update failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Any field edited in the dialog's Advanced tab is authoritative. Remember those paths so a
        // save-time curated replay cannot put the Overview draft back over them.
        RecordAdvancedRecorderEdits(
            row.RecorderIndex,
            CollectChangedRecorderPaths(originalFieldValues, form.UpdatedAdvancedFields));

        // The dialog drafted a copy; commit its values into the session row only after a successful apply.
        CopyMountValues(form.UpdatedMount, row);
        _mountTalentsOverride[row.RecorderIndex] = form.UpdatedTalents.ToList();
        _inspectorEdits.Add($"Mount advanced edits [{row.RecorderIndex}]");
        MarkMutated("Edited mount");
        RefreshTabs();

        if (merge.Conflicts.Count > 0)
        {
            MessageBox.Show(
                this,
                "Some fields were changed in both Overview and Advanced. The explicit Advanced value was kept:\n\n" +
                string.Join("\n", merge.Conflicts),
                "Overlapping mount edits",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        _status.Text = merge.Edit.Describe();
    }

    private static void CopyMountValues(MountRow from, MountRow to)
    {
        to.MountName = from.MountName;
        to.OwnerPlayerId = from.OwnerPlayerId;
        to.OwnerCharacterSlot = from.OwnerCharacterSlot;
        to.OwnerName = from.OwnerName;
        to.MountType = from.MountType;
        to.MountRace = from.MountRace;
        to.Level = from.Level;
        to.Experience = from.Experience;
        to.Health = from.Health;
        to.Sex = from.Sex;
        to.Lineage = from.Lineage;
        to.Variation = from.Variation;
        to.Vitality = from.Vitality;
        to.Endurance = from.Endurance;
        to.Muscle = from.Muscle;
        to.Agility = from.Agility;
        to.Toughness = from.Toughness;
        to.Hardiness = from.Hardiness;
        to.Utility = from.Utility;
        to.MountIconName = from.MountIconName;
        to.AiSetupRowName = from.AiSetupRowName;
        to.ActorClassName = from.ActorClassName;
        to.ActorPathName = from.ActorPathName;
    }

    private void InspectSelectedMount(DataGridView grid)
    {
        if (_document is null || grid.SelectedRows.Count != 1)
        {
            MessageBox.Show(this, "Select exactly one mount row.", "Inspector", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (grid.SelectedRows[0].DataBoundItem is not MountRow row)
        {
            return;
        }

        var fields = ProspectModelMapper.ReadRecorderFields(_document.Prospect, row.RecorderIndex);
        if (fields.Count == 0)
        {
            MessageBox.Show(this, "No editable internal fields were found for this mount recorder.", "Inspector", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var originalFieldValues = SnapshotRecorderFieldValues(fields);
        using var inspector = new RecorderInspectorForm(
            $"Mount recorder inspector — {row.MountName} [{row.RecorderIndex}]",
            fields,
            _members);
        inspector.ApplyTheme(_isDarkTheme);
        if (inspector.ShowDialog(this) != DialogResult.OK)
        {
            AppLogService.UserAction($"Mount raw recorder inspector cancelled: '{row.MountName}' [{row.RecorderIndex}].");
            return;
        }

        var advancedPaths = CollectChangedRecorderPaths(originalFieldValues, inspector.EditedRows);
        var result = ProspectModelMapper.ApplyRecorderFieldEdits(_document.Prospect, row.RecorderIndex, inspector.EditedRows);
        if (!result.Succeeded)
        {
            MessageBox.Show(this, result.Describe(), "Mount advanced edit failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!result.Changed)
        {
            _status.Text = "Mount recorder inspector closed with no changes.";
            return;
        }

        AppLogService.UserAction($"Mount raw recorder inspector applied: '{row.MountName}' [{row.RecorderIndex}].");
        RecordAdvancedRecorderEdits(row.RecorderIndex, advancedPaths);
        _inspectorEdits.Add($"Mount recorder [{row.RecorderIndex}]");
        MarkMutated("Edited mount recorder");
        RefreshTabs();
        _status.Text = result.Describe();
    }

    private static Dictionary<string, string> SnapshotRecorderFieldValues(IEnumerable<RecorderFieldRow> rows)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            values[row.Path] = row.Value;
        }

        return values;
    }

    private static HashSet<string> CollectChangedRecorderPaths(
        IReadOnlyDictionary<string, string> before,
        IEnumerable<RecorderFieldRow> edited)
    {
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in edited)
        {
            if (!before.TryGetValue(row.Path, out var previous) ||
                !string.Equals(previous, row.Value, StringComparison.Ordinal))
            {
                changed.Add(row.Path);
            }
        }

        return changed;
    }

    private void RecordAdvancedRecorderEdits(int recorderIndex, IEnumerable<string> paths)
    {
        var changed = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (changed.Count == 0)
        {
            return;
        }

        if (!_advancedRecorderFieldEdits.TryGetValue(recorderIndex, out var existing))
        {
            existing = new HashSet<string>(StringComparer.Ordinal);
            _advancedRecorderFieldEdits[recorderIndex] = existing;
        }

        foreach (var path in changed)
        {
            existing.Add(path);
        }
    }

    private void ResetRows<T>(BindingList<T> list, IEnumerable<T> values)
    {
        list.RaiseListChangedEvents = false;
        try
        {
            list.Clear();
            foreach (var value in values)
            {
                list.Add(value);
            }
        }
        finally
        {
            list.RaiseListChangedEvents = true;
            list.ResetBindings();
        }
    }

    private sealed record MetadataSnapshot(
        string LobbyName,
        string ProspectId,
        string Difficulty,
        int DropPoint,
        bool Insurance,
        bool NoRespawns,
        string ClaimedAccountId,
        int ClaimedCharacterSlot,
        string ProspectState,
        int ElapsedTime,
        int Cost,
        int Reward,
        string ProspectDtKey,
        string FactionMissionDtKey,
        long ExpirePersisted)
    {
        public static MetadataSnapshot Empty => new(
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            false,
            false,
            string.Empty,
            ProspectEditorMetadataDefaults.ClaimedCharacterSlot,
            string.Empty,
            0,
            0,
            0,
            string.Empty,
            string.Empty,
            0L);

        public static MetadataSnapshot Capture(MainForm form) => new(
            form._lobbyName.Text.Trim(),
            form._prospectId.Text.Trim(),
            form._difficulty.Text.Trim(),
            (int)form._dropPoint.Value,
            form._insurance.Checked,
            form._noRespawns.Checked,
            form._claimedAccountId.Text.Trim(),
            (int)form._claimedCharacterSlot.Value,
            form._prospectState.Text.Trim(),
            (int)form._elapsedTime.Value,
            (int)form._cost.Value,
            (int)form._reward.Value,
            form._prospectDtKey.Text.Trim(),
            form._factionMissionDtKey.Text.Trim(),
            ProspectExpireTimeUi.ToPersistedSeconds(form._neverExpires.Checked, form._expireTime.Value));
    }
}
