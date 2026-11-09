using System.Diagnostics;
using IcarusProspectEditor.Mapping;
using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace IcarusServerManager.Tests;

/// <summary>
/// Evidence for T10/E13: the projection cache must decode once per revision and repeated
/// lookups must stay cheap. Timing assertions are deliberately loose so the test detects an
/// accidental full re-decode, not machine-speed noise.
/// </summary>
public sealed class ProjectionPerformanceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "IcarusEditorPerf", Guid.NewGuid().ToString("N"));
    private readonly string _fixture = TestData.ResolveFile("SufferingResort.json");
    private readonly ITestOutputHelper _output;

    public ProjectionPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }
        catch
        {
            // Best-effort temporary-directory cleanup.
        }
    }

    [Fact]
    public void CachedProjections_DecodeOnceAndStayCheapAcrossRepeatedLookups()
    {
        var path = Path.Combine(_dir, "prospect.json");
        File.Copy(_fixture, path, overwrite: true);
        var document = ProspectLoadService.Load(path);
        var cache = new ProspectProjectionCache();

        ProspectModelMapper.ResetDecodeCounters();
        var uncachedTimer = Stopwatch.StartNew();
        var uncachedRows = ProspectModelMapper.ReadRecorderRows(document.Prospect, _ => true);
        uncachedTimer.Stop();
        var uncachedDecodes = ProspectModelMapper.RecorderMetadataDecodeCount;

        Assert.NotEmpty(uncachedRows);

        ProspectModelMapper.ResetDecodeCounters();
        var coldTimer = Stopwatch.StartNew();
        var coldRows = cache.GetRecorderRows(document.Prospect, revision: 1);
        var coldMounts = cache.GetMounts(document.Prospect, revision: 1);
        coldTimer.Stop();
        var coldDecodes = ProspectModelMapper.RecorderMetadataDecodeCount;

        Assert.Equal(uncachedRows.Count, coldRows.Count);
        Assert.Equal(1, coldDecodes);

        const int lookups = 200;
        var warmTimer = Stopwatch.StartNew();
        for (var i = 0; i < lookups; i++)
        {
            Assert.Same(coldRows, cache.GetRecorderRows(document.Prospect, revision: 1));
            Assert.Same(coldMounts, cache.GetMounts(document.Prospect, revision: 1));
        }

        warmTimer.Stop();

        Assert.Equal(1, ProspectModelMapper.RecorderMetadataDecodeCount);

        var perLookupMicroseconds = warmTimer.Elapsed.TotalMilliseconds * 1000 / (lookups * 2);
        _output.WriteLine($"fixture recorders: {coldRows.Count}, mounts: {coldMounts.Count}");
        _output.WriteLine($"uncached full enumeration: {uncachedTimer.Elapsed.TotalMilliseconds:F1} ms ({uncachedDecodes} decode)");
        _output.WriteLine($"cached cold projection: {coldTimer.Elapsed.TotalMilliseconds:F1} ms ({coldDecodes} decode)");
        _output.WriteLine($"cached warm lookup: {perLookupMicroseconds:F1} us/lookup over {lookups * 2} lookups");

        // A cached lookup is a dictionary hit plus nothing else; even a slow debug build stays
        // far below this, while a per-lookup re-decode would exceed it by orders of magnitude.
        Assert.True(
            perLookupMicroseconds < 500,
            $"cached lookups averaged {perLookupMicroseconds:F1} us, which suggests projections are being rebuilt");
        Assert.True(
            coldTimer.Elapsed.TotalMilliseconds >= 0 && uncachedTimer.Elapsed.TotalMilliseconds >= 0,
            "timers must be non-negative");
    }

    [Fact]
    public void DifferentRevisions_DoNotServeStaleRows()
    {
        var path = Path.Combine(_dir, "prospect.json");
        File.Copy(_fixture, path, overwrite: true);
        var document = ProspectLoadService.Load(path);
        var cache = new ProspectProjectionCache();

        ProspectModelMapper.ResetDecodeCounters();
        var first = cache.GetRecorderRows(document.Prospect, revision: 1);
        var second = cache.GetRecorderRows(document.Prospect, revision: 2);

        Assert.NotSame(first, second);
        Assert.Equal(2, ProspectModelMapper.RecorderMetadataDecodeCount);
        Assert.Equal(first.Count, second.Count);
    }
}
