using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using IcarusSaveLib;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ProspectEditorSafetyTests
{
    [Fact]
    public void SaveDocument_AlwaysCreatesBackupEvenWhenFlagFalse()
    {
        var tempDir = Directory.CreateTempSubdirectory("prospect-editor-safety");
        var path = Path.Combine(tempDir.FullName, "save.json");

        var prospect = new ProspectSave();
        prospect.ProspectInfo = new FProspectInfo
        {
            LobbyName = "Safety",
            ProspectID = "SafetyProspect",
            Difficulty = "medium",
            AssociatedMembers = [],
            CustomSettings = []
        };
        ProspectSaveService.SaveProspect(prospect, path);

        var doc = new ProspectDocument
        {
            ProspectPath = path,
            Prospect = prospect
        };

        ProspectSaveService.SaveDocument(doc, createBackup: false);
        Assert.NotEmpty(Directory.GetFiles(tempDir.FullName, "save.json.*.bak"));
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
}
