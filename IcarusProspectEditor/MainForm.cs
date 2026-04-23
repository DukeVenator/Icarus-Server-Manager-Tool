using System.ComponentModel;
using IcarusProspectEditor.Mapping;
using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using IcarusSaveLib;
using Newtonsoft.Json;

namespace IcarusProspectEditor;

internal sealed partial class MainForm : Form
{
    private readonly BindingList<MemberRow> _members = [];
    private readonly BindingList<CustomSettingRow> _customSettings = [];
    private readonly BindingList<RecorderRow> _characters = [];
    private readonly BindingList<RecorderRow> _missions = [];
    private readonly BindingList<RecorderRow> _prebuilts = [];
    private readonly BindingList<RecorderRow> _allRecorders = [];
    private readonly BindingList<MountRow> _mounts = [];

    private readonly TextBox _prospectPath = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 24, Text = "Ready." };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _themeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };

    private readonly TextBox _lobbyName = new() { Dock = DockStyle.Fill };
    private readonly TextBox _prospectId = new() { Dock = DockStyle.Fill };
    private readonly TextBox _difficulty = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _dropPoint = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 9999 };
    private readonly CheckBox _noRespawns = new() { Text = "No Respawns (hardcore)", Dock = DockStyle.Fill };
    private readonly CheckBox _insurance = new() { Text = "Insurance Enabled", Dock = DockStyle.Fill };
    private readonly TextBox _claimedAccountId = new() { Dock = DockStyle.Fill };
    private readonly Button _pickClaimedAccountButton = new() { Text = "Wizard Pick Player", AutoSize = true };
    private readonly NumericUpDown _claimedCharacterSlot = new() { Dock = DockStyle.Fill, Minimum = -1, Maximum = 100 };
    private readonly TextBox _prospectState = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _elapsedTime = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = int.MaxValue };
    private readonly NumericUpDown _cost = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = int.MaxValue };
    private readonly NumericUpDown _reward = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = int.MaxValue };
    private readonly TextBox _prospectDtKey = new() { Dock = DockStyle.Fill };
    private readonly TextBox _factionMissionDtKey = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _neverExpires = new() { Text = "Never expires (no calendar date)", AutoSize = true };
    private readonly DateTimePicker _expireTime = new() { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss" };

    private ProspectDocument? _document;
    private bool _dirty;
    private readonly HashSet<string> _dangerNotes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inspectorEdits = new(StringComparer.Ordinal);
    private readonly Dictionary<int, List<TalentRow>> _mountTalentsOverride = [];
    private readonly Dictionary<string, string> _membersBaseline = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _customSettingsBaseline = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _mountsBaseline = new(StringComparer.OrdinalIgnoreCase);
    private MetadataSnapshot _metadataBaseline = MetadataSnapshot.Empty;
    private bool _suspendDirtyTracking;
    private bool _isDarkTheme;
    private DataGridView? _membersGrid;
    private DataGridView? _customSettingsGrid;
    private DataGridView? _mountsGrid;

    private readonly ProspectEditorUpdateService _editorUpdateService = new();
    private ProspectEditorUpdateSettings _updateSettings = ProspectEditorUpdateSettings.Load();
    private readonly System.Windows.Forms.Timer _editorUpdateTimer = new() { Interval = 30000 };
    private DateTime _lastEditorUpdateCheckUtc = DateTime.MinValue;
    private bool _editorUpdateCheckInProgress;
    private bool _editorUpdatePromptShownThisRun;

    public MainForm()
    {
        Text = "Icarus Prospect Editor";
        Width = 1550;
        Height = 920;
        StartPosition = FormStartPosition.CenterScreen;
        BuildLayout();
        BindTables();
        CaptureBaselines();
        InitializeTheme();
        SetupEditorUpdateFlow();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(BuildTopBar(), 0, 0);
        root.Controls.Add(_tabs, 0, 1);
        root.Controls.Add(_status, 0, 2);

        _tabs.TabPages.Add(BuildMetadataTab());
        _tabs.TabPages.Add(BuildGridTab("Members", _members));
        _tabs.TabPages.Add(BuildGridTab("Custom Settings", _customSettings));
        _tabs.TabPages.Add(BuildRecorderTab("Characters", _characters));
        _tabs.TabPages.Add(BuildRecorderTab("Missions", _missions));
        _tabs.TabPages.Add(BuildRecorderTab("Prebuilts", _prebuilts));
        _tabs.TabPages.Add(BuildMountsTab());
        _tabs.TabPages.Add(BuildRecorderTab("Diagnostics", _allRecorders));
    }

    private Control BuildTopBar()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 12, RowCount = 2, AutoSize = true };
        for (var i = 0; i < panel.ColumnCount; i++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }

        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var openProspect = new Button { Text = "Open Prospect", AutoSize = true };
        openProspect.Click += (_, _) => OpenProspect();
        var save = new Button { Text = "Save", AutoSize = true };
        save.Click += (_, _) => SaveDocument();
        var saveAs = new Button { Text = "Save As", AutoSize = true };
        saveAs.Click += (_, _) => SaveAs();
        var refresh = new Button { Text = "Refresh View", AutoSize = true };
        refresh.Click += (_, _) =>
        {
            AppLogService.UserAction("Refresh view (toolbar).");
            RefreshTabs();
        };
        var exportDecoded = new Button { Text = "Export Decoded", AutoSize = true };
        exportDecoded.Click += async (_, _) => await ExportDecodedAsync();
        var revertAll = new Button { Text = "Revert All Unsaved", AutoSize = true };
        revertAll.Click += (_, _) => RevertAllUnsavedChanges();

        var checkUpdates = new Button { Text = "Check for updates", AutoSize = true };
        checkUpdates.Click += async (_, _) => await CheckForEditorUpdateAsync(userInitiated: true);
        var updateSettingsBtn = new Button { Text = "Update settings", AutoSize = true };
        updateSettingsBtn.Click += (_, _) => ShowEditorUpdateSettings();
        var viewLogFolder = new Button { Text = "Log folder", AutoSize = true };
        viewLogFolder.Click += (_, _) => AppLogService.OpenLogFolder();

        panel.Controls.Add(openProspect, 0, 0);
        panel.Controls.Add(save, 1, 0);
        panel.Controls.Add(saveAs, 2, 0);
        panel.Controls.Add(refresh, 3, 0);
        panel.Controls.Add(exportDecoded, 4, 0);
        panel.Controls.Add(revertAll, 5, 0);
        panel.Controls.Add(new Label { Text = "Prospect:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 6, 0);
        panel.Controls.Add(_prospectPath, 7, 0);
        _themeCombo.Items.AddRange(["Dark", "Light"]);
        _themeCombo.SelectedIndexChanged += (_, _) => OnThemeChanged();
        panel.Controls.Add(new Label { Text = "Theme:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 8, 0);
        panel.Controls.Add(_themeCombo, 9, 0);

        panel.Controls.Add(checkUpdates, 0, 1);
        panel.Controls.Add(updateSettingsBtn, 1, 1);
        panel.Controls.Add(viewLogFolder, 2, 1);
        return panel;
    }

    private TabPage BuildMetadataTab()
    {
        var page = new TabPage("Metadata");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 16, Padding = new Padding(12), AutoScroll = true };
        for (var i = 0; i < 16; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var expireWrap = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, AutoSize = true };
        expireWrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        expireWrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        expireWrap.Controls.Add(_neverExpires, 0, 0);
        expireWrap.Controls.Add(_expireTime, 0, 1);

        var fields = new (string Label, Control Control)[]
        {
            ("Lobby Name", _lobbyName),
            ("Prospect ID", _prospectId),
            ("Difficulty", _difficulty),
            ("Drop Point", _dropPoint),
            ("Claimed Account ID", _claimedAccountId),
            ("Claimed Character Slot", _claimedCharacterSlot),
            ("Prospect State", _prospectState),
            ("Elapsed Time", _elapsedTime),
            ("Cost", _cost),
            ("Reward", _reward),
            ("ProspectDTKey", _prospectDtKey),
            ("FactionMissionDTKey", _factionMissionDtKey),
            ("Expire Time", expireWrap)
        };

        var row = 0;
        foreach (var (label, control) in fields)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true }, 0, row);
            if (control == _claimedAccountId)
            {
                var claimedPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
                claimedPanel.Controls.Add(_claimedAccountId);
                claimedPanel.Controls.Add(_pickClaimedAccountButton);
                grid.Controls.Add(claimedPanel, 1, row);
            }
            else
            {
                grid.Controls.Add(control, 1, row);
            }
            row++;
        }
        grid.Controls.Add(_insurance, 1, row++);
        grid.Controls.Add(_noRespawns, 1, row++);

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
                    box.TextChanged += (_, _) => MarkDirty();
                    break;
                case NumericUpDown numeric:
                    numeric.ValueChanged += (_, _) => MarkDirty();
                    break;
                case CheckBox check:
                    check.CheckedChanged += (_, _) => MarkDirty();
                    break;
                case DateTimePicker picker:
                    picker.ValueChanged += (_, _) => MarkDirty();
                    break;
            }
        }

        _neverExpires.CheckedChanged += (_, _) =>
        {
            _expireTime.Enabled = !_neverExpires.Checked;
            MarkDirty();
        };

        _pickClaimedAccountButton.Click += (_, _) => PickClaimedAccountFromKnownPlayers();

        page.Controls.Add(grid);
        return page;
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
            AllowUserToDeleteRows = true
        };
        grid.CellValueChanged += (_, _) => MarkDirty();
        grid.UserDeletedRow += (_, _) =>
        {
            if (title == "Mounts")
            {
                _dangerNotes.Add("Mount entries were removed. This can break active mount ownership and state.");
            }
            MarkDirty();
        };
        var revertSelected = new Button { Text = $"Revert Selected {title} Row(s)", AutoSize = true };
        revertSelected.Click += (_, _) => RevertSelectedRows(title, grid);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        actions.Controls.Add(revertSelected);
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
            AllowUserToDeleteRows = false
        };
        grid.CellValueChanged += (_, _) => MarkDirty();
        _mountsGrid = grid;

        var editSelected = new Button { Text = "Edit Selected Mount", AutoSize = true };
        var inspectSelected = new Button { Text = "Inspect Raw Mount Recorder", AutoSize = true };
        editSelected.Click += (_, _) =>
        {
            if (_document is null || grid.SelectedRows.Count != 1)
            {
                MessageBox.Show("Select exactly one mount row.");
                return;
            }

            var row = grid.SelectedRows[0].DataBoundItem as MountRow;
            if (row is null)
            {
                return;
            }

            var talents = ProspectModelMapper.ReadTalentRows(_document.Prospect, row.RecorderIndex);
            var advancedFields = ProspectModelMapper.ReadRecorderFields(_document.Prospect, row.RecorderIndex);
            using var form = new MountEditorForm(row, talents, advancedFields, _members);
            form.ApplyTheme(_isDarkTheme);
            if (form.ShowDialog(this) != DialogResult.OK)
            {
                AppLogService.UserAction($"Mount editor closed without apply: '{row.MountName}' (recorder {row.RecorderIndex}).");
                return;
            }

            AppLogService.UserAction($"Mount editor applied: '{row.MountName}' (recorder {row.RecorderIndex}).");
            _mountTalentsOverride[row.RecorderIndex] = form.UpdatedTalents.ToList();
            if (!ProspectModelMapper.ApplyMountFromProspect(_document.Prospect, row, _mountTalentsOverride[row.RecorderIndex]))
            {
                MessageBox.Show("Failed to apply mount updates for this recorder.", "Mount update failed");
                return;
            }
            if (!ProspectModelMapper.ApplyRecorderFieldEdits(_document.Prospect, row.RecorderIndex, form.UpdatedAdvancedFields))
            {
                MessageBox.Show("Failed to apply advanced recorder edits for this mount.", "Advanced edit failed");
                return;
            }

            _inspectorEdits.Add($"Mount advanced edits [{row.RecorderIndex}]");
            MarkDirty();
            RefreshTabs();
        };
        inspectSelected.Click += (_, _) =>
        {
            if (_document is null || grid.SelectedRows.Count != 1)
            {
                MessageBox.Show("Select exactly one mount row.", "Inspector");
                return;
            }

            var row = grid.SelectedRows[0].DataBoundItem as MountRow;
            if (row is null)
            {
                return;
            }

            var fields = ProspectModelMapper.ReadRecorderFields(_document.Prospect, row.RecorderIndex);
            if (fields.Count == 0)
            {
                MessageBox.Show("No editable internal fields were found for this mount recorder.", "Inspector");
                return;
            }

            using var inspector = new RecorderInspectorForm(
                $"Mount Recorder Inspector - {row.MountName} [{row.RecorderIndex}]",
                fields,
                _members);
            inspector.ApplyTheme(_isDarkTheme);
            if (inspector.ShowDialog(this) != DialogResult.OK)
            {
                AppLogService.UserAction($"Mount raw recorder inspector cancelled: '{row.MountName}' [{row.RecorderIndex}].");
                return;
            }

            if (!ProspectModelMapper.ApplyRecorderFieldEdits(_document.Prospect, row.RecorderIndex, inspector.EditedRows))
            {
                MessageBox.Show("Failed to apply mount recorder field edits.", "Inspector");
                return;
            }

            AppLogService.UserAction($"Mount raw recorder inspector applied: '{row.MountName}' [{row.RecorderIndex}].");
            _inspectorEdits.Add($"Mount recorder [{row.RecorderIndex}]");
            MarkDirty();
            RefreshTabs();
        };

        panel.Controls.Add(grid, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        actions.Controls.Add(editSelected);
        actions.Controls.Add(inspectSelected);
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
            AllowUserToDeleteRows = false
        };

        var removeSelected = new Button { Text = $"Remove Selected {title}", AutoSize = true };
        var inspectSelected = new Button { Text = $"Inspect/Edit Selected {title}", AutoSize = true };

        inspectSelected.Click += (_, _) =>
        {
            if (_document is null || grid.SelectedRows.Count != 1)
            {
                MessageBox.Show("Select exactly one recorder to inspect.", "Inspector");
                return;
            }

            var selected = grid.SelectedRows[0].DataBoundItem as RecorderRow;
            if (selected is null)
            {
                return;
            }

            var fields = ProspectModelMapper.ReadRecorderFields(_document.Prospect, selected.Index);
            if (fields.Count == 0)
            {
                MessageBox.Show("No editable internal fields were found for this recorder.", "Inspector");
                return;
            }

            using var inspector = new RecorderInspectorForm(
                $"Recorder Inspector - {selected.ComponentClass} [{selected.Index}]",
                fields,
                _members);
            inspector.ApplyTheme(_isDarkTheme);
            if (inspector.ShowDialog(this) != DialogResult.OK)
            {
                AppLogService.UserAction($"Recorder inspector cancelled: {selected.ComponentClass} [{selected.Index}].");
                return;
            }

            if (!ProspectModelMapper.ApplyRecorderFieldEdits(_document.Prospect, selected.Index, inspector.EditedRows))
            {
                MessageBox.Show("Failed to apply recorder field edits.", "Inspector");
                return;
            }

            AppLogService.UserAction($"Recorder inspector applied: {selected.ComponentClass} [{selected.Index}].");
            _inspectorEdits.Add($"{selected.ComponentClass} [{selected.Index}]");
            MarkDirty();
            RefreshTabs();
        };
        removeSelected.Click += (_, _) =>
        {
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

            var removed = ProspectModelMapper.RemoveRecordersByIndex(_document.Prospect, indexes);
            if (!removed)
            {
                MessageBox.Show("Could not remove selected recorders from prospect data.", "Remove failed");
                return;
            }

            AppLogService.UserAction($"{title}: removed {indexes.Count} recorder(s): {string.Join(",", indexes)}.");
            _dangerNotes.Add($"{title} recorder removal performed. This may break active game state.");
            MarkDirty();
            RefreshTabs();
        };

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        actions.Controls.Add(inspectSelected);
        actions.Controls.Add(removeSelected);

        panel.Controls.Add(grid, 0, 0);
        panel.Controls.Add(actions, 0, 1);
        page.Controls.Add(panel);
        return page;
    }

    private void BindTables()
    {
        _tabs.SelectedIndexChanged += (_, _) => RefreshStatusLine();
    }

    private void OpenProspect()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "Icarus Prospect (*.json)|*.json|All files (*.*)|*.*",
            Title = "Open prospect JSON"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _document = ProspectLoadService.Load(dialog.FileName);
            _prospectPath.Text = _document.ProspectPath;
            _status.Text = "Prospect loaded.";
            AppLogService.UserAction($"Opened prospect: {dialog.FileName}");
            AppLogService.Info($"Prospect loaded: {dialog.FileName}");
            _dirty = false;
            _dangerNotes.Clear();
            _inspectorEdits.Clear();
            _mountTalentsOverride.Clear();
            RefreshTabs();
            CaptureBaselines();
            ApplyUnsavedIndicators();
        }
        catch (Exception ex)
        {
            AppLogService.Error($"Failed to load prospect: {dialog.FileName}", ex);
            MessageBox.Show($"Failed to load prospect.\n{ex.Message}", "Load failed");
        }
    }

    private void SaveDocument()
    {
        if (_document is null)
        {
            MessageBox.Show("Open a prospect first.");
            return;
        }

        if (!ValidateBeforeSave(out var validationError))
        {
            MessageBox.Show(validationError, "Validation failed");
            return;
        }

        ApplyCurrentFormState();
        if (!ShowDangerWarningsIfNeeded())
        {
            return;
        }

        var summary = BuildChangeSummary();
        var result = MessageBox.Show($"{summary}\n\nBackups are always created before save.", "Confirm save", MessageBoxButtons.OKCancel);
        if (result != DialogResult.OK)
        {
            AppLogService.UserAction("Save cancelled at confirmation dialog.");
            return;
        }

        try
        {
            ProspectSaveService.SaveDocument(_document, createBackup: true);
            _dirty = false;
            _status.Text = "Saved with backup.";
            AppLogService.UserAction($"Saved prospect with backup: {_document.ProspectPath}");
            AppLogService.Info($"Prospect saved with backup: {_document.ProspectPath}");
            _dangerNotes.Clear();
            _inspectorEdits.Clear();
            CaptureBaselines();
            ApplyUnsavedIndicators();
        }
        catch (Exception ex)
        {
            AppLogService.Error($"Save failed: {_document.ProspectPath}", ex);
            MessageBox.Show($"Save failed.\n{ex.Message}", "Save failed");
        }
    }

    private void SaveAs()
    {
        if (_document is null)
        {
            return;
        }

        using var dialog = new SaveFileDialog { Filter = "Icarus Prospect (*.json)|*.json", FileName = Path.GetFileName(_document.ProspectPath) };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _document = new ProspectDocument
        {
            ProspectPath = dialog.FileName,
            Prospect = _document.Prospect
        };
        _prospectPath.Text = _document.ProspectPath;
        AppLogService.UserAction($"Save As: target path set to {_document.ProspectPath}");
        SaveDocument();
    }

    private async Task ExportDecodedAsync()
    {
        if (_document is null)
        {
            MessageBox.Show("Open a prospect first.");
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
            var modeChoice = MessageBox.Show(
                "Choose export mode:\n\nYes = Raw decoded export (fastest)\nNo = Enriched export with metadata + mounts (slower)\nCancel = abort",
                "Decoded export mode",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            if (modeChoice == DialogResult.Cancel)
            {
                return;
            }

            var mode = modeChoice == DialogResult.Yes ? DecodedExportMode.Raw : DecodedExportMode.Enriched;
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
            AppLogService.Info($"Decoded export started ({mode}): {dialog.FileName}");
            await Task.Run(() => DecodedExportService.Export(_document, dialog.FileName, mode, progress));
            progressForm.Close();
            AppLogService.UserAction($"Decoded export completed ({mode}) -> {dialog.FileName}");
            AppLogService.Info($"Decoded export completed ({mode}): {dialog.FileName}");

            MessageBox.Show($"Decoded export written:\n{dialog.FileName}", "Export complete");
            _status.Text = "Decoded export complete.";
        }
        catch (Exception ex)
        {
            AppLogService.Error("Decoded export failed.", ex);
            MessageBox.Show($"Failed to export decoded JSON.\n{ex.Message}", "Export failed");
            _status.Text = "Decoded export failed.";
        }
    }

    private void RefreshTabs()
    {
        if (_document is null || string.IsNullOrWhiteSpace(_document.ProspectPath))
        {
            return;
        }

        _suspendDirtyTracking = true;
        var info = _document.Prospect.ProspectInfo;
        _lobbyName.Text = info.LobbyName ?? string.Empty;
        _prospectId.Text = info.ProspectID ?? string.Empty;
        _difficulty.Text = info.Difficulty ?? string.Empty;
        _dropPoint.Value = Math.Clamp(info.SelectedDropPoint, 0, (int)_dropPoint.Maximum);
        _insurance.Checked = info.Insurance;
        _noRespawns.Checked = info.NoRespawns;
        _claimedAccountId.Text = info.ClaimedAccountID ?? string.Empty;
        _claimedCharacterSlot.Value = info.ClaimedAccountCharacter;
        _prospectState.Text = info.ProspectState ?? string.Empty;
        _elapsedTime.Value = Math.Clamp(info.ElapsedTime, 0, int.MaxValue);
        _cost.Value = Math.Clamp(info.Cost, 0, int.MaxValue);
        _reward.Value = Math.Clamp(info.Reward, 0, int.MaxValue);
        _prospectDtKey.Text = info.ProspectDTKey ?? string.Empty;
        _factionMissionDtKey.Text = info.FactionMissionDTKey ?? string.Empty;
        ProspectExpireTimeUi.ApplyPersistedToControl(info.ExpireTime, _neverExpires, _expireTime);

        ResetRows(_members, ProspectModelMapper.ReadMembers(_document.Prospect));
        ResetRows(_customSettings, ProspectModelMapper.ReadCustomSettings(_document.Prospect));
        ResetRows(_characters, ProspectModelMapper.ReadRecorderRowsByCategory(_document.Prospect, RecorderCategory.Character));
        ResetRows(_missions, ProspectModelMapper.ReadRecorderRowsByCategory(_document.Prospect, RecorderCategory.Mission));
        ResetRows(_prebuilts, ProspectModelMapper.ReadRecorderRowsByCategory(_document.Prospect, RecorderCategory.Prebuilt));
        ResetRows(_allRecorders, ProspectModelMapper.ReadRecorderRows(_document.Prospect, _ => true));
        ResetRows(_mounts, ProspectModelMapper.ReadMountsFromProspect(_document.Prospect));
        _suspendDirtyTracking = false;
        ApplyUnsavedIndicators();
    }

    private void ApplyCurrentFormState()
    {
        if (_document is null)
        {
            return;
        }

        var info = _document.Prospect.ProspectInfo;
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
        _document.Prospect.ProspectInfo = info;

        ProspectModelMapper.ApplyMembers(_document.Prospect, _members.Where(m => !string.IsNullOrWhiteSpace(m.UserID)));
        ProspectModelMapper.ApplyCustomSettings(_document.Prospect, _customSettings.Where(s => !string.IsNullOrWhiteSpace(s.SettingRowName)));
        foreach (var mount in _mounts)
        {
            _mountTalentsOverride.TryGetValue(mount.RecorderIndex, out var talents);
            ProspectModelMapper.ApplyMountFromProspect(_document.Prospect, mount, talents);
        }
    }

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
            error = "Lobby Name is required.";
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

        return MessageBox.Show(message, "Danger warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private string BuildChangeSummary() =>
        $"About to save:\n" +
        $"- Lobby: {_lobbyName.Text}\n" +
        $"- Difficulty: {_difficulty.Text}\n" +
        $"- Prospect state: {_prospectState.Text}\n" +
        $"- Members: {_members.Count}\n" +
        $"- Custom settings: {_customSettings.Count}\n" +
        $"- Character recorders: {_characters.Count}\n" +
        $"- Mission recorders: {_missions.Count}\n" +
        $"- Prebuilt recorders: {_prebuilts.Count}\n" +
        $"- Mount entries: {_mounts.Count}";

    private bool ConfirmDiscardChanges()
    {
        if (!_dirty)
        {
            return true;
        }

        var answer = MessageBox.Show("Discard unsaved changes?", "Unsaved changes", MessageBoxButtons.YesNo);
        return answer == DialogResult.Yes;
    }

    private void MarkDirty()
    {
        if (_suspendDirtyTracking || _document is null)
        {
            return;
        }

        _dirty = true;
        _status.Text = "Unsaved changes.";
        ApplyUnsavedIndicators();
    }

    private void PickClaimedAccountFromKnownPlayers()
    {
        var ids = _members
            .Select(m => m.UserID)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0)
        {
            MessageBox.Show("No player IDs are available in this prospect yet.", "Player wizard");
            return;
        }

        using var picker = new Form
        {
            Text = "Select Claimed Account ID",
            Width = 520,
            Height = 420,
            StartPosition = FormStartPosition.CenterParent
        };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
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
        var apply = new Button { Text = "Use Selected", DialogResult = DialogResult.OK, AutoSize = true };
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
            return;
        }

        _claimedAccountId.Text = selected;
        AppLogService.UserAction($"Wizard pick player: set claimed account ID to {selected}.");
        MarkDirty();
    }

    private void InitializeTheme()
    {
        var theme = ThemePreferenceService.LoadTheme();
        _isDarkTheme = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);
        _themeCombo.SelectedItem = _isDarkTheme ? "Dark" : "Light";
        UiThemeService.ApplyTheme(this, _isDarkTheme);
        ApplyUnsavedIndicators();
    }

    private void OnThemeChanged()
    {
        var selected = (_themeCombo.SelectedItem as string) ?? "Dark";
        _isDarkTheme = string.Equals(selected, "Dark", StringComparison.OrdinalIgnoreCase);
        ThemePreferenceService.SaveTheme(selected);
        UiThemeService.ApplyTheme(this, _isDarkTheme);
        ApplyUnsavedIndicators();
    }

    private void CaptureBaselines()
    {
        _metadataBaseline = MetadataSnapshot.Capture(this);
        _membersBaseline.Clear();
        foreach (var row in _members)
        {
            _membersBaseline[MemberRowKey(row)] = JsonConvert.SerializeObject(row);
        }

        _customSettingsBaseline.Clear();
        foreach (var row in _customSettings)
        {
            _customSettingsBaseline[CustomSettingRowKey(row)] = JsonConvert.SerializeObject(row);
        }

        _mountsBaseline.Clear();
        foreach (var row in _mounts)
        {
            _mountsBaseline[MountRowKey(row)] = JsonConvert.SerializeObject(row);
        }
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

        HighlightGridRows(_membersGrid, _members.Cast<MemberRow>().ToList(), _membersBaseline, MemberRowKey);
        HighlightGridRows(_customSettingsGrid, _customSettings.Cast<CustomSettingRow>().ToList(), _customSettingsBaseline, CustomSettingRowKey);
        HighlightGridRows(_mountsGrid, _mounts.Cast<MountRow>().ToList(), _mountsBaseline, MountRowKey);
        _dirty = DetectUnsavedChanges();
        RefreshStatusLine();
    }

    private void RefreshStatusLine()
    {
        _status.Text = _dirty
            ? "Unsaved changes."
            : _document is null
                ? "Ready — open a prospect."
                : "Loaded.";
    }

    private void RevertAllUnsavedChanges()
    {
        if (_document is null)
        {
            return;
        }

        if (!_dirty)
        {
            MessageBox.Show("No unsaved changes to revert.");
            return;
        }

        var confirm = MessageBox.Show(
            "Revert all unsaved changes and reload from disk?",
            "Revert all",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        _document = ProspectLoadService.Load(_document.ProspectPath);
        _mountTalentsOverride.Clear();
        _dangerNotes.Clear();
        _inspectorEdits.Clear();
        RefreshTabs();
        _dirty = false;
        CaptureBaselines();
        ApplyUnsavedIndicators();
        _status.Text = "Unsaved changes reverted.";
        AppLogService.UserAction($"Revert all: reloaded from disk {_document.ProspectPath}");
    }

    private void RevertSelectedRows(string title, DataGridView grid)
    {
        if (grid.SelectedRows.Count == 0)
        {
            MessageBox.Show("Select one or more rows to revert.");
            return;
        }

        _suspendDirtyTracking = true;
        try
        {
            switch (title)
            {
                case "Members":
                    RevertSelectedRowsInternal(_members, _membersBaseline, MemberRowKey, grid);
                    break;
                case "Custom Settings":
                    RevertSelectedRowsInternal(_customSettings, _customSettingsBaseline, CustomSettingRowKey, grid);
                    break;
            }
        }
        finally
        {
            _suspendDirtyTracking = false;
        }

        AppLogService.UserAction($"Revert selected rows in {title} ({grid.SelectedRows.Count} row(s)).");
        ApplyUnsavedIndicators();
    }

    private void RevertSelectedRowsInternal<T>(
        BindingList<T> source,
        IDictionary<string, string> baseline,
        Func<T, string> keySelector,
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
            var key = keySelector(item);
            if (baseline.TryGetValue(key, out var snapshot))
            {
                var restored = JsonConvert.DeserializeObject<T>(snapshot);
                if (restored is not null && index >= 0 && index < source.Count)
                {
                    source[index] = restored;
                }
            }
            else if (index >= 0 && index < source.Count)
            {
                source.RemoveAt(index);
            }
        }
    }

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
        IDictionary<string, string> baseline,
        Func<T, string> keySelector) where T : class
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
            var key = keySelector(rows[i]);
            var snapshot = JsonConvert.SerializeObject(rows[i]);
            var changed = !baseline.TryGetValue(key, out var baseSnapshot) || !string.Equals(snapshot, baseSnapshot, StringComparison.Ordinal);
            grid.Rows[i].DefaultCellStyle.BackColor = changed ? changedBack : defaultBack;
            grid.Rows[i].DefaultCellStyle.ForeColor = changed ? changedFore : defaultFore;
        }
    }

    private bool DetectUnsavedChanges()
    {
        var metadataChanged =
            _lobbyName.Text.Trim() != _metadataBaseline.LobbyName ||
            _prospectId.Text.Trim() != _metadataBaseline.ProspectId ||
            _difficulty.Text.Trim() != _metadataBaseline.Difficulty ||
            (int)_dropPoint.Value != _metadataBaseline.DropPoint ||
            _insurance.Checked != _metadataBaseline.Insurance ||
            _noRespawns.Checked != _metadataBaseline.NoRespawns ||
            _claimedAccountId.Text.Trim() != _metadataBaseline.ClaimedAccountId ||
            (int)_claimedCharacterSlot.Value != _metadataBaseline.ClaimedCharacterSlot ||
            _prospectState.Text.Trim() != _metadataBaseline.ProspectState ||
            (int)_elapsedTime.Value != _metadataBaseline.ElapsedTime ||
            (int)_cost.Value != _metadataBaseline.Cost ||
            (int)_reward.Value != _metadataBaseline.Reward ||
            _prospectDtKey.Text.Trim() != _metadataBaseline.ProspectDtKey ||
            _factionMissionDtKey.Text.Trim() != _metadataBaseline.FactionMissionDtKey ||
            ProspectExpireTimeUi.ToPersistedSeconds(_neverExpires.Checked, _expireTime.Value) != _metadataBaseline.ExpirePersisted;

        return metadataChanged ||
               CollectionChanged(_members.Cast<MemberRow>().ToList(), _membersBaseline, MemberRowKey) ||
               CollectionChanged(_customSettings.Cast<CustomSettingRow>().ToList(), _customSettingsBaseline, CustomSettingRowKey) ||
               CollectionChanged(_mounts.Cast<MountRow>().ToList(), _mountsBaseline, MountRowKey);
    }

    private static bool CollectionChanged<T>(IReadOnlyList<T> current, IDictionary<string, string> baseline, Func<T, string> keySelector)
    {
        if (current.Count != baseline.Count)
        {
            return true;
        }

        foreach (var row in current)
        {
            var key = keySelector(row);
            var snapshot = JsonConvert.SerializeObject(row);
            if (!baseline.TryGetValue(key, out var baselineSnapshot) || !string.Equals(snapshot, baselineSnapshot, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string MemberRowKey(MemberRow row) => $"{row.UserID}|{row.ChrSlot}";
    private static string CustomSettingRowKey(CustomSettingRow row) => row.SettingRowName;
    private static string MountRowKey(MountRow row) => row.RecorderIndex.ToString();

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

    private static void ResetRows<T>(BindingList<T> list, IEnumerable<T> values)
    {
        list.Clear();
        foreach (var value in values)
        {
            list.Add(value);
        }
    }
}
