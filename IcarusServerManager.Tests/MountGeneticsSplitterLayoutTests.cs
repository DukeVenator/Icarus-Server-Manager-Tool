using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class MountGeneticsSplitterLayoutTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(63)]
    public void TryCompute_returns_false_when_client_too_narrow(int clientWidth)
    {
        Assert.False(MountGeneticsSplitterLayout.TryCompute(clientWidth, 4, out _));
    }

    [Fact]
    public void TryCompute_wide_client_uses_preferred_splitter_distance()
    {
        const int splitterWidth = 4;
        Assert.True(MountGeneticsSplitterLayout.TryCompute(900, splitterWidth, out var m));
        Assert.Equal(180, m.Panel1MinSize);
        Assert.Equal(220, m.Panel2MinSize);
        Assert.Equal(280, m.SplitterDistance);
        Assert.True(m.SplitterDistance >= m.Panel1MinSize);
        Assert.True(m.SplitterDistance <= 900 - m.Panel2MinSize - splitterWidth);
    }

    [Fact]
    public void TryCompute_narrow_client_clamps_distance_without_invalid_range()
    {
        const int splitterWidth = 4;
        const int w = 320;
        Assert.True(MountGeneticsSplitterLayout.TryCompute(w, splitterWidth, out var m));
        Assert.True(m.SplitterDistance >= m.Panel1MinSize);
        Assert.True(m.SplitterDistance <= w - m.Panel2MinSize - splitterWidth);
        Assert.True(m.SplitterDistance < 280);
    }

    [Fact]
    public void TryCompute_returns_false_when_panels_cannot_both_satisfy_minimums()
    {
        Assert.False(MountGeneticsSplitterLayout.TryCompute(120, 4, out _));
    }
}
