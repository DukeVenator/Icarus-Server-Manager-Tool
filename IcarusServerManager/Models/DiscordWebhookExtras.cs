namespace IcarusServerManager.Models;

/// <summary>Optional Discord embed fields and footer for richer webhook cards.</summary>
internal sealed record DiscordEmbedField(string Name, string Value, bool Inline = true);

/// <summary>Per-message extras appended to Discord webhook payloads when using embeds (or plain content).</summary>
internal sealed record DiscordWebhookExtras(
    IReadOnlyList<DiscordEmbedField>? Fields = null,
    string? FooterText = null,
    string? AuthorName = null,
    string? AuthorUrl = null,
    string? AuthorIconUrl = null,
    string? ThumbnailUrl = null);
