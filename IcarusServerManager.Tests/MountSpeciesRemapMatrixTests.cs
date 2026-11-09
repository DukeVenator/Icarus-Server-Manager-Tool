using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

/// <summary>
/// Checks the species remap contract rather than just "some rows came back": names must be the
/// source names with the species token replaced, ranks must survive unless the target rank cap is
/// lower, unrelated talents must be untouched, and repeated calls must be identical.
/// </summary>
public sealed class MountSpeciesRemapMatrixTests
{
    [Theory]
    [InlineData("Tusker", "Buffalo")]
    [InlineData("Tusker", "Bull")]
    [InlineData("Tusker", "Moa")]
    public void RemapTalentsForSpecies_RenamesSpeciesTokenAndKeepsRanks(string fromSpecies, string toSpecies)
    {
        var source = new[]
        {
            new TalentRow { Name = $"Creature_Base_Health_{fromSpecies}", Rank = 3, MaxRank = 10 },
            new TalentRow { Name = $"Creature_Base_Stamina_{fromSpecies}", Rank = 2, MaxRank = 10 },
            new TalentRow { Name = $"Creature_Base_WeightCapacity_{fromSpecies}", Rank = 1, MaxRank = 10 },
            new TalentRow { Name = "Regression_Unrelated_Talent", Rank = 5, MaxRank = 10 }
        };

        var result = MountSpeciesMetadataService.RemapTalentsForSpecies(source, fromSpecies, toSpecies);

        var unrelated = Assert.Single(result.Talents, t => t.Name == "Regression_Unrelated_Talent");
        Assert.Equal(5, unrelated.Rank);
        Assert.Equal("Unchanged", unrelated.RemapStatus);

        Assert.DoesNotContain(result.Talents, t => t.Name.Contains(fromSpecies, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(result.Talents.Count, result.Talents.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        if (result.DroppedCount == 0)
        {
            foreach (var (suffix, rank) in new[]
                     {
                         ("Health", 3),
                         ("Stamina", 2),
                         ("WeightCapacity", 1)
                     })
            {
                var renamed = Assert.Single(result.Talents, t => t.Name == $"Creature_Base_{suffix}_{toSpecies}");
                Assert.Equal(rank, renamed.Rank);
                Assert.Equal("Renamed", renamed.RemapStatus);
            }

            Assert.Equal(0, result.LostPoints);
        }
        else
        {
            Assert.True(result.LostPoints > 0, "dropping a ranked talent must report the lost points");
        }
    }

    [Theory]
    [InlineData("Tusker", "Buffalo")]
    [InlineData("Buffalo", "Moa")]
    public void RemapTalentsForSpecies_IsStableAcrossRepeatedCalls(string fromSpecies, string toSpecies)
    {
        var source = new[]
        {
            new TalentRow { Name = $"Creature_Base_Health_{fromSpecies}", Rank = 2, MaxRank = 4 },
            new TalentRow { Name = $"Creature_Base_Melee_{fromSpecies}", Rank = 4, MaxRank = 10 },
            new TalentRow { Name = "Regression_Shared_Talent", Rank = 1, MaxRank = 10 }
        };

        var first = MountSpeciesMetadataService.RemapTalentsForSpecies(source, fromSpecies, toSpecies);
        var second = MountSpeciesMetadataService.RemapTalentsForSpecies(source, fromSpecies, toSpecies);

        Assert.Equal(
            first.Talents.Select(t => (t.Name, t.Rank, t.MaxRank, t.RemapStatus)),
            second.Talents.Select(t => (t.Name, t.Rank, t.MaxRank, t.RemapStatus)));
        Assert.Equal(first.RemappedCount, second.RemappedCount);
        Assert.Equal(first.DroppedCount, second.DroppedCount);
        Assert.Equal(first.AddedCount, second.AddedCount);
        Assert.Equal(first.LostPoints, second.LostPoints);
    }
}
