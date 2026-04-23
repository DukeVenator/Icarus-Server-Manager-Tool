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
    public static void Export(
        ProspectDocument document,
        string outputPath,
        DecodedExportMode mode = DecodedExportMode.Enriched,
        IProgress<DecodedExportProgress>? progress = null)
    {
        progress?.Report(new DecodedExportProgress("Preparing recorder list...", 2));
        var recorderRows = ProspectModelMapper.ReadRecorderRows(document.Prospect, _ => true);
        var serializer = JsonSerializer.CreateDefault();
        var writerLock = new object();
        var completedRecorders = 0;

        using var stream = File.Create(outputPath);
        using var sw = new StreamWriter(stream);
        using var writer = new JsonTextWriter(sw) { Formatting = Formatting.Indented };

        progress?.Report(new DecodedExportProgress("Writing header...", 5));
        writer.WriteStartObject();

        writer.WritePropertyName("sourceProspectPath");
        writer.WriteValue(document.ProspectPath);

        writer.WritePropertyName("prospectInfo");
        serializer.Serialize(writer, document.Prospect.ProspectInfo);

        writer.WritePropertyName("recorders");
        writer.WriteStartArray();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
        };

        Parallel.ForEach(recorderRows, parallelOptions, row =>
        {
            if (!ProspectModelMapper.TryGetRecorderProperties(document.Prospect, row.Index, out var props))
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
            serializer.Serialize(writer, ProspectModelMapper.ReadMountsFromProspect(document.Prospect));
        }
        else
        {
            progress?.Report(new DecodedExportProgress("Finalizing raw export...", 95));
        }

        writer.WriteEndObject();
        writer.Flush();
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
