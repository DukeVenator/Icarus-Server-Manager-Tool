using System.Diagnostics;

namespace IcarusServerManager.Services;

internal sealed class MetricsSample
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public double CpuPercent { get; init; }
    public double MemoryMb { get; init; }
    public TimeSpan Uptime { get; init; }
}

internal sealed class MetricsSampler
{
    private DateTime _lastSampleTime = DateTime.Now;
    private TimeSpan _lastProcessorTime = TimeSpan.Zero;

    public MetricsSample Sample(Process? process, DateTime startedAt)
    {
        if (process == null || process.HasExited)
        {
            return new MetricsSample
            {
                CpuPercent = 0,
                MemoryMb = 0,
                Uptime = TimeSpan.Zero
            };
        }

        process.Refresh();
        var now = DateTime.Now;
        var procTime = process.TotalProcessorTime;
        var elapsed = (now - _lastSampleTime).TotalSeconds;
        var cpuDelta = (procTime - _lastProcessorTime).TotalSeconds;
        var cpu = elapsed > 0 ? (cpuDelta / (Environment.ProcessorCount * elapsed)) * 100 : 0;
        _lastSampleTime = now;
        _lastProcessorTime = procTime;

        return new MetricsSample
        {
            CpuPercent = Math.Max(0, cpu),
            MemoryMb = process.WorkingSet64 / 1024d / 1024d,
            Uptime = now - startedAt
        };
    }
}
