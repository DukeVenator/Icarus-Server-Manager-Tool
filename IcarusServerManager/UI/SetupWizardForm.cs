namespace IcarusServerManager.UI;

/// <summary>Short guided flow for choosing the dedicated server install root folder.</summary>
internal sealed class SetupWizardForm : Form
{
    private readonly TextBox _pathBox = new();

    public string SelectedPath { get; private set; } = string.Empty;

    public SetupWizardForm(string? initialPath)
    {
        Text = "Server install folder — setup wizard";
        Size = new Size(720, 360);
        MinimumSize = Size;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Padding = new Padding(12);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Where is the dedicated server installed?",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 11f, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 0, 8)
        };

        var help = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(660, 0),
            Margin = new Padding(0, 0, 0, 12),
            Text =
                "Pick the folder that contains (or will contain) this file:\r\n" +
                "  Icarus\\Binaries\\Win64\\IcarusServer-Win64-Shipping.exe\r\n\r\n" +
                "If the game is not installed yet: choose an empty folder, click OK here, then use " +
                "“Install/Update Server” on the main window. The manager reads ServerSettings.ini and " +
                "prospects under this folder unless you set UserDir / SavedDir overrides in Manager Settings."
        };

        var pathRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 12)
        };

        _pathBox.Text = initialPath?.Trim() ?? string.Empty;
        _pathBox.Width = 520;
        _pathBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        var browse = new Button { Text = "Browse…", AutoSize = true, Margin = new Padding(8, 2, 0, 0) };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Select the dedicated server install folder (game root)." };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _pathBox.Text = dlg.SelectedPath;
            }
        };

        pathRow.Controls.Add(_pathBox);
        pathRow.Controls.Add(browse);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill
        };

        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        var ok = new Button { Text = "OK", AutoSize = true };
        ok.Click += (_, _) =>
        {
            SelectedPath = _pathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(SelectedPath))
            {
                MessageBox.Show(this, "Choose a folder or enter a full path.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        AcceptButton = ok;
        CancelButton = cancel;

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(help, 0, 1);
        root.Controls.Add(pathRow, 0, 2);
        root.Controls.Add(buttons, 0, 3);

        Controls.Add(root);
        Shown += (_, _) => _pathBox.Focus();
    }
}
