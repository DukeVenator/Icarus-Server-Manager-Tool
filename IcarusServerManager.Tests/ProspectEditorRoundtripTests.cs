using IcarusProspectEditor.Models;
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
        try
        {
            var path = Path.Combine(tempDir.FullName, "test.json");

            var prospect = CreateProspect(new FProspectInfo
            {
                LobbyName = "TestLobby",
                ProspectID = "Prospect_Test",
                Difficulty = "medium",
                SelectedDropPoint = 1,
                AssociatedMembers = [],
                CustomSettings = []
            });
            var firstSave = ProspectSaveService.SaveProspect(prospect, path);
            Assert.True(firstSave.Succeeded, firstSave.Error);
            Assert.Null(firstSave.BackupPath);

            var loaded = ProspectLoadService.Load(path);
            var saveResult = ProspectSaveService.SaveDocument(loaded);
            Assert.True(saveResult.Succeeded, saveResult.Error);
            Assert.NotNull(saveResult.BackupPath);

            Assert.True(File.Exists(path));
            Assert.True(Directory.GetFiles(tempDir.FullName, "test.json.*.bak").Length >= 1);

            using var stream = File.OpenRead(path);
            var reloaded = ProspectSave.Load(stream);
            Assert.NotNull(reloaded);
            Assert.Equal("TestLobby", reloaded!.ProspectInfo.LobbyName);
            AssertNoTemporaryFiles(tempDir.FullName);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDir.FullName);
        }
    }

    [Fact]
    public void SaveDocument_RoundTripsExpandedMetadataFields()
    {
        var tempDir = Directory.CreateTempSubdirectory("prospect-editor-meta-tests");
        try
        {
            var path = Path.Combine(tempDir.FullName, "meta.json");

            var prospect = CreateProspect(new FProspectInfo
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
            });
            var firstSave = ProspectSaveService.SaveProspect(prospect, path);
            Assert.True(firstSave.Succeeded, firstSave.Error);

            var loaded = ProspectLoadService.Load(path);
            var saveResult = ProspectSaveService.SaveDocument(loaded);
            Assert.True(saveResult.Succeeded, saveResult.Error);

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
            AssertNoTemporaryFiles(tempDir.FullName);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDir.FullName);
        }
    }

    [Fact]
    public void SaveProspect_ToNewPath_SucceedsWithoutCreatingBackup()
    {
        var tempDir = Directory.CreateTempSubdirectory("prospect-editor-new-path");
        try
        {
            var path = Path.Combine(tempDir.FullName, "new-save.json");
            Assert.False(File.Exists(path));

            var result = ProspectSaveService.SaveProspect(CreateProspect(new FProspectInfo
            {
                LobbyName = "NewLobby",
                ProspectID = "Prospect_New",
                Difficulty = "medium",
                AssociatedMembers = [],
                CustomSettings = []
            }), path);

            Assert.True(result.Succeeded, result.Error);
            Assert.Null(result.BackupPath);
            Assert.True(File.Exists(path));
            Assert.Empty(Directory.GetFiles(tempDir.FullName, "*.bak"));
            Assert.Equal("NewLobby", LoadProspect(path).ProspectInfo.LobbyName);
            Assert.Single(Directory.GetFiles(tempDir.FullName));
            AssertNoTemporaryFiles(tempDir.FullName);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDir.FullName);
        }
    }

    [Fact]
    public void RepeatedSaves_CreateDistinctBackupsAndKeepNewestContentReadable()
    {
        var tempDir = Directory.CreateTempSubdirectory("prospect-editor-repeat");
        try
        {
            var path = Path.Combine(tempDir.FullName, "repeat.json");
            var firstSave = ProspectSaveService.SaveProspect(CreateProspect(new FProspectInfo
            {
                LobbyName = "LobbyOne",
                ProspectID = "Prospect_Repeat",
                Difficulty = "medium",
                AssociatedMembers = [],
                CustomSettings = []
            }), path);
            Assert.True(firstSave.Succeeded, firstSave.Error);
            Assert.Null(firstSave.BackupPath);

            var backups = new List<string>();
            foreach (var lobbyName in new[] { "LobbyTwo", "LobbyThree" })
            {
                var prospect = LoadProspect(path);
                SetLobby(prospect, lobbyName);

                var result = ProspectSaveService.SaveProspect(prospect, path);
                Assert.True(result.Succeeded, result.Error);
                Assert.NotNull(result.BackupPath);
                backups.Add(result.BackupPath!);
            }

            Assert.Equal(2, backups.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(backups, backup => Assert.True(File.Exists(backup)));

            var backedUpLobbies = backups
                .Select(LoadProspect)
                .Select(prospect => prospect.ProspectInfo.LobbyName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(new[] { "LobbyOne", "LobbyTwo" }, backedUpLobbies);
            Assert.Equal("LobbyThree", LoadProspect(path).ProspectInfo.LobbyName);

            // Only the current save and the two retained backups remain.
            Assert.Equal(3, Directory.GetFiles(tempDir.FullName).Length);
            AssertNoTemporaryFiles(tempDir.FullName);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDir.FullName);
        }
    }

    private static ProspectSave CreateProspect(FProspectInfo info) => new()
    {
        ProspectInfo = info
    };

    private static ProspectSave LoadProspect(string path)
    {
        using var stream = File.OpenRead(path);
        return ProspectSave.Load(stream)
               ?? throw new InvalidDataException($"Fixture '{path}' could not be loaded as an encoded prospect save.");
    }

    private static void SetLobby(ProspectSave prospect, string lobbyName)
    {
        var info = prospect.ProspectInfo;
        info.LobbyName = lobbyName;
        prospect.ProspectInfo = info;
    }

    private static void AssertNoTemporaryFiles(string directory)
    {
        var temporaries = Directory.GetFiles(directory)
            .Where(file => string.Equals(Path.GetExtension(file), ".tmp", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(temporaries);
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup of a test-owned temporary directory.
        }
    }
}
