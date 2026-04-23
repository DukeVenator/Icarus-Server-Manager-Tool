using Newtonsoft.Json.Linq;

namespace IcarusProspectEditor.Services;

internal sealed record SpeciesTalentDelta(
    string TargetSpecies,
    IReadOnlyList<string> AddedTalents,
    IReadOnlyList<string> RemovedTalents);

internal sealed record ParityDatasetSnapshot(
    IReadOnlyList<string> Species,
    IReadOnlyDictionary<string, SpeciesTalentDelta> DeltasBySpecies);

internal static class ParityDatasetService
{
    private static readonly Lazy<ParityDatasetSnapshot> Snapshot = new(LoadSnapshot);

    public static ParityDatasetSnapshot GetSnapshot() => Snapshot.Value;

    private static ParityDatasetSnapshot LoadSnapshot()
    {
        var path = ResolveParityDatasetPath();
        if (path is null || !File.Exists(path))
        {
            AppLogService.Info("Parity dataset not found. Falling back to built-in species/remap defaults.");
            return new ParityDatasetSnapshot([], new Dictionary<string, SpeciesTalentDelta>(StringComparer.OrdinalIgnoreCase));
        }

        try
        {
            var raw = File.ReadAllText(path);
            var json = JObject.Parse(raw);
            var species = json["speciesTalentTrees"] is JObject trees
                ? trees.Properties().Select(p => p.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
                : [];

            var deltas = new Dictionary<string, SpeciesTalentDelta>(StringComparer.OrdinalIgnoreCase);
            if (json["speciesTalentDeltas"] is JArray deltaArray)
            {
                foreach (var token in deltaArray.OfType<JObject>())
                {
                    var targetSpecies = token.Value<string>("targetSpecies") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(targetSpecies))
                    {
                        continue;
                    }

                    var added = token["addedTalents"] is JArray addedArray
                        ? addedArray.Values<string?>()
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x!)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList()
                        : [];
                    var removed = token["removedTalents"] is JArray removedArray
                        ? removedArray.Values<string?>()
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x!)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList()
                        : [];

                    deltas[targetSpecies] = new SpeciesTalentDelta(targetSpecies, added, removed);
                }
            }

            AppLogService.Info($"Parity dataset loaded: {path} (species={species.Count}, remapTargets={deltas.Count})");
            return new ParityDatasetSnapshot(species, deltas);
        }
        catch (Exception ex)
        {
            AppLogService.Error($"Failed to parse parity dataset at {path}", ex);
            return new ParityDatasetSnapshot([], new Dictionary<string, SpeciesTalentDelta>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private static string? ResolveParityDatasetPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var probe = new DirectoryInfo(baseDir);
        while (probe is not null)
        {
            var candidate = Path.Combine(probe.FullName, "docs", "reverse-engineering", "captures", "2026-04-22T11-14-53-943Z", "parity-dataset.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            probe = probe.Parent;
        }

        return null;
    }
}
