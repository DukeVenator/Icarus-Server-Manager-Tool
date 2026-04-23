using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class TalentIconBundleServiceTests
{
    [Fact]
    public void ResolveIconPath_ReturnsLocalFallbackWhenMissing()
    {
        var path = TalentIconBundleService.ResolveIconPath("Nonexistent_Talent_Icon");
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(File.Exists(path));
        Assert.Contains("assets", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetResolvedBundleRoot_returns_existing_directory()
    {
        TalentIconBundleService.EnsureInitialized();
        var root = TalentIconBundleService.GetResolvedBundleRoot();
        Assert.False(string.IsNullOrWhiteSpace(root));
        Assert.True(Directory.Exists(root));
    }
}
