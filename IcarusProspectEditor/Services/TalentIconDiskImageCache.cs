using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace IcarusProspectEditor.Services;

/// <summary>
/// Loads talent icons from disk once per path. Used by the mount editor talent grid so tab switches
/// and grid formatting do not repeatedly decode files (which can exhaust memory).
/// </summary>
internal sealed class TalentIconDiskImageCache : IDisposable
{
    private readonly Dictionary<string, Image> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public Image? GetOrLoad(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_byPath.TryGetValue(path, out var existing))
        {
            return existing;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var loaded = Image.FromStream(fs, false, false);
            var bitmap = new Bitmap(loaded);
            _byPath[path] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            AppLogService.Error($"Talent icon decode failed: {path}", ex);
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var image in _byPath.Values)
        {
            image.Dispose();
        }

        _byPath.Clear();
    }
}
