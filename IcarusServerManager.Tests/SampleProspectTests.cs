using IcarusProspectEditor.Mapping;
using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

/// <summary>
/// The repository ships a small anonymised example prospect under samples/. It must stay loadable as
/// a real encoded save, so anyone can open it in the editor without touching a live save.
/// </summary>
public sealed class SampleProspectTests
{
    [Fact]
    public void DemoProspectSample_LoadsAsAnEncodedSave()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var samplePath = Path.Combine(repoRoot, "samples", "DemoProspect.json");
        Assert.True(File.Exists(samplePath), $"Missing example save at {samplePath}");

        var document = ProspectLoadService.Load(samplePath);

        Assert.Equal("Demo Prospect", document.Prospect.ProspectInfo.LobbyName);
        Assert.Equal("SufferingResort", document.Prospect.ProspectInfo.ProspectID);
        Assert.Equal(4, document.Prospect.ProspectInfo.AssociatedMembers.Count);
        Assert.NotEmpty(ProspectModelMapper.ReadRecorderRows(document.Prospect, _ => true));
        Assert.NotEmpty(ProspectModelMapper.ReadMountsFromProspect(document.Prospect));
    }
}
