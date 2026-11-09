using IcarusProspectEditor.Models;
using IcarusSaveLib;

namespace IcarusProspectEditor.Services;

/// <summary>
/// Result of publishing a save. The destination is only reported saved when the staged file was
/// validated and published; on failure the original file and the in-memory edits are untouched.
/// </summary>
internal sealed class SaveResult
{
    public bool Succeeded { get; init; }
    public string? BackupPath { get; init; }
    public string? Error { get; init; }

    public static SaveResult Ok(string? backupPath) => new() { Succeeded = true, BackupPath = backupPath };

    public static SaveResult Fail(string error, string? backupPath = null) =>
        new() { Succeeded = false, Error = error, BackupPath = backupPath };
}

/// <summary>
/// Staged, backup-preserving save publication.
///
/// Contract:
/// 1. Serialize to a unique sibling temporary file; the destination is never truncated first.
/// 2. Flush and re-load the temporary file to prove it is a readable save.
/// 3. When replacing an existing file, create a collision-resistant backup before publication.
/// 4. Publish by atomically replacing the destination. A failed publication leaves the original
///    bytes intact; there is deliberately no non-atomic overwrite fallback.
/// 5. Backups are mandatory for replacement saves and are never pruned automatically.
/// </summary>
internal static class ProspectSaveService
{
    public static string CreateBackup(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new ArgumentException($"'{path}' has no parent directory.", nameof(path));
        var fileName = Path.GetFileName(fullPath);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var unique = Guid.NewGuid().ToString("N");
        var backupPath = Path.Combine(directory, $"{fileName}.{stamp}-{unique}.bak");

        // overwrite:false makes a name collision a hard failure instead of destroying an earlier backup.
        File.Copy(fullPath, backupPath, overwrite: false);
        return backupPath;
    }

    /// <summary>Serializes a prospect to the given path using staged publication.</summary>
    public static SaveResult SaveProspect(ProspectSave prospect, string path)
    {
        if (prospect is null)
        {
            return SaveResult.Fail("No prospect was supplied.");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return SaveResult.Fail("No destination path was supplied.");
        }

        string? tempPath = null;
        string? backupPath = null;
        try
        {
            // Canonicalization is part of the operation: an invalid path is a save failure,
            // never an exception escaping into the UI thread.
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
            {
                return SaveResult.Fail($"'{path}' has no parent directory.");
            }

            Directory.CreateDirectory(directory);
            tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                prospect.Save(stream);
                stream.Flush(flushToDisk: true);
            }

            if (new FileInfo(tempPath).Length == 0)
            {
                return SaveResult.Fail("The serialized save was empty; the original file was left unchanged.");
            }

            ValidateEncodedSave(tempPath);

            if (File.Exists(fullPath))
            {
                // A backup failure aborts here: publication has not started, so the original is untouched.
                backupPath = CreateBackup(fullPath);
            }

            Publish(tempPath, fullPath);
            tempPath = null;
            return SaveResult.Ok(backupPath);
        }
        catch (Exception ex)
        {
            return SaveResult.Fail(DescribeFailure(ex, backupPath), backupPath);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public static SaveResult SaveDocument(ProspectDocument document)
    {
        if (document is null)
        {
            return SaveResult.Fail("No document was supplied.");
        }

        return SaveProspect(document.Prospect, document.ProspectPath);
    }

    /// <summary>Serializes to memory. Used for export snapshots and tests.</summary>
    public static byte[] Serialize(ProspectSave prospect)
    {
        if (prospect is null)
        {
            throw new ArgumentNullException(nameof(prospect));
        }

        using var memory = new MemoryStream();
        prospect.Save(memory);
        return memory.ToArray();
    }

    /// <summary>Creates a fully independent copy of a loaded prospect by round-tripping the encoded format.</summary>
    public static ProspectSave CloneProspect(ProspectSave prospect)
    {
        if (prospect is null)
        {
            throw new ArgumentNullException(nameof(prospect));
        }

        // One stream, no intermediate byte[]; disposing the stream also releases it if Load throws.
        using var memory = new MemoryStream();
        prospect.Save(memory);
        memory.Position = 0;
        return ProspectSave.Load(memory)
               ?? throw new InvalidDataException("The in-memory save could not be re-loaded.");
    }

    /// <summary>
    /// Creates an export/save snapshot. The returned document shares no mutable state with the
    /// editable document, so a long-running export cannot observe half-applied edits.
    /// </summary>
    public static ProspectDocument CreateSnapshot(ProspectDocument document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return new ProspectDocument
        {
            ProspectPath = document.ProspectPath,
            Prospect = CloneProspect(document.Prospect)
        };
    }

    private static string DescribeFailure(Exception ex, string? backupPath)
    {
        if (backupPath is null)
        {
            return ex.Message;
        }

        return $"{ex.Message} The destination was not replaced. A backup was created at '{backupPath}' and was kept.";
    }

    private static void ValidateEncodedSave(string path)
    {
        using var stream = File.OpenRead(path);
        var reloaded = ProspectSave.Load(stream);
        if (reloaded is null)
        {
            throw new InvalidDataException("The serialized file could not be read back as an encoded prospect save.");
        }
    }

    private static void Publish(string tempPath, string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            // No original to preserve: a plain rename is a safe publication primitive.
            File.Move(tempPath, destinationPath);
            return;
        }

        try
        {
            // ReplaceFile is atomic with respect to readers: the destination is either the old
            // file or the new one, and a failure leaves the old file in place.
            File.Replace(tempPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        catch (NotSupportedException ex)
        {
            // Do not fall back to File.Move(overwrite:true): that can destroy the original when the
            // replacement cannot be published. Refuse, and let the caller keep the backup.
            throw new IOException(
                "This file system does not support atomic file replacement, so the existing save was left " +
                "unchanged rather than overwritten non-atomically. Use Save As to write a new file.",
                ex);
        }
    }

    private static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Operation-owned temporary files are best-effort cleanup only.
        }
    }
}
