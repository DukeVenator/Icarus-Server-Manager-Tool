using System.ComponentModel;
using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;

namespace IcarusProspectEditor;

internal sealed class RecorderInspectorForm : Form
{
    private readonly BindingList<RecorderFieldRow> _rows;
    private readonly RecorderFieldsEditorTabs _editor;

    public IReadOnlyList<RecorderFieldRow> EditedRows => _rows;

    public RecorderInspectorForm(string title, IEnumerable<RecorderFieldRow> rows, IEnumerable<MemberRow> knownPlayers)
    {
        _rows = new BindingList<RecorderFieldRow>(rows.ToList());
        _editor = new RecorderFieldsEditorTabs(_rows, knownPlayers) { Dock = DockStyle.Fill };
        Text = title;
        Width = 1100;
        Height = 700;
        StartPosition = FormStartPosition.CenterParent;
        BuildLayout();
        Load += (_, _) => AppLogService.UserAction($"Recorder inspector opened: {title}");
    }

    public void ApplyTheme(bool dark)
    {
        UiThemeService.ApplyTheme(this, dark);
        _editor.ApplyTheme(dark);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(_editor, 0, 0);

        var buttonBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var apply = new Button { Text = "Apply", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttonBar.Controls.Add(apply);
        buttonBar.Controls.Add(cancel);
        root.Controls.Add(buttonBar, 0, 1);
        AcceptButton = apply;
        CancelButton = cancel;
    }
}
