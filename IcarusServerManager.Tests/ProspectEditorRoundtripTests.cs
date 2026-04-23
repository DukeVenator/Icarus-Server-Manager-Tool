using IcarusProspectEditor.Services;
using IcarusSaveLib;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ProspectEditorRoundtripTests
{
    [Fact]
    public void SaveDocument_CreatesBackupAndWritesProspect()
    {
        var tempDir = Directory.CreateTempSubdirectory("prospect-editor-tests");
        var path = Path.Combine(tempDir.FullName, "test.json");

        var prospect = new ProspectSave();
        prospect.ProspectInfo = new FProspectInfo
        {
            LobbyName = "TestLobby",
            ProspectID = "Prospect_Test",
            Difficulty = "medium",
            SelectedDropPoint = 1,
            AssociatedMembers = [],
            CustomSettings = []
        };
        ProspectSaveService.SaveProspect(prospect, path);

        var loaded = ProspectLoadService.Load(path);
        ProspectSaveService.SaveDocument(loaded, createBackup: true);

        Assert.True(File.Exists(path));
        Assert.True(Directory.GetFiles(tempDir.FullName, "test.json.*.bak").Length >= 1);

        using var stream = File.OpenRead(path);
        var reloaded = ProspectSave.Load(stream);
        Assert.NotNull(reloaded);
        Assert.Equal("TestLobby", reloaded!.ProspectInfo.LobbyName);
    }

    [Fact]
    public void SaveDocument_RoundTripsExpandedMetadataFields()
    {
        var tempDir = Directory.CreateTempSubdirectory("prospect-editor-meta-tests");
        var path = Path.Combine(tempDir.FullName, "meta.json");

        var prospect = new ProspectSave();
        prospect.ProspectInfo = new FProspectInfo
        {
            LobbyName = "MetaLobby",
            ProspectID = "Prospect_Meta",
            Difficulty = "hard",
            ClaimedAccountID = "76561190000111111",
            ClaimedAccountCharacter = 2,
            ProspectState = "Active",
            ElapsedTime = 1357,
            Cost = 300,
            Reward = 900,
            ProspectDTKey = "prospect_key",
            FactionMissionDTKey = "faction_key",
            ExpireTime = DateTimeOffset.UtcNow.AddDays(5).ToUnixTimeSeconds(),
            SelectedDropPoint = 1,
            AssociatedMembers = [],
            CustomSettings = []
        };
        ProspectSaveService.SaveProspect(prospect, path);

        var loaded = ProspectLoadService.Load(path);
        ProspectSaveService.SaveDocument(loaded);

        using var stream = File.OpenRead(path);
        var reloaded = ProspectSave.Load(stream);
        Assert.NotNull(reloaded);
        Assert.Equal("76561190000111111", reloaded!.ProspectInfo.ClaimedAccountID);
        Assert.Equal(2, reloaded.ProspectInfo.ClaimedAccountCharacter);
        Assert.Equal("Active", reloaded.ProspectInfo.ProspectState);
        Assert.Equal(300, reloaded.ProspectInfo.Cost);
        Assert.Equal(900, reloaded.ProspectInfo.Reward);
        Assert.Equal("prospect_key", reloaded.ProspectInfo.ProspectDTKey);
        Assert.Equal("faction_key", reloaded.ProspectInfo.FactionMissionDTKey);
    }
}
