using System.Diagnostics;
using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class MetricsSamplerTests
{
    [Fact]
    public void Sample_ReturnsZeros_WhenProcessNull()
    {
        var s = new MetricsSampler();
        var started = DateTime.Now.AddMinutes(-5);
        var sample = s.Sample(null, started);
        Assert.Equal(0, sample.CpuPercent);
        Assert.Equal(0, sample.MemoryMb);
        Assert.Equal(TimeSpan.Zero, sample.Uptime);
    }

    [Fact]
    public void Sample_ReturnsReasonableValues_ForCurrentProcess()
    {
        var s = new MetricsSampler();
        var proc = Process.GetCurrentProcess();
        var started = DateTime.Now.AddSeconds(-2);
        var sample = s.Sample(proc, started);
        Assert.True(sample.MemoryMb > 0);
        Assert.True(sample.Uptime >= TimeSpan.Zero);
        Assert.True(sample.CpuPercent >= 0);
    }
}
