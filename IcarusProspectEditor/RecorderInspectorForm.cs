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
        // The dialog edits private copies. Cancel must never leak a half-typed value back into the
        // caller's rows, and the caller's rows must not change underneath the dialog.
        _rows = new BindingList<RecorderFieldRow>(rows.Select(row => row.Clone()).ToList());
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
        var apply = new Button { Text = "Apply", AutoSize = true, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        apply.Click += (_, _) =>
        {
            // Commit the active field first: an invalid value the user is still typing must block the
            // dialog instead of closing with the previous (valid) value silently kept.
            var commit = _editor.CommitCurrentValue();
            if (!commit.Succeeded)
            {
                AppLogService.UserAction($"Recorder inspector apply blocked: {Text} — {commit.Describe()}");
                MessageBox.Show(
                    this,
                    commit.Describe(),
                    "Value not applied",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            AppLogService.UserAction($"Recorder inspector apply clicked: {Text}");
            DialogResult = DialogResult.OK;
            Close();
        };
        cancel.Click += (_, _) => AppLogService.UserAction($"Recorder inspector cancel clicked: {Text}");
        buttonBar.Controls.Add(apply);
        buttonBar.Controls.Add(cancel);
        root.Controls.Add(buttonBar, 0, 1);
        AcceptButton = apply;
        CancelButton = cancel;
    }
}
