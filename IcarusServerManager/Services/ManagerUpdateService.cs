using Newtonsoft.Json.Linq;

namespace IcarusServerManager.Services;

internal sealed class ManagerUpdateService
{
    private static readonly HttpClient Http = new();

    static ManagerUpdateService()
    {
        Http.Timeout = TimeSpan.FromSeconds(30);
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("IcarusServerManager/1.0");
    }

    public async Task<ManagerReleaseInfo?> GetLatestReleaseAsync(bool includePrerelease, CancellationToken ct)
    {
        // Always scan recent releases: `releases/latest` may point at an editor-only tag when both tools share one repo.
        var url = "https://api.github.com/repos/DukeVenator/Icarus-Server-Manager-Tool/releases?per_page=30";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var token = JToken.Parse(json);
        if (token.Type != JTokenType.Array)
        {
            return null;
        }

        foreach (var release in token.Children<JObject>())
        {
            if (release.Value<bool?>("draft") ?? false)
            {
                continue;
            }

            if (!includePrerelease && (release.Value<bool?>("prerelease") ?? false))
            {
                continue;
            }

            var tag = release.Value<string>("tag_name")?.Trim();
            if (string.IsNullOrWhiteSpace(tag) || !IsManagerReleaseTag(tag))
            {
                continue;
            }

            var assets = release["assets"] as JArray;
            var zipAsset = TryPickManagerZipAsset(assets);
            if (zipAsset == null)
            {
                continue;
            }

            var downloadUrl = zipAsset.Value<string>("browser_download_url");
            var assetName = zipAsset.Value<string>("name");
            if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(assetName))
            {
                continue;
            }

            return new ManagerReleaseInfo(
                TagName: tag,
                Name: release.Value<string>("name") ?? tag,
                HtmlUrl: release.Value<string>("html_url") ?? string.Empty,
                DownloadUrl: downloadUrl,
                AssetName: assetName);
        }

        return null;
    }

    private static bool IsManagerReleaseTag(string tag)
    {
        if (tag.StartsWith("editor-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (tag.StartsWith("manager-", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Legacy manager tags: v1.2.3
        return tag.StartsWith("v", StringComparison.OrdinalIgnoreCase);
    }

    private static JObject? TryPickManagerZipAsset(JArray? assets)
    {
        return assets?.Children<JObject>().FirstOrDefault(a =>
        {
            var name = a.Value<string>("name") ?? string.Empty;
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                !name.Contains("IcarusServerManager", StringComparison.OrdinalIgnoreCase) ||
                !name.Contains("win", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Prefer manager track zips when both tools share the same release page.
            return name.Contains("manager-", StringComparison.OrdinalIgnoreCase) ||
                   !name.Contains("ProspectEditor", StringComparison.OrdinalIgnoreCase);
        }) ?? assets?.Children<JObject>().FirstOrDefault(a =>
        {
            var name = a.Value<string>("name") ?? string.Empty;
            return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                   name.Contains("IcarusServerManager", StringComparison.OrdinalIgnoreCase) &&
                   name.Contains("win", StringComparison.OrdinalIgnoreCase);
        }) ?? assets?.Children<JObject>().FirstOrDefault(a =>
            (a.Value<string>("name") ?? string.Empty).EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    public async Task DownloadAssetAsync(string url, string destinationPath, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = File.Create(destinationPath);
        await src.CopyToAsync(dst, ct).ConfigureAwait(false);
    }

    public static bool TryParseTagVersion(string tag, out Version version)
    {
        var raw = tag.Trim();
        if (raw.StartsWith("manager-", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw["manager-".Length..];
        }

        if (raw.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[1..];
        }

        return Version.TryParse(raw, out version!);
    }
}

internal sealed record ManagerReleaseInfo(
    string TagName,
    string Name,
    string HtmlUrl,
    string DownloadUrl,
    string AssetName);
