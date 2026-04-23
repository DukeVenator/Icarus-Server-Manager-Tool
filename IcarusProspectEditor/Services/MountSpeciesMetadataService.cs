using IcarusProspectEditor.Models;

namespace IcarusProspectEditor.Services;

internal sealed record TalentRemapResult(
    IReadOnlyList<TalentRow> Talents,
    int RemappedCount,
    int DroppedCount,
    int UnchangedCount,
    int AddedCount,
    int RankAdjustedCount,
    int LostPoints);

internal static class MountSpeciesMetadataService
{
    private static readonly string[] DefaultSpecies =
    [
        "ArcticMoa",
        "Buffalo",
        "Bull",
        "Chew",
        "Horse",
        "HorseStandard",
        "Moa",
        "Raptor",
        "RaptorDesert",
        "Slinker",
        "SwampBird",
        "Tusker",
        "Wolf",
        "WoollyMammoth",
        "WoolyZebra",
        "Zebra"
    ];

    private static readonly IReadOnlyDictionary<string, int[]> VariationDomains =
        new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["ArcticMoa"] = [0],
            ["Buffalo"] = [0],
            ["Bull"] = [0],
            ["Chew"] = [0],
            ["Horse"] = [0, 1, 2, 3],
            ["HorseStandard"] = [0, 1, 2, 3],
            ["Moa"] = [0, 1, 2],
            ["Raptor"] = [0, 1],
            ["RaptorDesert"] = [0, 1],
            ["Slinker"] = [0],
            ["SwampBird"] = [0, 1],
            ["Tusker"] = [0],
            ["Wolf"] = [0],
            ["WoollyMammoth"] = [0],
            ["WoolyZebra"] = [0, 1],
            ["Zebra"] = [0, 1]
        };

    private static readonly Lazy<ParityDatasetSnapshot> Snapshot = new(ParityDatasetService.GetSnapshot);

    public static IReadOnlyList<string> GetSpeciesOptions()
    {
        var combined = new HashSet<string>(DefaultSpecies, StringComparer.OrdinalIgnoreCase);
        foreach (var species in Snapshot.Value.Species)
        {
            combined.Add(species);
        }

        return combined.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static int[] GetVariationDomain(string species)
    {
        if (VariationDomains.TryGetValue(species, out var known))
        {
            return known;
        }

        return [0];
    }

    public static bool IsAllowedSpecies(string species) =>
        GetSpeciesOptions().Contains(species, StringComparer.OrdinalIgnoreCase);

    public static string NormalizeSpecies(string mountType, string mountRace)
    {
        foreach (var species in GetSpeciesOptions())
        {
            if (mountType.Contains(species, StringComparison.OrdinalIgnoreCase) ||
                mountRace.Contains(species, StringComparison.OrdinalIgnoreCase))
            {
                return species;
            }
        }

        if (mountType.StartsWith("Mount_", StringComparison.OrdinalIgnoreCase))
        {
            return mountType["Mount_".Length..];
        }

        return mountRace;
    }

    public static TalentRemapResult RemapTalentsForSpecies(IEnumerable<TalentRow> input, string fromSpecies, string toSpecies)
    {
        var remapped = new List<TalentRow>();
        var remappedCount = 0;
        var droppedCount = 0;
        var addedCount = 0;
        var unchangedCount = 0;
        var rankAdjustedCount = 0;
        var lostPoints = 0;
        var target = Snapshot.Value.DeltasBySpecies.TryGetValue(toSpecies, out var delta)
            ? delta
            : null;
        var removedSet = target?.RemovedTalents?.ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addedSet = target?.AddedTalents?.ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var talent in input)
        {
            var source = talent.Name ?? string.Empty;
            if (removedSet.Contains(source))
            {
                droppedCount++;
                lostPoints += Math.Max(0, talent.Rank);
                continue;
            }

            if (source.Contains(fromSpecies, StringComparison.OrdinalIgnoreCase))
            {
                var updated = ReplaceSpeciesToken(source, fromSpecies, toSpecies);
                remapped.Add(new TalentRow
                {
                    Name = updated,
                    Rank = talent.Rank,
                    MaxRank = talent.MaxRank,
                    DisplayName = ToDisplayName(updated),
                    IconKey = updated,
                    RemapStatus = "Renamed"
                });
                remappedCount++;
            }
            else
            {
                remapped.Add(new TalentRow
                {
                    Name = source,
                    Rank = talent.Rank,
                    MaxRank = talent.MaxRank,
                    DisplayName = ToDisplayName(source),
                    IconKey = source,
                    RemapStatus = "Unchanged"
                });
                unchangedCount++;
            }
        }

        foreach (var talentName in addedSet)
        {
            if (remapped.Any(x => string.Equals(x.Name, talentName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            remapped.Add(new TalentRow
            {
                Name = talentName,
                Rank = 0,
                MaxRank = 10,
                DisplayName = ToDisplayName(talentName),
                IconKey = talentName,
                RemapStatus = "Added"
            });
            addedCount++;
        }

        foreach (var row in remapped.Where(x => x.Rank > x.MaxRank))
        {
            row.Rank = row.MaxRank;
            row.RemapStatus = row.RemapStatus == "Unchanged" ? "RankAdjusted" : row.RemapStatus;
            rankAdjustedCount++;
        }

        remapped.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return new TalentRemapResult(remapped, remappedCount, droppedCount, unchangedCount, addedCount, rankAdjustedCount, lostPoints);
    }

    public static int ClampRiskyInt(int value) => Math.Clamp(value, 0, 2_147_000_000);

    public static int ClampGenetic(int value) => Math.Clamp(value, 0, 10);

    public static IReadOnlyList<string> ValidateMount(MountRow mount)
    {
        var issues = new List<string>();
        if (!IsAllowedSpecies(mount.MountRace))
        {
            issues.Add($"Species '{mount.MountRace}' is not in the safe allowlist.");
        }
        if (mount.Level < 0 || mount.Level > 999)
        {
            issues.Add("Level must be in range 0-999.");
        }
        var allowedVariations = GetVariationDomain(mount.MountRace);
        if (!allowedVariations.Contains(mount.Variation))
        {
            issues.Add($"Variation '{mount.Variation}' is invalid for species '{mount.MountRace}'.");
        }
        if (string.IsNullOrWhiteSpace(mount.OwnerPlayerId))
        {
            issues.Add("Owner Player ID is required.");
        }

        return issues;
    }

    private static string ReplaceSpeciesToken(string input, string fromSpecies, string toSpecies)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        return input.Replace(fromSpecies, toSpecies, StringComparison.OrdinalIgnoreCase);
    }

    public static string ToDisplayName(string talentName)
    {
        if (string.IsNullOrWhiteSpace(talentName))
        {
            return string.Empty;
        }

        return talentName.Replace('_', ' ').Trim();
    }
}
