using IcarusProspectEditor.Services;

namespace IcarusProspectEditor;

internal sealed class FreshProspectLoadWizardForm : Form
{
    private readonly TextBox _pathBox = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly Label _validation = new() { Dock = DockStyle.Fill, AutoSize = true, Text = "Pick a prospect JSON file to continue." };
    private readonly Button _continueButton = new() { Text = "Load Prospect", AutoSize = true, Enabled = false };

    public string SelectedPath { get; private set; } = string.Empty;

    public FreshProspectLoadWizardForm()
    {
        Text = "Load Prospect Wizard";
        Width = 760;
        Height = 330;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var intro = new Label
        {
            AutoSize = true,
            Text = "Welcome. This wizard helps you open a fresh Icarus prospect save."
        };
        var guidance = new Label
        {
            AutoSize = true,
            Text = "Steps:\n1) Click Browse\n2) Pick your encoded prospect .json save\n3) Click Load Prospect"
        };
        var pickerRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        pickerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pickerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var browse = new Button { Text = "Browse...", AutoSize = true };
        browse.Click += (_, _) => BrowseForProspect();
        pickerRow.Controls.Add(_pathBox, 0, 0);
        pickerRow.Controls.Add(browse, 1, 0);

        _validation.ForeColor = Color.FromArgb(220, 140, 60);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        _continueButton.DialogResult = DialogResult.OK;
        buttons.Controls.Add(_continueButton);
        buttons.Controls.Add(cancel);

        root.Controls.Add(intro, 0, 0);
        root.Controls.Add(guidance, 0, 1);
        root.Controls.Add(pickerRow, 0, 2);
        root.Controls.Add(_validation, 0, 3);
        root.Controls.Add(buttons, 0, 4);
        Controls.Add(root);

        AcceptButton = _continueButton;
        CancelButton = cancel;

        _continueButton.Click += (_, _) =>
        {
            SelectedPath = _pathBox.Text.Trim();
            AppLogService.UserAction($"Fresh load wizard accepted: {SelectedPath}");
        };
        cancel.Click += (_, _) => AppLogService.UserAction("Fresh load wizard cancelled.");
        Load += (_, _) => AppLogService.UserAction("Fresh load wizard opened.");
    }

    public void ApplyTheme(bool dark) => UiThemeService.ApplyTheme(this, dark);

    private void BrowseForProspect()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Icarus Prospect (*.json)|*.json|All files (*.*)|*.*",
            Title = "Select prospect JSON"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            AppLogService.UserAction("Fresh load wizard browse cancelled.");
            return;
        }

        _pathBox.Text = dialog.FileName;
        ValidateSelection();
        AppLogService.UserAction($"Fresh load wizard picked: {dialog.FileName}");
    }

    private void ValidateSelection()
    {
        var path = _pathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            _validation.Text = "Pick a prospect JSON file to continue.";
            _validation.ForeColor = Color.FromArgb(220, 140, 60);
            _continueButton.Enabled = false;
            return;
        }

        if (!File.Exists(path))
        {
            _validation.Text = "That file does not exist.";
            _validation.ForeColor = Color.FromArgb(220, 140, 60);
            _continueButton.Enabled = false;
            return;
        }

        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            _validation.Text = "Expected a .json prospect file.";
            _validation.ForeColor = Color.FromArgb(220, 140, 60);
            _continueButton.Enabled = false;
            return;
        }

        _validation.Text = "Ready to load.";
        _validation.ForeColor = Color.FromArgb(90, 170, 100);
        _continueButton.Enabled = true;
    }
}
