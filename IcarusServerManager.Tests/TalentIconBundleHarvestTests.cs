using Newtonsoft.Json.Linq;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class TalentIconBundleHarvestTests
{
    [Fact]
    public void HarvestedManifest_HasIconsAndBaseHealthAsset()
    {
        var dir = ResolveTalentAssetsDir();
        var manifestPath = Path.Combine(dir, "manifest.json");
        Assert.True(File.Exists(manifestPath));
        var json = JObject.Parse(File.ReadAllText(manifestPath));
        var icons = json["icons"] as JObject;
        Assert.NotNull(icons);
        Assert.True(icons!.Count > 50, "Expected harvested manifest with many talent icon entries.");

        var png = Path.Combine(dir, "icons", "T_Talent_Base_Health.png");
        Assert.True(File.Exists(png), "Expected bundled T_Talent_Base_Health.png from harvest.");
    }

    private static string ResolveTalentAssetsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "IcarusProspectEditor", "assets", "talents");
            if (File.Exists(Path.Combine(candidate, "manifest.json")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate IcarusProspectEditor/assets/talents from test base directory.");
    }
}
