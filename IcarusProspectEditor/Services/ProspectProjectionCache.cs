using IcarusProspectEditor.Mapping;
using IcarusProspectEditor.Models;
using IcarusSaveLib;

namespace IcarusProspectEditor.Services;

/// <summary>
/// Reusable recorder/mount projections for one document revision.
///
/// The save format stores recorder data as encoded blobs, so every projection pass decodes them.
/// A refresh used to decode the full recorder list five times (once per tab) plus once per mount.
/// This cache decodes metadata once and mount rows once, then serves every tab from the result.
/// It is invalidated by <see cref="Invalidate"/> whenever the document is mutated, reloaded, or
/// reverted, so a stale projection can never hide an edit.
/// </summary>
internal sealed class ProspectProjectionCache
{
    private ProspectSave? _source;
    private int _revision = int.MinValue;
    private List<RecorderRow> _allRecorders = [];
    private List<MountRow> _mounts = [];
    private readonly Dictionary<RecorderCategory, List<RecorderRow>> _byCategory = [];

    public int DecodeCount { get; private set; }

    public bool IsCurrent(ProspectSave prospect, int revision) =>
        ReferenceEquals(_source, prospect) && _revision == revision;

    public IReadOnlyList<RecorderRow> GetRecorderRows(ProspectSave prospect, int revision)
    {
        Ensure(prospect, revision);
        return _allRecorders;
    }

    public IReadOnlyList<RecorderRow> GetRecorderRows(ProspectSave prospect, int revision, RecorderCategory category)
    {
        Ensure(prospect, revision);
        if (!_byCategory.TryGetValue(category, out var rows))
        {
            rows = _allRecorders.Where(r => r.Category == category).ToList();
            _byCategory[category] = rows;
        }

        return rows;
    }

    public IReadOnlyList<MountRow> GetMounts(ProspectSave prospect, int revision)
    {
        Ensure(prospect, revision);
        return _mounts;
    }

    public void Invalidate()
    {
        _source = null;
        _revision = int.MinValue;
        _allRecorders = [];
        _mounts = [];
        _byCategory.Clear();
    }

    private void Ensure(ProspectSave prospect, int revision)
    {
        if (IsCurrent(prospect, revision))
        {
            return;
        }

        _source = prospect;
        _revision = revision;
        _byCategory.Clear();
        DecodeCount++;
        _allRecorders = ProspectModelMapper.ReadRecorderRows(prospect, _ => true);
        _mounts = ProspectModelMapper.ReadMountsFromProspect(
            prospect,
            _allRecorders.Where(r => r.Category == RecorderCategory.Mount));
    }
}
