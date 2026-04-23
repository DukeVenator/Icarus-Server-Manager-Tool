using Newtonsoft.Json.Linq;

namespace IcarusProspectEditor.Services;

internal sealed class ProspectEditorUpdateService
{
    private static readonly HttpClient Http = new();

    static ProspectEditorUpdateService()
    {
        Http.Timeout = TimeSpan.FromSeconds(30);
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("IcarusProspectEditor/1.0");
    }

    /// <summary>Release tags for the editor track use prefix <c>editor-v</c> (e.g. editor-v1.0.1).</summary>
    private const string TagPrefix = "editor-";

    public async Task<ProspectEditorReleaseInfo?> GetLatestEditorReleaseAsync(bool includePrerelease, CancellationToken ct)
    {
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

        var arr = JArray.Parse(json);
        foreach (var token in arr)
        {
            if (token is not JObject release)
            {
                continue;
            }

            if (release.Value<bool?>("draft") ?? false)
            {
                continue;
            }

            if (!includePrerelease && (release.Value<bool?>("prerelease") ?? false))
            {
                continue;
            }

            var tag = release.Value<string>("tag_name")?.Trim() ?? string.Empty;
            if (!tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var assets = release["assets"] as JArray;
            var zipAsset = assets?.Children<JObject>().FirstOrDefault(a =>
            {
                var name = a.Value<string>("name") ?? string.Empty;
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    !name.Contains("IcarusProspectEditor", StringComparison.OrdinalIgnoreCase) ||
                    !name.Contains("win", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return name.Contains("editor-", StringComparison.OrdinalIgnoreCase) ||
                       !name.Contains("IcarusServerManager", StringComparison.OrdinalIgnoreCase);
            }) ?? assets?.Children<JObject>().FirstOrDefault(a =>
            {
                var name = a.Value<string>("name") ?? string.Empty;
                return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                       name.Contains("IcarusProspectEditor", StringComparison.OrdinalIgnoreCase) &&
                       name.Contains("win", StringComparison.OrdinalIgnoreCase);
            }) ?? assets?.Children<JObject>().FirstOrDefault(a =>
                (a.Value<string>("name") ?? string.Empty).EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                (a.Value<string>("name") ?? string.Empty).Contains("ProspectEditor", StringComparison.OrdinalIgnoreCase));

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

            return new ProspectEditorReleaseInfo(
                TagName: tag,
                Name: release.Value<string>("name") ?? tag,
                HtmlUrl: release.Value<string>("html_url") ?? string.Empty,
                DownloadUrl: downloadUrl,
                AssetName: assetName);
        }

        return null;
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

    public static bool TryParseEditorTagVersion(string tag, out Version version)
    {
        var raw = tag.Trim();
        if (raw.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[TagPrefix.Length..];
        }

        if (raw.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[1..];
        }

        return Version.TryParse(raw, out version!);
    }
}

internal sealed record ProspectEditorReleaseInfo(
    string TagName,
    string Name,
    string HtmlUrl,
    string DownloadUrl,
    string AssetName);
