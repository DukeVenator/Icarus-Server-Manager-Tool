namespace IcarusServerManager.UI;

internal sealed class ThemeManager
{
    private const string DarkBorderPaintTag = "__darkBorderPaint";

    private bool _applyingDarkTheme;

    public void ApplyTheme(Control root, string theme)
    {
        _applyingDarkTheme = theme.Equals("Dark", StringComparison.OrdinalIgnoreCase);
        var isDark = _applyingDarkTheme;
        var back = isDark ? Color.FromArgb(24, 24, 28) : Color.FromArgb(246, 248, 251);
        var fore = isDark ? Color.FromArgb(230, 230, 235) : Color.FromArgb(28, 30, 33);
        Apply(root, back, fore, isDark);
    }

    private void Apply(Control control, Color back, Color fore, bool isDark)
    {
        control.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        if (control is TextBox tb)
        {
            tb.BackColor = isDark ? Color.FromArgb(37, 39, 46) : Color.White;
            tb.ForeColor = fore;
            tb.BorderStyle = isDark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
        }
        else if (control is RichTextBox rtb)
        {
            rtb.BackColor = isDark ? Color.FromArgb(37, 39, 46) : Color.White;
            rtb.ForeColor = fore;
            rtb.BorderStyle = isDark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
        }
        else if (control is ComboBox combo)
        {
            combo.BackColor = isDark ? Color.FromArgb(37, 39, 46) : Color.White;
            combo.ForeColor = fore;
            combo.FlatStyle = isDark ? FlatStyle.Flat : FlatStyle.Standard;
        }
        else if (control is NumericUpDown nud)
        {
            nud.BackColor = isDark ? Color.FromArgb(37, 39, 46) : Color.White;
            nud.ForeColor = fore;
            nud.BorderStyle = isDark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
        }
        else if (control is Button button)
        {
            button.BackColor = isDark ? Color.FromArgb(53, 56, 66) : Color.FromArgb(233, 238, 246);
            button.ForeColor = fore;
            // Flat + padding often clips text at the bottom on some DPI; Standard paints reliably.
            button.FlatStyle = FlatStyle.Standard;
            button.UseVisualStyleBackColor = false;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Padding = Padding.Empty;
            button.Margin = new Padding(2, 2, 2, 2);
            button.UseCompatibleTextRendering = true;
            const int minH = 40;
            button.MinimumSize = new Size(0, minH);
            if (button.Height < minH)
            {
                button.Height = minH;
            }
        }
        else if (control is TabControl tabControl)
        {
            tabControl.BackColor = back;
            tabControl.ForeColor = fore;
            tabControl.SizeMode = TabSizeMode.Fixed;
            tabControl.ItemSize = new Size(120, isDark ? 30 : 28);
            if (isDark)
            {
                tabControl.Appearance = TabAppearance.Normal;
                tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
                tabControl.DrawItem -= OnTabControlDrawItem;
                tabControl.DrawItem += OnTabControlDrawItem;
            }
            else
            {
                tabControl.DrawItem -= OnTabControlDrawItem;
                tabControl.DrawMode = TabDrawMode.Normal;
                tabControl.Appearance = TabAppearance.Normal;
            }
        }
        else if (control is TabPage tabPage)
        {
            // Otherwise WinForms keeps a light “themed” tab client area regardless of BackColor.
            tabPage.UseVisualStyleBackColor = false;
            tabPage.BackColor = back;
            tabPage.ForeColor = fore;
        }
        else if (control is CheckBox checkBox)
        {
            checkBox.ForeColor = fore;
            if (checkBox is DarkThemedCheckBox dtc)
            {
                dtc.UseDarkPaint = isDark;
            }

            if (isDark)
            {
                checkBox.FlatStyle = FlatStyle.Flat;
                checkBox.BackColor = Color.Transparent;
                checkBox.UseVisualStyleBackColor = false;
                checkBox.FlatAppearance.BorderColor = Color.FromArgb(85, 88, 100);
                checkBox.FlatAppearance.BorderSize = 1;
                checkBox.FlatAppearance.CheckedBackColor = Color.FromArgb(48, 110, 190);
                checkBox.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 50, 58);
                checkBox.FlatAppearance.MouseDownBackColor = Color.FromArgb(40, 42, 50);
            }
            else
            {
                checkBox.FlatStyle = FlatStyle.Standard;
                checkBox.UseVisualStyleBackColor = true;
                checkBox.FlatAppearance.BorderSize = 1;
            }
        }
        else if (control is Panel panel)
        {
            if (isDark && panel.BorderStyle == BorderStyle.FixedSingle)
            {
                panel.BorderStyle = BorderStyle.None;
                if (panel.Tag as string != DarkBorderPaintTag)
                {
                    panel.Tag = DarkBorderPaintTag;
                    panel.Paint -= OnPanelThemedBorderPaint;
                    panel.Paint += OnPanelThemedBorderPaint;
                }
            }
            else if (!isDark && panel.Tag as string == DarkBorderPaintTag)
            {
                panel.Paint -= OnPanelThemedBorderPaint;
                panel.Tag = null;
                panel.BorderStyle = BorderStyle.FixedSingle;
            }

            panel.BackColor = back;
            panel.ForeColor = fore;
        }
        else if (control is Label)
        {
            control.BackColor = Color.Transparent;
            control.ForeColor = fore;
        }
        else
        {
            control.BackColor = back;
            control.ForeColor = fore;
        }

        foreach (Control child in control.Controls)
        {
            Apply(child, back, fore, isDark);
        }
    }

    private void OnPanelThemedBorderPaint(object? sender, PaintEventArgs e)
    {
        if (!_applyingDarkTheme || sender is not Panel panel)
        {
            return;
        }

        var g = e.Graphics;
        var rc = panel.ClientRectangle;
        rc.Width -= 1;
        rc.Height -= 1;
        using var pen = new Pen(Color.FromArgb(58, 62, 72), 1);
        g.DrawRectangle(pen, rc);
    }

    private void OnTabControlDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (!_applyingDarkTheme || sender is not TabControl tabControl || e.Index < 0 || e.Index >= tabControl.TabPages.Count)
        {
            return;
        }

        var page = tabControl.TabPages[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0;
        var tabBg = selected ? Color.FromArgb(26, 28, 34) : Color.FromArgb(44, 46, 54);
        var tabFg = Color.FromArgb(230, 230, 235);
        var font = e.Font ?? tabControl.Font;

        using (var brush = new SolidBrush(tabBg))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }

        TextRenderer.DrawText(e.Graphics, page.Text, font, e.Bounds, tabFg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

        if (selected)
        {
            using var accent = new Pen(Color.FromArgb(70, 130, 210), 2);
            e.Graphics.DrawLine(accent, e.Bounds.Left + 2, e.Bounds.Top + 2, e.Bounds.Right - 2, e.Bounds.Top + 2);
        }

        using (var sep = new Pen(Color.FromArgb(32, 34, 40)))
        {
            e.Graphics.DrawLine(sep, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 2);
        }
    }
}
