using IcarusServerManager.Models;
using Newtonsoft.Json;

namespace IcarusServerManager.Services;

internal sealed class AutomationService : IDisposable
{
    private readonly string _profileFolder;
    private readonly HttpClient _httpClient;
    private readonly Action<string>? _diagnostics;
    private bool _disposed;
    private static readonly JsonSerializerSettings WebhookJsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };

    private static long _lastChatWebhookTick;
    private static readonly object ChatThrottleLock = new();
    private static long _lastGameplayWebhookTick;
    private static readonly object GameplayThrottleLock = new();

    public AutomationService()
        : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles"), null, null)
    {
    }

    /// <summary>For tests: use a dedicated profile directory.</summary>
    public AutomationService(string profileFolder)
        : this(profileFolder, null, null)
    {
    }

    /// <summary>
    /// For tests: supply a fake <see cref="HttpMessageHandler"/> so webhook boundaries and failures
    /// can be exercised without sending live requests. <paramref name="diagnostics"/> receives
    /// redacted delivery problems (status code and host only).
    /// </summary>
    public AutomationService(string profileFolder, HttpMessageHandler? handler, Action<string>? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileFolder);
        _profileFolder = profileFolder;
        Directory.CreateDirectory(_profileFolder);
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
        _diagnostics = diagnostics;
    }

    public IEnumerable<string> GetProfiles()
    {
        return Directory.EnumerateFiles(_profileFolder, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
    }

    public void SaveProfile(string name, DedicatedServerSettingsModel settings, ManagerOptions options)
    {
        var payload = new ProfilePayload { ServerSettings = settings, ManagerOptions = options };
        var path = Path.Combine(_profileFolder, $"{Sanitize(name)}.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(payload, Formatting.Indented));
    }

    public (DedicatedServerSettingsModel Settings, ManagerOptions Options)? LoadProfile(string name)
    {
        var path = Path.Combine(_profileFolder, $"{Sanitize(name)}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var payload = JsonConvert.DeserializeObject<ProfilePayload>(File.ReadAllText(path));
        if (payload == null)
        {
            return null;
        }

        return (payload.ServerSettings ?? new DedicatedServerSettingsModel(), payload.ManagerOptions ?? new ManagerOptions());
    }

    public void ExportBundle(string path, DedicatedServerSettingsModel settings, ManagerOptions options)
    {
        var payload = new ProfilePayload { ServerSettings = settings, ManagerOptions = options };
        File.WriteAllText(path, JsonConvert.SerializeObject(payload, Formatting.Indented));
    }

    public (DedicatedServerSettingsModel Settings, ManagerOptions Options)? ImportBundle(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var payload = JsonConvert.DeserializeObject<ProfilePayload>(File.ReadAllText(path));
        if (payload == null)
        {
            return null;
        }

        return (payload.ServerSettings ?? new DedicatedServerSettingsModel(), payload.ManagerOptions ?? new ManagerOptions());
    }

    public bool IsUpdateDue(ManagerOptions options, DateTime now)
    {
        if (!options.UpdateScheduleEnabled || !TimeSpan.TryParse(options.UpdateScheduleTime, out var schedule))
        {
            return false;
        }

        var target = now.Date.Add(schedule);
        return now >= target && now < target.AddMinutes(1);
    }

    /// <summary>Legacy single-message webhook (treated as automated restart notification).</summary>
    public Task SendWebhookAsync(ManagerOptions options, string message) =>
        SendWebhookEventAsync(options, DiscordWebhookEventKind.ServerRestart, "Automated server restart", message);

    /// <summary>
    /// Sends one webhook event. Returns <see langword="true"/> only when the endpoint accepted the
    /// request; disabled events, non-success status codes and transport failures return
    /// <see langword="false"/> and are reported through the diagnostics callback.
    /// </summary>
    public async Task<bool> SendWebhookEventAsync(
        ManagerOptions options,
        DiscordWebhookEventKind kind,
        string title,
        string? description = null,
        DiscordWebhookExtras? extras = null)
    {
        if (!options.EnableDiscordWebhook || string.IsNullOrWhiteSpace(options.DiscordWebhookUrl))
        {
            return false;
        }

        if (!IsEventEnabled(options, kind))
        {
            return false;
        }

        if (kind == DiscordWebhookEventKind.Chat && options.DiscordWebhookChatThrottleSeconds > 0)
        {
            var minMs = options.DiscordWebhookChatThrottleSeconds * 1000L;
            lock (ChatThrottleLock)
            {
                var now = Environment.TickCount64;
                if (now - _lastChatWebhookTick < minMs)
                {
                    return false;
                }

                _lastChatWebhookTick = now;
            }
        }

        if (kind is DiscordWebhookEventKind.LevelUp or DiscordWebhookEventKind.PlayerDeath)
        {
            if (options.DiscordWebhookGameplayThrottleSeconds > 0)
            {
                var minMs = options.DiscordWebhookGameplayThrottleSeconds * 1000L;
                lock (GameplayThrottleLock)
                {
                    var now = Environment.TickCount64;
                    if (now - _lastGameplayWebhookTick < minMs)
                    {
                        return false;
                    }

                    _lastGameplayWebhookTick = now;
                }
            }
        }

        try
        {
            var body = BuildWebhookPayload(options, kind, title, description, extras);
            var json = JsonConvert.SerializeObject(body, WebhookJsonSettings);
            using var payload = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(options.DiscordWebhookUrl, payload).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // Host and status only: the URL path/token and the response body are never logged.
                _diagnostics?.Invoke(
                    $"Discord webhook delivery to '{DescribeHost(options.DiscordWebhookUrl)}' failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // The URL, token, payload, and response body are never included in the diagnostic.
            _diagnostics?.Invoke(
                $"Discord webhook delivery to '{DescribeHost(options.DiscordWebhookUrl)}' failed: {ex.GetType().Name}.");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }

    /// <summary>Returns only the host of a webhook URL so diagnostics cannot leak the secret path/token.</summary>
    internal static string DescribeHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "unconfigured";
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "invalid-url";
    }

    private static bool IsEventEnabled(ManagerOptions o, DiscordWebhookEventKind kind) =>
        kind switch
        {
            DiscordWebhookEventKind.PlayerJoin => o.DiscordWebhookNotifyPlayerJoin,
            DiscordWebhookEventKind.PlayerLeave => o.DiscordWebhookNotifyPlayerLeave,
            DiscordWebhookEventKind.ServerRestart => o.DiscordWebhookNotifyServerRestart,
            DiscordWebhookEventKind.ServerRestartFailed => o.DiscordWebhookNotifyRestartFailed,
            DiscordWebhookEventKind.ServerStart => o.DiscordWebhookNotifyServerStart,
            DiscordWebhookEventKind.ServerStop => o.DiscordWebhookNotifyServerStop,
            DiscordWebhookEventKind.UnexpectedExit => o.DiscordWebhookNotifyUnexpectedExit,
            DiscordWebhookEventKind.Chat => o.DiscordWebhookNotifyChat,
            DiscordWebhookEventKind.RestartWarning => o.DiscordWebhookNotifyRestartWarning,
            DiscordWebhookEventKind.ScheduledUpdateWindow => o.DiscordWebhookNotifyScheduledUpdate,
            DiscordWebhookEventKind.SteamCmdFinished => o.DiscordWebhookNotifySteamCmd,
            DiscordWebhookEventKind.IniSaveFailed => o.DiscordWebhookNotifyIniSaveFailed,
            DiscordWebhookEventKind.IniValidationFailed => o.DiscordWebhookNotifyIniValidationFailed,
            DiscordWebhookEventKind.IniLoadFailed => o.DiscordWebhookNotifyIniLoadFailed,
            DiscordWebhookEventKind.LevelUp => o.DiscordWebhookNotifyLevelUp,
            DiscordWebhookEventKind.PlayerDeath => o.DiscordWebhookNotifyPlayerDeath,
            DiscordWebhookEventKind.InstallPathIssue => o.DiscordWebhookNotifyInstallPathIssue,
            DiscordWebhookEventKind.ManagerHeartbeat => o.DiscordWebhookHeartbeatIntervalHours > 0,
            _ => false
        };

    private static string TitleEmojiFor(DiscordWebhookEventKind kind) =>
        kind switch
        {
            DiscordWebhookEventKind.PlayerJoin => "🟢",
            DiscordWebhookEventKind.PlayerLeave => "🟠",
            DiscordWebhookEventKind.ServerRestart => "🔄",
            DiscordWebhookEventKind.ServerRestartFailed => "⛔",
            DiscordWebhookEventKind.ServerStart => "🛰️",
            DiscordWebhookEventKind.ServerStop => "🌙",
            DiscordWebhookEventKind.UnexpectedExit => "⚡",
            DiscordWebhookEventKind.Chat => "💬",
            DiscordWebhookEventKind.RestartWarning => "⚠️",
            DiscordWebhookEventKind.ScheduledUpdateWindow => "📅",
            DiscordWebhookEventKind.SteamCmdFinished => "📦",
            DiscordWebhookEventKind.IniSaveFailed => "📝",
            DiscordWebhookEventKind.IniValidationFailed => "📋",
            DiscordWebhookEventKind.IniLoadFailed => "📋",
            DiscordWebhookEventKind.LevelUp => "⭐",
            DiscordWebhookEventKind.PlayerDeath => "☠️",
            DiscordWebhookEventKind.InstallPathIssue => "📂",
            DiscordWebhookEventKind.ManagerHeartbeat => "💠",
            _ => "📡"
        };

    private static int EmbedColorFor(DiscordWebhookEventKind kind) =>
        kind switch
        {
            DiscordWebhookEventKind.PlayerJoin => 0x1abc9c,
            DiscordWebhookEventKind.PlayerLeave => 0xe67e22,
            DiscordWebhookEventKind.ServerRestart => 0x8e44ad,
            DiscordWebhookEventKind.ServerRestartFailed => 0xc0392b,
            DiscordWebhookEventKind.ServerStart => 0x3498db,
            DiscordWebhookEventKind.ServerStop => 0x6c5ce7,
            DiscordWebhookEventKind.UnexpectedExit => 0xe74c3c,
            DiscordWebhookEventKind.Chat => 0x7f8c8d,
            DiscordWebhookEventKind.RestartWarning => 0xf39c12,
            DiscordWebhookEventKind.ScheduledUpdateWindow => 0x1abc9c,
            DiscordWebhookEventKind.SteamCmdFinished => 0x16a085,
            DiscordWebhookEventKind.IniSaveFailed => 0xc0392b,
            DiscordWebhookEventKind.IniValidationFailed => 0xe67e22,
            DiscordWebhookEventKind.IniLoadFailed => 0xe67e22,
            DiscordWebhookEventKind.LevelUp => 0xf1c40f,
            DiscordWebhookEventKind.PlayerDeath => 0x95a5a6,
            DiscordWebhookEventKind.InstallPathIssue => 0xd35400,
            DiscordWebhookEventKind.ManagerHeartbeat => 0x5dade2,
            _ => 0x5865f2
        };

    /// <summary>Builds the JSON-serializable webhook body. Internal for unit tests.</summary>
    internal static object BuildWebhookPayload(
        ManagerOptions o,
        DiscordWebhookEventKind kind,
        string title,
        string? description,
        DiscordWebhookExtras? extras)
    {
        var descMax = Math.Clamp(o.DiscordWebhookDescriptionMaxChars, 800, 4096);
        var emoji = o.DiscordWebhookUseTitleEmojis ? TitleEmojiFor(kind) : string.Empty;
        var rawTitle = Truncate(title.Trim(), 250);
        var t = string.IsNullOrEmpty(emoji)
            ? (rawTitle.Length == 0 ? "\u200b" : rawTitle)
            : (rawTitle.Length == 0 ? emoji : $"{emoji} {rawTitle}");
        var desc = string.IsNullOrWhiteSpace(description)
            ? null
            : MaybePlainDiscordDescription(o, SanitizeForDiscordBody(description, descMax));
        var username = string.IsNullOrWhiteSpace(o.DiscordWebhookUsername) ? null : Truncate(o.DiscordWebhookUsername.Trim(), 80);
        var avatarUrl = string.IsNullOrWhiteSpace(o.DiscordWebhookAvatarUrl) ? null : Truncate(o.DiscordWebhookAvatarUrl.Trim(), 512);
        var footerText = string.IsNullOrWhiteSpace(extras?.FooterText)
            ? "Icarus Server Manager"
            : Truncate(extras!.FooterText!.Trim(), 2048);
        object[]? fieldPayload = BuildEmbedFields(extras?.Fields);

        static Dictionary<string, object?>? BuildAuthorBlock(DiscordWebhookExtras? x)
        {
            if (x == null || string.IsNullOrWhiteSpace(x.AuthorName))
            {
                return null;
            }

            var author = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = Truncate(x.AuthorName.Trim(), 256)
            };
            if (!string.IsNullOrWhiteSpace(x.AuthorUrl))
            {
                author["url"] = Truncate(x.AuthorUrl.Trim(), 2048);
            }

            if (!string.IsNullOrWhiteSpace(x.AuthorIconUrl))
            {
                author["icon_url"] = Truncate(x.AuthorIconUrl.Trim(), 2048);
            }

            return author;
        }

        static Dictionary<string, object?>? BuildThumbnailBlock(DiscordWebhookExtras? x)
        {
            if (x == null || string.IsNullOrWhiteSpace(x.ThumbnailUrl))
            {
                return null;
            }

            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["url"] = Truncate(x.ThumbnailUrl.Trim(), 2048)
            };
        }

        if (!o.DiscordWebhookUseEmbeds)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("**").Append(t).Append("**");
            if (!string.IsNullOrEmpty(desc))
            {
                sb.Append('\n').Append(desc);
            }

            if (extras?.Fields is { Count: > 0 } plainFields)
            {
                foreach (var f in plainFields.Take(12))
                {
                    var fn = Truncate(f.Name.Trim(), 80);
                    var fv = Truncate(SanitizeForDiscordBody(f.Value.Trim(), 500), 500);
                    if (fn.Length > 0)
                    {
                        sb.Append("\n**").Append(fn).Append(":** ").Append(fv);
                    }
                }
            }

            var content = Truncate(sb.ToString(), 2000);
            return new { content, username, avatar_url = avatarUrl };
        }

        var embed = new Dictionary<string, object?>
        {
            ["title"] = Truncate(t, 256),
            ["description"] = string.IsNullOrWhiteSpace(desc) ? "\u200b" : desc,
            ["color"] = EmbedColorFor(kind),
            ["footer"] = new { text = footerText }
        };

        if (o.DiscordWebhookShowEmbedTimestamp)
        {
            embed["timestamp"] = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        }

        var authorBlock = BuildAuthorBlock(extras);
        if (authorBlock != null)
        {
            embed["author"] = authorBlock;
        }

        var thumbBlock = BuildThumbnailBlock(extras);
        if (thumbBlock != null)
        {
            embed["thumbnail"] = thumbBlock;
        }

        if (fieldPayload is { Length: > 0 })
        {
            embed["fields"] = fieldPayload;
        }

        return new { username, avatar_url = avatarUrl, embeds = new object[] { embed } };
    }

    private static object[]? BuildEmbedFields(IReadOnlyList<DiscordEmbedField>? fields)
    {
        if (fields == null || fields.Count == 0)
        {
            return null;
        }

        var list = new List<object>(Math.Min(fields.Count, 25));
        foreach (var f in fields)
        {
            if (list.Count >= 25)
            {
                break;
            }

            var name = Truncate(f.Name.Trim(), 256);
            var value = Truncate(SanitizeForDiscordBody(f.Value.Trim(), 1024), 1024);
            if (name.Length == 0 || value.Length == 0)
            {
                continue;
            }

            list.Add(new { name, value, inline = f.Inline });
        }

        return list.Count == 0 ? null : list.ToArray();
    }

    /// <summary>
    /// Truncates to at most <paramref name="max"/> characters including the trailing ellipsis, so a
    /// caller that enforces a platform limit cannot exceed it by one.
    /// </summary>
    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || max <= 0)
        {
            return string.Empty;
        }

        return s.Length <= max ? s : string.Concat(s.AsSpan(0, max - 1), "…");
    }

    private static string SanitizeForDiscordBody(string s, int maxLen)
    {
        s = s.Replace("```", "`\u200b``", StringComparison.Ordinal);
        return Truncate(s, maxLen);
    }

    private static string MaybePlainDiscordDescription(ManagerOptions o, string s)
    {
        if (!o.DiscordWebhookPlainTextDescriptions || string.IsNullOrEmpty(s))
        {
            return s;
        }

        return StripSimpleMarkdown(s);
    }

    /// <summary>Best-effort removal of common Discord markdown so channels without formatting still read cleanly.</summary>
    private static string StripSimpleMarkdown(string s)
    {
        var t = s;
        t = t.Replace("**", "", StringComparison.Ordinal);
        t = t.Replace("__", "", StringComparison.Ordinal);
        t = t.Replace("*", "", StringComparison.Ordinal);
        t = t.Replace("_", "", StringComparison.Ordinal);
        t = t.Replace("`", "'", StringComparison.Ordinal);
        return t;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "default" : name;
    }

    private sealed class ProfilePayload
    {
        public DedicatedServerSettingsModel? ServerSettings { get; set; }
        public ManagerOptions? ManagerOptions { get; set; }
    }
}
