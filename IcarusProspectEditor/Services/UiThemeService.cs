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
            case DateTimePicker picker:
                picker.CalendarMonthBackground = dark ? Color.FromArgb(37, 39, 46) : Color.White;
                picker.CalendarTitleBackColor = dark ? Color.FromArgb(53, 56, 66) : Color.FromArgb(233, 238, 246);
                picker.CalendarTitleForeColor = fore;
                picker.CalendarTrailingForeColor = dark ? Color.FromArgb(150, 150, 160) : Color.Gray;
                break;
            case Button button:
                button.BackColor = dark ? Color.FromArgb(53, 56, 66) : Color.FromArgb(233, 238, 246);
                button.ForeColor = fore;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = dark ? Color.FromArgb(70, 74, 86) : Color.FromArgb(160, 170, 185);
                break;
            case LinkLabel link:
                link.LinkColor = dark ? Color.FromArgb(130, 180, 240) : Color.FromArgb(32, 100, 180);
                link.ActiveLinkColor = dark ? Color.FromArgb(170, 205, 250) : Color.FromArgb(20, 70, 140);
                link.VisitedLinkColor = link.LinkColor;
                break;
            case MenuStrip menu:
                // ToolStrip paints itself through a renderer, so BackColor alone leaves a light menu
                // bar in dark mode. ManagerRenderMode ignores a custom Renderer, so switch first.
                menu.RenderMode = ToolStripRenderMode.Professional;
                menu.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable(dark)) { RoundedEdges = false };
                menu.BackColor = back;
                menu.ForeColor = fore;
                foreach (ToolStripItem item in menu.Items)
                {
                    ThemeMenuItem(item, dark);
                }

                break;
            case TabControl tabs:
                tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                tabs.DrawItem -= DrawTab;
                tabs.DrawItem += DrawTab;
                // Normal sizing gives each tab its own width from its label. Fixed sizing forced one
                // uniform width, so eight tabs needed ~1024 px and the strip was clipped (or fell back
                // to scroll arrows) in any window narrower than that.
                tabs.SizeMode = TabSizeMode.Normal;
                // Height follows the font so a larger DPI/text scale cannot clip the label; the old
                // fixed 34 px was the most likely break point at 150-200 % scaling.
                tabs.ItemSize = new Size(0, TextRenderer.MeasureText("Ag", tabs.Font).Height + 14);
                // Horizontal padding shrinks when the text itself grows, otherwise eight tabs no
                // longer fit across a 1024 px window at 200 % text size.
                var fontScale = Math.Max(1f, tabs.Font.Size / 9f);
                // Owner-drawn tabs ignore Multiline, so large text is handled by shrinking the
                // horizontal padding: at 200 % text this keeps all eight tabs inside a 1024 px
                // window (long labels ellipsize rather than being clipped off the control).
                tabs.Padding = new Point(fontScale >= 1.4f ? 4 : 10, 6);
                tabs.Tag = dark ? "dark" : "light";
                if (tabs is IcarusProspectEditor.Controls.ThemedTabControl themed)
                {
                    themed.StripBackColor = dark ? Color.FromArgb(24, 24, 28) : Color.FromArgb(246, 248, 251);
                    themed.Invalidate();
                }

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
        // Every alternating row needs an opaque colour; a transparent one leaves stale row pixels
        // behind and makes adjacent rows look overprinted.
        grid.AlternatingRowsDefaultCellStyle.BackColor = dark ? Color.FromArgb(34, 36, 42) : Color.FromArgb(248, 250, 252);
        grid.AlternatingRowsDefaultCellStyle.ForeColor = dark ? Color.FromArgb(230, 230, 235) : Color.FromArgb(28, 30, 33);
        grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = dark ? Color.FromArgb(63, 72, 92) : Color.FromArgb(214, 230, 250);
        grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = dark ? Color.White : Color.Black;
        grid.ColumnHeadersDefaultCellStyle.BackColor = dark ? Color.FromArgb(53, 56, 66) : Color.FromArgb(233, 238, 246);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = dark ? Color.FromArgb(230, 230, 235) : Color.FromArgb(28, 30, 33);
        grid.RowHeadersDefaultCellStyle.BackColor = dark ? Color.FromArgb(53, 56, 66) : Color.FromArgb(233, 238, 246);
        grid.RowHeadersDefaultCellStyle.ForeColor = dark ? Color.FromArgb(230, 230, 235) : Color.FromArgb(28, 30, 33);
    }

    private static void ThemeMenuItem(ToolStripItem item, bool dark)
    {
        item.ForeColor = dark ? Color.FromArgb(230, 230, 235) : Color.FromArgb(28, 30, 33);
        if (item is not ToolStripMenuItem menuItem)
        {
            return;
        }

        if (menuItem.HasDropDownItems)
        {
            menuItem.DropDown.RenderMode = ToolStripRenderMode.Professional;
            menuItem.DropDown.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable(dark))
            {
                RoundedEdges = false
            };
            menuItem.DropDown.BackColor = dark ? Color.FromArgb(36, 38, 46) : Color.White;
            menuItem.DropDown.ForeColor = item.ForeColor;
        }

        foreach (ToolStripItem child in menuItem.DropDownItems)
        {
            ThemeMenuItem(child, dark);
        }
    }

    /// <summary>Menu/drop-down palette so the menu bar and its popups match the selected theme.</summary>
    private sealed class ThemeColorTable : ProfessionalColorTable
    {
        private readonly Color _surface;
        private readonly Color _dropDown;
        private readonly Color _selected;
        private readonly Color _border;

        public ThemeColorTable(bool dark)
        {
            _surface = dark ? Color.FromArgb(24, 24, 28) : Color.FromArgb(246, 248, 251);
            _dropDown = dark ? Color.FromArgb(36, 38, 46) : Color.White;
            _selected = dark ? Color.FromArgb(63, 72, 92) : Color.FromArgb(214, 230, 250);
            _border = dark ? Color.FromArgb(70, 74, 86) : Color.FromArgb(200, 206, 216);
        }

        public override Color MenuStripGradientBegin => _surface;
        public override Color MenuStripGradientEnd => _surface;
        public override Color ToolStripDropDownBackground => _dropDown;
        public override Color ImageMarginGradientBegin => _dropDown;
        public override Color ImageMarginGradientMiddle => _dropDown;
        public override Color ImageMarginGradientEnd => _dropDown;
        public override Color MenuItemSelected => _selected;
        public override Color MenuItemSelectedGradientBegin => _selected;
        public override Color MenuItemSelectedGradientEnd => _selected;
        public override Color MenuItemPressedGradientBegin => _selected;
        public override Color MenuItemPressedGradientEnd => _selected;
        public override Color MenuItemBorder => _border;
        public override Color MenuBorder => _border;
        public override Color ToolStripBorder => _border;
        public override Color SeparatorDark => _border;
        public override Color SeparatorLight => _dropDown;
    }

    private static void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs)
        {
            return;
        }

        var dark = string.Equals(tabs.Tag as string, "dark", StringComparison.OrdinalIgnoreCase);
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var focused = (e.State & DrawItemState.Focus) == DrawItemState.Focus;
        var surface = dark ? Color.FromArgb(24, 24, 28) : Color.FromArgb(246, 248, 251);
        var selectedSurface = dark ? Color.FromArgb(34, 36, 43) : Color.White;
        var accent = dark ? Color.FromArgb(96, 156, 226) : Color.FromArgb(32, 100, 180);
        var fore = selected
            ? (dark ? Color.White : Color.FromArgb(20, 22, 26))
            : (dark ? Color.FromArgb(178, 182, 192) : Color.FromArgb(90, 96, 106));

        using (var background = new SolidBrush(selected ? selectedSurface : surface))
        {
            e.Graphics.FillRectangle(background, e.Bounds);
        }

        var textBounds = new Rectangle(e.Bounds.X, e.Bounds.Y + 2, e.Bounds.Width, e.Bounds.Height - 6);
        TextRenderer.DrawText(
            e.Graphics,
            tabs.TabPages[e.Index].Text,
            tabs.Font,
            textBounds,
            fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (selected)
        {
            using var underline = new SolidBrush(accent);
            e.Graphics.FillRectangle(underline, e.Bounds.X + 8, e.Bounds.Bottom - 4, e.Bounds.Width - 16, 3);
        }

        if (focused)
        {
            var focusRect = Rectangle.Inflate(e.Bounds, -4, -6);
            ControlPaint.DrawFocusRectangle(e.Graphics, focusRect, fore, selected ? selectedSurface : surface);
        }
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
