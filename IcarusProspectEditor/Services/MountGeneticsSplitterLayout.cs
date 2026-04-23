namespace IcarusProspectEditor.Services;

/// <summary>
/// Computes safe <see cref="System.Windows.Forms.SplitContainer"/> metrics for the mount genetics tab.
/// Splitter distance must not be set until the container has a real client width, or WinForms throws
/// <see cref="InvalidOperationException"/> (e.g. SplitterDistance vs Panel2MinSize when Width is still 0).
/// </summary>
internal readonly record struct GeneticsSplitterMetrics(int Panel1MinSize, int Panel2MinSize, int SplitterDistance);

internal static class MountGeneticsSplitterLayout
{
    internal const int MinClientWidthToLayout = 64;

    private const int PreferredSplitterDistance = 280;
    private const int WantPanel1Min = 180;
    private const int WantPanel2Min = 220;

    /// <summary>
    /// Returns false when the client is too narrow for a valid splitter configuration.
    /// </summary>
    internal static bool TryCompute(int clientWidth, int splitterWidth, out GeneticsSplitterMetrics metrics)
    {
        metrics = default;
        if (clientWidth < MinClientWidthToLayout)
        {
            return false;
        }

        var gap = clientWidth - splitterWidth;
        var panel2Min = Math.Clamp(WantPanel2Min, 80, Math.Max(80, gap - WantPanel1Min - 4));
        var panel1Min = Math.Clamp(WantPanel1Min, 80, Math.Max(80, gap - panel2Min - 4));

        var minDist = panel1Min;
        var maxDist = clientWidth - panel2Min - splitterWidth;
        if (maxDist < minDist)
        {
            return false;
        }

        var distance = Math.Clamp(PreferredSplitterDistance, minDist, maxDist);
        metrics = new GeneticsSplitterMetrics(panel1Min, panel2Min, distance);
        return true;
    }
}
