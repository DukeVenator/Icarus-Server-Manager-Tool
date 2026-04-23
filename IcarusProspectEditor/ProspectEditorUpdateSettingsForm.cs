using IcarusProspectEditor.Services;

namespace IcarusProspectEditor;

internal sealed class ProspectEditorUpdateSettingsForm : Form
{
    private readonly CheckBox _enabled = new() { Text = "Check for updates automatically", AutoSize = true };
    private readonly NumericUpDown _intervalHours = new() { Minimum = 1, Maximum = 168, Width = 80 };
    private readonly CheckBox _includePrerelease = new() { Text = "Include GitHub prereleases", AutoSize = true };
    private readonly CheckBox _prompt = new() { Text = "Prompt before downloading update", AutoSize = true };

    public ProspectEditorUpdateSettings Settings { get; private set; }

    public ProspectEditorUpdateSettingsForm(ProspectEditorUpdateSettings settings)
    {
        Settings = settings;
        Text = "Prospect Editor — update settings";
        Width = 420;
        Height = 260;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 6 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _enabled.Checked = settings.UpdateCheckEnabled;
        _intervalHours.Value = settings.UpdateCheckIntervalHours;
        _includePrerelease.Checked = settings.UpdateIncludePrerelease;
        _prompt.Checked = settings.UpdatePromptBeforeDownload;

        var row = 0;
        root.Controls.Add(_enabled, 0, row);
        root.SetColumnSpan(_enabled, 2);
        row++;
        root.Controls.Add(new Label { Text = "Interval (hours):", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        root.Controls.Add(_intervalHours, 1, row++);
        root.Controls.Add(_includePrerelease, 0, row);
        root.SetColumnSpan(_includePrerelease, 2);
        row++;
        root.Controls.Add(_prompt, 0, row);
        root.SetColumnSpan(_prompt, 2);
        row++;

        var info = new Label
        {
            AutoSize = true,
            Text = "Releases use tags like editor-v1.0.0 with a zip asset containing IcarusProspectEditor and win in the file name."
        };
        root.Controls.Add(info, 0, row);
        root.SetColumnSpan(info, 2);
        row++;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) =>
        {
            Settings = new ProspectEditorUpdateSettings
            {
                UpdateCheckEnabled = _enabled.Checked,
                UpdateCheckIntervalHours = (int)_intervalHours.Value,
                UpdateIncludePrerelease = _includePrerelease.Checked,
                UpdatePromptBeforeDownload = _prompt.Checked
            };
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, row);
        root.SetColumnSpan(buttons, 2);

        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public void ApplyTheme(bool dark)
    {
        UiThemeService.ApplyTheme(this, dark);
    }
}
