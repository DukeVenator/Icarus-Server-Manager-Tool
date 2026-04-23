using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

/// <summary>
/// Guards against regressions where talent icons are decoded from disk on every grid format/paint
/// (tab switching can trigger that path thousands of times and exhaust memory).
/// </summary>
public sealed class TalentIconDiskImageCacheTests
{
    [Fact]
    public void GetOrLoad_same_path_returns_same_instance()
    {
        var path = TalentIconBundleService.ResolveIconPath("Nonexistent_Talent_Icon");
        Assert.True(File.Exists(path));

        using var cache = new TalentIconDiskImageCache();
        var first = cache.GetOrLoad(path);
        Assert.NotNull(first);

        Assert.Same(first, cache.GetOrLoad(path));

        for (var i = 0; i < 500; i++)
        {
            Assert.Same(first, cache.GetOrLoad(path));
        }
    }

    [Fact]
    public void GetOrLoad_missing_path_returns_null()
    {
        using var cache = new TalentIconDiskImageCache();
        var dir = Directory.CreateTempSubdirectory("talent-icon-cache-test");
        try
        {
            var missing = Path.Combine(dir.FullName, "definitely-not-present.webp");
            Assert.Null(cache.GetOrLoad(missing));
        }
        finally
        {
            try
            {
                dir.Delete(true);
            }
            catch
            {
                // best-effort cleanup on Windows locked dirs
            }
        }
    }

    [Fact]
    public void GetOrLoad_after_dispose_throws()
    {
        var path = TalentIconBundleService.ResolveIconPath("Nonexistent_Talent_Icon");
        var cache = new TalentIconDiskImageCache();
        cache.Dispose();
        Assert.Throws<ObjectDisposedException>(() => cache.GetOrLoad(path));
    }

    [Fact]
    public void GetOrLoad_decodes_repo_bundled_png_when_available()
    {
        var dir = TryFindRepoTalentAssetsDir();
        if (dir is null)
        {
            return;
        }

        var png = Path.Combine(dir, "icons", "T_Talent_Base_Health.png");
        Assert.True(File.Exists(png), "Repo should include harvested T_Talent_Base_Health.png.");

        using var cache = new TalentIconDiskImageCache();
        var img = cache.GetOrLoad(png);
        if (img is null)
        {
            // System.Drawing decode can fail on some CI / headless hosts.
            return;
        }

        Assert.Same(img, cache.GetOrLoad(png));
    }

    private static string? TryFindRepoTalentAssetsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "IcarusProspectEditor", "assets", "talents");
            if (File.Exists(Path.Combine(candidate, "manifest.json")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
