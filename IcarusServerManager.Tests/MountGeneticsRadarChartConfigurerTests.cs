using System.Drawing;
using System.Windows.Forms.DataVisualization.Charting;
using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

/// <summary>
/// Ensures the genetics radar keeps a fixed 0–10 scale matching mount gene NumericUpDown controls,
/// so auto-scaling cannot show misleading negative or fractional tick labels.
/// </summary>
public sealed class MountGeneticsRadarChartConfigurerTests
{
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(4, 4)]
    [InlineData(10, 10)]
    [InlineData(99, 10)]
    public void ClampGeneValue_clamps_to_chart_range(int input, double expected)
    {
        Assert.Equal(expected, MountGeneticsRadarChartConfigurer.ClampGeneValue(input));
    }

    [Fact]
    public void Apply_sets_y_axis_to_gene_range()
    {
        var area = new ChartArea("test");
        MountGeneticsRadarChartConfigurer.Apply(area);

        Assert.Equal(0, area.AxisY.Minimum);
        Assert.Equal(10, area.AxisY.Maximum);
        Assert.Equal(2, area.AxisY.Interval);
        Assert.True(area.AxisY.MajorGrid.Enabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplyRadarChartTheme_sets_visible_axis_lines(bool dark)
    {
        var chart = new Chart();
        chart.ChartAreas.Add(new ChartArea("a"));
        MountGeneticsRadarChartConfigurer.Apply(chart.ChartAreas[0]);
        chart.Series.Add(new Series("Genetics") { ChartType = SeriesChartType.Radar });
        MountGeneticsRadarChartConfigurer.ApplyRadarChartTheme(chart, dark);
        Assert.NotEqual(Color.Empty, chart.ChartAreas[0].AxisX.MajorGrid.LineColor);
        Assert.NotEqual(Color.Empty, chart.Series[0].BorderColor);
    }
}
