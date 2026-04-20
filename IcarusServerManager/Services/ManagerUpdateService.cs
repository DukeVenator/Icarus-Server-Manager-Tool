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
        var url = includePrerelease
            ? "https://api.github.com/repos/DukeVenator/Icarus-Server-Manager-Tool/releases?per_page=10"
            : "https://api.github.com/repos/DukeVenator/Icarus-Server-Manager-Tool/releases/latest";
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
        var release = token.Type == JTokenType.Array
            ? token.Children<JObject>().FirstOrDefault(r =>
                !(r.Value<bool?>("draft") ?? false) &&
                (includePrerelease || !(r.Value<bool?>("prerelease") ?? false)))
            : token as JObject;
        if (release == null)
        {
            return null;
        }

        var tag = release.Value<string>("tag_name")?.Trim();
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var assets = release["assets"] as JArray;
        var zipAsset = assets?.Children<JObject>().FirstOrDefault(a =>
        {
            var name = a.Value<string>("name") ?? string.Empty;
            return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                   name.Contains("IcarusServerManager", StringComparison.OrdinalIgnoreCase) &&
                   name.Contains("win", StringComparison.OrdinalIgnoreCase);
        }) ?? assets?.Children<JObject>().FirstOrDefault(a =>
            (a.Value<string>("name") ?? string.Empty).EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (zipAsset == null)
        {
            return null;
        }

        var downloadUrl = zipAsset.Value<string>("browser_download_url");
        var assetName = zipAsset.Value<string>("name");
        if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(assetName))
        {
            return null;
        }

        return new ManagerReleaseInfo(
            TagName: tag,
            Name: release.Value<string>("name") ?? tag,
            HtmlUrl: release.Value<string>("html_url") ?? string.Empty,
            DownloadUrl: downloadUrl,
            AssetName: assetName);
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
