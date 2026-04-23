using System.ComponentModel;
using System.Windows.Forms.DataVisualization.Charting;
using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;

namespace IcarusProspectEditor;

internal sealed class MountEditorForm : Form
{
    private readonly MountRow _mount;
    private readonly BindingList<TalentRow> _talents;
    private readonly BindingList<RecorderFieldRow> _advancedFields;
    private readonly IReadOnlyList<MemberRow> _knownPlayers;

    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TextBox _type = new() { Dock = DockStyle.Fill };
    private readonly TextBox _lineage = new() { Dock = DockStyle.Fill };
    private readonly TextBox _sex = new() { Dock = DockStyle.Fill };
    private readonly TextBox _ownerPlayerId = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _ownerCharacterSlot = new() { Dock = DockStyle.Fill, Minimum = -1, Maximum = 100 };
    private readonly TextBox _ownerName = new() { Dock = DockStyle.Fill };
    private readonly Button _ownerWizard = new() { Text = "Pick Prospect Player", AutoSize = true };
    private readonly NumericUpDown _level = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 999 };
    private readonly NumericUpDown _xp = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = int.MaxValue };
    private readonly NumericUpDown _health = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = int.MaxValue };
    private readonly ComboBox _species = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _variation = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _swapSpecies = new() { Text = "Swap Species", AutoSize = true };
    private readonly Button _previewSwap = new() { Text = "Preview Swap", AutoSize = true };
    private readonly Label _swapSummary = new() { Dock = DockStyle.Fill, AutoSize = true, Text = "No species swap applied." };
    private readonly Label _validation = new() { Dock = DockStyle.Fill, AutoSize = true };
    private DataGridView? _talentsGrid;
    private Chart? _geneticsChart;
    private SplitContainer? _geneticsSplit;
    private RecorderFieldsEditorTabs? _advancedRecorderEditor;
    private TalentRemapResult? _pendingSwap;
    private readonly TalentIconDiskImageCache _talentIconCache = new();
    private bool _isDarkTheme;
    private readonly Dictionary<string, string> _talentCellBeforeValues = new(StringComparer.Ordinal);

    private readonly Dictionary<string, NumericUpDown> _genetics = new(StringComparer.Ordinal)
    {
        ["Vitality"] = new NumericUpDown { Minimum = 0, Maximum = 10, Dock = DockStyle.Fill },
        ["Endurance"] = new NumericUpDown { Minimum = 0, Maximum = 10, Dock = DockStyle.Fill },
        ["Muscle"] = new NumericUpDown { Minimum = 0, Maximum = 10, Dock = DockStyle.Fill },
        ["Agility"] = new NumericUpDown { Minimum = 0, Maximum = 10, Dock = DockStyle.Fill },
        ["Toughness"] = new NumericUpDown { Minimum = 0, Maximum = 10, Dock = DockStyle.Fill },
        ["Hardiness"] = new NumericUpDown { Minimum = 0, Maximum = 10, Dock = DockStyle.Fill },
        ["Utility"] = new NumericUpDown { Minimum = 0, Maximum = 10, Dock = DockStyle.Fill }
    };

    public MountRow UpdatedMount => _mount;
    public IReadOnlyList<TalentRow> UpdatedTalents => _talents.ToList();
    public IReadOnlyList<RecorderFieldRow> UpdatedAdvancedFields => _advancedFields.ToList();

    public MountEditorForm(
        MountRow mount,
        IEnumerable<TalentRow> talents,
        IEnumerable<RecorderFieldRow> advancedFields,
        IEnumerable<MemberRow> knownPlayers)
    {
        _mount = mount;
        _talents = new BindingList<TalentRow>(talents.ToList());
        _advancedFields = new BindingList<RecorderFieldRow>(advancedFields.ToList());
        _knownPlayers = knownPlayers.ToList();
        Text = $"Edit Mount - {mount.MountName}";
        Width = 980;
        Height = 760;
        StartPosition = FormStartPosition.CenterParent;
        BuildLayout();
        Load += (_, _) =>
        {
            AppLogService.UserAction($"Mount editor opened: '{mount.MountName}' (recorder {mount.RecorderIndex}).");
            BeginInvoke(new Action(ApplyGeneticsSplitterLayout));
        };
        LoadMountData();
    }

    public void ApplyTheme(bool dark)
    {
        _isDarkTheme = dark;
        UiThemeService.ApplyTheme(this, dark);
        if (_geneticsChart is not null)
        {
            _geneticsChart.BackColor = dark ? Color.FromArgb(30, 32, 36) : Color.White;
            _geneticsChart.ChartAreas[0].BackColor = dark ? Color.FromArgb(30, 32, 36) : Color.White;
            _geneticsChart.ChartAreas[0].AxisX.LabelStyle.ForeColor = dark ? Color.Gainsboro : Color.Black;
            _geneticsChart.ChartAreas[0].AxisY.LabelStyle.ForeColor = dark ? Color.Gainsboro : Color.Black;
            _geneticsChart.Series["Genetics"].Color = dark ? Color.Gold : Color.SteelBlue;
            MountGeneticsRadarChartConfigurer.ApplyRadarChartTheme(_geneticsChart, dark);
        }

        _advancedRecorderEditor?.ApplyTheme(dark);
        _talentsGrid?.Invalidate();
        UpdateValidation();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _talentIconCache.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildOverviewTab());
        tabs.TabPages.Add(BuildGeneticsTab());
        tabs.TabPages.Add(BuildTalentsTab());
        tabs.TabPages.Add(BuildAdvancedTab());
        root.Controls.Add(tabs, 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var apply = new Button { Text = "Apply Updates", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        apply.Click += (_, e) =>
        {
            if (!UpdateValidation())
            {
                AppLogService.UserAction($"Mount apply blocked by validation: '{_mount.MountName}' (recorder {_mount.RecorderIndex}).");
                MessageBox.Show("Mount has validation issues. Resolve them before applying changes.", "Validation");
                DialogResult = DialogResult.None;
                return;
            }

            ApplyToModel();
        };
        actions.Controls.Add(apply);
        actions.Controls.Add(cancel);
        root.Controls.Add(actions, 0, 1);
        AcceptButton = apply;
        CancelButton = cancel;
    }

    private TabPage BuildOverviewTab()
    {
        var page = new TabPage("Overview");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddField(grid, 0, "Name", _name);
        AddField(grid, 1, "Type", _type);
        AddField(grid, 2, "Lineage", _lineage);
        AddField(grid, 3, "Sex", _sex);
        AddField(grid, 4, "Owner Player ID", _ownerPlayerId);
        AddField(grid, 5, "Owner Character Slot", _ownerCharacterSlot);
        AddOwnerNameField(grid, 6);
        AddSpeciesField(grid, 7);
        AddField(grid, 8, "Variation", _variation);
        AddField(grid, 9, "Level", _level);
        AddField(grid, 10, "Experience", _xp);
        AddField(grid, 11, "Health", _health);
        AddField(grid, 12, "Swap Result", _swapSummary);
        AddField(grid, 13, "Validation", _validation);

        page.Controls.Add(grid);
        return page;
    }

    private TabPage BuildGeneticsTab()
    {
        var page = new TabPage("Genetics");
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var i = 0;
        foreach (var kvp in _genetics)
        {
            AddField(grid, i++, kvp.Key, kvp.Value);
            kvp.Value.ValueChanged += (_, _) => RefreshGeneticsRadar();
        }

        _geneticsChart = BuildGeneticsChart();
        _geneticsSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        _geneticsSplit.Panel1.Controls.Add(grid);
        _geneticsSplit.Panel2.Controls.Add(_geneticsChart);
        page.Controls.Add(_geneticsSplit);
        return page;
    }

    private void ApplyGeneticsSplitterLayout()
    {
        if (_geneticsSplit is null)
        {
            return;
        }

        if (!MountGeneticsSplitterLayout.TryCompute(_geneticsSplit.ClientSize.Width, _geneticsSplit.SplitterWidth, out var m))
        {
            return;
        }

        _geneticsSplit.SuspendLayout();
        try
        {
            _geneticsSplit.Panel1MinSize = m.Panel1MinSize;
            _geneticsSplit.Panel2MinSize = m.Panel2MinSize;
            _geneticsSplit.SplitterDistance = m.SplitterDistance;
        }
        catch (InvalidOperationException)
        {
            // Rare layout ordering edge case; defaults remain usable.
        }
        finally
        {
            _geneticsSplit.ResumeLayout();
        }
    }

    private TabPage BuildTalentsTab()
    {
        var page = new TabPage("Talents");
        _talentsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            DataSource = _talents,
            AutoGenerateColumns = false,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true
        };
        _talentsGrid.Columns.Add(new DataGridViewImageColumn
        {
            Name = "Icon",
            HeaderText = "Icon",
            Width = 40,
            ImageLayout = DataGridViewImageCellLayout.Zoom,
            ReadOnly = true
        });
        _talentsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(TalentRow.Name),
            HeaderText = "Talent Name",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _talentsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(TalentRow.DisplayName),
            HeaderText = "Display",
            Width = 220
        });
        _talentsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(TalentRow.Rank),
            HeaderText = "Rank",
            Width = 60
        });
        _talentsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(TalentRow.MaxRank),
            HeaderText = "Max",
            Width = 60
        });
        _talentsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(TalentRow.RemapStatus),
            HeaderText = "Status",
            Width = 110
        });
        _talentsGrid.DataBindingComplete += (_, _) => PopulateTalentIconCells();
        _talents.ListChanged += TalentsOnListChanged;
        _talentsGrid.CellBeginEdit += (_, e) => CaptureTalentCellBefore(e.RowIndex, e.ColumnIndex);
        _talentsGrid.CellValueChanged += (_, e) => LogTalentGridCellChange(e.RowIndex, e.ColumnIndex);
        _talentsGrid.UserAddedRow += (_, e) => AppLogService.UserAction($"Mount talent row added: '{_mount.MountName}' row={e.Row?.Index ?? -1}.");
        _talentsGrid.UserDeletedRow += (_, _) => AppLogService.UserAction($"Mount talent row deleted: '{_mount.MountName}'.");
        _talentsGrid.CellValidating += TalentGridOnCellValidating;
        _talentsGrid.RowPrePaint += TalentGridOnRowPrePaint;
        page.Controls.Add(_talentsGrid);
        return page;
    }

    private TabPage BuildAdvancedTab()
    {
        var page = new TabPage("Advanced");
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Text = "Unsafe editor: changing raw recorder fields can corrupt save compatibility.\nOnly edit if you understand the field meaning and keep a backup."
        }, 0, 0);

        _advancedRecorderEditor = new RecorderFieldsEditorTabs(_advancedFields, _knownPlayers) { Dock = DockStyle.Fill };
        panel.Controls.Add(_advancedRecorderEditor, 0, 1);
        page.Controls.Add(panel);
        return page;
    }

    private void LoadMountData()
    {
        _name.Text = _mount.MountName;
        _type.Text = _mount.MountType;
        _lineage.Text = _mount.Lineage;
        _sex.Text = _mount.Sex;
        _ownerPlayerId.Text = _mount.OwnerPlayerId;
        _ownerCharacterSlot.Value = _mount.OwnerCharacterSlot;
        _ownerName.Text = _mount.OwnerName;
        _species.Items.Clear();
        _species.Items.AddRange(MountSpeciesMetadataService.GetSpeciesOptions().Cast<object>().ToArray());
        var normalized = MountSpeciesMetadataService.NormalizeSpecies(_mount.MountType, _mount.MountRace);
        _species.SelectedItem = _species.Items.Cast<object>()
            .Select(x => x.ToString())
            .FirstOrDefault(x => x?.Equals(normalized, StringComparison.OrdinalIgnoreCase) == true)
            ?? _species.Items.Cast<object>().FirstOrDefault();
        RefreshVariationOptions();
        _variation.SelectedItem = _mount.Variation.ToString();
        _level.Value = _mount.Level;
        _xp.Value = _mount.Experience;
        _health.Value = _mount.Health;
        _genetics["Vitality"].Value = _mount.Vitality;
        _genetics["Endurance"].Value = _mount.Endurance;
        _genetics["Muscle"].Value = _mount.Muscle;
        _genetics["Agility"].Value = _mount.Agility;
        _genetics["Toughness"].Value = _mount.Toughness;
        _genetics["Hardiness"].Value = _mount.Hardiness;
        _genetics["Utility"].Value = _mount.Utility;
        RefreshGeneticsRadar();
        _ownerPlayerId.TextChanged += (_, _) => UpdateValidation();
        _level.ValueChanged += (_, _) => UpdateValidation();
        UpdateValidation();
    }

    private void ApplyToModel()
    {
        _mount.MountName = _name.Text.Trim();
        _mount.MountType = _type.Text.Trim();
        _mount.Lineage = _lineage.Text.Trim();
        _mount.Sex = _sex.Text.Trim();
        _mount.OwnerPlayerId = _ownerPlayerId.Text.Trim();
        _mount.OwnerCharacterSlot = (int)_ownerCharacterSlot.Value;
        _mount.OwnerName = _ownerName.Text.Trim();
        _mount.MountRace = _species.Text.Trim();
        _mount.MountType = $"Mount_{_mount.MountRace}";
        _mount.Variation = int.TryParse(_variation.Text, out var variation) ? variation : 0;
        _mount.Level = (int)_level.Value;
        _mount.Experience = MountSpeciesMetadataService.ClampRiskyInt((int)_xp.Value);
        _mount.Health = MountSpeciesMetadataService.ClampRiskyInt((int)_health.Value);
        _mount.Vitality = (int)_genetics["Vitality"].Value;
        _mount.Endurance = (int)_genetics["Endurance"].Value;
        _mount.Muscle = (int)_genetics["Muscle"].Value;
        _mount.Agility = (int)_genetics["Agility"].Value;
        _mount.Toughness = (int)_genetics["Toughness"].Value;
        _mount.Hardiness = (int)_genetics["Hardiness"].Value;
        _mount.Utility = (int)_genetics["Utility"].Value;
    }

    private static void AddField(TableLayoutPanel grid, int row, string label, Control control)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label { Text = label, AutoSize = true }, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private void AddOwnerNameField(TableLayoutPanel grid, int row)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        panel.Controls.Add(_ownerName);
        panel.Controls.Add(_ownerWizard);
        _ownerWizard.Click += (_, _) =>
        {
            AppLogService.UserAction($"Mount owner wizard opened: '{_mount.MountName}' (recorder {_mount.RecorderIndex}).");
            PickOwnerFromProspect();
        };
        AddField(grid, row, "Owner Name", panel);
    }

    private void AddSpeciesField(TableLayoutPanel grid, int row)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        panel.Controls.Add(_species);
        panel.Controls.Add(_previewSwap);
        panel.Controls.Add(_swapSpecies);
        _previewSwap.Click += (_, _) =>
        {
            AppLogService.UserAction($"Mount species swap preview clicked: '{_mount.MountName}' -> {_species.Text.Trim()}.");
            PreviewSpeciesSwap();
        };
        _swapSpecies.Click += (_, _) =>
        {
            AppLogService.UserAction($"Mount species swap clicked: '{_mount.MountName}' -> {_species.Text.Trim()}.");
            ApplySpeciesSwap();
        };
        _species.SelectedIndexChanged += (_, _) =>
        {
            AppLogService.UserAction($"Mount species dropdown changed: '{_mount.MountName}' -> {_species.Text.Trim()}.");
            RefreshVariationOptions();
        };
        AddField(grid, row, "Species", panel);
    }

    private void CaptureTalentCellBefore(int rowIndex, int columnIndex)
    {
        if (_talentsGrid is null || rowIndex < 0 || columnIndex < 0 || rowIndex >= _talentsGrid.Rows.Count || columnIndex >= _talentsGrid.Columns.Count)
        {
            return;
        }

        var key = $"{rowIndex}:{columnIndex}";
        var value = _talentsGrid.Rows[rowIndex].Cells[columnIndex].Value?.ToString() ?? string.Empty;
        _talentCellBeforeValues[key] = value;
    }

    private void LogTalentGridCellChange(int rowIndex, int columnIndex)
    {
        if (_talentsGrid is null || rowIndex < 0 || columnIndex < 0 || rowIndex >= _talentsGrid.Rows.Count || columnIndex >= _talentsGrid.Columns.Count)
        {
            return;
        }

        var key = $"{rowIndex}:{columnIndex}";
        var before = _talentCellBeforeValues.TryGetValue(key, out var prior) ? prior : "<unknown>";
        var after = _talentsGrid.Rows[rowIndex].Cells[columnIndex].Value?.ToString() ?? string.Empty;
        var columnName = _talentsGrid.Columns[columnIndex].DataPropertyName;
        if (string.IsNullOrWhiteSpace(columnName))
        {
            columnName = _talentsGrid.Columns[columnIndex].Name;
        }

        AppLogService.UserAction($"Mount talent edited: '{_mount.MountName}' row={rowIndex} col={columnName} '{before}' -> '{after}'.");
        _talentCellBeforeValues.Remove(key);
    }

    private void RefreshVariationOptions()
    {
        var species = _species.Text.Trim();
        var values = MountSpeciesMetadataService.GetVariationDomain(species);
        _variation.Items.Clear();
        foreach (var value in values)
        {
            _variation.Items.Add(value.ToString());
        }

        if (_variation.Items.Count == 0)
        {
            _variation.Items.Add("0");
        }

        var selected = _mount.Variation.ToString();
        _variation.SelectedItem = _variation.Items.Cast<object>().Select(v => v.ToString()).FirstOrDefault(v => v == selected)
            ?? _variation.Items[0];
        UpdateValidation();
    }

    private void ApplySpeciesSwap()
    {
        var from = MountSpeciesMetadataService.NormalizeSpecies(_mount.MountType, _mount.MountRace);
        var to = _species.Text.Trim();
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            _swapSummary.Text = "Species unchanged.";
            return;
        }

        var remap = _pendingSwap ?? MountSpeciesMetadataService.RemapTalentsForSpecies(_talents, from, to);
        if (remap.DroppedCount > 0)
        {
            var confirm = MessageBox.Show(
                $"This swap removes {remap.DroppedCount} talents and can lose {remap.LostPoints} point(s).\nContinue?",
                "Confirm lossy swap",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
            {
                return;
            }
        }

        _talents.RaiseListChangedEvents = false;
        _talents.Clear();
        foreach (var talent in remap.Talents)
        {
            _talents.Add(talent);
        }
        _talents.RaiseListChangedEvents = true;
        _talents.ResetBindings();
        _mount.MountRace = to;
        _mount.MountType = $"Mount_{to}";
        _swapSummary.Text = $"{remap.RemappedCount} renamed, {remap.AddedCount} added, {remap.DroppedCount} dropped, {remap.UnchangedCount} unchanged, {remap.LostPoints} points lost";
        _pendingSwap = null;
        AppLogService.UserAction($"Mount species swap applied: '{_mount.MountName}' {from} -> {to} (recorder {_mount.RecorderIndex}).");

        if (_talentsGrid is not null)
        {
            _talentsGrid.ClearSelection();
            for (var i = 0; i < _talents.Count; i++)
            {
                if (_talents[i].Name.Contains(to, StringComparison.OrdinalIgnoreCase))
                {
                    _talentsGrid.Rows[i].Selected = true;
                }
            }
        }

        UpdateValidation();
    }

    private void PreviewSpeciesSwap()
    {
        var from = MountSpeciesMetadataService.NormalizeSpecies(_mount.MountType, _mount.MountRace);
        var to = _species.Text.Trim();
        _pendingSwap = MountSpeciesMetadataService.RemapTalentsForSpecies(_talents, from, to);
        _swapSummary.Text = $"Preview -> renamed: {_pendingSwap.RemappedCount}, added: {_pendingSwap.AddedCount}, dropped: {_pendingSwap.DroppedCount}, lost points: {_pendingSwap.LostPoints}";
        AppLogService.UserAction($"Mount species swap preview: '{_mount.MountName}' {from} -> {to}.");
    }

    private bool UpdateValidation()
    {
        var probe = new MountRow
        {
            MountRace = _species.Text.Trim(),
            Variation = int.TryParse(_variation.Text, out var variation) ? variation : 0,
            Level = (int)_level.Value,
            OwnerPlayerId = _ownerPlayerId.Text.Trim()
        };
        var issues = MountSpeciesMetadataService.ValidateMount(probe);
        _validation.Text = issues.Count == 0 ? "Valid" : string.Join(" | ", issues);
        _validation.ForeColor = issues.Count == 0
            ? ThemeHighlightColors.ValidationOk(_isDarkTheme)
            : Color.OrangeRed;
        return issues.Count == 0;
    }

    private Chart BuildGeneticsChart()
    {
        var chart = new Chart { Dock = DockStyle.Fill, MinimumSize = new Size(360, 360) };
        var area = new ChartArea("Genetics");
        MountGeneticsRadarChartConfigurer.Apply(area);
        chart.ChartAreas.Add(area);
        var series = new Series("Genetics")
        {
            ChartType = SeriesChartType.Radar,
            BorderWidth = 2
        };
        chart.Series.Add(series);
        chart.Legends.Clear();
        return chart;
    }

    private void RefreshGeneticsRadar()
    {
        if (_geneticsChart is null)
        {
            return;
        }

        var series = _geneticsChart.Series["Genetics"];
        series.Points.Clear();
        foreach (var pair in _genetics)
        {
            var v = MountGeneticsRadarChartConfigurer.ClampGeneValue((int)pair.Value.Value);
            series.Points.AddXY(pair.Key, v);
        }
    }

    private void TalentsOnListChanged(object? sender, ListChangedEventArgs e)
    {
        switch (e.ListChangedType)
        {
            case ListChangedType.Reset:
            case ListChangedType.ItemAdded:
            case ListChangedType.ItemDeleted:
                PopulateTalentIconCells();
                break;
        }
    }

    private void PopulateTalentIconCells()
    {
        if (_talentsGrid is null)
        {
            return;
        }

        foreach (DataGridViewRow row in _talentsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            if (row.DataBoundItem is not TalentRow tr)
            {
                row.Cells[0].Value = null;
                continue;
            }

            var path = TalentIconBundleService.ResolveIconPath(tr.IconKey);
            row.Cells[0].Value = ResolveTalentGridIcon(path);
        }
    }

    private Image? ResolveTalentGridIcon(string resolvedPath)
    {
        if (File.Exists(resolvedPath))
        {
            var primary = _talentIconCache.GetOrLoad(resolvedPath);
            if (primary is not null)
            {
                return primary;
            }
        }

        var fallbackPath = TalentIconBundleService.ResolveIconPath(string.Empty);
        if (File.Exists(fallbackPath))
        {
            return _talentIconCache.GetOrLoad(fallbackPath);
        }

        return null;
    }

    private void TalentGridOnCellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_talentsGrid is null || e.RowIndex < 0 || _talentsGrid.Rows[e.RowIndex].DataBoundItem is not TalentRow row)
        {
            return;
        }

        if (_talentsGrid.Columns[e.ColumnIndex].DataPropertyName == nameof(TalentRow.Rank) &&
            int.TryParse(e.FormattedValue?.ToString(), out var rank))
        {
            if (rank < 0 || rank > row.MaxRank)
            {
                e.Cancel = true;
                MessageBox.Show($"Rank must be between 0 and {row.MaxRank}.", "Invalid talent rank");
            }
        }
    }

    private void TalentGridOnRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (_talentsGrid is null || e.RowIndex < 0 || _talentsGrid.Rows[e.RowIndex].DataBoundItem is not TalentRow row)
        {
            return;
        }

        var dark = _isDarkTheme;
        var back = row.RemapStatus switch
        {
            "Added" => ThemeHighlightColors.RemapRowAdded(dark),
            "Removed" => ThemeHighlightColors.RemapRowRemoved(dark),
            "Renamed" => ThemeHighlightColors.RemapRowRenamed(dark),
            "RankAdjusted" => ThemeHighlightColors.RemapRowRankAdjusted(dark),
            _ => _talentsGrid.DefaultCellStyle.BackColor
        };
        var style = _talentsGrid.Rows[e.RowIndex].DefaultCellStyle;
        style.BackColor = back;
        if (row.RemapStatus is "Added" or "Removed" or "Renamed" or "RankAdjusted")
        {
            style.ForeColor = ThemeHighlightColors.RemapRowFore(dark);
        }
        else
        {
            style.ForeColor = dark ? Color.FromArgb(230, 230, 235) : Color.FromArgb(28, 30, 33);
        }
    }

    private void PickOwnerFromProspect()
    {
        if (_knownPlayers.Count == 0)
        {
            MessageBox.Show("No known players are available in this prospect.", "Owner picker");
            return;
        }

        using var picker = new Form
        {
            Text = "Select Mount Owner",
            Width = 620,
            Height = 430,
            StartPosition = FormStartPosition.CenterParent
        };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var list = new ListBox { Dock = DockStyle.Fill };
        var items = _knownPlayers
            .Select(p => $"{p.CharacterName} | {p.AccountName} | {p.UserID} | Slot {p.ChrSlot}")
            .ToList();
        list.Items.AddRange(items.Cast<object>().ToArray());

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var apply = new Button { Text = "Use Selected", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        actions.Controls.Add(apply);
        actions.Controls.Add(cancel);
        root.Controls.Add(list, 0, 0);
        root.Controls.Add(actions, 0, 1);
        picker.Controls.Add(root);
        picker.AcceptButton = apply;
        picker.CancelButton = cancel;

        if (picker.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0)
        {
            return;
        }

        var selected = _knownPlayers[list.SelectedIndex];
        _ownerPlayerId.Text = selected.UserID;
        _ownerCharacterSlot.Value = Math.Clamp(selected.ChrSlot, (int)_ownerCharacterSlot.Minimum, (int)_ownerCharacterSlot.Maximum);
        _ownerName.Text = string.IsNullOrWhiteSpace(selected.CharacterName) ? selected.AccountName : selected.CharacterName;
    }
}
