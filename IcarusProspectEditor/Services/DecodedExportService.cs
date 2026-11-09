using IcarusProspectEditor.Mapping;
using IcarusProspectEditor.Models;
using IcarusSaveLib;
using Newtonsoft.Json;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace IcarusProspectEditor.Services;

internal readonly record struct DecodedExportProgress(string Stage, int Percent);
internal enum DecodedExportMode
{
    Raw,
    Enriched
}

internal static class DecodedExportService
{
    /// <summary>
    /// Writes a decoded export from a snapshot captured at the start of this call, via a temporary
    /// sibling file. The source save is rejected as a destination before anything is written.
    /// Callers that can keep mutating the document while the export runs should capture a snapshot
    /// first and use the overload that accepts it.
    /// </summary>
    public static void Export(
        ProspectDocument document,
        string outputPath,
        DecodedExportMode mode = DecodedExportMode.Enriched,
        IProgress<DecodedExportProgress>? progress = null)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        // Reject a doomed destination before paying for the snapshot round-trip.
        EnsureDestinationIsNotSource(document.ProspectPath, outputPath);

        progress?.Report(new DecodedExportProgress("Capturing document snapshot...", 1));
        var snapshot = ProspectSaveService.CreateSnapshot(document);
        Export(snapshot, document.ProspectPath, outputPath, mode, progress);
    }

    /// <summary>
    /// Writes a decoded export from an independent snapshot supplied by the caller, so concurrent UI
    /// edits to the live document cannot leak into the exported file. Both the source-path guard and
    /// the encoded-save guard run before any output file is created or replaced.
    /// </summary>
    public static void Export(
        ProspectDocument snapshot,
        string sourceProspectPath,
        string outputPath,
        DecodedExportMode mode = DecodedExportMode.Enriched,
        IProgress<DecodedExportProgress>? progress = null)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("An export destination is required.", nameof(outputPath));
        }

        // Guard 1: the encoded source save must never be the destination (including path aliases).
        EnsureDestinationIsNotSource(sourceProspectPath, outputPath);

        var fullOutputPath = Path.GetFullPath(outputPath);

        // Guard 2: never replace an existing encoded prospect save with decoded JSON.
        EnsureDestinationIsNotEncodedSave(fullOutputPath);

        var directory = Path.GetDirectoryName(fullOutputPath)
                        ?? throw new InvalidOperationException($"'{outputPath}' has no parent directory.");

        progress?.Report(new DecodedExportProgress("Preparing export...", 2));
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullOutputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            WriteExport(snapshot, tempPath, mode, progress);
            Publish(tempPath, fullOutputPath);
        }
        finally
        {
            TryDeleteTemporaryFile(tempPath);
        }
    }

    /// <summary>
    /// Rejects an export destination that resolves to the source save, including relative-path and
    /// case aliases. Writing the decoded JSON over the encoded save would destroy it.
    /// </summary>
    internal static void EnsureDestinationIsNotSource(string sourcePath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("An export destination is required.", nameof(outputPath));
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        var sourceFull = NormalizeForComparison(sourcePath);
        var outputFull = NormalizeForComparison(outputPath);
        if (string.Equals(sourceFull, outputFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The export destination is the source prospect save. Choose a different file so the encoded save cannot be overwritten.");
        }

        // Existing symbolic links must not be used to reach the source file either.
        if (File.Exists(outputPath))
        {
            try
            {
                var resolved = File.ResolveLinkTarget(outputPath, returnFinalTarget: true);
                if (resolved is not null &&
                    string.Equals(NormalizeForComparison(resolved.FullName), sourceFull, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The export destination links to the source prospect save. Choose a different file.");
                }
            }
            catch (IOException)
            {
                // Target could not be resolved; the direct comparison above still applies.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above.
            }
        }
    }

    /// <summary>
    /// Refuses to export decoded JSON over a file that can still be read as an encoded prospect save.
    /// The destination is read in full up-front so a locked or unreadable file fails closed instead of
    /// being overwritten unchecked. Exposed internally so disposable-file tests can target the check.
    /// </summary>
    internal static void EnsureDestinationIsNotEncodedSave(string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            return;
        }

        byte[] existing;
        try
        {
            existing = File.ReadAllBytes(destinationPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The export destination '{destinationPath}' exists but could not be read to verify that it " +
                "is not an encoded prospect save. Choose a different file.",
                ex);
        }

        if (existing.Length > 0 && IsEncodedProspectSave(existing))
        {
            throw new InvalidOperationException(
                $"The export destination '{destinationPath}' already contains an encoded prospect save. " +
                "Writing decoded JSON there would destroy it; choose a different file.");
        }
    }

    /// <summary>A successfully readable encoded prospect save is never a valid decoded-export target.</summary>
    internal static bool IsEncodedProspectSave(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            return ProspectSave.Load(stream) is not null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Decoded or unrelated JSON is simply not an encoded save.
            return false;
        }
    }

    private static string NormalizeForComparison(string path)
    {
        var full = Path.GetFullPath(path);
        return Path.TrimEndingDirectorySeparator(full);
    }

    private static void Publish(string tempPath, string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            // No existing destination to preserve: a plain rename publishes the finished export.
            File.Move(tempPath, destinationPath);
            return;
        }

        try
        {
            // Prefer atomic replacement so a failed export leaves the previous decoded file untouched.
            File.Replace(tempPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        catch (NotSupportedException)
        {
            // Decoded exports are regenerable, unlike encoded saves, so on a file system without
            // ReplaceFile the rename-overwrite is the only available publication primitive. It still
            // does not truncate the destination before the final move.
            File.Move(tempPath, destinationPath, overwrite: true);
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
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

    private static void WriteExport(
        ProspectDocument snapshot,
        string outputPath,
        DecodedExportMode mode,
        IProgress<DecodedExportProgress>? progress)
    {
        progress?.Report(new DecodedExportProgress("Preparing recorder list...", 2));
        var recorderRows = ProspectModelMapper.ReadRecorderRows(snapshot.Prospect, _ => true);
        var serializer = JsonSerializer.CreateDefault();
        var writerLock = new object();
        var completedRecorders = 0;

        using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var sw = new StreamWriter(stream);
        using var writer = new JsonTextWriter(sw) { Formatting = Formatting.Indented };

        progress?.Report(new DecodedExportProgress("Writing header...", 5));
        writer.WriteStartObject();

        writer.WritePropertyName("sourceProspectPath");
        writer.WriteValue(snapshot.ProspectPath);

        writer.WritePropertyName("prospectInfo");
        serializer.Serialize(writer, snapshot.Prospect.ProspectInfo);

        writer.WritePropertyName("recorders");
        writer.WriteStartArray();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
        };

        Parallel.ForEach(recorderRows, parallelOptions, row =>
        {
            if (!ProspectModelMapper.TryGetRecorderProperties(snapshot.Prospect, row.Index, out var props))
            {
                var skipped = Interlocked.Increment(ref completedRecorders);
                ReportProgress(progress, skipped, recorderRows.Count);
                return;
            }

            var fields = mode == DecodedExportMode.Enriched
                ? ProspectModelMapper.ReadRecorderFieldsFromProperties(props)
                : [];
            var metadata = mode == DecodedExportMode.Enriched
                ? ProspectModelMapper.ExtractRecorderMetadata(row, fields)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var localSerializer = JsonSerializer.CreateDefault();

            lock (writerLock)
            {
                WriteRecorderObject(writer, localSerializer, row, props, metadata, mode);
            }

            var completed = Interlocked.Increment(ref completedRecorders);
            ReportProgress(progress, completed, recorderRows.Count);
        });

        writer.WriteEndArray();

        if (mode == DecodedExportMode.Enriched)
        {
            progress?.Report(new DecodedExportProgress("Writing mounts summary...", 92));
            writer.WritePropertyName("mounts");
            serializer.Serialize(writer, ProspectModelMapper.ReadMountsFromProspect(snapshot.Prospect));
        }
        else
        {
            progress?.Report(new DecodedExportProgress("Finalizing raw export...", 95));
        }

        writer.WriteEndObject();
        writer.Flush();
        sw.Flush();
        stream.Flush(flushToDisk: true);
        progress?.Report(new DecodedExportProgress("Export complete", 100));
    }

    private static void ReportProgress(IProgress<DecodedExportProgress>? progress, int completed, int total)
    {
        var percent = 5 + (int)(85.0 * completed / Math.Max(1, total));
        progress?.Report(new DecodedExportProgress($"Writing recorder {completed}/{total}", percent));
    }

    private static void WriteRecorderObject(
        JsonTextWriter writer,
        JsonSerializer serializer,
        RecorderRow row,
        IEnumerable<FPropertyTag> props,
        IDictionary<string, string> metadata,
        DecodedExportMode mode)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("index");
        writer.WriteValue(row.Index);
        writer.WritePropertyName("componentClass");
        writer.WriteValue(row.ComponentClass);
        writer.WritePropertyName("componentShortName");
        writer.WriteValue(row.ComponentShortName);
        writer.WritePropertyName("category");
        writer.WriteValue(row.Category.ToString());
        if (mode == DecodedExportMode.Enriched)
        {
            writer.WritePropertyName("metaSummary");
            writer.WriteValue(row.MetaSummary);
            writer.WritePropertyName("metadata");
            serializer.Serialize(writer, metadata);
        }
        writer.WritePropertyName("propertyCount");
        writer.WriteValue(row.PropertyCount);
        writer.WritePropertyName("properties");
        WriteProperties(writer, props, serializer);

        writer.WriteEndObject();
    }

    private static void WriteProperties(JsonTextWriter writer, IEnumerable<FPropertyTag> properties, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        foreach (var property in properties)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("name");
            writer.WriteValue(property.Name.Value);
            writer.WritePropertyName("type");
            writer.WriteValue(property.Type.Name.Value);
            writer.WritePropertyName("value");
            WritePropertyValue(writer, property.Property, serializer);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WritePropertyValue(JsonTextWriter writer, FProperty? property, JsonSerializer serializer)
    {
        switch (property)
        {
            case null:
                writer.WriteNull();
                return;
            case BoolProperty p:
                writer.WriteValue(p.Value);
                return;
            case IntProperty p:
                writer.WriteValue(p.Value);
                return;
            case Int64Property p:
                writer.WriteValue(p.Value);
                return;
            case UInt32Property p:
                writer.WriteValue(p.Value);
                return;
            case UInt64Property p:
                writer.WriteValue(p.Value);
                return;
            case FloatProperty p:
                writer.WriteValue(p.Value);
                return;
            case DoubleProperty p:
                writer.WriteValue(p.Value);
                return;
            case NameProperty p:
                writer.WriteValue(p.Value?.Value ?? string.Empty);
                return;
            case StrProperty p:
                writer.WriteValue(p.Value?.Value ?? string.Empty);
                return;
            case EnumProperty p:
                writer.WriteValue(p.Value?.Value ?? string.Empty);
                return;
            case StructProperty { Value: PropertiesStruct ps }:
                WriteProperties(writer, ps.Properties, serializer);
                return;
            case ArrayProperty p when p.Value is byte[] bytes:
                writer.WriteValue(Convert.ToBase64String(bytes));
                return;
            case ArrayProperty p when p.Value is Array arr:
                WriteArray(writer, arr, serializer);
                return;
            default:
                writer.WriteValue(property.Value?.ToString() ?? string.Empty);
                return;
        }
    }

    private static void WriteArray(JsonTextWriter writer, Array arr, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        foreach (var item in arr)
        {
            switch (item)
            {
                case null:
                    writer.WriteNull();
                    break;
                case FString fs:
                    writer.WriteValue(fs.Value);
                    break;
                case FProperty prop:
                    WritePropertyValue(writer, prop, serializer);
                    break;
                case IStructData:
                    serializer.Serialize(writer, item);
                    break;
                case Array nested:
                    WriteArray(writer, nested, serializer);
                    break;
                default:
                    serializer.Serialize(writer, item);
                    break;
            }
        }
        writer.WriteEndArray();
    }
}
