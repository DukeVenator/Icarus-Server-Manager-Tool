using System.Globalization;
using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using IcarusSaveLib;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace IcarusProspectEditor.Mapping;

internal static class ProspectModelMapper
{
    /// <summary>
    /// Diagnostic counters used by regression tests to prove projections are not repeatedly decoded.
    /// They are thread-local: xUnit runs test classes in parallel, so a process-wide counter would be
    /// incremented by unrelated tests and make exact-count assertions flaky.
    /// </summary>
    [ThreadStatic]
    private static int _recorderMetadataDecodeCount;

    [ThreadStatic]
    private static int _recorderFieldDecodeCount;

    internal static int RecorderMetadataDecodeCount
    {
        get => _recorderMetadataDecodeCount;
        set => _recorderMetadataDecodeCount = value;
    }

    internal static int RecorderFieldDecodeCount
    {
        get => _recorderFieldDecodeCount;
        set => _recorderFieldDecodeCount = value;
    }

    internal static void ResetDecodeCounters()
    {
        RecorderMetadataDecodeCount = 0;
        RecorderFieldDecodeCount = 0;
    }

    public static RecorderCategory ClassifyRecorderComponent(string componentClass)
    {
        if (componentClass.Contains("SecurityDoor", StringComparison.OrdinalIgnoreCase))
        {
            return RecorderCategory.Security;
        }
        if (componentClass.Contains("Container", StringComparison.OrdinalIgnoreCase))
        {
            return RecorderCategory.Containers;
        }
        if (componentClass.Contains("CaveAI", StringComparison.OrdinalIgnoreCase) ||
            componentClass.Contains("Creature", StringComparison.OrdinalIgnoreCase) ||
            componentClass.Contains("Boss", StringComparison.OrdinalIgnoreCase))
        {
            return RecorderCategory.AI;
        }
        if (componentClass.Contains("Geyser", StringComparison.OrdinalIgnoreCase) ||
            componentClass.Contains("Ore", StringComparison.OrdinalIgnoreCase) ||
            componentClass.Contains("Harvest", StringComparison.OrdinalIgnoreCase))
        {
            return RecorderCategory.Resource;
        }
        if (componentClass.Contains("Mount", StringComparison.OrdinalIgnoreCase))
        {
            return RecorderCategory.Mount;
        }
        if (componentClass.Contains("Mission", StringComparison.OrdinalIgnoreCase))
        {
            return RecorderCategory.Mission;
        }
        if (componentClass.Contains("PreBuilt", StringComparison.OrdinalIgnoreCase) ||
            componentClass.Contains("Prebuilt", StringComparison.OrdinalIgnoreCase))
        {
            return RecorderCategory.Prebuilt;
        }
        if (componentClass.Contains("Character", StringComparison.OrdinalIgnoreCase))
        {
            return RecorderCategory.Character;
        }
        if (componentClass.Contains("Blocker", StringComparison.OrdinalIgnoreCase) ||
            componentClass.Contains("Entrance", StringComparison.OrdinalIgnoreCase))
        {
            return RecorderCategory.Structures;
        }
        if (componentClass.Contains("Manager", StringComparison.OrdinalIgnoreCase) ||
            componentClass.Contains("Quest", StringComparison.OrdinalIgnoreCase) ||
            componentClass.Contains("Talent", StringComparison.OrdinalIgnoreCase) ||
            componentClass.Contains("PlayerHistory", StringComparison.OrdinalIgnoreCase))
        {
            return RecorderCategory.Systems;
        }
        if (componentClass.Contains("Weather", StringComparison.OrdinalIgnoreCase) ||
            componentClass.Contains("Map", StringComparison.OrdinalIgnoreCase) ||
            componentClass.Contains("GameMode", StringComparison.OrdinalIgnoreCase) ||
            componentClass.Contains("FLOD", StringComparison.OrdinalIgnoreCase) ||
            componentClass.Contains("Tile", StringComparison.OrdinalIgnoreCase))
        {
            return RecorderCategory.World;
        }
        return RecorderCategory.Unknown;
    }

    public static List<RecorderRow> ReadRecorderRows(ProspectSave prospect, Func<string, bool> filter)
    {
        var rows = new List<RecorderRow>();
        foreach (var (index, componentClass, propertyCount) in EnumerateRecorderMeta(prospect))
        {
            if (!filter(componentClass))
            {
                continue;
            }
            var category = ClassifyRecorderComponent(componentClass);

            rows.Add(new RecorderRow
            {
                Index = index,
                ComponentClass = componentClass,
                ComponentShortName = NormalizeComponentShortName(componentClass),
                Category = category,
                PropertyCount = propertyCount,
                IsDangerous = category is RecorderCategory.Mission or RecorderCategory.Prebuilt,
                MetaSummary = $"{NormalizeComponentShortName(componentClass)} ({propertyCount} fields)"
            });
        }
        return rows;
    }

    public static List<RecorderRow> ReadRecorderRowsByCategory(ProspectSave prospect, RecorderCategory category) =>
        ReadRecorderRows(prospect, _ => true).Where(r => r.Category == category).ToList();

    public static List<MemberRow> ReadMembers(ProspectSave prospect) =>
        prospect.ProspectInfo.AssociatedMembers?.Select(m => new MemberRow
        {
            AccountName = m.AccountName,
            CharacterName = m.CharacterName,
            UserID = m.UserID,
            ChrSlot = m.ChrSlot,
            Experience = m.Experience,
            Status = m.Status,
            Settled = m.Settled,
            IsCurrentlyPlaying = m.IsCurrentlyPlaying
        }).ToList() ?? [];

    public static List<CustomSettingRow> ReadCustomSettings(ProspectSave prospect) =>
        prospect.ProspectInfo.CustomSettings?.Select(s => new CustomSettingRow
        {
            SettingRowName = s.SettingRowName,
            SettingValue = s.SettingValue
        }).ToList() ?? [];

    public static List<MountRow> ReadMountsFromProspect(ProspectSave prospect) =>
        ReadMountsFromProspect(prospect, ReadRecorderRowsByCategory(prospect, RecorderCategory.Mount));

