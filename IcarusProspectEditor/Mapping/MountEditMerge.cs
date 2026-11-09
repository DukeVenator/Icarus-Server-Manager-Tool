using IcarusProspectEditor.Mapping;
using IcarusProspectEditor.Models;
using IcarusSaveLib;

namespace IcarusProspectEditor.Services;

/// <summary>
/// Outcome of merging the mount Overview draft with the Advanced recorder draft.
/// <see cref="AppliedFields"/> lists the field paths whose effective value differs from the recorder
/// baseline (Overview-owned and explicit Advanced edits); <see cref="Conflicts"/> lists every path
/// where the two drafts disagreed and the explicit Advanced value was kept.
/// </summary>
internal sealed record MountMergeResult(
    RecorderEditResult Edit,
    IReadOnlyList<string> Conflicts)
{
    /// <summary>
    /// Field paths whose effective value differs from the recorder baseline (Overview-owned and
    /// explicit Advanced edits). Kept out of the positional signature so existing call sites and
    /// deconstruction remain unchanged.
    /// </summary>
    public IReadOnlyList<string> AppliedFields { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Merges the mount Overview draft and the Advanced recorder draft into one field change set and
/// applies it exactly once.
///
/// Policy (deterministic, recorded for the user):
/// - A field only edited in Overview takes the Overview value.
/// - A field only edited in Advanced keeps the Advanced value (an untouched Advanced row never
///   overwrites the Overview edit).
/// - A field edited differently in both is a conflict: the explicit raw (Advanced) value is kept
///   and the conflict is reported, instead of whichever snapshot happened to be applied last.
///
/// Fields and talents are submitted through the mapper's single recorder transaction, so a rejected
/// talent edit can no longer leave the scalar fields committed.
/// </summary>
internal static class MountEditMerge
{
    public static MountMergeResult Apply(
        ProspectSave prospect,
        int recorderIndex,
        IReadOnlyList<RecorderFieldRow> originalAdvancedFields,
        MountRow updatedMount,
        IReadOnlyList<RecorderFieldRow> advancedDraft,
        IReadOnlyList<TalentRow> talents)
    {
        var merged = new Dictionary<string, RecorderFieldRow>(StringComparer.Ordinal);
        foreach (var row in advancedDraft)
        {
            merged[row.Path] = new RecorderFieldRow
            {
                Path = row.Path,
                PropertyType = row.PropertyType,
                Value = row.Value,
                Editable = row.Editable
            };
        }

        var baseline = originalAdvancedFields
            .GroupBy(f => f.Path, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
        var originalByPath = originalAdvancedFields
            .GroupBy(f => f.Path, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var overviewValues = ProspectModelMapper.BuildMountFieldValues(updatedMount, originalAdvancedFields);
        var conflicts = new List<string>();

        foreach (var (path, overviewValue) in overviewValues.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            baseline.TryGetValue(path, out var baselineValue);
            baselineValue ??= string.Empty;
            var overviewChanged = !string.Equals(overviewValue, baselineValue, StringComparison.Ordinal);

            if (!merged.TryGetValue(path, out var row))
            {
                // The Advanced draft omitted this field entirely; the Overview edit still applies.
                if (!overviewChanged)
                {
                    continue;
                }

                originalByPath.TryGetValue(path, out var originalRow);
                merged[path] = new RecorderFieldRow
                {
                    Path = path,
                    PropertyType = originalRow?.PropertyType ?? string.Empty,
                    Value = overviewValue,
                    Editable = true
                };
                continue;
            }

            if (!overviewChanged)
            {
                continue;
            }

            if (!row.Editable)
            {
                conflicts.Add($"{path}: Overview value \"{overviewValue}\" was not applied because the Advanced field is read-only.");
                continue;
            }

            var advancedValue = row.Value ?? string.Empty;
            var advancedChanged = !string.Equals(advancedValue, baselineValue, StringComparison.Ordinal);
            if (advancedChanged && !string.Equals(advancedValue, overviewValue, StringComparison.Ordinal))
            {
                conflicts.Add($"{path}: kept Advanced value \"{advancedValue}\" over Overview value \"{overviewValue}\".");
                continue;
            }

            row.Value = overviewValue;
        }

        var appliedFields = merged.Values
            .Where(r => r.Editable)
            .Where(r => !baseline.TryGetValue(r.Path, out var before) ||
                        !string.Equals(r.Value ?? string.Empty, before, StringComparison.Ordinal))
            .Select(r => r.Path)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        // Only ask the mapper to rebuild the talent array when the draft actually differs from what
        // the recorder currently stores. This keeps scalar edits available on recorders whose talent
        // data is not structured, while any real talent change is still submitted in the same
        // transaction as the scalar fields (so a rejected talent leaves the fields uncommitted).
        var talentEdit = HasTalentChanges(prospect, recorderIndex, talents) ? talents : null;

        // One call = one recorder transaction: scalar validation and the talent plan both succeed
        // before either is written, and the recorder blob is replaced once.
        var result = ProspectModelMapper.ApplyMountEdits(prospect, recorderIndex, merged.Values, talentEdit);
        return new MountMergeResult(result, conflicts) { AppliedFields = appliedFields };
    }

    private static bool HasTalentChanges(
        ProspectSave prospect,
        int recorderIndex,
        IReadOnlyList<TalentRow> talents)
    {
        var existing = ProspectModelMapper.ReadTalentRows(prospect, recorderIndex);
        if (existing.Count != talents.Count)
        {
            return true;
        }

        var existingRanks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in existing)
        {
            existingRanks[row.Name] = row.Rank;
        }

        var requestedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var talent in talents)
        {
            var name = talent.Name?.Trim() ?? string.Empty;
            if (name.Length == 0 || !requestedNames.Add(name))
            {
                // Empty or duplicate names must reach the mapper so they are reported as Invalid and
                // nothing is committed.
                return true;
            }

            if (!existingRanks.TryGetValue(name, out var rank) || rank != talent.Rank)
            {
                return true;
            }
        }

        return false;
    }
}
