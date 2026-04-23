using System.Drawing;
using System.Windows.Forms.DataVisualization.Charting;

namespace IcarusProspectEditor.Services;

/// <summary>
/// Central place for genetics radar axis limits (must match NumericUpDown 0–10 on the mount editor).
/// </summary>
internal static class MountGeneticsRadarChartConfigurer
{
    internal static double ClampGeneValue(int value) => Math.Clamp((double)value, 0, 10);

    internal static void Apply(ChartArea area)
    {
        area.InnerPlotPosition = new ElementPosition(10, 10, 80, 80);
        area.AxisY.Minimum = 0;
        area.AxisY.Maximum = 10;
        area.AxisY.Interval = 2;
        area.AxisY.MajorGrid.Enabled = true;
        area.AxisY.LabelStyle.Font = new Font("Segoe UI", 8.5f);
        area.AxisX.LabelStyle.Font = new Font("Segoe UI", 8.5f);
    }

    /// <summary>Axis/grid/series outline colors so the radar stays readable on dark chart backgrounds.</summary>
    internal static void ApplyRadarChartTheme(Chart chart, bool dark)
    {
        if (chart.ChartAreas.Count == 0 || chart.Series.Count == 0)
        {
            return;
        }

        var area = chart.ChartAreas[0];
        var series = chart.Series[0];
        if (dark)
        {
            var line = Color.FromArgb(110, 115, 125);
            area.AxisX.LineColor = line;
            area.AxisY.LineColor = line;
            area.AxisX.MajorGrid.LineColor = line;
            area.AxisY.MajorGrid.LineColor = line;
            area.AxisX.MajorGrid.Enabled = true;
            area.AxisY.MajorGrid.Enabled = true;
            series.BorderColor = Color.FromArgb(255, 215, 90);
            series.BorderWidth = 2;
        }
        else
        {
            var axisLine = Color.FromArgb(120, 125, 135);
            area.AxisX.LineColor = axisLine;
            area.AxisY.LineColor = axisLine;
            var grid = Color.FromArgb(210, 215, 220);
            area.AxisX.MajorGrid.LineColor = grid;
            area.AxisY.MajorGrid.LineColor = grid;
            series.BorderColor = Color.SteelBlue;
            series.BorderWidth = 2;
        }
    }
}
