using System.Drawing.Drawing2D;

namespace IcarusServerManager.UI;

/// <summary>
/// When <see cref="UseDarkPaint"/> is true, paints the glyph with a dark fill and a light check on blue
/// so the checked state is readable (default WinForms flat checkbox can render white-on-white in dark UI).
/// </summary>
internal sealed class DarkThemedCheckBox : CheckBox
{
    private const int BoxSize = 15;
    private static readonly Color UncheckedFill = Color.FromArgb(40, 42, 50);
    private static readonly Color CheckedFill = Color.FromArgb(48, 110, 190);
    private static readonly Color BorderCol = Color.FromArgb(110, 118, 135);
    private static readonly Color CheckStroke = Color.FromArgb(252, 252, 255);

    public bool UseDarkPaint { get; set; }

    public DarkThemedCheckBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!UseDarkPaint || FlatStyle != FlatStyle.Flat)
        {
            base.OnPaint(e);
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? BackColor);

        var boxTop = (Height - BoxSize) / 2;
        var boxRect = new Rectangle(2, boxTop, BoxSize, BoxSize);
        var fill = Checked ? CheckedFill : UncheckedFill;
        if (!Enabled)
        {
            fill = ControlPaint.Dark(fill, 0.15f);
        }

        using (var fillBrush = new SolidBrush(fill))
        {
            g.FillRectangle(fillBrush, boxRect);
        }

        using (var borderPen = new Pen(BorderCol, 1f))
        {
            g.DrawRectangle(borderPen, boxRect.X, boxRect.Y, boxRect.Width - 1, boxRect.Height - 1);
        }

        if (Checked)
        {
            using var pen = new Pen(CheckStroke, 2.1f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            var inset = 3.2f;
            var pts = new[]
            {
                new PointF(boxRect.Left + inset, boxRect.Top + BoxSize * 0.52f),
                new PointF(boxRect.Left + BoxSize * 0.38f, boxRect.Bottom - inset),
                new PointF(boxRect.Right - inset - 0.5f, boxRect.Top + inset + 0.5f)
            };
            g.DrawLines(pen, pts);
        }

        var textRect = new Rectangle(boxRect.Right + 8, 0, Math.Max(0, Width - boxRect.Right - 10), Height);
        var textColor = Enabled ? ForeColor : ControlPaint.Dark(ForeColor, 0.25f);
        TextRenderer.DrawText(g, Text, Font, textRect, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

        if (Focused && ShowFocusCues)
        {
            var focusRect = new Rectangle(boxRect.X - 1, boxRect.Y - 1, boxRect.Width + textRect.Width + 6, boxRect.Height + 2);
            ControlPaint.DrawFocusRectangle(g, focusRect, BackColor, fill);
        }
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        if (UseDarkPaint)
        {
            Invalidate();
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        if (UseDarkPaint)
        {
            Invalidate();
        }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (UseDarkPaint)
        {
            Invalidate();
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        if (UseDarkPaint)
        {
            Invalidate();
        }
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        if (UseDarkPaint)
        {
            Invalidate();
        }
    }
}
