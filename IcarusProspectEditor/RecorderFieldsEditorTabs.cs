using System.ComponentModel;
using System.Globalization;
using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using UeSaveGame.PropertyTypes;

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
    private Label? _wizardValidation;
    private TextBox? _wizardValue;
    private ComboBox? _wizardPlayerPicker;
    private Button? _wizardBack;
    private Button? _wizardNext;
    private readonly Dictionary<string, string> _gridBeforeValues = new(StringComparer.Ordinal);
    private bool _suppressFieldWrites;
    private bool _wizardPlayerPickerActive;
    private string? _wizardLoadedValue;

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

            var key = $"{e.RowIndex}:{e.ColumnIndex}";
            var before = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? string.Empty;
            _gridBeforeValues[key] = before;

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
        grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _rows.Count || e.ColumnIndex < 0 || e.ColumnIndex >= grid.Columns.Count)
            {
                return;
            }

            var key = $"{e.RowIndex}:{e.ColumnIndex}";
            var before = _gridBeforeValues.TryGetValue(key, out var prior) ? prior : "<unknown>";
            var after = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? string.Empty;
            var row = _rows[e.RowIndex];
            var column = grid.Columns[e.ColumnIndex].DataPropertyName;
            if (string.IsNullOrWhiteSpace(column))
            {
                column = grid.Columns[e.ColumnIndex].Name;
            }

            AppLogService.UserAction($"Recorder field edited: {row.Path} [{column}] '{before}' -> '{after}'.");
            _gridBeforeValues.Remove(key);
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
        _wizardValidation = new Label { AutoSize = true, ForeColor = Color.OrangeRed };
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
        panel.Controls.Add(_wizardValidation, 1, 5);

        _wizardBack.Click += (_, _) => NavigateWizard(-1);
        _wizardNext.Click += (_, _) => NavigateWizard(1);
        applyCurrent.Click += (_, _) =>
        {
            // Explicit commit: report invalid input instead of silently discarding it.
            var result = CommitCurrentValue();
            if (!result.Succeeded)
            {
                MessageBox.Show(this, result.Describe(), "Invalid field value", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        _wizardValue.TextChanged += (_, _) =>
        {
            if (!_suppressFieldWrites)
            {
                // Live-draft only valid values; the inline label reports a rejected value and the
                // stored row keeps its last valid value.
                CommitCurrentValue();
            }
        };
        _wizardPlayerPicker.SelectedIndexChanged += (_, _) =>
        {
            if (!_suppressFieldWrites)
            {
                CommitCurrentValue();
            }
        };

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

        var commit = CommitCurrentValue();
        if (!commit.Succeeded)
        {
            AppLogService.UserAction($"Recorder wizard navigation blocked by invalid value: {_wizardRows[_wizardIndex].Path}.");
            MessageBox.Show(this, commit.Describe(), "Invalid field value", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _wizardIndex = Math.Clamp(_wizardIndex + delta, 0, _wizardRows.Count - 1);
        var row = _wizardRows[_wizardIndex];
        AppLogService.UserAction($"Recorder wizard navigate: index={_wizardIndex + 1}/{_wizardRows.Count} path={row.Path}");
        RefreshWizard();
    }

    /// <summary>
    /// Commits the value the user can currently see, and reports a validation failure instead of
    /// swallowing it. When the player picker is showing it is the single authority; the disabled
    /// textbox may still hold a stale pre-picker value. An unchanged value is never revalidated, so
    /// browsing a recorder that already stores an unusual value cannot be blocked.
    /// </summary>
    public RecorderEditResult CommitCurrentValue()
    {
        if (_wizardRows.Count == 0 || _wizardValue is null || _suppressFieldWrites)
        {
            return RecorderEditResult.Unchanged;
        }

        _wizardIndex = Math.Clamp(_wizardIndex, 0, _wizardRows.Count - 1);
        var row = _wizardRows[_wizardIndex];
        var before = row.Value ?? string.Empty;

        if (_wizardLoadedValue is not null && !string.Equals(before, _wizardLoadedValue, StringComparison.Ordinal))
        {
            // The bound row changed outside the wizard (for example in the Quick Grid). Reload the
            // wizard rather than writing its stale text over the newer value.
            RefreshWizard();
            SetWizardValidation($"{row.Path} changed outside this tab; the stored value was reloaded.");
            return RecorderEditResult.Unchanged;
        }

        string after;
        if (_wizardPlayerPickerActive && _wizardPlayerPicker is not null)
        {
            var selected = _wizardPlayerPicker.SelectedItem?.ToString();
            // Keep the stored spelling when the selection only differs by case, so browsing the
            // picker cannot rewrite an unknown-but-equivalent stored value.
            after = string.IsNullOrWhiteSpace(selected) ||
                    string.Equals(selected, before, StringComparison.OrdinalIgnoreCase)
                ? before
                : selected;
        }
        else
        {
            after = _wizardValue.Text ?? string.Empty;
        }

        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            SetWizardValidation(null);
            return RecorderEditResult.Unchanged;
        }

        var error = ValidateValue(row, after);
        if (error.Length > 0)
        {
            var invalid = RecorderEditResult.Invalid(new[] { new RecorderFieldError(row.Path, error) });
            SetWizardValidation(invalid.Describe());
            return invalid;
        }

        row.Value = after;
        _wizardLoadedValue = after;
        SetWizardValidation(null);
        AppLogService.UserAction($"Recorder wizard apply: {row.Path} '{before}' -> '{after}'.");
        return RecorderEditResult.Applied(1);
    }

    private static string ValidateValue(RecorderFieldRow row, string value)
    {
        switch (row.PropertyType)
        {
            case nameof(BoolProperty):
                return bool.TryParse(value, out _) ? string.Empty : "Expected true or false.";
            case nameof(IntProperty):
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    ? string.Empty
                    : "Expected a whole number between -2,147,483,648 and 2,147,483,647.";
            case nameof(Int64Property):
                return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    ? string.Empty
                    : "Expected a whole 64-bit number.";
            case nameof(UInt32Property):
                return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    ? string.Empty
                    : "Expected a whole number between 0 and 4,294,967,295.";
            case nameof(UInt64Property):
                return ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    ? string.Empty
                    : "Expected a whole number between 0 and 18,446,744,073,709,551,615.";
            case nameof(FloatProperty):
                return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                    ? string.Empty
                    : "Expected a number.";
            case nameof(DoubleProperty):
                return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                    ? string.Empty
                    : "Expected a number.";
        }

        return string.Empty;
    }

    private void SetWizardValidation(string? message)
    {
        if (_wizardValidation is null)
        {
            return;
        }

        _wizardValidation.Text = message ?? string.Empty;
    }

    private void RefreshWizard()
    {
        if (_wizardPath is null || _wizardType is null || _wizardValue is null || _wizardBack is null || _wizardNext is null)
        {
            return;
        }

        _suppressFieldWrites = true;
        try
        {
            SetWizardValidation(null);
            if (_wizardRows.Count == 0)
            {
                _wizardPath.Text = "No editable fields";
                _wizardType.Text = "-";
                _wizardValue.Text = string.Empty;
                _wizardValue.Enabled = false;
                _wizardBack.Enabled = false;
                _wizardNext.Enabled = false;
                _wizardLoadedValue = null;
                return;
            }

            _wizardIndex = Math.Clamp(_wizardIndex, 0, _wizardRows.Count - 1);
            var row = _wizardRows[_wizardIndex];
            _wizardPath.Text = $"{row.Path} ({_wizardIndex + 1}/{_wizardRows.Count})";
            _wizardType.Text = row.PropertyType;
            _wizardValue.Text = row.Value;
            _wizardLoadedValue = row.Value ?? string.Empty;
            ConfigurePlayerPicker(row);
            _wizardBack.Enabled = _wizardIndex > 0;
            _wizardNext.Enabled = _wizardIndex < _wizardRows.Count - 1;
        }
        finally
        {
            _suppressFieldWrites = false;
        }
    }

    private void ConfigurePlayerPicker(RecorderFieldRow row)
    {
        if (_wizardValue is null || _wizardPlayerPicker is null)
        {
            return;
        }

        if (!IsPlayerLinkField(row))
        {
            _wizardPlayerPickerActive = false;
            _wizardValue.Enabled = true;
            _wizardPlayerPicker.Visible = false;
            return;
        }

        _wizardPlayerPickerActive = true;
        _wizardPlayerPicker.Visible = true;
        _wizardValue.Enabled = false;
        _wizardPlayerPicker.Items.Clear();

        var candidates = BuildPlayerCandidates(row.Path);
        var storedValue = row.Value?.Trim() ?? string.Empty;
        var storedIsKnown = candidates.Any(c => string.Equals(c, storedValue, StringComparison.OrdinalIgnoreCase));
        if (storedValue.Length > 0 && !storedIsKnown)
        {
            // Preserve an unmatched stored ID/name so opening the wizard cannot reassign it.
            _wizardPlayerPicker.Items.Add(storedValue);
        }

        foreach (var candidate in candidates)
        {
            _wizardPlayerPicker.Items.Add(candidate);
        }

        var selectedIndex = -1;
        for (var i = 0; i < _wizardPlayerPicker.Items.Count; i++)
        {
            if (string.Equals(_wizardPlayerPicker.Items[i]?.ToString(), storedValue, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                break;
            }
        }

        // No automatic first-candidate selection: an unset field stays unset until the user chooses.
        _wizardPlayerPicker.SelectedIndex = selectedIndex;
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
