using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Newtonsoft.Json.Linq;

namespace IcarusProspectEditor.Services;

internal static class TalentIconBundleService
{
    private static readonly ConcurrentDictionary<string, string> IconMap = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> KeyMap = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<string> BundleRoot = new(ResolveBundleRoot);
    private static readonly Lazy<string> FallbackIconPath = new(EnsureFallbackIcon);
    private static int _initialized;

    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        var manifestPath = Path.Combine(BundleRoot.Value, "manifest.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                var json = JObject.Parse(File.ReadAllText(manifestPath));
                if (json["icons"] is JObject icons)
                {
                    foreach (var prop in icons.Properties())
                    {
                        var relative = prop.Value?.ToString() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(relative))
                        {
                            continue;
                        }

                        IconMap[prop.Name] = relative.Replace('\\', '/');
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogService.Error($"Failed to parse talent icon manifest: {manifestPath}", ex);
            }
        }
        else
        {
            AppLogService.Info($"Talent icon manifest missing at {manifestPath}. Using heuristics and fallback only.");
        }

        var mapPath = Path.Combine(BundleRoot.Value, "icon-key-map.json");
        if (File.Exists(mapPath))
        {
            try
            {
                var mapJson = JObject.Parse(File.ReadAllText(mapPath));
                foreach (var prop in mapJson.Properties())
                {
                    var iconKey = prop.Value?.ToString();
                    if (string.IsNullOrWhiteSpace(iconKey))
                    {
                        continue;
                    }

                    KeyMap[prop.Name] = iconKey!;
                }
            }
            catch (Exception ex)
            {
                AppLogService.Error($"Failed to parse icon-key-map: {mapPath}", ex);
            }
        }
    }

    public static string ResolveIconPath(string talentName)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(talentName))
        {
            return FallbackIconPath.Value;
        }

        if (IconMap.TryGetValue(talentName, out var relativePath))
        {
            var fullPath = Path.Combine(BundleRoot.Value, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        if (KeyMap.TryGetValue(talentName, out var iconKey))
        {
            var fromKey = Path.Combine(BundleRoot.Value, "icons", $"{iconKey}.png");
            if (File.Exists(fromKey))
            {
                return fromKey;
            }
        }

        var guessed = GuessBundledIconPath(talentName);
        if (guessed is not null)
        {
            return guessed;
        }

        return FallbackIconPath.Value;
    }

    private static string? GuessBundledIconPath(string talentName)
    {
        var root = BundleRoot.Value;
        if (talentName.StartsWith("Creature_Base_", StringComparison.OrdinalIgnoreCase))
        {
            var rest = talentName["Creature_Base_".Length..];
            var last = rest.LastIndexOf('_');
            if (last > 0)
            {
                var mid = rest[..last];
                if (!string.IsNullOrEmpty(mid))
                {
                    var p = Path.Combine(root, "icons", $"T_Talent_Base_{mid}.png");
                    if (File.Exists(p))
                    {
                        return p;
                    }
                }
            }
        }

        if (talentName.StartsWith("Creature_", StringComparison.OrdinalIgnoreCase))
        {
            var swapped = "T_Talent_" + talentName["Creature_".Length..];
            var p = Path.Combine(root, "icons", $"{swapped}.png");
            if (File.Exists(p))
            {
                return p;
            }

            if (swapped.EndsWith("Standard", StringComparison.OrdinalIgnoreCase))
            {
                var trimmed = swapped[..^"Standard".Length];
                p = Path.Combine(root, "icons", $"{trimmed}.png");
                if (File.Exists(p))
                {
                    return p;
                }
            }
        }

        return null;
    }

    private static string ResolveBundleRoot()
    {
        foreach (var root in EnumerateBundleRootCandidates())
        {
            if (LooksLikeTalentBundle(root))
            {
                return root;
            }
        }

        var fallback = Path.Combine(AppContext.BaseDirectory, "assets", "talents");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    /// <summary>Directory containing manifest.json / icons (for tests and diagnostics).</summary>
    internal static string GetResolvedBundleRoot() => BundleRoot.Value;

    private static IEnumerable<string> EnumerateBundleRootCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "assets", "talents");

        var processDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(processDir))
        {
            yield return Path.Combine(processDir, "assets", "talents");
        }

        var asmDir = TryGetAssemblyDirectory();
        if (!string.IsNullOrEmpty(asmDir))
        {
            yield return Path.Combine(asmDir, "assets", "talents");
        }
    }

    private static string? TryGetAssemblyDirectory()
    {
        try
        {
            var asm = typeof(TalentIconBundleService).Assembly.Location;
            return string.IsNullOrEmpty(asm) ? null : Path.GetDirectoryName(asm);
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeTalentBundle(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return false;
        }

        if (File.Exists(Path.Combine(dir, "manifest.json")))
        {
            return true;
        }

        var iconsDir = Path.Combine(dir, "icons");
        return Directory.Exists(iconsDir) && Directory.EnumerateFileSystemEntries(iconsDir).Any();
    }

    private static string EnsureFallbackIcon()
    {
        var fallback = Path.Combine(BundleRoot.Value, "fallback.png");
        if (File.Exists(fallback))
        {
            return fallback;
        }

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(52, 54, 62));
            using var fill = new SolidBrush(Color.FromArgb(120, 125, 140));
            g.FillEllipse(fill, 6, 6, 20, 20);
            using var edge = new Pen(Color.FromArgb(180, 185, 200), 1.5f);
            g.DrawEllipse(edge, 6, 6, 20, 20);
        }

        bmp.Save(fallback, ImageFormat.Png);
        return fallback;
    }
}