    /// <summary>Builds mount rows from an already-decoded recorder projection (avoids a second metadata pass).</summary>
    public static List<MountRow> ReadMountsFromProspect(ProspectSave prospect, IEnumerable<RecorderRow> mountRecorders)
    {
        var mounts = new List<MountRow>();
        foreach (var recorder in mountRecorders)
        {
            var fields = ReadRecorderFields(prospect, recorder.Index);
            var talentRanks = ReadMountTalentRanks(prospect, recorder.Index);
            var mountType = ResolveMountType(fields, talentRanks, recorder.ComponentShortName);
            var mountName = ResolveMountName(fields, recorder.ComponentShortName, recorder.Index);
            mounts.Add(new MountRow
            {
                RecorderIndex = recorder.Index,
                RecorderPath = recorder.ComponentClass,
                MountName = mountName,
                OwnerPlayerId = FirstValueOrDefault(fields, string.Empty, "OwnerCharacterID.PlayerID", "PlayerID"),
                OwnerCharacterSlot = FirstInt(fields, "OwnerCharacterID.ChrSlot", "ChrSlot"),
                OwnerName = FirstValueOrDefault(fields, string.Empty, "OwnerName"),
                Level = FirstInt(fields, "Level", "CurrentLevel"),
                Experience = FirstInt(fields, "Experience", "XP"),
                Health = FirstInt(fields, "Health", "CurrentHealth"),
                Sex = FirstValueOrDefault(fields, "Unknown", "Sex"),
                Lineage = FirstValueOrDefault(fields, "None", "Lineage"),
                MountType = mountType,
                MountRace = MountSpeciesMetadataService.NormalizeSpecies(
                    mountType,
                    InferMountRace(mountType, mountName)),
                Variation = FirstInt(fields, "Variation", "MountVariation"),
                MountIconName = FirstValueOrDefault(fields, string.Empty, "MountIconName"),
                AiSetupRowName = FirstValueOrDefault(fields, string.Empty, "AISetupRowName"),
                ActorClassName = FirstValueOrDefault(fields, string.Empty, "ActorClassName"),
                ActorPathName = FirstValueOrDefault(fields, string.Empty, "ActorPathName"),
                Vitality = PickFirstNonZero(
                    FirstInt(fields, "Vitality"),
                    FindTalentRank(talentRanks, "Base_Health_")),
                Endurance = PickFirstNonZero(
                    FirstInt(fields, "Endurance"),
                    FindTalentRank(talentRanks, "Base_Stamina_")),
                Muscle = PickFirstNonZero(
                    FirstInt(fields, "Muscle"),
                    FindTalentRank(talentRanks, "Base_WeightCapacity_")),
                Agility = PickFirstNonZero(
                    FirstInt(fields, "Agility"),
                    FindTalentRank(talentRanks, "SprintSpeed", "MoveSpeed")),
                Toughness = PickFirstNonZero(
                    FirstInt(fields, "Toughness"),
                    FindTalentRank(talentRanks, "DamageReduction")),
                Hardiness = PickFirstNonZero(
                    FirstInt(fields, "Hardiness"),
                    FindTalentRank(talentRanks, "HealthRegeneration", "StaminaRegeneration")),
                Utility = PickFirstNonZero(
                    FirstInt(fields, "Utility"),
                    FindTalentRank(talentRanks, "FoodSlot", "ReducedFoodConsumption"))
            });
        }

        return mounts;
    }

    private static string ResolveMountName(IEnumerable<RecorderFieldRow> fields, string fallbackShortName, int fallbackIndex)
    {
        var mountName = FirstNonEmptyValue(fields, "MountName");
        if (!string.IsNullOrWhiteSpace(mountName))
        {
            return mountName;
        }

        var creatureName = FirstNonEmptyValue(fields, "CreatureName");
        if (!string.IsNullOrWhiteSpace(creatureName))
        {
            return creatureName;
        }

        var genericName = FirstNonEmptyValue(fields, "Name");
        if (!string.IsNullOrWhiteSpace(genericName))
        {
            return genericName;
        }

        return $"{fallbackShortName}#{fallbackIndex}";
    }

    private static string ResolveMountType(
        IEnumerable<RecorderFieldRow> fields,
        IReadOnlyDictionary<string, int> talentRanks,
        string fallback)
    {
        var explicitType = FirstValue(fields, "MountType", "CreatureType");
        if (!string.IsNullOrWhiteSpace(explicitType))
        {
            return explicitType;
        }

        var recorderName = FirstValue(fields, "Name");
        if (recorderName.StartsWith("Mount_", StringComparison.OrdinalIgnoreCase))
        {
            return recorderName;
        }

        var talentBased = TryExtractMountTypeFromTalents(talentRanks.Keys);
        if (!string.IsNullOrWhiteSpace(talentBased))
        {
            return talentBased;
        }

        return fallback;
    }

