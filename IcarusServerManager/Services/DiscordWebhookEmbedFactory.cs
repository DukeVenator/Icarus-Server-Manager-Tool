using System.Globalization;
using IcarusServerManager.Models;

namespace IcarusServerManager.Services;

/// <summary>Builds Discord embed payloads for manager webhooks (pure; no I/O).</summary>
internal static class DiscordWebhookEmbedFactory
{
    private const string DiscordIcarusStoreUrl = "https://store.steampowered.com/app/949230/ICARUS/";
    private const string DiscordManagerBrand = "Icarus Server Manager";

    public static string SteamServerLabel(DedicatedServerSettingsModel serverSettings)
    {
        var steam = serverSettings.SteamServerName.Trim();
        return string.IsNullOrWhiteSpace(steam) ? "Dedicated server" : steam;
    }

    public static string TruncateDescription(string s, ManagerOptions managerOptions, int? hardCap = null)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        var cap = Math.Clamp(managerOptions.DiscordWebhookDescriptionMaxChars, 800, 4096);
        if (hardCap is int h)
        {
            cap = Math.Min(cap, h);
        }

        s = s.Replace("\r\n", "\n", StringComparison.Ordinal);
        return s.Length <= cap ? s : s[..cap] + "…";
    }

    /// <summary>Minimal card for stops, exits, and manager shutdown — no long field grids.</summary>
    public static DiscordWebhookExtras BuildLifecycleExtras(
        ManagerOptions managerOptions,
        DedicatedServerSettingsModel serverSettings,
        DateTime nowLocal)
    {
        var label = SteamServerLabel(serverSettings);
        var fields = new List<DiscordEmbedField>
        {
            new DiscordEmbedField(FieldServerLabel(managerOptions), label, false)
        };
        if (managerOptions.DiscordWebhookShowPortsOnEmbeds)
        {
            fields.Add(new DiscordEmbedField("Ports", $"{managerOptions.LaunchGamePort} · {managerOptions.LaunchQueryPort}", true));
        }

        var author = EmbedAuthorFromOptions(managerOptions);
        return new DiscordWebhookExtras(
            fields,
            FooterText: CardFooter(managerOptions, $"{nowLocal:MMM d, HH:mm} local"),
            AuthorName: author.Name,
            AuthorUrl: author.Url);
    }

    /// <summary>Short context for policy / install / SteamCMD style alerts.</summary>
    public static DiscordWebhookExtras BuildOperationalExtras(
        ManagerOptions managerOptions,
        DedicatedServerSettingsModel serverSettings)
    {
        var label = SteamServerLabel(serverSettings);
        var fields = new List<DiscordEmbedField>
        {
            new DiscordEmbedField(FieldServerLabel(managerOptions), label, false)
        };
        if (managerOptions.DiscordWebhookShowPortsOnEmbeds)
        {
            fields.Add(new DiscordEmbedField("Ports", $"{managerOptions.LaunchGamePort} · {managerOptions.LaunchQueryPort}", true));
        }

        if (managerOptions.DiscordWebhookShowSessionOnEmbeds &&
            !string.IsNullOrWhiteSpace(serverSettings.SessionName))
        {
            fields.Add(new DiscordEmbedField("Session", serverSettings.SessionName.Trim(), true));
        }

        var author = EmbedAuthorFromOptions(managerOptions);
        return new DiscordWebhookExtras(
            fields,
            FooterText: CardFooter(managerOptions, "ops feed"),
            AuthorName: author.Name,
            AuthorUrl: author.Url);
    }

    /// <summary>INI and other diagnostics: compact facts, leave stack traces in the message body.</summary>
    public static DiscordWebhookExtras BuildDiagnosticExtras(
        ManagerOptions managerOptions,
        DedicatedServerSettingsModel serverSettings)
    {
        var label = SteamServerLabel(serverSettings);
        var fields = new List<DiscordEmbedField>
        {
            new DiscordEmbedField(FieldServerLabel(managerOptions), label, false)
        };
        if (managerOptions.DiscordWebhookShowPortsOnEmbeds)
        {
            fields.Add(new DiscordEmbedField("Ports", $"{managerOptions.LaunchGamePort} · {managerOptions.LaunchQueryPort}", true));
        }

        if (managerOptions.DiscordWebhookShowSessionOnEmbeds &&
            !string.IsNullOrWhiteSpace(serverSettings.SessionName))
        {
            fields.Add(new DiscordEmbedField("Session", serverSettings.SessionName.Trim(), true));
        }

        var author = EmbedAuthorFromOptions(managerOptions);
        return new DiscordWebhookExtras(
            fields,
            FooterText: CardFooter(managerOptions, "diagnostic"),
            AuthorName: author.Name,
            AuthorUrl: author.Url);
    }

    public static DiscordWebhookExtras BuildHeartbeatExtras(
        ManagerOptions managerOptions,
        DedicatedServerSettingsModel serverSettings,
        bool serverStarted,
        DateTime startTime,
        string lastRestartReason)
    {
        var label = SteamServerLabel(serverSettings);
        var uptime = serverStarted ? (DateTime.Now - startTime).ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture) : "—";
        var reason = string.IsNullOrWhiteSpace(lastRestartReason)
            ? "—"
            : (lastRestartReason.Trim().Length > 220 ? lastRestartReason.Trim()[..220] + "…" : lastRestartReason.Trim());
        var fields = new List<DiscordEmbedField>
        {
            new DiscordEmbedField("Game process", serverStarted ? "● Online" : "○ Idle", true),
            new DiscordEmbedField("Uptime", uptime, true),
            new DiscordEmbedField(FieldServerLabel(managerOptions), label, false)
        };
        if (managerOptions.DiscordWebhookHeartbeatShowPolicyLine)
        {
            fields.Add(new DiscordEmbedField("Last policy note", reason, false));
        }

        if (managerOptions.DiscordWebhookShowPortsOnEmbeds)
        {
            fields.Add(new DiscordEmbedField("Ports", $"{managerOptions.LaunchGamePort} · {managerOptions.LaunchQueryPort}", true));
        }

        var author = EmbedAuthorFromOptions(managerOptions);
        return new DiscordWebhookExtras(
            fields,
            FooterText: CardFooter(managerOptions, "heartbeat"),
            AuthorName: author.Name,
            AuthorUrl: author.Url);
    }

    public static DiscordWebhookExtras BuildLaunchExtras(
        ManagerOptions managerOptions,
        DedicatedServerSettingsModel serverSettings,
        ProspectSummary? prospectSummary)
    {
        var label = SteamServerLabel(serverSettings);
        var fields = new List<DiscordEmbedField>
        {
            new DiscordEmbedField(FieldServerLabel(managerOptions), label, false)
        };
        if (managerOptions.DiscordWebhookShowPortsOnEmbeds)
        {
            fields.Add(new DiscordEmbedField("Ports", $"{managerOptions.LaunchGamePort} · {managerOptions.LaunchQueryPort}", true));
        }

        if (managerOptions.DiscordWebhookIncludeProspectOnStart)
        {
            if (prospectSummary != null)
            {
                var world = string.IsNullOrWhiteSpace(prospectSummary.ProspectId)
                    ? prospectSummary.BaseName
                    : prospectSummary.ProspectId.Trim();
                fields.Add(new DiscordEmbedField("Prospect", world, true));
            }
            else if (!string.IsNullOrWhiteSpace(serverSettings.LastProspectName.Trim()))
            {
                fields.Add(new DiscordEmbedField("Prospect", serverSettings.LastProspectName.Trim(), true));
            }
        }

        var author = EmbedAuthorFromOptions(managerOptions);
        return new DiscordWebhookExtras(
            fields,
            FooterText: CardFooter(managerOptions, "live"),
            AuthorName: author.Name,
            AuthorUrl: author.Url);
    }

    public static DiscordWebhookExtras BuildMinimalServerTag(
        ManagerOptions managerOptions,
        string steamServerName)
    {
        var steam = string.IsNullOrWhiteSpace(steamServerName) ? "—" : steamServerName.Trim();
        var fields = new List<DiscordEmbedField>
        {
            new DiscordEmbedField(FieldServerLabel(managerOptions), steam, true)
        };
        if (managerOptions.DiscordWebhookShowPortsOnEmbeds)
        {
            fields.Add(new DiscordEmbedField("Ports", $"{managerOptions.LaunchGamePort} · {managerOptions.LaunchQueryPort}", true));
        }

        var author = EmbedAuthorFromOptions(managerOptions);
        return new DiscordWebhookExtras(
            fields,
            FooterText: CardFooter(managerOptions, "log hint"),
            AuthorName: author.Name,
            AuthorUrl: author.Url);
    }

    public static DiscordWebhookExtras BuildPlayerEventExtras(ManagerOptions managerOptions, DedicatedServerSettingsModel serverSettings, string playerName)
    {
        var label = SteamServerLabel(serverSettings);
        var fields = new List<DiscordEmbedField>
        {
            new DiscordEmbedField(FieldPlayerLabel(managerOptions), playerName.Trim(), false),
            new DiscordEmbedField(FieldServerLabel(managerOptions), label, false)
        };
        if (managerOptions.DiscordWebhookShowPortsOnEmbeds)
        {
            fields.Add(new DiscordEmbedField("Ports", $"{managerOptions.LaunchGamePort} · {managerOptions.LaunchQueryPort}", true));
        }

        var author = EmbedAuthorFromOptions(managerOptions);
        return new DiscordWebhookExtras(
            fields,
            FooterText: CardFooter(managerOptions, "roster"),
            AuthorName: author.Name,
            AuthorUrl: author.Url);
    }

    private static string FieldServerLabel(ManagerOptions managerOptions) =>
        managerOptions.DiscordWebhookUseThemedLabels ? "Beacon" : "Server";

    private static string FieldPlayerLabel(ManagerOptions managerOptions) =>
        managerOptions.DiscordWebhookUseThemedLabels ? "Crew" : "Player";

    private static (string? Name, string? Url) EmbedAuthorFromOptions(ManagerOptions managerOptions) =>
        managerOptions.DiscordWebhookShowEmbedAuthor
            ? (DiscordManagerBrand, DiscordIcarusStoreUrl)
            : ((string?)null, (string?)null);

    private static string CardFooter(ManagerOptions managerOptions, string defaultSuffix)
    {
        var custom = (managerOptions.DiscordWebhookCustomFooter ?? string.Empty).Trim();
        if (custom.Length > 0)
        {
            return custom.Length > 2048 ? custom[..2048] : custom;
        }

        return $"{DiscordManagerBrand} · {defaultSuffix}";
    }
}
