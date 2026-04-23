namespace IcarusProspectEditor.Services;

internal static class UiThemeService
{
    public static void ApplyTheme(Control root, bool dark)
    {
        var back = dark ? Color.FromArgb(24, 24, 28) : Color.FromArgb(246, 248, 251);
        var fore = dark ? Color.FromArgb(230, 230, 235) : Color.FromArgb(28, 30, 33);
        ApplyRecursive(root, back, fore, dark);
    }

    private static void ApplyRecursive(Control control, Color back, Color fore, bool dark)
    {
        control.BackColor = back;
        control.ForeColor = fore;

        switch (control)
        {
            case TextBox tb:
                tb.BackColor = dark ? Color.FromArgb(37, 39, 46) : Color.White;
                tb.ForeColor = fore;
                tb.BorderStyle = dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                break;
            case RichTextBox rtb:
                rtb.BackColor = dark ? Color.FromArgb(37, 39, 46) : Color.White;
                rtb.ForeColor = fore;
                rtb.BorderStyle = dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                break;
            case ComboBox combo:
                combo.BackColor = dark ? Color.FromArgb(37, 39, 46) : Color.White;
                combo.ForeColor = fore;
                combo.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
                break;
            case NumericUpDown nud:
                nud.BackColor = dark ? Color.FromArgb(37, 39, 46) : Color.White;
                nud.ForeColor = fore;
                nud.BorderStyle = dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                break;
            case DataGridView dgv:
                ApplyDataGridTheme(dgv, dark);
                break;
            case Button button:
                button.BackColor = dark ? Color.FromArgb(53, 56, 66) : Color.FromArgb(233, 238, 246);
                button.ForeColor = fore;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = dark ? Color.FromArgb(70, 74, 86) : Color.FromArgb(160, 170, 185);
                break;
            case TabControl tabs:
                tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                tabs.DrawItem -= DrawTab;
                tabs.DrawItem += DrawTab;
                tabs.ItemSize = new Size(120, dark ? 30 : 28);
                tabs.SizeMode = TabSizeMode.Fixed;
                tabs.Tag = dark ? "dark" : "light";
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyRecursive(child, back, fore, dark);
        }
    }

    private static void ApplyDataGridTheme(DataGridView grid, bool dark)
    {
        grid.BackgroundColor = dark ? Color.FromArgb(32, 34, 41) : Color.White;
        grid.GridColor = dark ? Color.FromArgb(70, 74, 86) : Color.FromArgb(215, 220, 228);
        grid.EnableHeadersVisualStyles = false;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.DefaultCellStyle.BackColor = dark ? Color.FromArgb(37, 39, 46) : Color.White;
        grid.DefaultCellStyle.ForeColor = dark ? Color.FromArgb(230, 230, 235) : Color.FromArgb(28, 30, 33);
        grid.DefaultCellStyle.SelectionBackColor = dark ? Color.FromArgb(63, 72, 92) : Color.FromArgb(214, 230, 250);
        grid.DefaultCellStyle.SelectionForeColor = dark ? Color.White : Color.Black;
        grid.ColumnHeadersDefaultCellStyle.BackColor = dark ? Color.FromArgb(53, 56, 66) : Color.FromArgb(233, 238, 246);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = dark ? Color.FromArgb(230, 230, 235) : Color.FromArgb(28, 30, 33);
        grid.RowHeadersDefaultCellStyle.BackColor = dark ? Color.FromArgb(53, 56, 66) : Color.FromArgb(233, 238, 246);
        grid.RowHeadersDefaultCellStyle.ForeColor = dark ? Color.FromArgb(230, 230, 235) : Color.FromArgb(28, 30, 33);
    }

    private static void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs)
        {
            return;
        }

        var dark = string.Equals(tabs.Tag as string, "dark", StringComparison.OrdinalIgnoreCase);
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var back = dark
            ? (selected ? Color.FromArgb(53, 56, 66) : Color.FromArgb(34, 36, 43))
            : (selected ? Color.White : Color.FromArgb(226, 231, 239));
        var fore = dark
            ? Color.FromArgb(230, 230, 235)
            : Color.FromArgb(28, 30, 33);

        using var b = new SolidBrush(back);
        e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(
            e.Graphics,
            tabs.TabPages[e.Index].Text,
            e.Font,
            e.Bounds,
            fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Contrast-safe colors for unsaved-change highlights and mount remap rows.</summary>
internal static class ThemeHighlightColors
{
    public static Color ChangedFieldBack(bool dark) =>
        dark ? Color.FromArgb(92, 72, 28) : Color.FromArgb(255, 245, 194);

    public static Color ChangedGridBack(bool dark) =>
        dark ? Color.FromArgb(88, 70, 32) : Color.FromArgb(255, 245, 194);

    public static Color ChangedGridFore(bool dark) =>
        dark ? Color.FromArgb(255, 248, 220) : Color.FromArgb(28, 30, 33);

    public static Color ValidationOk(bool dark) =>
        dark ? Color.FromArgb(160, 255, 190) : Color.FromArgb(0, 110, 45);

    public static Color RemapRowAdded(bool dark) =>
        dark ? Color.FromArgb(34, 72, 48) : Color.FromArgb(215, 245, 215);

    public static Color RemapRowRemoved(bool dark) =>
        dark ? Color.FromArgb(88, 36, 36) : Color.FromArgb(255, 222, 222);

    public static Color RemapRowRenamed(bool dark) =>
        dark ? Color.FromArgb(38, 52, 88) : Color.FromArgb(226, 238, 255);

    public static Color RemapRowRankAdjusted(bool dark) =>
        dark ? Color.FromArgb(88, 72, 28) : Color.FromArgb(255, 245, 210);

    public static Color RemapRowFore(bool dark) =>
        dark ? Color.FromArgb(240, 240, 245) : Color.FromArgb(28, 30, 33);
}
