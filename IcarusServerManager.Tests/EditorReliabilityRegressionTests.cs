using IcarusProspectEditor.Mapping;
using IcarusProspectEditor.Models;
using IcarusProspectEditor.Services;
using IcarusSaveLib;
using Xunit;

namespace IcarusServerManager.Tests;

/// <summary>
/// Regression coverage for the audited editor defects (E01–E13): staged saves, export safety,
/// structured validation, talent persistence, draft merging, and projection caching.
/// </summary>
public sealed class EditorReliabilityRegressionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "IcarusEditorRegression", Guid.NewGuid().ToString("N"));
    private readonly string _fixture = TestData.ResolveFile("SufferingResort.json");

    public EditorReliabilityRegressionTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    private string CopyFixture()
    {
        var path = Path.Combine(_dir, "prospect.json");
        File.Copy(_fixture, path, overwrite: true);
        return path;
    }

    // ---------------------------------------------------------------- persistence (E03–E05)

    [Fact]
    public void SaveProspect_RepeatedSavesWithinOneSecond_UseDistinctBackups()
    {
        var path = CopyFixture();
        var document = ProspectLoadService.Load(path);

        var first = ProspectSaveService.SaveDocument(document);
        var second = ProspectSaveService.SaveDocument(document);

        Assert.True(first.Succeeded, first.Error);
        Assert.True(second.Succeeded, second.Error);
        var backups = Directory.GetFiles(_dir, "prospect.json.*.bak");
        Assert.Equal(2, backups.Length);
        Assert.Equal(2, backups.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void SaveProspect_WhenDestinationCannotBeReplaced_LeavesOriginalBytesIntact()
    {
        var path = CopyFixture();
        var document = ProspectLoadService.Load(path);
        var originalBytes = File.ReadAllBytes(path);
        var info = document.Prospect.ProspectInfo;
        info.LobbyName = "Changed";
        document.Prospect.ProspectInfo = info;

        SaveResult result;
        using (var hold = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = ProspectSaveService.SaveDocument(document);
        }

        Assert.False(result.Succeeded);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void SaveProspect_ToNewPath_DoesNotCreateBackupOrTouchExistingFile()
    {
        var path = CopyFixture();
        var document = ProspectLoadService.Load(path);
        var originalBytes = File.ReadAllBytes(path);
        var newPath = Path.Combine(_dir, "copy.json");

        var result = ProspectSaveService.SaveProspect(document.Prospect, newPath);

        Assert.True(result.Succeeded, result.Error);
        Assert.Null(result.BackupPath);
        Assert.True(File.Exists(newPath));
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Export_RejectsSourcePathAndEquivalentAlias()
    {
        var path = CopyFixture();

        Assert.Throws<InvalidOperationException>(() => DecodedExportService.EnsureDestinationIsNotSource(path, path));
        Assert.Throws<InvalidOperationException>(() => DecodedExportService.EnsureDestinationIsNotSource(
            path,
            Path.Combine(_dir, ".", Path.GetFileName(path))));
        Assert.Throws<InvalidOperationException>(() => DecodedExportService.EnsureDestinationIsNotSource(
            path,
            path.ToUpperInvariant()));

        DecodedExportService.EnsureDestinationIsNotSource(path, Path.Combine(_dir, "decoded.json"));
    }

    [Fact]
    public void Export_WritesSnapshotWithoutModifyingSource()
    {
        var path = CopyFixture();
        var document = ProspectLoadService.Load(path);
        var originalBytes = File.ReadAllBytes(path);
        var output = Path.Combine(_dir, "decoded.json");

        DecodedExportService.Export(document, output, DecodedExportMode.Raw);

        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        var json = File.ReadAllText(output);
        Assert.Contains("\"recorders\"", json, StringComparison.Ordinal);
        Assert.Contains("\"sourceProspectPath\"", json, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- validation (E12)

    [Fact]
    public void ApplyRecorderFieldEdits_InvalidValue_ReportsFieldAndCommitsNothing()
    {
        var path = CopyFixture();
        var document = ProspectLoadService.Load(path);
        var recorder = ProspectModelMapper.ReadRecorderRows(document.Prospect, _ => true)
            .First(r => ProspectModelMapper.ReadRecorderFields(document.Prospect, r.Index)
                .Any(f => f.PropertyType == "IntProperty"));
        var fields = ProspectModelMapper.ReadRecorderFields(document.Prospect, recorder.Index);
        var intField = fields.First(f => f.Editable && f.PropertyType == "IntProperty");
        var originalValue = intField.Value;
        var intIndex = fields.IndexOf(intField);

        intField.Value = "not-a-number";
        var result = ProspectModelMapper.ApplyRecorderFieldEdits(document.Prospect, recorder.Index, fields);

        Assert.Equal(RecorderEditStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Path == intField.Path);
        var reloaded = ProspectModelMapper.ReadRecorderFields(document.Prospect, recorder.Index);
        Assert.Equal(originalValue, reloaded[intIndex].Value);
    }

    [Fact]
    public void ApplyRecorderFieldEdits_UnchangedValues_ReportUnchanged()
    {
        var path = CopyFixture();
        var document = ProspectLoadService.Load(path);
        var recorder = ProspectModelMapper.ReadRecorderRows(document.Prospect, _ => true)
            .First(r => ProspectModelMapper.ReadRecorderFields(document.Prospect, r.Index).Any(f => f.Editable));
        var fields = ProspectModelMapper.ReadRecorderFields(document.Prospect, recorder.Index);

        var result = ProspectModelMapper.ApplyRecorderFieldEdits(document.Prospect, recorder.Index, fields);

        Assert.Equal(RecorderEditStatus.Unchanged, result.Status);
        Assert.False(result.Changed);
    }

    // ---------------------------------------------------------------- talents (E09)

    [Fact]
    public void ApplyTalentRows_RankChangeAddDelete_SurvivesSaveAndReload()
    {
        var path = CopyFixture();
        var document = ProspectLoadService.Load(path);
        var mount = ProspectModelMapper.ReadMountsFromProspect(document.Prospect).First();
        var originalFields = ProspectModelMapper.ReadRecorderFields(document.Prospect, mount.RecorderIndex);
        var talents = ProspectModelMapper.ReadTalentRows(document.Prospect, mount.RecorderIndex);
        Assert.True(talents.Count >= 2, "fixture must contain at least two mount talents");

        var retained = talents[0];
        var removed = talents[1];
        var expectedRank = Math.Min(retained.MaxRank, retained.Rank + 1);
        retained.Rank = expectedRank;
        talents.Remove(removed);
        var added = new TalentRow
        {
            Name = "Creature_Base_Regression_Tusker",
            Rank = 3,
            MaxRank = 10,
            DisplayName = "Regression",
            IconKey = "Creature_Base_Regression_Tusker"
        };
        talents.Add(added);

        var result = ProspectModelMapper.ApplyTalentRows(document.Prospect, mount.RecorderIndex, talents);
        Assert.True(result.Succeeded, result.Describe());
        Assert.True(result.Changed);

        var save = ProspectSaveService.SaveDocument(document);
        Assert.True(save.Succeeded, save.Error);

        var reloadedDocument = ProspectLoadService.Load(path);
        var reloadedTalents = ProspectModelMapper.ReadTalentRows(reloadedDocument.Prospect, mount.RecorderIndex)
            .ToDictionary(t => t.Name, t => t.Rank, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(expectedRank, reloadedTalents[retained.Name]);
        Assert.Equal(3, reloadedTalents[added.Name]);
        Assert.False(reloadedTalents.ContainsKey(removed.Name));

        var reloadedFields = ProspectModelMapper.ReadRecorderFields(reloadedDocument.Prospect, mount.RecorderIndex);
        Assert.Equal(originalFields.Count, reloadedFields.Count);
        foreach (var field in originalFields)
        {
            var after = reloadedFields.FirstOrDefault(f => f.Path == field.Path);
            Assert.NotNull(after);
            Assert.Equal(field.Value, after!.Value);
        }
    }

    [Fact]
    public void ApplyTalentRows_InvalidRank_ReportsAndLeavesArrayUnchanged()
    {
        var path = CopyFixture();
        var document = ProspectLoadService.Load(path);
        var mount = ProspectModelMapper.ReadMountsFromProspect(document.Prospect).First();
        var talents = ProspectModelMapper.ReadTalentRows(document.Prospect, mount.RecorderIndex);
        var before = talents.ToDictionary(t => t.Name, t => t.Rank, StringComparer.OrdinalIgnoreCase);

        talents[0].Rank = talents[0].MaxRank + 1;
        var result = ProspectModelMapper.ApplyTalentRows(document.Prospect, mount.RecorderIndex, talents);

        Assert.Equal(RecorderEditStatus.Invalid, result.Status);
        var after = ProspectModelMapper.ReadTalentRows(document.Prospect, mount.RecorderIndex)
            .ToDictionary(t => t.Name, t => t.Rank, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(before.Count, after.Count);
        foreach (var (name, rank) in before)
        {
            Assert.Equal(rank, after[name]);
        }
    }

    [Fact]
    public void RemapTalentsForSpecies_IsDeterministicAcrossRepeatedCalls()
    {
        var source = new[]
        {
            new TalentRow { Name = "Creature_Base_Health_Tusker", Rank = 3, MaxRank = 10 },
            new TalentRow { Name = "Creature_Base_Stamina_Tusker", Rank = 2, MaxRank = 10 },
            new TalentRow { Name = "Creature_Base_WeightCapacity_Tusker", Rank = 1, MaxRank = 10 }
        };

        var first = MountSpeciesMetadataService.RemapTalentsForSpecies(source, "Tusker", "Buffalo");
        var second = MountSpeciesMetadataService.RemapTalentsForSpecies(source, "Tusker", "Buffalo");

        Assert.Equal(first.RemappedCount, second.RemappedCount);
        Assert.Equal(first.AddedCount, second.AddedCount);
        Assert.Equal(first.DroppedCount, second.DroppedCount);
        Assert.Equal(first.LostPoints, second.LostPoints);
        Assert.Equal(
            first.Talents.Select(t => (t.Name, t.Rank)).OrderBy(t => t.Name, StringComparer.Ordinal),
            second.Talents.Select(t => (t.Name, t.Rank)).OrderBy(t => t.Name, StringComparer.Ordinal));
        Assert.All(first.Talents, t => Assert.EndsWith("Buffalo", t.Name, StringComparison.OrdinalIgnoreCase));
        Assert.All(first.Talents, t => Assert.InRange(t.Rank, 0, t.MaxRank));
    }

    // ---------------------------------------------------------------- mount draft merge (E08/E10)

    [Fact]
    public void MountEditMerge_OverviewChangeSurvivesUntouchedAdvancedDraft()
    {
        var path = CopyFixture();
        var document = ProspectLoadService.Load(path);
        var mount = ProspectModelMapper.ReadMountsFromProspect(document.Prospect).First();
        var originalFields = ProspectModelMapper.ReadRecorderFields(document.Prospect, mount.RecorderIndex);
        var talents = ProspectModelMapper.ReadTalentRows(document.Prospect, mount.RecorderIndex);

        var draftMount = mount.Clone();
        draftMount.Level = 42;
        var advancedDraft = originalFields.Select(f => f.Clone()).ToList();

        var merge = MountEditMerge.Apply(
            document.Prospect,
            mount.RecorderIndex,
            originalFields,
            draftMount,
            advancedDraft,
            talents);

        Assert.True(merge.Edit.Succeeded, merge.Edit.Describe());
        var refreshed = ProspectModelMapper.ReadMountsFromProspect(document.Prospect)
            .First(m => m.RecorderIndex == mount.RecorderIndex);
        Assert.Equal(42, refreshed.Level);
    }

    [Fact]
    public void MountEditMerge_ConflictingEdits_KeepAdvancedValueAndReportConflict()
    {
        var path = CopyFixture();
        var document = ProspectLoadService.Load(path);
        var mount = ProspectModelMapper.ReadMountsFromProspect(document.Prospect).First();
        var originalFields = ProspectModelMapper.ReadRecorderFields(document.Prospect, mount.RecorderIndex);
        var talents = ProspectModelMapper.ReadTalentRows(document.Prospect, mount.RecorderIndex);

        var overviewValues = ProspectModelMapper.BuildMountFieldValues(mount, originalFields);
        var levelPath = overviewValues.Keys.First(k => k.EndsWith("Level", StringComparison.Ordinal));
        var baselineLevel = int.Parse(originalFields.First(f => f.Path == levelPath).Value);
        var advancedLevel = baselineLevel + 5;

        var draftMount = mount.Clone();
        draftMount.Level = baselineLevel + 9;
        var advancedDraft = originalFields.Select(f => f.Clone()).ToList();
        advancedDraft.First(f => f.Path == levelPath).Value = advancedLevel.ToString();

        var merge = MountEditMerge.Apply(
            document.Prospect,
            mount.RecorderIndex,
            originalFields,
            draftMount,
            advancedDraft,
            talents);

        Assert.True(merge.Edit.Succeeded, merge.Edit.Describe());
        Assert.NotEmpty(merge.Conflicts);
        var refreshed = ProspectModelMapper.ReadMountsFromProspect(document.Prospect)
            .First(m => m.RecorderIndex == mount.RecorderIndex);
        Assert.Equal(advancedLevel, refreshed.Level);
    }

    [Fact]
    public void MountRowClone_IsIndependentOfSourceRow()
    {
        var original = new MountRow { MountName = "Original", Level = 4, OwnerPlayerId = "123" };
        var clone = original.Clone();
        clone.Level = 9;
        clone.MountName = "Clone";

        Assert.Equal(4, original.Level);
        Assert.Equal("Original", original.MountName);
        Assert.Equal(original.RowId, clone.RowId);
    }

    // ---------------------------------------------------------------- projection decoding (E13)

    [Fact]
    public void ProjectionCache_DecodesRecordersAndMountsOncePerRevision()
    {
        var path = CopyFixture();
        var document = ProspectLoadService.Load(path);
        var cache = new ProspectProjectionCache();
        ProspectModelMapper.ResetDecodeCounters();

        var revision = 1;
        var allRows = cache.GetRecorderRows(document.Prospect, revision);
        Assert.Equal(1, ProspectModelMapper.RecorderMetadataDecodeCount);

        var again = cache.GetRecorderRows(document.Prospect, revision);
        Assert.Same(allRows, again);
        Assert.Equal(1, ProspectModelMapper.RecorderMetadataDecodeCount);

        cache.GetRecorderRows(document.Prospect, revision, RecorderCategory.Mount);
        Assert.Equal(1, ProspectModelMapper.RecorderMetadataDecodeCount);

        cache.GetMounts(document.Prospect, revision);
        Assert.Equal(1, ProspectModelMapper.RecorderMetadataDecodeCount);

        // A new revision must not serve stale projections.
        cache.GetRecorderRows(document.Prospect, revision + 1);
        Assert.Equal(2, ProspectModelMapper.RecorderMetadataDecodeCount);
    }
}
