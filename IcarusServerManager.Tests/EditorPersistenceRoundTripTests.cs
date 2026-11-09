using IcarusProspectEditor.Mapping;
using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

/// <summary>
/// End-to-end persistence evidence: edit each supported class of data through the mapper, save the
/// encoded prospect, reload it, and confirm both the edits and the untouched data survive.
/// Uses a temporary copy of the shared fixture; never a real save.
/// </summary>
public sealed class EditorPersistenceRoundTripTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "IcarusEditorRoundTrip", Guid.NewGuid().ToString("N"));
    private readonly string _fixture = TestData.ResolveFile("SufferingResort.json");

    public EditorPersistenceRoundTripTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }
        catch
        {
            // Best-effort cleanup of the test-owned directory.
        }
    }

    [Fact]
    public void EditedMetadataMembersSettingsMountAndTalents_SurviveSaveAndReload()
    {
        var path = CopyFixture();
        var document = ProspectLoadService.Load(path);
        var originalRecorders = ProspectModelMapper.ReadRecorderRows(document.Prospect, _ => true)
            .Select(r => (r.Index, r.ComponentClass))
            .ToList();

        var info = document.Prospect.ProspectInfo;
        info.LobbyName = "Round-trip Lobby";
        info.Difficulty = "hard";
        info.SelectedDropPoint = 3;
        info.Insurance = true;
        document.Prospect.ProspectInfo = info;

        ProspectModelMapper.ApplyMembers(
            document.Prospect,
            new[]
            {
                new MemberRow
                {
                    AccountName = "RoundTripAccount",
                    CharacterName = "RoundTripCharacter",
                    UserID = "76561190000000001",
                    ChrSlot = 0,
                    Status = "Active"
                }
            });

        ProspectModelMapper.ApplyCustomSettings(
            document.Prospect,
            new[] { new CustomSettingRow { SettingRowName = "RegressionRoundTrip", SettingValue = 42 } });

        var mount = ProspectModelMapper.ReadMountsFromProspect(document.Prospect).First();
        var fields = ProspectModelMapper.ReadRecorderFields(document.Prospect, mount.RecorderIndex);
        var editedMount = mount.Clone();
        editedMount.Level = Math.Min(999, editedMount.Level + 1);
        editedMount.MountName = $"{editedMount.MountName} RT";

        var mountValues = ProspectModelMapper.BuildMountFieldValues(editedMount, fields);
        var editRows = mountValues.Select(kv => new RecorderFieldRow
        {
            Path = kv.Key,
            PropertyType = string.Empty,
            Value = kv.Value,
            Editable = true
        }).ToList();

        var talents = ProspectModelMapper.ReadTalentRows(document.Prospect, mount.RecorderIndex);
        Assert.NotEmpty(talents);
        var expectedRank = Math.Min(talents[0].MaxRank, talents[0].Rank + 1);
        talents[0].Rank = expectedRank;
        var expectedTalentName = talents[0].Name;

        var applied = ProspectModelMapper.ApplyMountEdits(document.Prospect, mount.RecorderIndex, editRows, talents);
        Assert.True(applied.Succeeded, applied.Describe());

        var saved = ProspectSaveService.SaveDocument(document);
        Assert.True(saved.Succeeded, saved.Error);
        Assert.NotNull(saved.BackupPath);

        var reloaded = ProspectLoadService.Load(path);

        Assert.Equal("Round-trip Lobby", reloaded.Prospect.ProspectInfo.LobbyName);
        Assert.Equal("hard", reloaded.Prospect.ProspectInfo.Difficulty);
        Assert.Equal(3, reloaded.Prospect.ProspectInfo.SelectedDropPoint);
        Assert.True(reloaded.Prospect.ProspectInfo.Insurance);

        var members = reloaded.Prospect.ProspectInfo.AssociatedMembers;
        Assert.Contains(members, m => m.UserID == "76561190000000001");

        var settings = reloaded.Prospect.ProspectInfo.CustomSettings;
        Assert.Contains(settings, s => s.SettingRowName == "RegressionRoundTrip" && s.SettingValue == 42);

        var reloadedMount = ProspectModelMapper.ReadMountsFromProspect(reloaded.Prospect)
            .Single(m => m.RecorderIndex == mount.RecorderIndex);
        Assert.Equal(editedMount.Level, reloadedMount.Level);
        Assert.Contains("RT", reloadedMount.MountName, StringComparison.Ordinal);

        var reloadedTalents = ProspectModelMapper.ReadTalentRows(reloaded.Prospect, mount.RecorderIndex);
        Assert.Equal(expectedRank, reloadedTalents.Single(t => t.Name == expectedTalentName).Rank);

        var afterRecorders = ProspectModelMapper.ReadRecorderRows(reloaded.Prospect, _ => true)
            .Select(r => (r.Index, r.ComponentClass))
            .ToList();
        Assert.Equal(originalRecorders, afterRecorders);
    }

    [Fact]
    public void FailedTalentEdit_DoesNotLeavePartialScalarChangesInTheSavedFile()
    {
        var path = CopyFixture();
        var document = ProspectLoadService.Load(path);
        var before = ProspectSaveService.Serialize(document.Prospect);

        var mount = ProspectModelMapper.ReadMountsFromProspect(document.Prospect).First();
        var fields = ProspectModelMapper.ReadRecorderFields(document.Prospect, mount.RecorderIndex);
        var editedMount = mount.Clone();
        editedMount.Level = Math.Min(999, editedMount.Level + 2);
        var editRows = ProspectModelMapper.BuildMountFieldValues(editedMount, fields)
            .Select(kv => new RecorderFieldRow
            {
                Path = kv.Key,
                PropertyType = string.Empty,
                Value = kv.Value,
                Editable = true
            })
            .ToList();

        var invalidTalents = new List<TalentRow>
        {
            new() { Name = "Regression_Invalid_Rank", Rank = 999, MaxRank = 10 }
        };

        var result = ProspectModelMapper.ApplyMountEdits(document.Prospect, mount.RecorderIndex, editRows, invalidTalents);
        Assert.False(result.Succeeded);

        // The rejected edit must leave the recorder bytes untouched, so the level change is not saved
        // and the document still equals its pre-edit state.
        Assert.Equal(before, ProspectSaveService.Serialize(document.Prospect));
    }

    private string CopyFixture()
    {
        var path = Path.Combine(_dir, "prospect.json");
        File.Copy(_fixture, path, overwrite: true);
        return path;
    }
}