    private static string TryExtractMountTypeFromTalents(IEnumerable<string> talentNames)
    {
        foreach (var talentName in talentNames)
        {
            // Talent names usually end in mount archetype: Creature_Base_Health_Tusker
            var lastUnderscore = talentName.LastIndexOf('_');
            if (lastUnderscore < 0 || lastUnderscore >= talentName.Length - 1)
            {
                continue;
            }

            var suffix = talentName[(lastUnderscore + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(suffix))
            {
                return $"Mount_{suffix}";
            }
        }

        return string.Empty;
    }

    public static void ApplyMembers(ProspectSave prospect, IEnumerable<MemberRow> rows)
    {
        var info = prospect.ProspectInfo;
        info.AssociatedMembers = rows.Select(r => new FAssociatedMember
        {
            AccountName = r.AccountName,
            CharacterName = r.CharacterName,
            UserID = r.UserID,
            ChrSlot = r.ChrSlot,
            Experience = r.Experience,
            Status = r.Status,
            Settled = r.Settled,
            IsCurrentlyPlaying = r.IsCurrentlyPlaying
        }).ToList();
        prospect.ProspectInfo = info;
    }

    public static void ApplyCustomSettings(ProspectSave prospect, IEnumerable<CustomSettingRow> rows)
    {
        var info = prospect.ProspectInfo;
        info.CustomSettings = rows.Select(r => new FCustomGameSetting
        {
            SettingRowName = r.SettingRowName,
            SettingValue = r.SettingValue
        }).ToList();
        prospect.ProspectInfo = info;
    }

    /// <summary>
    /// Field values the mount Overview owns. Paths are matched against the recorder's editable
    /// fields; values are clamped to the same domains the editor has always used.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildMountFieldValues(MountRow row, IEnumerable<RecorderFieldRow> fields)
    {
        // Only fields the mapper can actually write take part in the Overview projection. Without this
        // filter a suffix match against a read-only/unsupported property would make the whole mount edit
        // fail validation ("field does not exist") even though every real value was fine.
        var fieldList = fields
            .Where(f => f.Editable && IsSupportedFieldTypeName(f.PropertyType))
            .ToList();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        void Set(string name, string value)
        {
            var exact = fieldList.FirstOrDefault(f => f.Path.Equals(name, StringComparison.OrdinalIgnoreCase));
            var key = exact?.Path
                ?? fieldList
                    .Select(f => f.Path)
                    .FirstOrDefault(k => k.EndsWith(name, StringComparison.OrdinalIgnoreCase));
            if (key is not null)
            {
                values[key] = value;
            }
        }

        var clampedLevel = Math.Clamp(row.Level, 0, 999);
        var clampedXp = MountSpeciesMetadataService.ClampRiskyInt(row.Experience);
        var clampedHealth = MountSpeciesMetadataService.ClampRiskyInt(row.Health);
        var variationDomain = MountSpeciesMetadataService.GetVariationDomain(row.MountRace);
        var clampedVariation = variationDomain.Contains(row.Variation) ? row.Variation : variationDomain.FirstOrDefault();

        Set("MountName", row.MountName);
        Set("OwnerCharacterID.PlayerID", row.OwnerPlayerId);
        Set("OwnerCharacterID.ChrSlot", row.OwnerCharacterSlot.ToString(CultureInfo.InvariantCulture));
        Set("PlayerID", row.OwnerPlayerId);
        Set("ChrSlot", row.OwnerCharacterSlot.ToString(CultureInfo.InvariantCulture));
        Set("OwnerName", row.OwnerName);
        Set("Level", clampedLevel.ToString(CultureInfo.InvariantCulture));
        Set("CurrentLevel", clampedLevel.ToString(CultureInfo.InvariantCulture));
        Set("Experience", clampedXp.ToString(CultureInfo.InvariantCulture));
        Set("XP", clampedXp.ToString(CultureInfo.InvariantCulture));
        Set("Health", clampedHealth.ToString(CultureInfo.InvariantCulture));
        Set("CurrentHealth", clampedHealth.ToString(CultureInfo.InvariantCulture));
        Set("Sex", row.Sex);
        Set("Lineage", row.Lineage);
        Set("MountType", row.MountType);
        Set("CreatureType", row.MountType);
        Set("MountIconName", row.MountIconName);
        Set("AISetupRowName", row.AiSetupRowName);
        Set("ActorClassName", row.ActorClassName);
        Set("ActorPathName", row.ActorPathName);
        Set("Variation", clampedVariation.ToString(CultureInfo.InvariantCulture));
        Set("MountVariation", clampedVariation.ToString(CultureInfo.InvariantCulture));
        Set("Vitality", MountSpeciesMetadataService.ClampGenetic(row.Vitality).ToString(CultureInfo.InvariantCulture));
        Set("Endurance", MountSpeciesMetadataService.ClampGenetic(row.Endurance).ToString(CultureInfo.InvariantCulture));
        Set("Muscle", MountSpeciesMetadataService.ClampGenetic(row.Muscle).ToString(CultureInfo.InvariantCulture));
        Set("Agility", MountSpeciesMetadataService.ClampGenetic(row.Agility).ToString(CultureInfo.InvariantCulture));
        Set("Toughness", MountSpeciesMetadataService.ClampGenetic(row.Toughness).ToString(CultureInfo.InvariantCulture));
        Set("Hardiness", MountSpeciesMetadataService.ClampGenetic(row.Hardiness).ToString(CultureInfo.InvariantCulture));
        Set("Utility", MountSpeciesMetadataService.ClampGenetic(row.Utility).ToString(CultureInfo.InvariantCulture));

        return values;
    }

    /// <summary>
    /// Applies Overview fields and, when supplied, the talent array as one atomic edit. Both are
    /// validated before either is written, and the recorder blob is replaced once.
    /// </summary>
    public static RecorderEditResult ApplyMountFromProspect(ProspectSave prospect, MountRow row, IEnumerable<TalentRow>? talents = null)
    {
        var fields = ReadRecorderFields(prospect, row.RecorderIndex);
        if (fields.Count == 0)
        {
            return RecorderEditResult.NotFound();
        }

        var editRows = BuildMountFieldValues(row, fields)
            .Select(kv => new RecorderFieldRow
            {
                Path = kv.Key,
                PropertyType = string.Empty,
                Value = kv.Value,
                Editable = true
            });
        return ApplyMountEdits(prospect, row.RecorderIndex, editRows, talents?.ToList());
    }

    /// <summary>
    /// Atomically applies recorder fields and an optional full talent list. Every field value and
    /// every requested talent is validated before the recorder is mutated, so a rejected talent can
    /// never leave half of the scalar edit committed (or vice versa).
    /// </summary>
    public static RecorderEditResult ApplyMountEdits(
        ProspectSave prospect,
        int recorderIndex,
        IEnumerable<RecorderFieldRow> fieldRows,
        IReadOnlyList<TalentRow>? talents) =>
        ApplyRecorderEdits(prospect, recorderIndex, fieldRows, talents);

    private static string InferMountRace(string mountType, string mountName)
    {
        var source = $"{mountType} {mountName}";
        if (source.Contains("buffalo", StringComparison.OrdinalIgnoreCase))
        {
            return "Buffalo";
        }
        if (source.Contains("moa", StringComparison.OrdinalIgnoreCase))
        {
            return "Moa";
        }
        if (source.Contains("horse", StringComparison.OrdinalIgnoreCase))
        {
            return "Horse";
        }
        if (source.Contains("raptor", StringComparison.OrdinalIgnoreCase))
        {
            return "Raptor";
        }
        if (source.Contains("slinker", StringComparison.OrdinalIgnoreCase) || source.Contains("chew", StringComparison.OrdinalIgnoreCase))
        {
            return "Slinker";
        }
        if (source.Contains("swampbird", StringComparison.OrdinalIgnoreCase))
        {
            return "SwampBird";
        }
        if (source.Contains("woollymammoth", StringComparison.OrdinalIgnoreCase))
        {
            return "WoollyMammoth";
        }
        if (source.Contains("woolyzebra", StringComparison.OrdinalIgnoreCase))
        {
            return "WoolyZebra";
        }
        if (source.Contains("zebra", StringComparison.OrdinalIgnoreCase))
        {
            return "Zebra";
        }
        if (source.Contains("tusker", StringComparison.OrdinalIgnoreCase))
        {
            return "Tusker";
        }
        return "Unknown";
    }

    public static List<TalentRow> ReadTalentRows(ProspectSave prospect, int recorderIndex)
    {
        var talentRanks = ReadMountTalentRanks(prospect, recorderIndex);
        return talentRanks
            .Select(kvp => new TalentRow
            {
                Name = kvp.Key,
                Rank = kvp.Value,
                DisplayName = MountSpeciesMetadataService.ToDisplayName(kvp.Key),
                IconKey = kvp.Key
            })
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Writes the actual <c>Talents</c> struct array: rank changes update in place, new talents are
    /// appended as copies of the existing struct template, and talents absent from
    /// <paramref name="talents"/> are removed. Duplicate stored names collapse to one row, entries the
    /// editor cannot name are preserved verbatim, and an array that cannot hold structured talents
    /// (or an empty array being extended) returns <see cref="RecorderEditStatus.Unsupported"/>.
    /// Validation happens before any mutation, so a rejected list leaves the document untouched.
    /// </summary>
    public static RecorderEditResult ApplyTalentRows(ProspectSave prospect, int recorderIndex, IReadOnlyList<TalentRow> talents) =>
        ApplyRecorderEdits(prospect, recorderIndex, Array.Empty<RecorderFieldRow>(), talents);

    private sealed record TalentEditPlan(
        ArrayProperty Array,
        List<FProperty> Items,
        List<(PropertiesStruct Structure, int Rank)> RankAssignments,
        int ChangedCount);

    /// <summary>
    /// One mutation path for recorder fields and talents. All field parses, talent names/ranks, and
    /// array structure are validated first; only then are values assigned and the recorder blob
    /// replaced exactly once.
    ///
    /// Transaction boundary: the code before the first <c>AssignPropertyValue</c> call is pure
    /// validation plus staging of new talent structs. Any Invalid/Unsupported result therefore
    /// leaves the deserialized recorder (and the document) byte-for-byte unchanged.
    /// </summary>
    private static RecorderEditResult ApplyRecorderEdits(
        ProspectSave prospect,
        int recorderIndex,
        IEnumerable<RecorderFieldRow> fieldRows,
        IReadOnlyList<TalentRow>? talents)
    {
        if (!TryGetRecorder(prospect, recorderIndex, out var recorderProperties, out var recorderDataProperty))
        {
            return RecorderEditResult.NotFound();
        }

        var fieldIndex = new Dictionary<string, FProperty>(StringComparer.Ordinal);
        IndexEditableFields(recorderProperties, string.Empty, fieldIndex);

        var errors = new List<RecorderFieldError>();
        var pendingFields = ValidateFieldRows(fieldIndex, fieldRows, errors);
        var talentPlan = PlanTalentEdits(recorderProperties, talents, errors, out var unsupportedReason);
        if (errors.Count > 0)
        {
            return RecorderEditResult.Invalid(errors);
        }

        if (unsupportedReason is not null)
        {
            return RecorderEditResult.Unsupported(unsupportedReason);
        }

        // --- transaction body: no validation failures are possible past this point ---
        foreach (var (property, parsed) in pendingFields)
        {
            AssignPropertyValue(property, parsed);
        }

        var talentChanges = 0;
        if (talentPlan is not null)
        {
            foreach (var (structure, rank) in talentPlan.RankAssignments)
            {
                SetTalentRank(structure.Properties, rank);
            }

            talentPlan.Array.Value = talentPlan.Items.ToArray();
            talentChanges = talentPlan.ChangedCount;
        }

        var changed = pendingFields.Count + talentChanges;
        if (changed == 0)
        {
            return RecorderEditResult.Unchanged;
        }

        ReplaceRecorderData(prospect, recorderIndex, recorderProperties, recorderDataProperty);
        return RecorderEditResult.Applied(changed);
    }

    private static List<(FProperty Property, object Parsed)> ValidateFieldRows(
        IReadOnlyDictionary<string, FProperty> fieldIndex,
        IEnumerable<RecorderFieldRow> rows,
        List<RecorderFieldError> errors)
    {
        var rowMap = new Dictionary<string, RecorderFieldRow>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            rowMap[row.Path] = row;
        }

        var pending = new List<(FProperty Property, object Parsed)>();
        foreach (var (path, row) in rowMap)
        {
            if (!row.Editable)
            {
                continue;
            }

            if (!fieldIndex.TryGetValue(path, out var property))
            {
                errors.Add(new RecorderFieldError(path, "This field does not exist in the recorder."));
                continue;
            }

            if (!TryParsePropertyValue(property, row.Value, out var parsed, out var error))
            {
                errors.Add(new RecorderFieldError(path, error));
                continue;
            }

            if (parsed is not null && PropertyValueDiffers(property, parsed))
            {
                pending.Add((property, parsed));
            }
        }

        return pending;
    }

    /// <summary>
    /// Builds the complete talent-array replacement without touching the document. Structural
    /// problems (no array, non-struct array, empty array when adding, no usable template) are reported
    /// through <paramref name="unsupported"/> so the caller can return an explicit Unsupported result
    /// instead of silently appending nothing or dropping data.
    /// </summary>
    private static TalentEditPlan? PlanTalentEdits(
        IReadOnlyList<FPropertyTag> recorderProperties,
        IReadOnlyList<TalentRow>? talents,
        List<RecorderFieldError> errors,
        out string? unsupported)
    {
        unsupported = null;
        if (talents is null)
        {
            return null;
        }

        var requested = new List<TalentRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var talent in talents)
        {
            var name = talent.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                errors.Add(new RecorderFieldError(string.Empty, "A talent row has no name."));
                continue;
            }

            if (!seen.Add(name))
            {
                errors.Add(new RecorderFieldError(name, "Duplicate talent name."));
                continue;
            }

            var maxRank = Math.Max(1, talent.MaxRank);
            if (talent.Rank < 0 || talent.Rank > maxRank)
            {
                errors.Add(new RecorderFieldError(name, $"Rank must be between 0 and {maxRank}."));
                continue;
            }

            requested.Add(talent);
        }

