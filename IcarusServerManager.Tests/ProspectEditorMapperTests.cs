using IcarusProspectEditor.Mapping;
using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using IcarusSaveLib;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ProspectEditorMapperTests
{
    [Fact]
    public void ApplyMembers_MapsRowsToProspectInfo()
    {
        var prospect = new ProspectSave();
        var rows = new[]
        {
            new MemberRow
            {
                AccountName = "acc",
                CharacterName = "char",
                UserID = "76561190000000000",
                ChrSlot = 0,
                Experience = 12,
                Status = "InWorld",
                Settled = true,
                IsCurrentlyPlaying = false
            }
        };

        ProspectModelMapper.ApplyMembers(prospect, rows);

        Assert.Single(prospect.ProspectInfo.AssociatedMembers);
        Assert.Equal("char", prospect.ProspectInfo.AssociatedMembers[0].CharacterName);
        Assert.Equal("76561190000000000", prospect.ProspectInfo.AssociatedMembers[0].UserID);
    }

    [Fact]
    public void ClassifyRecorderComponent_MapsBroadBuckets()
    {
        Assert.Equal(RecorderCategory.Resource, ProspectModelMapper.ClassifyRecorderComponent("/Script/Icarus.OilGeyserRecorderComponent"));
        Assert.Equal(RecorderCategory.World, ProspectModelMapper.ClassifyRecorderComponent("/Script/Icarus.WeatherControllerRecorderComponent"));
        Assert.Equal(RecorderCategory.Systems, ProspectModelMapper.ClassifyRecorderComponent("/Script/Icarus.IcarusQuestManagerRecorderComponent"));
        Assert.Equal(RecorderCategory.AI, ProspectModelMapper.ClassifyRecorderComponent("/Script/Icarus.CaveAIRecorderComponent"));
        Assert.Equal(RecorderCategory.Security, ProspectModelMapper.ClassifyRecorderComponent("/Script/Icarus.SecurityDoorRecorderComponent"));
        Assert.Equal(RecorderCategory.Containers, ProspectModelMapper.ClassifyRecorderComponent("/Script/Icarus.IcarusContainerManagerRecorderComponent"));
    }

    [Fact]
    public void ApplyCustomSettings_MapsRowsToProspectInfo()
    {
        var prospect = new ProspectSave();
        var rows = new[]
        {
            new CustomSettingRow
            {
                SettingRowName = "XPScale",
                SettingValue = 2
            }
        };

        ProspectModelMapper.ApplyCustomSettings(prospect, rows);

        Assert.Single(prospect.ProspectInfo.CustomSettings);
        Assert.Equal("XPScale", prospect.ProspectInfo.CustomSettings[0].SettingRowName);
        Assert.Equal(2, prospect.ProspectInfo.CustomSettings[0].SettingValue);
    }

    [Fact]
    public void ReadTalentRows_ReturnsOnlyNumericTalentLikeFields()
    {
        var sourcePath = TestData.ResolveFile("SufferingResort.json");
        var loaded = ProspectLoadService.Load(sourcePath);
        var mounts = ProspectModelMapper.ReadRecorderRowsByCategory(loaded.Prospect, RecorderCategory.Mount);
        Assert.NotEmpty(mounts);

        var talentRows = ProspectModelMapper.ReadTalentRows(loaded.Prospect, mounts[0].Index);
        Assert.All(talentRows, t => Assert.True(t.Rank >= 0));
    }

    [Fact]
    public void ApplyMountFromProspect_UpdatesMountAndPersistsRoundtripFields()
    {
        var sourcePath = TestData.ResolveFile("SufferingResort.json");
        var loaded = ProspectLoadService.Load(sourcePath);
        var mounts = ProspectModelMapper.ReadMountsFromProspect(loaded.Prospect);
        Assert.NotEmpty(mounts);

        var mount = mounts[0];
        mount.MountRace = "Buffalo";
        mount.MountType = "Mount_Buffalo";
        mount.Level = 22;
        mount.OwnerPlayerId = mount.OwnerPlayerId.Length == 0 ? "76561190000000000" : mount.OwnerPlayerId;
        mount.Variation = 0;
        var talents = ProspectModelMapper.ReadTalentRows(loaded.Prospect, mount.RecorderIndex);

        var applied = ProspectModelMapper.ApplyMountFromProspect(loaded.Prospect, mount, talents);
        Assert.True(applied);

        var refreshed = ProspectModelMapper.ReadMountsFromProspect(loaded.Prospect)
            .FirstOrDefault(x => x.RecorderIndex == mount.RecorderIndex);
        Assert.NotNull(refreshed);
        Assert.Equal(22, refreshed!.Level);
    }

}
