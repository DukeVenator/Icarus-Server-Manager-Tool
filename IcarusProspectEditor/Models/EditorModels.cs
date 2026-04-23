using IcarusSaveLib;

namespace IcarusProspectEditor.Models;

internal enum RecorderCategory
{
    Unknown,
    Character,
    Mount,
    Mission,
    Prebuilt,
    Resource,
    World,
    AI,
    Systems,
    Structures,
    Security,
    Containers
}

internal sealed class ProspectDocument
{
    public required string ProspectPath { get; init; }
    public required ProspectSave Prospect { get; init; }
}

internal sealed class RecorderRow
{
    public int Index { get; set; }
    public string ComponentClass { get; set; } = string.Empty;
    public string ComponentShortName { get; set; } = string.Empty;
    public RecorderCategory Category { get; set; }
    public int PropertyCount { get; set; }
    public bool IsDangerous { get; set; }
    public string MetaSummary { get; set; } = string.Empty;
}

internal sealed class RecorderFieldRow
{
    public string Path { get; set; } = string.Empty;
    public string PropertyType { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Editable { get; set; }
}

internal sealed class MemberRow
{
    public string AccountName { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public string UserID { get; set; } = string.Empty;
    public int ChrSlot { get; set; }
    public int Experience { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Settled { get; set; }
    public bool IsCurrentlyPlaying { get; set; }
}

internal sealed class CustomSettingRow
{
    public string SettingRowName { get; set; } = string.Empty;
    public int SettingValue { get; set; }
}

internal sealed class MountRow
{
    public int RecorderIndex { get; set; }
    public string RecorderPath { get; set; } = string.Empty;
    public string MountName { get; set; } = string.Empty;
    public string OwnerPlayerId { get; set; } = string.Empty;
    public int OwnerCharacterSlot { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public int Level { get; set; }
    public int Experience { get; set; }
    public int Health { get; set; }
    public string Sex { get; set; } = string.Empty;
    public string Lineage { get; set; } = string.Empty;
    public string MountType { get; set; } = string.Empty;
    public string MountRace { get; set; } = string.Empty;
    public int Variation { get; set; }
    public int Vitality { get; set; }
    public int Endurance { get; set; }
    public int Muscle { get; set; }
    public int Agility { get; set; }
    public int Toughness { get; set; }
    public int Hardiness { get; set; }
    public int Utility { get; set; }
    public string MountIconName { get; set; } = string.Empty;
    public string AiSetupRowName { get; set; } = string.Empty;
    public string ActorClassName { get; set; } = string.Empty;
    public string ActorPathName { get; set; } = string.Empty;
}

internal sealed class TalentRow
{
    public string Name { get; set; } = string.Empty;
    public int Rank { get; set; }
    public int MaxRank { get; set; } = 10;
    public string DisplayName { get; set; } = string.Empty;
    public string IconKey { get; set; } = string.Empty;
    public string RemapStatus { get; set; } = "Unchanged";
}
