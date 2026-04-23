using System.ComponentModel;
using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;

namespace IcarusProspectEditor;

/// <summary>
/// Quick grid + nested wizard for <see cref="RecorderFieldRow"/> editing (shared by mount advanced tab and <see cref="RecorderInspectorForm"/>).
/// </summary>
internal sealed class RecorderFieldsEditorTabs : UserControl
{
    private readonly IReadOnlyList<MemberRow> _knownPlayers;
    private readonly BindingList<RecorderFieldRow> _rows;
    private readonly List<RecorderFieldRow> _wizardRows;
    private int _wizardIndex;
    private Label? _wizardPath;
    private Label? _wizardType;
    private TextBox? _wizardValue;
    private ComboBox? _wizardPlayerPicker;
    private Button? _wizardBack;
    private Button? _wizardNext;

    public RecorderFieldsEditorTabs(BindingList<RecorderFieldRow> rows, IEnumerable<MemberRow> knownPlayers)
    {
        _rows = rows;
        _knownPlayers = knownPlayers.ToList();
        _wizardRows = _rows.Where(r => r.Editable).ToList();
        _wizardIndex = 0;
        Dock = DockStyle.Fill;
        BuildLayout();
    }

    public void ApplyTheme(bool dark)
    {
        UiThemeService.ApplyTheme(this, dark);
    }