        if (errors.Count > 0)
        {
            return null;
        }

        var talentsTag = recorderProperties.FirstOrDefault(p => p.Name.Value.Equals("Talents", StringComparison.OrdinalIgnoreCase));
        if (talentsTag?.Property is not ArrayProperty talentsArray)
        {
            if (requested.Count == 0)
            {
                return null;
            }

            unsupported = "This recorder has no structured talent array, so talents cannot be added or changed.";
            return null;
        }

        if (!IsStructuredTalentArray(talentsArray))
        {
            unsupported = "This recorder's Talents array does not store structured talent entries, so it cannot be edited without changing the save format.";
            return null;
        }

        var existingItems = talentsArray.Value as Array;
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingSequence = new List<(string? Name, FProperty Item)>();
        var duplicateCount = 0;
        if (existingItems is not null)
        {
            foreach (var item in existingItems)
            {
                if (item is not FProperty property)
                {
                    unsupported = "This recorder's Talents array contains data that cannot be rebuilt without losing it.";
                    return null;
                }

                if (property is StructProperty { Value: PropertiesStruct structure } structProperty)
                {
                    var existingName = ReadStringProperty(structure.Properties, "TalentRowName").Trim();
                    if (existingName.Length > 0)
                    {
                        if (!seenNames.Add(existingName))
                        {
                            // Duplicate rows are dropped deterministically; the first occurrence is
                            // kept and receives the requested rank, so no duplicate rows are written.
                            duplicateCount++;
                            continue;
                        }

                        existingSequence.Add((existingName, structProperty));
                        continue;
                    }
                }

                // A talent entry the editor cannot name (or a non-struct item) is preserved verbatim
                // instead of being dropped when the array is rebuilt.
                existingSequence.Add((null, property));
            }
        }

