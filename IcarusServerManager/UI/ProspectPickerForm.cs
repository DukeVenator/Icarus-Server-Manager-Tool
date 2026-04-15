using System.Globalization;
using IcarusServerManager.Models;

namespace IcarusServerManager.UI;

internal sealed class ProspectPickerForm : Form
{
    private readonly ListView _list = new();
    private readonly TextBox _details = new();

    public string? SelectedName { get; private set; }

    public ProspectPickerForm(IReadOnlyList<ProspectSummary> prospects)
    {
        Text = "Select prospect";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(920, 480);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var intro = new Label
        {
            Text = "Choose a prospect save. Columns come from the JSON header (first ~2 MB — ProspectBlob is skipped). Associated members and IsCurrentlyPlaying are shown when present in that slice.",
            AutoSize = true,
            MaximumSize = new Size(900, 0),
            Padding = new Padding(0, 0, 0, 8)
        };

        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.GridLines = true;
        _list.HideSelection = false;
        _list.MultiSelect = false;
        _list.Columns.Add("Prospect file", 120);
        _list.Columns.Add("Map (DTKey)", 160);
        _list.Columns.Add("Difficulty", 80);
        _list.Columns.Add("State", 70);
        _list.Columns.Add("Elapsed (min)", 90);
        _list.Columns.Add("Members", 55);
        _list.Columns.Add("Online", 55);
        foreach (var p in prospects)
        {
            var item = new ListViewItem(p.BaseName)
            {
                Tag = p
            };
            item.SubItems.Add(p.ProspectDtKey ?? "—");
            item.SubItems.Add(p.Difficulty ?? "—");
            item.SubItems.Add(p.ProspectState ?? "—");
            item.SubItems.Add(p.ElapsedGameMinutes?.ToString() ?? "—");
            item.SubItems.Add(p.Members.Count.ToString(CultureInfo.InvariantCulture));
            item.SubItems.Add(p.OnlineMemberCount.ToString(CultureInfo.InvariantCulture));
            _list.Items.Add(item);
        }

        if (_list.Items.Count > 0)
        {
            _list.Items[0].Selected = true;
        }

        _details.ReadOnly = true;
        _details.Multiline = true;
        _details.WordWrap = true;
        _details.ScrollBars = ScrollBars.Vertical;
        _details.Dock = DockStyle.Fill;
        _details.BorderStyle = BorderStyle.FixedSingle;
        _details.TabStop = false;
        _details.Font = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point);

        _list.SelectedIndexChanged += (_, _) => UpdateDetails();
        UpdateDetails();

        split.Controls.Add(_list, 0, 0);
        split.Controls.Add(_details, 1, 0);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Padding = new Padding(0, 8, 0, 0)
        };

        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "OK", AutoSize = true };
        ok.Click += (_, _) => TryAccept();
        _list.DoubleClick += (_, _) => TryAccept();

        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        root.Controls.Add(intro, 0, 0);
        root.Controls.Add(split, 0, 1);
        root.Controls.Add(buttons, 0, 2);

        Controls.Add(root);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void UpdateDetails()
    {
        if (_list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is ProspectSummary s)
        {
            _details.Text = s.BuildDetailsText();
        }
        else
        {
            _details.Text = string.Empty;
        }
    }

    private void TryAccept()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not ProspectSummary s || string.IsNullOrWhiteSpace(s.BaseName))
        {
            MessageBox.Show(this, "Select a prospect from the list.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectedName = s.BaseName;
        DialogResult = DialogResult.OK;
    }
}
