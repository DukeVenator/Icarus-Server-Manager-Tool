using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using IcarusSaveLib;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ProspectEditorSafetyTests
{
    [Fact]
    public void SaveDocument_ExistingDestination_CreatesBackup()
    {
        var tempDir = Directory.CreateTempSubdirectory("prospect-editor-safety");
        try
        {
            var path = Path.Combine(tempDir.FullName, "save.json");
            var first = ProspectSaveService.SaveProspect(NewProspect("FirstLobby"), path);
            Assert.True(first.Succeeded, first.Error);
            Assert.Null(first.BackupPath);

            var document = new ProspectDocument
            {
                ProspectPath = path,
                Prospect = LoadProspect(path)
            };
            SetLobby(document.Prospect, "SecondLobby");

            var saveResult = ProspectSaveService.SaveDocument(document);
            Assert.True(saveResult.Succeeded, saveResult.Error);
            Assert.NotNull(saveResult.BackupPath);
            Assert.True(File.Exists(saveResult.BackupPath));
            Assert.Single(Directory.GetFiles(tempDir.FullName, "save.json.*.bak"));
            Assert.Equal("SecondLobby", LoadProspect(path).ProspectInfo.LobbyName);
            AssertNoTemporaryFiles(tempDir.FullName);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDir.FullName);
        }
    }

    [Fact]
    public void SaveProspect_InvalidPath_ReturnsFailureInsteadOfThrowing()
    {
        var result = ProspectSaveService.SaveProspect(NewProspect("InvalidPath"), "invalid\0path.json");

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void SaveProspect_WhenBackupCannotBeCreated_DoesNotPublishOrRemoveOriginal()
    {
        var tempDir = Directory.CreateTempSubdirectory("prospect-editor-backup-failure");
        try
        {
            var path = Path.Combine(tempDir.FullName, "save.json");
            var created = ProspectSaveService.SaveProspect(NewProspect("OriginalLobby"), path);
            Assert.True(created.Succeeded, created.Error);

            var originalBytes = File.ReadAllBytes(path);
            var updated = LoadProspect(path);
            SetLobby(updated, "UpdatedLobby");

            SaveResult result;
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                result = ProspectSaveService.SaveProspect(updated, path);
            }

            Assert.False(result.Succeeded);
            Assert.Null(result.BackupPath);
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
            Assert.Equal("OriginalLobby", LoadProspect(path).ProspectInfo.LobbyName);
            Assert.Empty(Directory.GetFiles(tempDir.FullName, "*.bak"));
            AssertNoTemporaryFiles(tempDir.FullName);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDir.FullName);
        }
    }

    [Fact]
    public void SaveProspect_WhenPublicationFails_KeepsBackupAndPreservesOriginalBytes()
    {
        var tempDir = Directory.CreateTempSubdirectory("prospect-editor-publish-failure");
        try
        {
            var path = Path.Combine(tempDir.FullName, "save.json");
            var created = ProspectSaveService.SaveProspect(NewProspect("OriginalLobby"), path);
            Assert.True(created.Succeeded, created.Error);

            var originalBytes = File.ReadAllBytes(path);
            var updated = LoadProspect(path);
            SetLobby(updated, "UpdatedLobby");

            SaveResult result;
            // Read+write sharing permits the pre-publication backup copy but denies the delete access
            // File.Replace needs, so publication fails after a real backup exists.
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                result = ProspectSaveService.SaveProspect(updated, path);
            }

            Assert.False(result.Succeeded);
            Assert.NotNull(result.BackupPath);
            Assert.True(File.Exists(result.BackupPath));
            Assert.Contains(result.BackupPath!, result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
            Assert.Equal("OriginalLobby", LoadProspect(path).ProspectInfo.LobbyName);
            Assert.Equal("OriginalLobby", LoadProspect(result.BackupPath!).ProspectInfo.LobbyName);
            AssertNoTemporaryFiles(tempDir.FullName);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDir.FullName);
        }
    }

    [Fact]
    public void Export_PathAliasesOfSource_AreRejectedAndPreserveSourceBytes()
    {
        var tempDir = Directory.CreateTempSubdirectory("prospect-editor-export-alias");
        try
        {
            var sourcePath = Path.Combine(tempDir.FullName, "source.json");
            var created = ProspectSaveService.SaveProspect(NewProspect("SourceLobby"), sourcePath);
            Assert.True(created.Succeeded, created.Error);

            var sourceBytes = File.ReadAllBytes(sourcePath);
            var document = ProspectLoadService.Load(sourcePath);
            var snapshot = ProspectSaveService.CreateSnapshot(document);

            // Aliases that normalize to the source without mutating process-global state such as
            // Environment.CurrentDirectory (which would race other parallel tests).
            var aliases = new[]
            {
                Path.Combine(tempDir.FullName, ".", "source.json"),
                Path.Combine(tempDir.FullName, "SOURCE.JSON")
            };

            foreach (var alias in aliases)
            {
                var exception = Assert.Throws<InvalidOperationException>(
                    () => DecodedExportService.Export(snapshot, sourcePath, alias, DecodedExportMode.Raw));
                Assert.Contains("source prospect save", exception.Message, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));
            AssertNoTemporaryFiles(tempDir.FullName);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDir.FullName);
        }
    }

    [Fact]
    public void Export_ExistingEncodedSaveDestination_IsRejectedAndPreserved()
    {
        var tempDir = Directory.CreateTempSubdirectory("prospect-editor-export-encoded");
        try
        {
            var sourcePath = Path.Combine(tempDir.FullName, "source.json");
            var createdSource = ProspectSaveService.SaveProspect(NewProspect("SourceLobby"), sourcePath);
            Assert.True(createdSource.Succeeded, createdSource.Error);

            var destinationPath = Path.Combine(tempDir.FullName, "existing-save.json");
            var createdDestination = ProspectSaveService.SaveProspect(NewProspect("DestinationLobby"), destinationPath);
            Assert.True(createdDestination.Succeeded, createdDestination.Error);
            var destinationBytes = File.ReadAllBytes(destinationPath);

            var document = ProspectLoadService.Load(sourcePath);
            var snapshot = ProspectSaveService.CreateSnapshot(document);

            var exception = Assert.Throws<InvalidOperationException>(
                () => DecodedExportService.Export(snapshot, sourcePath, destinationPath, DecodedExportMode.Raw));
            Assert.Contains("encoded prospect save", exception.Message, StringComparison.OrdinalIgnoreCase);

            Assert.Equal(destinationBytes, File.ReadAllBytes(destinationPath));
            Assert.Equal("DestinationLobby", LoadProspect(destinationPath).ProspectInfo.LobbyName);
            Assert.Equal("SourceLobby", LoadProspect(sourcePath).ProspectInfo.LobbyName);
            AssertNoTemporaryFiles(tempDir.FullName);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDir.FullName);
        }
    }

    [Fact]
    public void Export_FailedReplacement_LeavesDestinationUntouchedAndRemovesTempFile()
    {
        var tempDir = Directory.CreateTempSubdirectory("prospect-editor-export-publish");
        try
        {
            var sourcePath = Path.Combine(tempDir.FullName, "source.json");
            var created = ProspectSaveService.SaveProspect(NewProspect("SourceLobby"), sourcePath);
            Assert.True(created.Succeeded, created.Error);

            var destinationPath = Path.Combine(tempDir.FullName, "existing-decoded.json");
            File.WriteAllText(destinationPath, "{}");
            var destinationBytes = File.ReadAllBytes(destinationPath);

            var document = ProspectLoadService.Load(sourcePath);
            var snapshot = ProspectSaveService.CreateSnapshot(document);

            using (new FileStream(destinationPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                Assert.ThrowsAny<IOException>(
                    () => DecodedExportService.Export(snapshot, sourcePath, destinationPath, DecodedExportMode.Raw));
            }

            Assert.Equal(destinationBytes, File.ReadAllBytes(destinationPath));
            AssertNoTemporaryFiles(tempDir.FullName);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDir.FullName);
        }
    }

    [Fact]
    public void Export_Snapshot_IsIndependentOfLaterSourceDocumentMutation()
    {
        var tempDir = Directory.CreateTempSubdirectory("prospect-editor-export-snapshot");
        try
        {
            var sourcePath = Path.Combine(tempDir.FullName, "source.json");
            var created = ProspectSaveService.SaveProspect(NewProspect("SnapshotBeforeLobby"), sourcePath);
            Assert.True(created.Succeeded, created.Error);

            var document = ProspectLoadService.Load(sourcePath);
            var snapshot = ProspectSaveService.CreateSnapshot(document);

            // The live document keeps changing after the snapshot was captured.
            SetLobby(document.Prospect, "MutatedAfterLobby");

            var outputPath = Path.Combine(tempDir.FullName, "snapshot.decoded.json");
            DecodedExportService.Export(snapshot, document.ProspectPath, outputPath, DecodedExportMode.Raw);

            var exportedJson = File.ReadAllText(outputPath);
            Assert.Contains("SnapshotBeforeLobby", exportedJson, StringComparison.Ordinal);
            Assert.DoesNotContain("MutatedAfterLobby", exportedJson, StringComparison.Ordinal);
            Assert.Equal("MutatedAfterLobby", document.Prospect.ProspectInfo.LobbyName);
            AssertNoTemporaryFiles(tempDir.FullName);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempDir.FullName);
        }
    }

    [Fact]
    public void DangerWarningMessage_IncludesInspectorWarning()
    {
        var message = DangerWarningService.BuildWarningMessage(
            new[] { "ComponentA[1]", "ComponentB[2]" },
            new[] { "Recorder removal performed." });

        Assert.Contains("Inspector edits present (2)", message);
        Assert.Contains("Recorder removal performed.", message);
    }

    private static ProspectSave NewProspect(string lobbyName, string prospectId = "Prospect_Safety") => new()
    {
        ProspectInfo = new FProspectInfo
        {
            LobbyName = lobbyName,
            ProspectID = prospectId,
            Difficulty = "medium",
            AssociatedMembers = [],
            CustomSettings = []
        }
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
