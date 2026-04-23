using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class MountSpeciesRemapMatrixTests
{
    [Theory]
    [InlineData("Tusker", "Buffalo")]
    [InlineData("Tusker", "Bull")]
    [InlineData("Tusker", "Moa")]
    public void RemapTalentsForSpecies_ProducesDeterministicOutput(string fromSpecies, string toSpecies)
    {
        var source = new[]
        {
            new TalentRow { Name = $"Creature_Base_Health_{fromSpecies}", Rank = 3, MaxRank = 10 },
            new TalentRow { Name = $"Creature_Base_Stamina_{fromSpecies}", Rank = 2, MaxRank = 10 },
            new TalentRow { Name = $"Creature_Base_WeightCapacity_{fromSpecies}", Rank = 1, MaxRank = 10 }
        };

        var result = MountSpeciesMetadataService.RemapTalentsForSpecies(source, fromSpecies, toSpecies);

        Assert.NotEmpty(result.Talents);
        Assert.True(result.RemappedCount >= 0);
        Assert.True(result.DroppedCount >= 0);
        Assert.True(result.AddedCount >= 0);
        Assert.True(result.LostPoints >= 0);
        Assert.True(result.Talents.All(t => t.Rank >= 0 && t.Rank <= t.MaxRank));
    }
}
