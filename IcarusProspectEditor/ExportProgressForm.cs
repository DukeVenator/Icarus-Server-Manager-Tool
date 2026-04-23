using IcarusProspectEditor.Services;

namespace IcarusProspectEditor;

internal sealed class ExportProgressForm : Form
{
    private readonly Label _stage = new() { Dock = DockStyle.Top, Height = 28, Text = "Preparing export..." };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 22, Minimum = 0, Maximum = 100 };

    public ExportProgressForm()
    {
        Text = "Exporting decoded prospect";
        Width = 520;
        Height = 130;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
        panel.Controls.Add(_stage, 0, 0);
        panel.Controls.Add(_bar, 0, 1);
        Controls.Add(panel);
    }

    public void ApplyTheme(bool dark) => UiThemeService.ApplyTheme(this, dark);

    public void UpdateProgress(string stage, int percent)
    {
        _stage.Text = stage;
        _bar.Value = Math.Clamp(percent, _bar.Minimum, _bar.Maximum);
    }
}