    private void BuildLayout()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildQuickEditPage());
        tabs.TabPages.Add(BuildWizardPage());
        Controls.Add(tabs);
    }

    private TabPage BuildQuickEditPage()
    {
        var page = new TabPage("Quick Grid");
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            DataSource = _rows,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(RecorderFieldRow.Path),
            HeaderText = "Path",
            Width = 450,
            ReadOnly = true
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(RecorderFieldRow.PropertyType),
            HeaderText = "Type",
            Width = 180,
            ReadOnly = true
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(RecorderFieldRow.Value),
            HeaderText = "Value",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(RecorderFieldRow.Editable),
            HeaderText = "Editable",
            Width = 80,
            ReadOnly = true
        });

        grid.CellBeginEdit += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _rows.Count)
            {
                return;
            }

            if (!_rows[e.RowIndex].Editable && e.ColumnIndex == 2)
            {
                e.Cancel = true;
            }
            else if (e.ColumnIndex == 2 && IsPlayerLinkField(_rows[e.RowIndex]))
            {
                e.Cancel = true;
                MessageBox.Show(
                    "Player-linked fields are protected. Use the Nested Wizard tab and pick a player from the loaded prospect.",
                    "Protected field",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        };

        page.Controls.Add(grid);
        return page;
    }

    private TabPage BuildWizardPage()
    {
        var page = new TabPage("Nested Wizard");
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(12) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _wizardPath = new Label { AutoSize = true };
        _wizardType = new Label { AutoSize = true };
        _wizardValue = new TextBox { Dock = DockStyle.Fill };
        _wizardPlayerPicker = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Visible = false };
        _wizardBack = new Button { Text = "Back", AutoSize = true };
        _wizardNext = new Button { Text = "Next", AutoSize = true };
        var applyCurrent = new Button { Text = "Apply Current", AutoSize = true };

        panel.Controls.Add(new Label { Text = "Path", AutoSize = true }, 0, 0);
        panel.Controls.Add(_wizardPath, 1, 0);
        panel.Controls.Add(new Label { Text = "Type", AutoSize = true }, 0, 1);
        panel.Controls.Add(_wizardType, 1, 1);
        panel.Controls.Add(new Label { Text = "Value", AutoSize = true }, 0, 2);
        panel.Controls.Add(_wizardValue, 1, 2);
        panel.Controls.Add(new Label { Text = "Player Picker", AutoSize = true }, 0, 3);
        panel.Controls.Add(_wizardPlayerPicker, 1, 3);

        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        nav.Controls.Add(_wizardBack);
        nav.Controls.Add(_wizardNext);
        nav.Controls.Add(applyCurrent);
        panel.Controls.Add(nav, 1, 4);

        _wizardBack.Click += (_, _) => NavigateWizard(-1);
        _wizardNext.Click += (_, _) => NavigateWizard(1);
        applyCurrent.Click += (_, _) => ApplyWizardValue();
        _wizardValue.TextChanged += (_, _) => ApplyWizardValue();
        _wizardPlayerPicker.SelectedIndexChanged += (_, _) => ApplyPlayerPickerValue();

        page.Controls.Add(panel);
        RefreshWizard();
        return page;
    }

    private void NavigateWizard(int delta)
    {
        if (_wizardRows.Count == 0)
        {
            return;
        }

        ApplyWizardValue();
        _wizardIndex = Math.Clamp(_wizardIndex + delta, 0, _wizardRows.Count - 1);
        RefreshWizard();
    }

    private void ApplyWizardValue()
    {
        if (_wizardRows.Count == 0 || _wizardValue is null)
        {
            return;
        }

        _wizardRows[_wizardIndex].Value = _wizardValue.Text;
    }

    private void RefreshWizard()
    {
        if (_wizardPath is null || _wizardType is null || _wizardValue is null || _wizardBack is null || _wizardNext is null)
        {
            return;
        }

        if (_wizardRows.Count == 0)
        {
            _wizardPath.Text = "No editable fields";
            _wizardType.Text = "-";
            _wizardValue.Text = string.Empty;
            _wizardValue.Enabled = false;
            _wizardBack.Enabled = false;
            _wizardNext.Enabled = false;
            return;
        }

        var row = _wizardRows[_wizardIndex];
        _wizardPath.Text = $"{row.Path} ({_wizardIndex + 1}/{_wizardRows.Count})";
        _wizardType.Text = row.PropertyType;
        _wizardValue.Text = row.Value;
        ConfigurePlayerPicker(row);
        _wizardBack.Enabled = _wizardIndex > 0;
        _wizardNext.Enabled = _wizardIndex < _wizardRows.Count - 1;
    }

    private void ConfigurePlayerPicker(RecorderFieldRow row)
    {
        if (_wizardValue is null || _wizardPlayerPicker is null)
        {
            return;
        }

        if (!IsPlayerLinkField(row))
        {
            _wizardValue.Enabled = true;
            _wizardPlayerPicker.Visible = false;
            return;
        }

        _wizardPlayerPicker.Visible = true;
        _wizardValue.Enabled = false;
        _wizardPlayerPicker.Items.Clear();

        var candidates = BuildPlayerCandidates(row.Path);
        foreach (var candidate in candidates)
        {
            _wizardPlayerPicker.Items.Add(candidate);
        }

        var selectedIndex = -1;
        for (var i = 0; i < _wizardPlayerPicker.Items.Count; i++)
        {
            if (string.Equals(_wizardPlayerPicker.Items[i]?.ToString(), row.Value, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                break;
            }
        }

        if (selectedIndex < 0 && _wizardPlayerPicker.Items.Count > 0)
        {
            selectedIndex = 0;
        }

        if (selectedIndex >= 0)
        {
            _wizardPlayerPicker.SelectedIndex = selectedIndex;
        }
    }

    private void ApplyPlayerPickerValue()
    {
        if (_wizardRows.Count == 0 || _wizardPlayerPicker is null || !_wizardPlayerPicker.Visible)
        {
            return;
        }

        var value = _wizardPlayerPicker.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _wizardRows[_wizardIndex].Value = value;
    }

    private List<string> BuildPlayerCandidates(string path)
    {
        var isName = path.Contains("name", StringComparison.OrdinalIgnoreCase);
        var values = isName
            ? _knownPlayers.SelectMany(p => new[] { p.AccountName, p.CharacterName })
            : _knownPlayers.Select(p => p.UserID);

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsPlayerLinkField(RecorderFieldRow row)
    {
        var path = row.Path;
        return path.Contains("Steam", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("UserID", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("AccountID", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("PlayerID", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("AssignedPlayer", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("OwnerID", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("CharacterName", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("AccountName", StringComparison.OrdinalIgnoreCase);
    }
}
