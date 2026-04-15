using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ProspectWorldServiceTests
{
    [Fact]
    public void ListProspectBaseNames_Ignores_ServerJsonBackups()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ism-prospects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Alpha.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "Beta.json.backup"), "x");
            File.WriteAllText(Path.Combine(dir, "Beta.json.backup_1"), "x");
            File.WriteAllText(Path.Combine(dir, "Gamma.json"), "{}");

            var names = ProspectWorldService.ListProspectBaseNames(dir);
            Assert.Equal(2, names.Count);
            Assert.Contains("Alpha", names, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Gamma", names, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public void GetFilesForWorldBackup_Includes_MainJson_And_RotationBackups()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ism-prospects2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "World.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "World.json.backup"), "b");
            File.WriteAllText(Path.Combine(dir, "ignore.txt"), "n");

            var files = ProspectWorldService.GetFilesForWorldBackup(dir);
            Assert.Equal(2, files.Count);
            Assert.All(files, f => Assert.True(f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).Contains(".json.backup", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
            }
        }
    }
}
