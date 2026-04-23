using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class MountSpeciesMetadataServiceTests
{
    [Fact]
    public void RemapTalentsForSpecies_RewritesSpeciesTaggedNames()
    {
        var source = new[]
        {
            new TalentRow { Name = "Creature_Base_Health_ArcticMoa", Rank = 3 },
            new TalentRow { Name = "Creature_Base_Stamina_ArcticMoa", Rank = 2 },
            new TalentRow { Name = "Creature_Base_ReducedThreat", Rank = 1 }
        };

        var result = MountSpeciesMetadataService.RemapTalentsForSpecies(source, "ArcticMoa", "Buffalo");

        Assert.Equal(2, result.RemappedCount);
        Assert.True(result.DroppedCount >= 0);
        Assert.True(result.UnchangedCount >= 0);
        Assert.Contains(result.Talents, t => t.Name == "Creature_Base_Health_Buffalo");
        Assert.Contains(result.Talents, t => t.Name == "Creature_Base_Stamina_Buffalo");
    }

    [Fact]
    public void ValidateMount_RejectsUnknownSpeciesAndMissingOwner()
    {
        var issues = MountSpeciesMetadataService.ValidateMount(new MountRow
        {
            MountRace = "Dragon",
            Variation = 99,
            OwnerPlayerId = string.Empty,
            Level = 10
        });

        Assert.Contains(issues, x => x.Contains("allowlist", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, x => x.Contains("Owner Player ID", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ClampHelpers_KeepValuesInSafeBounds()
    {
        Assert.Equal(10, MountSpeciesMetadataService.ClampGenetic(22));
        Assert.Equal(0, MountSpeciesMetadataService.ClampGenetic(-5));
        Assert.Equal(2_147_000_000, MountSpeciesMetadataService.ClampRiskyInt(int.MaxValue));
    }

    [Fact]
    public void SpeciesOptions_IncludeParitySpecies()
    {
        var options = MountSpeciesMetadataService.GetSpeciesOptions();
        Assert.Contains(options, x => x.Equals("Raptor", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(options, x => x.Equals("WoollyMammoth", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateMount_RejectsVariationOutsideDomain()
    {
        var issues = MountSpeciesMetadataService.ValidateMount(new MountRow
        {
            MountRace = "Buffalo",
            Variation = 9,
            OwnerPlayerId = "76561190000000000",
            Level = 1
        });

        Assert.Contains(issues, x => x.Contains("Variation", StringComparison.OrdinalIgnoreCase));
    }
}
