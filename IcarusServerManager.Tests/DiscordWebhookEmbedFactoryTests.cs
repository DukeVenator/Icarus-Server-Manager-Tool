using IcarusServerManager.Models;
using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class DiscordWebhookEmbedFactoryTests
{
    private static ManagerOptions BaseOptions() => new()
    {
        LaunchGamePort = 17777,
        LaunchQueryPort = 27015,
        DiscordWebhookShowPortsOnEmbeds = false,
        DiscordWebhookShowSessionOnEmbeds = false,
        DiscordWebhookUseThemedLabels = false,
        DiscordWebhookShowEmbedAuthor = false,
        DiscordWebhookHeartbeatShowPolicyLine = false,
        DiscordWebhookIncludeProspectOnStart = false,
        DiscordWebhookDescriptionMaxChars = 3500
    };

    private static DedicatedServerSettingsModel BaseServer(string steam = "Test Server", string? session = null) =>
        new()
        {
            SteamServerName = steam,
            SessionName = session ?? string.Empty
        };

    [Fact]
    public void SteamServerLabel_trims_and_returns_name()
    {
        var s = new DedicatedServerSettingsModel { SteamServerName = "  My World  " };
        Assert.Equal("My World", DiscordWebhookEmbedFactory.SteamServerLabel(s));
    }

    [Fact]
    public void SteamServerLabel_falls_back_when_blank()
    {
        var s = new DedicatedServerSettingsModel { SteamServerName = "  " };
        Assert.Equal("Dedicated server", DiscordWebhookEmbedFactory.SteamServerLabel(s));
    }

    [Fact]
    public void TruncateDescription_empty_returns_empty()
    {
        var o = BaseOptions();
        Assert.Equal(string.Empty, DiscordWebhookEmbedFactory.TruncateDescription("", o));
    }

    [Fact]
    public void TruncateDescription_normalizes_crlf()
    {
        var o = BaseOptions();
        var t = DiscordWebhookEmbedFactory.TruncateDescription("a\r\nb", o, hardCap: 10_000);
        Assert.Equal("a\nb", t);
    }

    [Fact]
    public void TruncateDescription_respects_max_chars_from_options()
    {
        var o = BaseOptions();
        o.DiscordWebhookDescriptionMaxChars = 800;
        var longText = new string('x', 900);
        var t = DiscordWebhookEmbedFactory.TruncateDescription(longText, o);
        Assert.Equal(801, t.Length);
        Assert.EndsWith("…", t);
    }

    [Fact]
    public void TruncateDescription_clamps_options_cap_to_range()
    {
        var o = BaseOptions();
        o.DiscordWebhookDescriptionMaxChars = 100; // below min 800 -> clamps to 800
        var longText = new string('z', 900);
        var t = DiscordWebhookEmbedFactory.TruncateDescription(longText, o);
        Assert.Equal(801, t.Length);
    }

    [Fact]
    public void TruncateDescription_respects_hard_cap()
    {
        var o = BaseOptions();
        o.DiscordWebhookDescriptionMaxChars = 4096;
        var longText = new string('a', 5000);
        var t = DiscordWebhookEmbedFactory.TruncateDescription(longText, o, hardCap: 100);
        Assert.Equal(101, t.Length);
        Assert.EndsWith("…", t);
    }

    [Fact]
    public void BuildLifecycleExtras_includes_server_field_and_footer()
    {
        var o = BaseOptions();
        var s = BaseServer("My Beacon");
        var extras = DiscordWebhookEmbedFactory.BuildLifecycleExtras(o, s, new DateTime(2026, 4, 1, 14, 30, 0));
        Assert.NotNull(extras.Fields);
        Assert.Single(extras.Fields!);
        Assert.Equal("Server", extras.Fields![0].Name);
        Assert.Equal("My Beacon", extras.Fields[0].Value);
        Assert.Contains("Apr 1, 14:30 local", extras.FooterText ?? "", StringComparison.Ordinal);
        Assert.Contains("Icarus Server Manager", extras.FooterText ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLifecycleExtras_adds_ports_when_enabled()
    {
        var o = BaseOptions();
        o.DiscordWebhookShowPortsOnEmbeds = true;
        var s = BaseServer();
        var extras = DiscordWebhookEmbedFactory.BuildLifecycleExtras(o, s, DateTime.Now);
        Assert.NotNull(extras.Fields);
        Assert.Contains(extras.Fields!, f => f.Name == "Ports" && f.Value.Contains("17777", StringComparison.Ordinal) && f.Value.Contains("27015", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildLifecycleExtras_custom_footer_replaces_default()
    {
        var o = BaseOptions();
        o.DiscordWebhookCustomFooter = "Custom footer line";
        var s = BaseServer();
        var extras = DiscordWebhookEmbedFactory.BuildLifecycleExtras(o, s, DateTime.Now);
        Assert.Equal("Custom footer line", extras.FooterText);
    }

    [Fact]
    public void BuildLifecycleExtras_custom_footer_truncates_at_2048()
    {
        var o = BaseOptions();
        o.DiscordWebhookCustomFooter = new string('c', 3000);
        var s = BaseServer();
        var extras = DiscordWebhookEmbedFactory.BuildLifecycleExtras(o, s, DateTime.Now);
        Assert.Equal(2048, extras.FooterText!.Length);
    }

    [Fact]
    public void BuildLifecycleExtras_embed_author_when_enabled()
    {
        var o = BaseOptions();
        o.DiscordWebhookShowEmbedAuthor = true;
        var s = BaseServer();
        var extras = DiscordWebhookEmbedFactory.BuildLifecycleExtras(o, s, DateTime.Now);
        Assert.Equal("Icarus Server Manager", extras.AuthorName);
        Assert.Equal("https://store.steampowered.com/app/949230/ICARUS/", extras.AuthorUrl);
    }

    [Fact]
    public void BuildOperationalExtras_adds_session_when_enabled()
    {
        var o = BaseOptions();
        o.DiscordWebhookShowSessionOnEmbeds = true;
        var s = BaseServer(session: "  Session-A  ");
        var extras = DiscordWebhookEmbedFactory.BuildOperationalExtras(o, s);
        Assert.Contains(extras.Fields!, f => f.Name == "Session" && f.Value == "Session-A");
    }

    [Fact]
    public void BuildOperationalExtras_skips_blank_session()
    {
        var o = BaseOptions();
        o.DiscordWebhookShowSessionOnEmbeds = true;
        var s = BaseServer(session: "   ");
        var extras = DiscordWebhookEmbedFactory.BuildOperationalExtras(o, s);
        Assert.DoesNotContain(extras.Fields!, f => f.Name == "Session");
    }

    [Fact]
    public void BuildDiagnosticExtras_matches_operational_shape_for_session()
    {
        var o = BaseOptions();
        o.DiscordWebhookShowSessionOnEmbeds = true;
        var s = BaseServer(session: "Diag");
        var extras = DiscordWebhookEmbedFactory.BuildDiagnosticExtras(o, s);
        Assert.Contains("diagnostic", extras.FooterText ?? "", StringComparison.Ordinal);
        Assert.Contains(extras.Fields!, f => f is { Name: "Session", Value: "Diag" });
    }

    [Fact]
    public void BuildHeartbeatExtras_idle_shows_dash_uptime()
    {
        var o = BaseOptions();
        var s = BaseServer();
        var extras = DiscordWebhookEmbedFactory.BuildHeartbeatExtras(o, s, serverStarted: false, DateTime.Now, "reason");
        var uptimeField = Assert.Single(extras.Fields!, f => f.Name == "Uptime");
        Assert.Equal("—", uptimeField.Value);
        var proc = Assert.Single(extras.Fields!, f => f.Name == "Game process");
        Assert.Equal("○ Idle", proc.Value);
    }

    [Fact]
    public void BuildHeartbeatExtras_online_shows_uptime_not_dash()
    {
        var o = BaseOptions();
        var s = BaseServer();
        var start = DateTime.Now.AddMinutes(-3);
        var extras = DiscordWebhookEmbedFactory.BuildHeartbeatExtras(o, s, serverStarted: true, start, "N/A");
        var uptimeField = Assert.Single(extras.Fields!, f => f.Name == "Uptime");
        Assert.NotEqual("—", uptimeField.Value);
        Assert.Contains(":", uptimeField.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildHeartbeatExtras_policy_line_and_long_reason_truncation()
    {
        var o = BaseOptions();
        o.DiscordWebhookHeartbeatShowPolicyLine = true;
        var s = BaseServer();
        var longReason = new string('r', 250);
        var extras = DiscordWebhookEmbedFactory.BuildHeartbeatExtras(o, s, false, DateTime.Now, longReason);
        var note = Assert.Single(extras.Fields!, f => f.Name == "Last policy note");
        Assert.Equal(221, note.Value.Length);
        Assert.EndsWith("…", note.Value);
    }

    [Fact]
    public void BuildHeartbeatExtras_blank_reason_shows_dash()
    {
        var o = BaseOptions();
        o.DiscordWebhookHeartbeatShowPolicyLine = true;
        var s = BaseServer();
        var extras = DiscordWebhookEmbedFactory.BuildHeartbeatExtras(o, s, false, DateTime.Now, "  ");
        var note = Assert.Single(extras.Fields!, f => f.Name == "Last policy note");
        Assert.Equal("—", note.Value);
    }

    [Fact]
    public void BuildLaunchExtras_skips_prospect_when_option_off()
    {
        var o = BaseOptions();
        o.DiscordWebhookIncludeProspectOnStart = false;
        var s = new DedicatedServerSettingsModel { SteamServerName = "S", LastProspectName = "WorldA" };
        var sum = new ProspectSummary { BaseName = "B", FullPath = "/p.json", ProspectId = "Pid" };
        var extras = DiscordWebhookEmbedFactory.BuildLaunchExtras(o, s, sum);
        Assert.DoesNotContain(extras.Fields!, f => f.Name == "Prospect");
    }

    [Fact]
    public void BuildLaunchExtras_uses_prospect_id_when_set()
    {
        var o = BaseOptions();
        o.DiscordWebhookIncludeProspectOnStart = true;
        var s = new DedicatedServerSettingsModel { SteamServerName = "S", LastProspectName = "ignored" };
        var sum = new ProspectSummary { BaseName = "Base", FullPath = "/p.json", ProspectId = "  Olympus  " };
        var extras = DiscordWebhookEmbedFactory.BuildLaunchExtras(o, s, sum);
        var p = Assert.Single(extras.Fields!, f => f.Name == "Prospect");
        Assert.Equal("Olympus", p.Value);
    }

    [Fact]
    public void BuildLaunchExtras_falls_back_to_base_name_when_prospect_id_blank()
    {
        var o = BaseOptions();
        o.DiscordWebhookIncludeProspectOnStart = true;
        var s = new DedicatedServerSettingsModel { SteamServerName = "S", LastProspectName = "x" };
        var sum = new ProspectSummary { BaseName = "BaseOnly", FullPath = "/p.json", ProspectId = "  " };
        var extras = DiscordWebhookEmbedFactory.BuildLaunchExtras(o, s, sum);
        var p = Assert.Single(extras.Fields!, f => f.Name == "Prospect");
        Assert.Equal("BaseOnly", p.Value);
    }

    [Fact]
    public void BuildLaunchExtras_falls_back_to_last_prospect_name_when_no_summary()
    {
        var o = BaseOptions();
        o.DiscordWebhookIncludeProspectOnStart = true;
        var s = new DedicatedServerSettingsModel { SteamServerName = "S", LastProspectName = "  FromIni  " };
        var extras = DiscordWebhookEmbedFactory.BuildLaunchExtras(o, s, prospectSummary: null);
        var p = Assert.Single(extras.Fields!, f => f.Name == "Prospect");
        Assert.Equal("FromIni", p.Value);
    }

    [Fact]
    public void BuildMinimalServerTag_blank_steam_shows_em_dash()
    {
        var o = BaseOptions();
        var extras = DiscordWebhookEmbedFactory.BuildMinimalServerTag(o, "  ");
        var f = Assert.Single(extras.Fields!);
        Assert.Equal("—", f.Value);
    }

    [Fact]
    public void BuildPlayerEventExtras_uses_themed_labels_when_enabled()
    {
        var o = BaseOptions();
        o.DiscordWebhookUseThemedLabels = true;
        var s = BaseServer("BeaconName");
        var extras = DiscordWebhookEmbedFactory.BuildPlayerEventExtras(o, s, "  Player1  ");
        Assert.Contains(extras.Fields!, f => f.Name == "Crew" && f.Value == "Player1");
        Assert.Contains(extras.Fields!, f => f.Name == "Beacon" && f.Value == "BeaconName");
    }

    [Fact]
    public void BuildPlayerEventExtras_adds_ports_when_enabled()
    {
        var o = BaseOptions();
        o.DiscordWebhookShowPortsOnEmbeds = true;
        var s = BaseServer();
        var extras = DiscordWebhookEmbedFactory.BuildPlayerEventExtras(o, s, "P");
        Assert.Contains(extras.Fields!, f => f.Name == "Ports");
        Assert.Equal(3, extras.Fields!.Count);
    }

    [Fact]
    public void BuildLifecycleExtras_uses_beacon_label_when_themed()
    {
        var o = BaseOptions();
        o.DiscordWebhookUseThemedLabels = true;
        var s = BaseServer("Srv");
        var extras = DiscordWebhookEmbedFactory.BuildLifecycleExtras(o, s, DateTime.Now);
        Assert.Equal("Beacon", extras.Fields![0].Name);
    }

    [Fact]
    public void BuildHeartbeatExtras_online_shows_online_marker()
    {
        var o = BaseOptions();
        var s = BaseServer();
        var extras = DiscordWebhookEmbedFactory.BuildHeartbeatExtras(o, s, serverStarted: true, DateTime.Now, "ok");
        var proc = Assert.Single(extras.Fields!, f => f.Name == "Game process");
        Assert.Equal("● Online", proc.Value);
    }

    [Fact]
    public void BuildOperationalExtras_adds_ports_when_enabled()
    {
        var o = BaseOptions();
        o.DiscordWebhookShowPortsOnEmbeds = true;
        var s = BaseServer();
        var extras = DiscordWebhookEmbedFactory.BuildOperationalExtras(o, s);
        Assert.Equal(2, extras.Fields!.Count);
        Assert.Contains(extras.Fields, f => f.Name == "Ports");
    }
}
