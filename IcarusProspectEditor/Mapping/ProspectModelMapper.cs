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

    public static List<MountRow> ReadMountsFromProspect(ProspectSave prospect)
    {
        var mounts = new List<MountRow>();
        foreach (var recorder in ReadRecorderRowsByCategory(prospect, RecorderCategory.Mount))
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

    public static bool ApplyMountFromProspect(ProspectSave prospect, MountRow row, IEnumerable<TalentRow>? talents = null)
    {
        var fields = ReadRecorderFields(prospect, row.RecorderIndex);
        var map = fields.ToDictionary(f => f.Path, StringComparer.Ordinal);

        UpdateNamedField(map, "MountName", row.MountName);
        UpdateNamedField(map, "OwnerCharacterID.PlayerID", row.OwnerPlayerId);
        UpdateNamedField(map, "OwnerCharacterID.ChrSlot", row.OwnerCharacterSlot.ToString());
        UpdateNamedField(map, "OwnerName", row.OwnerName);
        var clampedLevel = Math.Clamp(row.Level, 0, 999);
        var clampedXp = MountSpeciesMetadataService.ClampRiskyInt(row.Experience);
        var clampedHealth = MountSpeciesMetadataService.ClampRiskyInt(row.Health);
        var variationDomain = MountSpeciesMetadataService.GetVariationDomain(row.MountRace);
        var clampedVariation = variationDomain.Contains(row.Variation) ? row.Variation : variationDomain.FirstOrDefault();

        UpdateNamedField(map, "Level", clampedLevel.ToString());
        UpdateNamedField(map, "CurrentLevel", clampedLevel.ToString());
        UpdateNamedField(map, "Experience", clampedXp.ToString());
        UpdateNamedField(map, "XP", clampedXp.ToString());
        UpdateNamedField(map, "Health", clampedHealth.ToString());
        UpdateNamedField(map, "CurrentHealth", clampedHealth.ToString());
        UpdateNamedField(map, "Sex", row.Sex);
        UpdateNamedField(map, "Lineage", row.Lineage);
        UpdateNamedField(map, "MountType", row.MountType);
        UpdateNamedField(map, "CreatureType", row.MountType);
        UpdateNamedField(map, "MountIconName", row.MountIconName);
        UpdateNamedField(map, "AISetupRowName", row.AiSetupRowName);
        UpdateNamedField(map, "ActorClassName", row.ActorClassName);
        UpdateNamedField(map, "ActorPathName", row.ActorPathName);
        UpdateNamedField(map, "Variation", clampedVariation.ToString());
        UpdateNamedField(map, "MountVariation", clampedVariation.ToString());
        UpdateNamedField(map, "Vitality", MountSpeciesMetadataService.ClampGenetic(row.Vitality).ToString());
        UpdateNamedField(map, "Endurance", MountSpeciesMetadataService.ClampGenetic(row.Endurance).ToString());
        UpdateNamedField(map, "Muscle", MountSpeciesMetadataService.ClampGenetic(row.Muscle).ToString());
        UpdateNamedField(map, "Agility", MountSpeciesMetadataService.ClampGenetic(row.Agility).ToString());
        UpdateNamedField(map, "Toughness", MountSpeciesMetadataService.ClampGenetic(row.Toughness).ToString());
        UpdateNamedField(map, "Hardiness", MountSpeciesMetadataService.ClampGenetic(row.Hardiness).ToString());
        UpdateNamedField(map, "Utility", MountSpeciesMetadataService.ClampGenetic(row.Utility).ToString());

        if (talents is not null)
        {
            foreach (var talent in talents)
            {
                UpdateNamedField(map, talent.Name, talent.Rank.ToString());
            }
        }

        return ApplyRecorderFieldEdits(prospect, row.RecorderIndex, map.Values);
    }

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

        var list = props.ToList();
        foreach (var index in indexes)
        {
            if (index < 0 || index >= list.Count)
            {
                continue;
            }
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

    public static bool ApplyRecorderFieldEdits(ProspectSave prospect, int recorderIndex, IEnumerable<RecorderFieldRow> rows)
    {
        if (!TryGetRecorder(prospect, recorderIndex, out var recorderProperties, out var recorderDataProperty))
        {
            return false;
        }

        var rowMap = rows.ToDictionary(r => r.Path, StringComparer.Ordinal);
        var anyChanged = ApplyEditsRecursive(recorderProperties, string.Empty, rowMap);
        if (!anyChanged)
        {
            return true;
        }

        ReplaceRecorderData(prospect, recorderIndex, recorderProperties, recorderDataProperty);
        return true;
    }

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

    private static bool ApplyEditsRecursive(IList<FPropertyTag> properties, string prefix, IDictionary<string, RecorderFieldRow> rowMap)
    {
        var changed = false;
        foreach (var propertyTag in properties)
        {
            var path = string.IsNullOrEmpty(prefix) ? propertyTag.Name.Value : $"{prefix}.{propertyTag.Name.Value}";
            if (propertyTag.Property is StructProperty structProperty && structProperty.Value is PropertiesStruct nested)
            {
                changed |= ApplyEditsRecursive(nested.Properties, path, rowMap);
                continue;
            }

            if (!rowMap.TryGetValue(path, out var row) || !row.Editable)
            {
                continue;
            }

            changed |= TryApplyValue(propertyTag.Property, row.Value);
        }
        return changed;
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

    private static void UpdateNamedField(IDictionary<string, RecorderFieldRow> rows, string name, string value)
    {
        var key = rows.Keys.FirstOrDefault(k => k.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        if (key is null)
        {
            return;
        }

        rows[key].Value = value;
    }

    private static bool TryApplyValue(FProperty? property, string value)
    {
        switch (property)
        {
            case BoolProperty boolProperty when bool.TryParse(value, out var b):
                if (boolProperty.Value == b) return false;
                boolProperty.Value = b;
                return true;
            case IntProperty intProperty when int.TryParse(value, out var i):
                if (intProperty.Value == i) return false;
                intProperty.Value = i;
                return true;
            case Int64Property int64Property when long.TryParse(value, out var i64):
                if (int64Property.Value == i64) return false;
                int64Property.Value = i64;
                return true;
            case UInt32Property uint32Property when uint.TryParse(value, out var u32):
                if (uint32Property.Value == u32) return false;
                uint32Property.Value = u32;
                return true;
            case UInt64Property uint64Property when ulong.TryParse(value, out var u64):
                if (uint64Property.Value == u64) return false;
                uint64Property.Value = u64;
                return true;
            case FloatProperty floatProperty when float.TryParse(value, out var f):
                if (Math.Abs(floatProperty.Value - f) < 0.000001f) return false;
                floatProperty.Value = f;
                return true;
            case DoubleProperty doubleProperty when double.TryParse(value, out var d):
                if (Math.Abs(doubleProperty.Value - d) < 0.0000001d) return false;
                doubleProperty.Value = d;
                return true;
            case NameProperty nameProperty:
                var nameText = new FString(value ?? string.Empty);
                if (nameProperty.Value?.Value == nameText.Value) return false;
                nameProperty.Value = nameText;
                return true;
            case StrProperty strProperty:
                var text = new FString(value ?? string.Empty);
                if (strProperty.Value?.Value == text.Value) return false;
                strProperty.Value = text;
                return true;
            case EnumProperty enumProperty:
                var enumText = new FString(value ?? string.Empty);
                if (enumProperty.Value?.Value == enumText.Value) return false;
                enumProperty.Value = enumText;
                return true;
            default:
                return false;
        }
    }
}
