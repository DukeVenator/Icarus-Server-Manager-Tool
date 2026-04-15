using System.IO.Compression;
using IcarusServerManager.Models;

namespace IcarusServerManager.Services;

internal static class ProspectWorldService
{    /// <summary>
    /// Base names (no .json) for each main prospect save file in the folder.
    /// </summary>
    public static IReadOnlyList<string> ListProspectBaseNames(string prospectsDirectory)
    {
        if (!Directory.Exists(prospectsDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(prospectsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Where(static f =>
            {
                var n = Path.GetFileName(f);
                return n != null && !n.Contains(".json.backup", StringComparison.OrdinalIgnoreCase);
            })
            .Select(static f => Path.GetFileNameWithoutExtension(f))
            .Where(static n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Enumerates main prospect JSON files and reads <see cref="ProspectSummary"/> from each file header (fast for large saves).
    /// </summary>
    public static IReadOnlyList<ProspectSummary> ListProspectSummaries(string prospectsDirectory)
    {
        if (!Directory.Exists(prospectsDirectory))
        {
            return Array.Empty<ProspectSummary>();
        }

        return Directory.EnumerateFiles(prospectsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Where(static f =>
            {
                var n = Path.GetFileName(f);
                return n != null && !n.Contains(".json.backup", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(static f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .Select(ProspectSummaryReader.Read)
            .ToList();
    }

    /// <summary>
    /// Main <c>*.json</c> worlds plus the game's own <c>*.json.backup*</c> rotation files.
    /// </summary>
    public static IReadOnlyList<string> GetFilesForWorldBackup(string prospectsDirectory)
    {
        if (!Directory.Exists(prospectsDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(prospectsDirectory)
            .Where(static f => IncludeInWorldBackup(Path.GetFileName(f)))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IncludeInWorldBackup(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && !fileName.Contains(".json.backup", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fileName.Contains(".json.backup", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static void ZipFiles(IReadOnlyList<string> absolutePaths, string zipFilePath)
    {
        ArgumentNullException.ThrowIfNull(absolutePaths);
        if (absolutePaths.Count == 0)
        {
            throw new InvalidOperationException("No files to zip.");
        }

        if (File.Exists(zipFilePath))
        {
            File.Delete(zipFilePath);
        }

        using var fs = new FileStream(zipFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var file in absolutePaths)
        {
            var name = Path.GetFileName(file);
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
            using var entryStream = entry.Open();
            using var src = File.OpenRead(file);
            src.CopyTo(entryStream);
        }
    }

    public static void CopyFilesToDirectory(IReadOnlyList<string> absolutePaths, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in absolutePaths)
        {
            var dest = Path.Combine(destinationDirectory, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }
    }
}