        var requestedByName = new Dictionary<string, TalentRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var talent in requested)
        {
            requestedByName[talent.Name.Trim()] = talent;
        }

        var items = new List<FProperty>(existingSequence.Count + requested.Count);
        var rankAssignments = new List<(PropertiesStruct Structure, int Rank)>();
        var changed = duplicateCount;

        // Retained talents keep their original array position; deleted rows are counted as changes.
        foreach (var (existingName, item) in existingSequence)
        {
            if (existingName is null)
            {
                items.Add(item);
                continue;
            }

            if (!requestedByName.Remove(existingName, out var talent))
            {
                changed++;
                continue;
            }

            var structure = (PropertiesStruct)((StructProperty)item).Value!;
            if (!TryPlanTalentRank(structure.Properties, talent.Rank, out var differs, out var rankError))
            {
                unsupported = rankError;
                return null;
            }

            if (differs)
            {
                rankAssignments.Add((structure, talent.Rank));
                changed++;
            }

            items.Add(item);
        }

        if (requestedByName.Count > 0)
        {
            var template = FindTalentTemplate(talentsArray);
            if (template is null)
            {
                unsupported = "This recorder's Talents array has no usable talent struct to copy, so a talent cannot be added without losing fields.";
                return null;
            }

            foreach (var talent in requested)
            {
                var name = talent.Name.Trim();
                if (!requestedByName.ContainsKey(name))
                {
                    continue;
                }

                var created = CreateTalentItem(template, name, talent.Rank);
                if (created is null)
                {
                    unsupported = $"The talent template does not expose writable TalentRowName/TalentRank properties, so '{name}' cannot be added.";
                    return null;
                }

                items.Add(created);
                changed++;
            }
        }

        return changed == 0 ? null : new TalentEditPlan(talentsArray, items, rankAssignments, changed);
    }

    private static bool IsStructuredTalentArray(ArrayProperty array)
    {
        if (array.ItemType is not null)
        {
            return string.Equals(array.ItemType.Name.Value, nameof(StructProperty), StringComparison.Ordinal);
        }

        return array.Value is not null && array.Value.GetType().GetElementType() == typeof(FProperty);
    }

    private static StructProperty? FindTalentTemplate(ArrayProperty array)
    {
        if (array.Value is not Array items)
        {
            return null;
        }

        foreach (var item in items)
        {
            if (item is not StructProperty { Value: PropertiesStruct structure } structProperty)
            {
                continue;
            }

            var hasName = structure.Properties.Any(p => p.Name.Value.Equals("TalentRowName", StringComparison.OrdinalIgnoreCase));
            var hasRank = structure.Properties.Any(p => p.Name.Value.Equals("TalentRank", StringComparison.OrdinalIgnoreCase));
            if (hasName && hasRank)
            {
                return structProperty;
            }
        }

        return null;
    }

    private static bool TryPlanTalentRank(
        IEnumerable<FPropertyTag> properties,
        int rank,
        out bool differs,
        out string error)
    {
        differs = false;
        error = string.Empty;
        var tag = properties.FirstOrDefault(p => p.Name.Value.Equals("TalentRank", StringComparison.OrdinalIgnoreCase));
        switch (tag?.Property)
        {
            case IntProperty intProperty:
                differs = intProperty.Value != rank;
                return true;
            case Int64Property int64Property:
                differs = int64Property.Value != rank;
                return true;
            case UInt32Property uint32Property:
                differs = uint32Property.Value != (uint)Math.Max(0, rank);
                return true;
            case UInt64Property uint64Property:
                differs = uint64Property.Value != (ulong)Math.Max(0, rank);
                return true;
            case null:
                error = "TalentRank is missing, so the rank cannot be changed.";
                return false;
            default:
                error = $"TalentRank type {tag.Property.GetType().Name} is not supported.";
                return false;
        }
    }

    private static void SetTalentRank(IEnumerable<FPropertyTag> properties, int rank)
    {
        var tag = properties.FirstOrDefault(p => p.Name.Value.Equals("TalentRank", StringComparison.OrdinalIgnoreCase));
        switch (tag?.Property)
        {
            case IntProperty intProperty:
                intProperty.Value = rank;
                break;
            case Int64Property int64Property:
                int64Property.Value = rank;
                break;
            case UInt32Property uint32Property:
                uint32Property.Value = (uint)Math.Max(0, rank);
                break;
            case UInt64Property uint64Property:
                uint64Property.Value = (ulong)Math.Max(0, rank);
                break;
        }
    }

    /// <summary>
    /// Creates a new talent array item by cloning the whole template struct (including properties the
    /// editor does not understand), then replacing only TalentRowName and TalentRank. Unknown fields
    /// therefore survive for talents added by the dialog.
    /// </summary>
    private static StructProperty? CreateTalentItem(StructProperty template, string name, int rank)
    {
        if (template.Value is not PropertiesStruct templateStructure)
        {
            return null;
        }

        var nameIndex = -1;
        var rankIndex = -1;
        var clonedTags = new List<FPropertyTag>(templateStructure.Properties.Count);
        for (var i = 0; i < templateStructure.Properties.Count; i++)
        {
            var sourceTag = templateStructure.Properties[i];
            var clonedTag = CloneTagForTemplate(sourceTag);
            if (clonedTag is null)
            {
                return null;
            }

            if (sourceTag.Name.Value.Equals("TalentRowName", StringComparison.OrdinalIgnoreCase))
            {
                nameIndex = clonedTags.Count;
            }
            else if (sourceTag.Name.Value.Equals("TalentRank", StringComparison.OrdinalIgnoreCase))
            {
                rankIndex = clonedTags.Count;
            }

            clonedTags.Add(clonedTag);
        }

        if (nameIndex < 0 || rankIndex < 0)
        {
            return null;
        }

        var nameTag = CloneTagWithValue(templateStructure.Properties[nameIndex], name);
        var rankTag = CloneTagWithValue(templateStructure.Properties[rankIndex], rank.ToString(CultureInfo.InvariantCulture));
        if (nameTag is null || rankTag is null)
        {
            return null;
        }

        clonedTags[nameIndex] = nameTag;
        clonedTags[rankIndex] = rankTag;

        var created = new StructProperty(new FString("Talents"))
        {
            StructType = template.StructType,
            StructGuid = template.StructGuid
        };
        created.Value = new PropertiesStruct
        {
            Properties = clonedTags
        };
        return created;
    }

    /// <summary>
    /// Clones one tag of the talent template. Known scalar and nested struct properties are copied;
    /// property shapes this mapper cannot reconstruct are kept by reference because the mapper never
    /// mutates them, which preserves their serialized bytes instead of dropping them.
    /// </summary>
    private static FPropertyTag? CloneTagForTemplate(FPropertyTag source)
    {
        if (source.Property is null)
        {
            return null;
        }

        var clonedProperty = ClonePropertyForTemplate(source);
        return clonedProperty is null ? null : new FPropertyTag(source) { Property = clonedProperty };
    }

    private static FProperty? ClonePropertyForTemplate(FPropertyTag source)
    {
        var property = source.Property;
        if (property is null)
        {
            return null;
        }

        if (property is StructProperty structProperty)
        {
            var clonedStruct = new StructProperty(source.Name)
            {
                StructType = structProperty.StructType?.Clone(),
                StructGuid = structProperty.StructGuid
            };

            if (structProperty.Value is PropertiesStruct nested)
            {
                var nestedTags = new List<FPropertyTag>(nested.Properties.Count);
                foreach (var tag in nested.Properties)
                {
                    var clonedTag = CloneTagForTemplate(tag);
                    if (clonedTag is null)
                    {
                        return null;
                    }

                    nestedTags.Add(clonedTag);
                }

                clonedStruct.Value = new PropertiesStruct { Properties = nestedTags };
            }
            else
            {
                // Unknown/custom struct payload: keep the leaf data object, which this mapper never mutates.
                clonedStruct.Value = structProperty.Value;
            }

            return clonedStruct;
        }

        if (property is EnumProperty enumProperty)
        {
            var clonedEnum = new EnumProperty(source.Name, enumProperty.EnumType?.Clone());
            clonedEnum.Value = enumProperty.Value;
            return clonedEnum;
        }

        if (property is ByteProperty byteProperty)
        {
            var clonedByte = new ByteProperty(source.Name) { EnumType = byteProperty.EnumType?.Clone() };
            clonedByte.Value = byteProperty.Value;
            return clonedByte;
        }

        if (property is BoolProperty or IntProperty or Int64Property or UInt32Property or UInt64Property or
            FloatProperty or DoubleProperty or StrProperty)
        {
            var cloned = FProperty.Create(source.Name, source.Type);
            cloned.Value = property.Value;
            return cloned;
        }

        // Arrays, maps, sets, delegates and unimplemented property types are retained by reference so
        // their exact serialized shape survives; the mapper never writes into them.
        return property;
    }

    private static FPropertyTag? CloneTagWithValue(FPropertyTag source, string value)
    {
        var property = ClonePropertyForTemplate(source);
        if (property is null || ReferenceEquals(property, source.Property))
        {
            // A shared/uncloneable property type cannot be rewritten without mutating the template.
            return null;
        }

        if (!TryParsePropertyValue(property, value, out var parsed, out _) || parsed is null)
        {
            return null;
        }

        AssignPropertyValue(property, parsed);
        return new FPropertyTag(source) { Property = property };
    }

    private static int PickFirstNonZero(int preferred, int fallback) => preferred != 0 ? preferred : fallback;

    private static int FindTalentRank(IReadOnlyDictionary<string, int> talentRanks, params string[] contains)
    {
        foreach (var key in talentRanks.Keys)
        {
            if (contains.Any(token => key.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                return talentRanks[key];
            }
        }

        return 0;
    }

    private static Dictionary<string, int> ReadMountTalentRanks(ProspectSave prospect, int recorderIndex)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetRecorderProperties(prospect, recorderIndex, out var props))
        {
            return map;
        }

        var talentsTag = props.FirstOrDefault(p => p.Name.Value.Equals("Talents", StringComparison.OrdinalIgnoreCase));
        if (talentsTag?.Property is not ArrayProperty { Value: Array array })
        {
            return map;
        }

        foreach (var item in array)
        {
            if (item is not StructProperty { Value: PropertiesStruct talentStruct })
            {
                continue;
            }

            var name = ReadStringProperty(talentStruct.Properties, "TalentRowName");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var rank = ReadIntProperty(talentStruct.Properties, "TalentRank");
            map[name] = rank;
        }

        return map;
    }

    private static string ReadStringProperty(IEnumerable<FPropertyTag> properties, string propertyName)
    {
        var prop = properties.FirstOrDefault(p => p.Name.Value.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
        return prop?.Property switch
        {
            NameProperty n => n.Value?.Value ?? string.Empty,
            StrProperty s => s.Value?.Value ?? string.Empty,
            EnumProperty e => e.Value?.Value ?? string.Empty,
            _ => string.Empty
        };
    }

    private static int ReadIntProperty(IEnumerable<FPropertyTag> properties, string propertyName)
    {
        var prop = properties.FirstOrDefault(p => p.Name.Value.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
        return prop?.Property switch
        {
            IntProperty i => i.Value,
            Int64Property i64 => (int)i64.Value,
            UInt32Property u => (int)u.Value,
            UInt64Property u64 => (int)u64.Value,
            _ => 0
        };
    }

    public static bool RemoveRecordersByIndex(ProspectSave prospect, IEnumerable<int> recorderIndexes)
    {
        var indexes = recorderIndexes.Distinct().OrderByDescending(i => i).ToList();
        var first = prospect.ProspectData.FirstOrDefault();
        var arr = first?.Property as ArrayProperty;
        if (arr?.Value is not FProperty[] props)
        {
            return false;
        }

        if (indexes.Any(index => index < 0 || index >= props.Length))
        {
            return false;
        }

        var list = props.ToList();
        foreach (var index in indexes)
        {
            list.RemoveAt(index);
        }

        arr.Value = list.ToArray();
        return true;
    }

    public static List<RecorderFieldRow> ReadRecorderFields(ProspectSave prospect, int recorderIndex)
    {
        if (!TryGetRecorder(prospect, recorderIndex, out var recorderProperties, out _))
        {
            return [];
        }

        return ReadRecorderFieldsFromProperties(recorderProperties);
    }

    public static List<RecorderFieldRow> ReadRecorderFieldsFromProperties(IEnumerable<FPropertyTag> recorderProperties)
    {
        var rows = new List<RecorderFieldRow>();
        FlattenSimpleFields(recorderProperties, string.Empty, rows);
        return rows.OrderBy(r => r.Path, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Validates every editable row before committing any of them. Invalid values are reported per
    /// field instead of being silently skipped while the rest of the edit is applied.
    /// </summary>
    public static RecorderEditResult ApplyRecorderFieldEdits(ProspectSave prospect, int recorderIndex, IEnumerable<RecorderFieldRow> rows) =>
        ApplyRecorderEdits(prospect, recorderIndex, rows, talents: null);

    public static bool TryGetRecorderProperties(ProspectSave prospect, int recorderIndex, out List<FPropertyTag> recorderProperties)
    {
        recorderProperties = [];
        return TryGetRecorder(prospect, recorderIndex, out recorderProperties, out _);
    }

    public static Dictionary<string, string> ExtractRecorderMetadata(ProspectSave prospect, int recorderIndex)
    {
        var rows = ReadRecorderRows(prospect, _ => true);
        var row = rows.FirstOrDefault(r => r.Index == recorderIndex);
        var fields = ReadRecorderFields(prospect, recorderIndex);
        return row is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Summary"] = BuildMetaSummary(fields) }
            : ExtractRecorderMetadata(row, fields);
    }

    public static Dictionary<string, string> ExtractRecorderMetadata(RecorderRow row, IEnumerable<RecorderFieldRow> fieldsInput)
    {
        var fields = fieldsInput as IReadOnlyList<RecorderFieldRow> ?? fieldsInput.ToList();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Summary"] = BuildMetaSummary(fields)
        };

        switch (row.Category)
        {
            case RecorderCategory.Mount:
                map["Name"] = FirstValueOrDefault(fields, $"{row.ComponentShortName}#{row.Index}", "MountName", "Name");
                map["Type"] = FirstValueOrDefault(fields, row.ComponentShortName, "MountType", "CreatureType");
                map["Level"] = FirstInt(fields, "Level", "CurrentLevel").ToString();
                map["Lineage"] = FirstValueOrDefault(fields, "None", "Lineage");
                break;
            case RecorderCategory.Character:
                map["CharacterName"] = FirstValueOrDefault(fields, "Unknown", "CharacterName", "Name");
                map["PlayerId"] = FirstValueOrDefault(fields, "Unknown", "PlayerID", "AssignedPlayerCharacterID");
                map["Health"] = FirstInt(fields, "Health", "CurrentHealth").ToString();
                break;
            case RecorderCategory.Mission:
                map["MissionName"] = FirstValueOrDefault(fields, "Unknown", "MissionName", "FactionMissionName");
                map["Completed"] = FirstValueOrDefault(fields, "False", "bMissionComplete");
                map["Active"] = FirstValueOrDefault(fields, "Unknown", "ProspectState", "State");
                break;
            case RecorderCategory.Prebuilt:
                map["ActorClass"] = FirstValueOrDefault(fields, "Unknown", "ActorClassName", "ClassName", "Name");
                map["Owner"] = FirstValueOrDefault(fields, "Unknown", "OwnerID", "AssignedPlayerCharacterID");
                break;
            case RecorderCategory.Resource:
                map["ResourceType"] = row.ComponentShortName;
                map["Seed"] = FirstValueOrDefault(fields, "0", "Seed", "GameStateSeed");
                break;
            case RecorderCategory.World:
                map["WorldSystem"] = row.ComponentShortName;
                map["State"] = FirstValueOrDefault(fields, "Unknown", "State", "WeatherState", "ProspectState");
                break;
            case RecorderCategory.AI:
                map["AIType"] = row.ComponentShortName;
                map["ActorName"] = FirstValueOrDefault(fields, "Unknown", "Name", "ActorName");
                break;
            case RecorderCategory.Systems:
                map["System"] = row.ComponentShortName;
                map["State"] = FirstValueOrDefault(fields, "Unknown", "State", "ProspectState");
                break;
            case RecorderCategory.Structures:
                map["Structure"] = row.ComponentShortName;
                map["Location"] = FirstValueOrDefault(fields, "Unknown", "Location", "WorldLocation");
                break;
            case RecorderCategory.Security:
                map["SecuritySystem"] = row.ComponentShortName;
                map["Locked"] = FirstValueOrDefault(fields, "Unknown", "bLocked", "IsLocked");
                break;
            case RecorderCategory.Containers:
                map["ContainerSystem"] = row.ComponentShortName;
                map["ItemCount"] = FirstValueOrDefault(fields, "0", "ItemCount", "NumItems");
                break;
        }

        return map;
    }

    private static IEnumerable<(int Index, string ComponentClass, int PropertyCount)> EnumerateRecorderMeta(ProspectSave prospect)
    {
        RecorderMetadataDecodeCount++;
        var first = prospect.ProspectData.FirstOrDefault();
        var array = first?.Property as ArrayProperty;
        if (array?.Value is not FProperty[] recorderProps)
        {
            yield break;
        }

        for (var i = 0; i < recorderProps.Length; i++)
        {
            if (recorderProps[i] is not StructProperty structProperty || structProperty.Value is not PropertiesStruct recorderValue)
            {
                continue;
            }

            if (recorderValue.Properties.Count < 2)
            {
                continue;
            }

            var className = (recorderValue.Properties[0].Property?.Value as FString)?.Value ?? "<unknown>";
            var dataProperty = recorderValue.Properties[1];
            var propertyCount = 0;
            try
            {
                propertyCount = ProspectSerlializationUtil.DeserializeRecorderData(dataProperty).Count;
            }
            catch
            {
                propertyCount = 0;
            }

            yield return (i, className, propertyCount);
        }
    }

    private static bool TryGetRecorder(
        ProspectSave prospect,
        int recorderIndex,
        out List<FPropertyTag> recorderProperties,
        out FPropertyTag recorderDataProperty)
    {
        recorderProperties = [];
        recorderDataProperty = FPropertyTag.NoneProperty;

        var first = prospect.ProspectData.FirstOrDefault();
        var array = first?.Property as ArrayProperty;
        if (array?.Value is not FProperty[] recorderProps || recorderIndex < 0 || recorderIndex >= recorderProps.Length)
        {
            return false;
        }

        if (recorderProps[recorderIndex] is not StructProperty structProperty || structProperty.Value is not PropertiesStruct recorderValue)
        {
            return false;
        }

        if (recorderValue.Properties.Count < 2)
        {
            return false;
        }

        RecorderFieldDecodeCount++;
        recorderDataProperty = recorderValue.Properties[1];
        recorderProperties = ProspectSerlializationUtil.DeserializeRecorderData(recorderDataProperty).ToList();
        return true;
    }

    private static void ReplaceRecorderData(
        ProspectSave prospect,
        int recorderIndex,
        IEnumerable<FPropertyTag> recorderProperties,
        FPropertyTag originalRecorderDataProperty)
    {
        var first = prospect.ProspectData.FirstOrDefault();
        var array = first?.Property as ArrayProperty;
        if (array?.Value is not FProperty[] recorderProps)
        {
            return;
        }

        if (recorderProps[recorderIndex] is not StructProperty structProperty || structProperty.Value is not PropertiesStruct recorderValue)
        {
            return;
        }

        recorderValue.Properties[1] = ProspectSerlializationUtil.SerializeRecorderData(originalRecorderDataProperty, recorderProperties);
    }

    private static void IndexEditableFields(IEnumerable<FPropertyTag> properties, string prefix, IDictionary<string, FProperty> output)
    {
        foreach (var propertyTag in properties)
        {
            var path = string.IsNullOrEmpty(prefix) ? propertyTag.Name.Value : $"{prefix}.{propertyTag.Name.Value}";
            if (propertyTag.Property is StructProperty structProperty && structProperty.Value is PropertiesStruct nested)
            {
                IndexEditableFields(nested.Properties, path, output);
                continue;
            }

            if (IsEditableProperty(propertyTag.Property))
            {
                output[path] = propertyTag.Property!;
            }
        }
    }

    private static bool IsEditableProperty(FProperty? property) => property switch
    {
        BoolProperty or IntProperty or Int64Property or UInt32Property or UInt64Property or
        FloatProperty or DoubleProperty or NameProperty or StrProperty or EnumProperty => true,
        _ => false
    };

    private static bool IsSupportedFieldTypeName(string propertyType) => propertyType is
        nameof(BoolProperty) or nameof(IntProperty) or nameof(Int64Property) or nameof(UInt32Property) or
        nameof(UInt64Property) or nameof(FloatProperty) or nameof(DoubleProperty) or nameof(NameProperty) or
        nameof(StrProperty) or nameof(EnumProperty);

    private static void FlattenSimpleFields(IEnumerable<FPropertyTag> properties, string prefix, IList<RecorderFieldRow> output)
    {
        foreach (var propertyTag in properties)
        {
            var path = string.IsNullOrEmpty(prefix) ? propertyTag.Name.Value : $"{prefix}.{propertyTag.Name.Value}";
            switch (propertyTag.Property)
            {
                case BoolProperty boolProperty:
                    output.Add(new RecorderFieldRow { Path = path, PropertyType = nameof(BoolProperty), Value = boolProperty.Value.ToString(), Editable = true });
                    break;
                case IntProperty intProperty:
                    output.Add(new RecorderFieldRow { Path = path, PropertyType = nameof(IntProperty), Value = intProperty.Value.ToString(), Editable = true });
                    break;
                case Int64Property int64Property:
                    output.Add(new RecorderFieldRow { Path = path, PropertyType = nameof(Int64Property), Value = int64Property.Value.ToString(), Editable = true });
                    break;
                case UInt32Property uint32Property:
                    output.Add(new RecorderFieldRow { Path = path, PropertyType = nameof(UInt32Property), Value = uint32Property.Value.ToString(), Editable = true });
                    break;
                case UInt64Property uint64Property:
                    output.Add(new RecorderFieldRow { Path = path, PropertyType = nameof(UInt64Property), Value = uint64Property.Value.ToString(), Editable = true });
                    break;
                case FloatProperty floatProperty:
                    output.Add(new RecorderFieldRow { Path = path, PropertyType = nameof(FloatProperty), Value = floatProperty.Value.ToString("R"), Editable = true });
                    break;
                case DoubleProperty doubleProperty:
                    output.Add(new RecorderFieldRow { Path = path, PropertyType = nameof(DoubleProperty), Value = doubleProperty.Value.ToString("R"), Editable = true });
                    break;
                case NameProperty nameProperty:
                    output.Add(new RecorderFieldRow { Path = path, PropertyType = nameof(NameProperty), Value = nameProperty.Value?.Value ?? string.Empty, Editable = true });
                    break;
                case StrProperty strProperty:
                    output.Add(new RecorderFieldRow { Path = path, PropertyType = nameof(StrProperty), Value = strProperty.Value?.Value ?? string.Empty, Editable = true });
                    break;
                case EnumProperty enumProperty:
                    output.Add(new RecorderFieldRow { Path = path, PropertyType = nameof(EnumProperty), Value = enumProperty.Value?.Value ?? string.Empty, Editable = true });
                    break;
                case StructProperty structProperty when structProperty.Value is PropertiesStruct nestedStruct:
                    output.Add(new RecorderFieldRow { Path = path, PropertyType = nameof(StructProperty), Value = $"{nestedStruct.Properties.Count} nested fields", Editable = false });
                    FlattenSimpleFields(nestedStruct.Properties, path, output);
                    break;
                default:
                    var value = propertyTag.Property?.Value?.ToString() ?? "<unsupported>";
                    output.Add(new RecorderFieldRow { Path = path, PropertyType = propertyTag.Type.Name, Value = value, Editable = false });
                    break;
            }
        }
    }

    private static string NormalizeComponentShortName(string componentClass)
    {
        var shortName = componentClass.Split('/').LastOrDefault() ?? componentClass;
        return shortName.Replace("RecorderComponent", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildMetaSummary(IEnumerable<RecorderFieldRow> fields)
    {
        var bits = new List<string>();
        var name = FirstValue(fields, "Name", "MountName", "MissionName", "FactionMissionName");
        if (!string.IsNullOrWhiteSpace(name))
        {
            bits.Add(name);
        }

        var state = FirstValue(fields, "State", "ProspectState");
        if (!string.IsNullOrWhiteSpace(state))
        {
            bits.Add($"state={state}");
        }

        var level = FirstInt(fields, "Level", "CurrentLevel");
        if (level > 0)
        {
            bits.Add($"lvl {level}");
        }

        return bits.Count == 0 ? "No extracted metadata" : string.Join(" | ", bits);
    }

    private static string FirstValue(IEnumerable<RecorderFieldRow> rows, params string[] names)
    {
        var row = rows.FirstOrDefault(r => names.Any(n => r.Path.EndsWith(n, StringComparison.OrdinalIgnoreCase)));
        return row?.Value ?? string.Empty;
    }

    private static string FirstNonEmptyValue(IEnumerable<RecorderFieldRow> rows, params string[] names)
    {
        var nonEmpty = rows
            .Where(r => names.Any(n => r.Path.EndsWith(n, StringComparison.OrdinalIgnoreCase)))
            .Select(r => r.Value?.Trim() ?? string.Empty)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        return nonEmpty ?? string.Empty;
    }

    private static string FirstValueOrDefault(IEnumerable<RecorderFieldRow> rows, string defaultValue, params string[] names)
    {
        var row = rows.FirstOrDefault(r => names.Any(n => r.Path.EndsWith(n, StringComparison.OrdinalIgnoreCase)));
        return row?.Value ?? defaultValue;
    }

    private static int FirstInt(IEnumerable<RecorderFieldRow> rows, params string[] names)
    {
        var value = FirstValueOrDefault(rows, "0", names);
        return int.TryParse(value, out var i) ? i : 0;
    }

    private static bool TryParsePropertyValue(FProperty property, string? raw, out object? parsed, out string error)
    {
        parsed = null;
        error = string.Empty;
        var text = raw?.Trim() ?? string.Empty;

        switch (property)
        {
            case BoolProperty:
                if (bool.TryParse(text, out var boolean))
                {
                    parsed = boolean;
                    return true;
                }

                error = "expected true or false";
                return false;
            case IntProperty:
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                {
                    parsed = intValue;
                    return true;
                }

                error = "expected a whole number between -2,147,483,648 and 2,147,483,647";
                return false;
            case Int64Property:
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
                {
                    parsed = longValue;
                    return true;
                }

                error = "expected a whole 64-bit number";
                return false;
            case UInt32Property:
                if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uintValue))
                {
                    parsed = uintValue;
                    return true;
                }

                error = "expected a whole number between 0 and 4,294,967,295";
                return false;
            case UInt64Property:
                if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ulongValue))
                {
                    parsed = ulongValue;
                    return true;
                }

                error = "expected a whole number between 0 and 18,446,744,073,709,551,615";
                return false;
            case FloatProperty:
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
                {
                    parsed = floatValue;
                    return true;
                }

                error = "expected a number";
                return false;
            case DoubleProperty:
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
                {
                    parsed = doubleValue;
                    return true;
                }

                error = "expected a number";
                return false;
            case NameProperty or StrProperty:
                parsed = text;
                return true;
            case EnumProperty:
                if (text.Length == 0)
                {
                    error = "expected an enum value";
                    return false;
                }

                parsed = text;
                return true;
            default:
                error = $"type {property.GetType().Name} is not editable here";
                return false;
        }
    }

    private static void AssignPropertyValue(FProperty property, object parsed)
    {
        switch (property)
        {
            case BoolProperty boolProperty:
                boolProperty.Value = (bool)parsed;
                break;
            case IntProperty intProperty:
                intProperty.Value = (int)parsed;
                break;
            case Int64Property int64Property:
                int64Property.Value = (long)parsed;
                break;
            case UInt32Property uint32Property:
                uint32Property.Value = (uint)parsed;
                break;
            case UInt64Property uint64Property:
                uint64Property.Value = (ulong)parsed;
                break;
            case FloatProperty floatProperty:
                floatProperty.Value = (float)parsed;
                break;
            case DoubleProperty doubleProperty:
                doubleProperty.Value = (double)parsed;
                break;
            case NameProperty nameProperty:
                nameProperty.Value = new FString((string)parsed);
                break;
            case StrProperty strProperty:
                strProperty.Value = new FString((string)parsed);
                break;
            case EnumProperty enumProperty:
                enumProperty.Value = new FString((string)parsed);
                break;
        }
    }

    private static bool PropertyValueDiffers(FProperty property, object? parsed) => property switch
    {
        BoolProperty p => p.Value != (bool)parsed!,
        IntProperty p => p.Value != (int)parsed!,
        Int64Property p => p.Value != (long)parsed!,
        UInt32Property p => p.Value != (uint)parsed!,
        UInt64Property p => p.Value != (ulong)parsed!,
        FloatProperty p => Math.Abs(p.Value - (float)parsed!) >= 0.000001f,
        DoubleProperty p => Math.Abs(p.Value - (double)parsed!) >= 0.0000001d,
        NameProperty p => !string.Equals(p.Value?.Value, (string)parsed!, StringComparison.Ordinal),
        StrProperty p => !string.Equals(p.Value?.Value, (string)parsed!, StringComparison.Ordinal),
        EnumProperty p => !string.Equals(p.Value?.Value, (string)parsed!, StringComparison.Ordinal),
        _ => true
    };
}
