using IcarusProspectEditor.Mapping;
using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ProspectEditorCategorizationAndExportTests
{
    [Fact]
    public void Categorization_SeparatesMountsFromCharacters()
    {
        var sourcePath = ResolveWorkspaceFile("SufferingResort.json");
        var loaded = ProspectLoadService.Load(sourcePath);

        var characterRows = ProspectModelMapper.ReadRecorderRowsByCategory(loaded.Prospect, RecorderCategory.Character);
        var mountRows = ProspectModelMapper.ReadRecorderRowsByCategory(loaded.Prospect, RecorderCategory.Mount);

        Assert.NotEmpty(characterRows);
        Assert.NotEmpty(mountRows);
        Assert.DoesNotContain(characterRows, r => r.ComponentClass.Contains("Mount", StringComparison.OrdinalIgnoreCase));
        Assert.All(mountRows, r => Assert.Contains("Mount", r.ComponentClass, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MountRows_AreReadFromProspectRecordersWithoutMountsFile()
    {
        var sourcePath = ResolveWorkspaceFile("SufferingResort.json");
        var loaded = ProspectLoadService.Load(sourcePath);

        var mounts = ProspectModelMapper.ReadMountsFromProspect(loaded.Prospect);
        Assert.NotEmpty(mounts);
        Assert.All(mounts, m => Assert.True(m.RecorderIndex >= 0));
        Assert.Contains(mounts, m => !string.IsNullOrWhiteSpace(m.MountName));
    }

    [Fact]
    public void Categorization_AssignsBroadBucketsForKnownComponentFamilies()
    {
        var sourcePath = ResolveWorkspaceFile("SufferingResort.json");
        var loaded = ProspectLoadService.Load(sourcePath);

        var rows = ProspectModelMapper.ReadRecorderRows(loaded.Prospect, _ => true);
        Assert.Contains(rows, r => r.Category == RecorderCategory.Resource);
        Assert.Contains(rows, r => r.Category == RecorderCategory.World);
        Assert.Contains(rows, r => r.Category == RecorderCategory.AI);
        Assert.Contains(rows, r => r.Category == RecorderCategory.Systems);
        Assert.Contains(rows, r => r.Category == RecorderCategory.Security);
        Assert.Contains(rows, r => r.Category == RecorderCategory.Containers);
    }

    [Fact]
    public void MetadataExtraction_ReturnsCategorySpecificFields()
    {
        var sourcePath = ResolveWorkspaceFile("SufferingResort.json");
        var loaded = ProspectLoadService.Load(sourcePath);
        var rows = ProspectModelMapper.ReadRecorderRowsByCategory(loaded.Prospect, RecorderCategory.Mount);
        Assert.NotEmpty(rows);

        var metadata = ProspectModelMapper.ExtractRecorderMetadata(loaded.Prospect, rows[0].Index);
        Assert.True(metadata.ContainsKey("Summary"));
        Assert.True(metadata.ContainsKey("Name"));
        Assert.True(metadata.ContainsKey("Type"));
    }

    [Fact]
    public void DecodedExport_WritesFullJsonStructure()
    {
        var sourcePath = ResolveWorkspaceFile("SufferingResort.json");
        var tempDir = Directory.CreateTempSubdirectory("decoded-export-tests");
        var tempProspectPath = Path.Combine(tempDir.FullName, "export-source.json");
        File.Copy(sourcePath, tempProspectPath, overwrite: true);
        var outputPath = Path.Combine(tempDir.FullName, "decoded.json");

        var loaded = ProspectLoadService.Load(tempProspectPath);
        DecodedExportService.Export(loaded, outputPath);

        Assert.True(File.Exists(outputPath));
        var doc = JObject.Parse(File.ReadAllText(outputPath));
        Assert.NotNull(doc["prospectInfo"]);
        Assert.NotNull(doc["recorders"]);
        Assert.NotNull(doc["mounts"]);

        var recorderArray = (JArray?)doc["recorders"];
        Assert.NotNull(recorderArray);
        Assert.NotEmpty(recorderArray!);
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
}
