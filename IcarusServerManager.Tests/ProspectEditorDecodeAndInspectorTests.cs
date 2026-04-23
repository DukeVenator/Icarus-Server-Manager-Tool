using IcarusProspectEditor.Mapping;
using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ProspectEditorDecodeAndInspectorTests
{
    [Fact]
    public void Load_SufferingResort_DecodesProspectAndRecorderData()
    {
        var sourcePath = ResolveWorkspaceFile("SufferingResort.json");
        var loaded = ProspectLoadService.Load(sourcePath);

        Assert.NotNull(loaded.Prospect);
        Assert.True(loaded.Prospect.ProspectBlob.UncompressedLength > 0);
        Assert.True(loaded.Prospect.ProspectData.Count > 0);

        var recorders = ProspectModelMapper.ReadRecorderRows(loaded.Prospect, _ => true);
        Assert.NotEmpty(recorders);
        Assert.Contains(recorders, r => r.PropertyCount > 0);
    }

    [Fact]
    public void ApplyRecorderFieldEdits_ChangesFieldAndRoundTrips()
    {
        var sourcePath = ResolveWorkspaceFile("SufferingResort.json");
        var tempDir = Directory.CreateTempSubdirectory("prospect-inspector-tests");
        var tempProspectPath = Path.Combine(tempDir.FullName, "editable.json");
        File.Copy(sourcePath, tempProspectPath, overwrite: true);

        var document = ProspectLoadService.Load(tempProspectPath);
        var recorders = ProspectModelMapper.ReadRecorderRows(document.Prospect, _ => true);
        Assert.NotEmpty(recorders);

        var selected = recorders.FirstOrDefault(r =>
            ProspectModelMapper.ReadRecorderFields(document.Prospect, r.Index).Any(f => f.Editable));
        Assert.NotNull(selected);

        var fields = ProspectModelMapper.ReadRecorderFields(document.Prospect, selected!.Index);
        var editable = fields.First(f => f.Editable);
        var updatedValue = BuildUpdatedValue(editable);
        editable.Value = updatedValue;

        var updated = ProspectModelMapper.ApplyRecorderFieldEdits(document.Prospect, selected.Index, fields);
        Assert.True(updated);

        ProspectSaveService.SaveDocument(document, createBackup: false);

        var reloaded = ProspectLoadService.Load(tempProspectPath);
        var reloadedFields = ProspectModelMapper.ReadRecorderFields(reloaded.Prospect, selected.Index);
        var editedField = reloadedFields.FirstOrDefault(f => f.Path == editable.Path);
        Assert.NotNull(editedField);
        Assert.Equal(updatedValue, editedField!.Value);
    }

    private static string ResolveWorkspaceFile(string filename)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, filename);
            var slnCandidate = Path.Combine(dir.FullName, "IcarusServerManager.sln");
            if (File.Exists(candidate) && File.Exists(slnCandidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Unable to resolve {filename} from test base directory.");
    }

    private static string BuildUpdatedValue(RecorderFieldRow field) =>
        field.PropertyType switch
        {
            "BoolProperty" => field.Value.Equals("True", StringComparison.OrdinalIgnoreCase) ? "False" : "True",
            "IntProperty" => "7",
            "Int64Property" => "9",
            "UInt32Property" => "11",
            "UInt64Property" => "13",
            "FloatProperty" => "1.5",
            "DoubleProperty" => "2.5",
            _ => field.Value + "_edited"
        };
}
