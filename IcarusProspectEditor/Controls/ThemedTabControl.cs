using System.Drawing;

namespace IcarusProspectEditor.Controls;

/// <summary>
/// A <see cref="TabControl"/> whose unused header area follows the theme.
///
/// The native control paints the strip with a system colour and ignores <see cref="Control.BackColor"/>,
/// which leaves a bright band beside the last tab in dark mode. The strip gaps are painted after the
/// native pass, so tab drawing is untouched. Both WM_PAINT (normal display) and WM_PRINT (used by
/// DrawToBitmap and print preview) are handled, otherwise screenshots would misrepresent the theme.
/// </summary>
internal sealed class ThemedTabControl : TabControl
{
    private const int WmPaint = 0x000F;
    private const int WmPrint = 0x0317;

    /// <summary>Colour for the header area not covered by tabs. Empty keeps the native appearance.</summary>
    public Color StripBackColor { get; set; } = Color.Empty;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (StripBackColor.IsEmpty || TabCount == 0)
        {
            return;
        }

        if (m.Msg == WmPrint)
        {
            if (m.WParam == IntPtr.Zero)
            {
                return;
            }

            using var printGraphics = Graphics.FromHdc(m.WParam);
            PaintStripGaps(printGraphics);
            return;
        }

        if (m.Msg == WmPaint && IsHandleCreated)
        {
            using var paintGraphics = Graphics.FromHwnd(m.HWnd);
            PaintStripGaps(paintGraphics);
        }
    }

    private void PaintStripGaps(Graphics graphics)
    {
        var stripHeight = DisplayRectangle.Top;
        if (stripHeight <= 0)
        {
            return;
        }

        var first = GetTabRect(0);
        var last = GetTabRect(TabCount - 1);
        using var brush = new SolidBrush(StripBackColor);
        if (first.Left > 0)
        {
            graphics.FillRectangle(brush, 0, 0, first.Left, stripHeight);
        }

        if (last.Right < Width)
        {
            graphics.FillRectangle(brush, last.Right, 0, Width - last.Right, stripHeight);
        }
    }
}
