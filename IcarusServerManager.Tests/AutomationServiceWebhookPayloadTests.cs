using IcarusServerManager.Models;
using IcarusServerManager.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class AutomationServiceWebhookPayloadTests
{
    private static ManagerOptions BaseOptions() =>
        new()
        {
            DiscordWebhookUseEmbeds = true,
            DiscordWebhookUseTitleEmojis = true,
            DiscordWebhookShowEmbedTimestamp = true,
            DiscordWebhookDescriptionMaxChars = 3500,
            DiscordWebhookPlainTextDescriptions = false
        };

    [Fact]
    public void BuildWebhookPayload_EmbedTitle_WithoutEmoji_WhenDisabled()
    {
        var o = BaseOptions();
        o.DiscordWebhookUseTitleEmojis = false;
        var payload = AutomationService.BuildWebhookPayload(
            o,
            DiscordWebhookEventKind.ServerStop,
            "Session offline",
            "*Stopped.*",
            null);
        var json = JsonConvert.SerializeObject(payload);
        var title = JObject.Parse(json)["embeds"]![0]!["title"]!.ToString();
        Assert.Equal("Session offline", title);
        Assert.DoesNotContain("🌙", title, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWebhookPayload_EmbedTitle_PrefixesEmoji_WhenEnabled()
    {
        var o = BaseOptions();
        var payload = AutomationService.BuildWebhookPayload(
            o,
            DiscordWebhookEventKind.ServerStop,
            "Session offline",
            null,
            null);
        var json = JsonConvert.SerializeObject(payload);
        var title = JObject.Parse(json)["embeds"]![0]!["title"]!.ToString();
        Assert.StartsWith("🌙", title, StringComparison.Ordinal);
        Assert.Contains("Session offline", title, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWebhookPayload_Embed_OmitsTimestamp_WhenDisabled()
    {
        var o = BaseOptions();
        o.DiscordWebhookShowEmbedTimestamp = false;
        var payload = AutomationService.BuildWebhookPayload(
            o,
            DiscordWebhookEventKind.PlayerJoin,
            "Test",
            "Hi",
            null);
        var json = JsonConvert.SerializeObject(payload);
        var embed = JObject.Parse(json)["embeds"]![0]!;
        Assert.Null(embed["timestamp"]);
    }

    [Fact]
    public void BuildWebhookPayload_Embed_IncludesTimestamp_WhenEnabled()
    {
        var o = BaseOptions();
        var payload = AutomationService.BuildWebhookPayload(
            o,
            DiscordWebhookEventKind.PlayerJoin,
            "Test",
            "Hi",
            null);
        var json = JsonConvert.SerializeObject(payload);
        var ts = JObject.Parse(json)["embeds"]![0]!["timestamp"]?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(ts));
        Assert.InRange(ts!.Length, 10, 64);
    }

    [Fact]
    public void BuildWebhookPayload_Description_RespectsMaxChars()
    {
        var o = BaseOptions();
        o.DiscordWebhookDescriptionMaxChars = 900;
        var longBody = new string('x', 2000);
        var payload = AutomationService.BuildWebhookPayload(
            o,
            DiscordWebhookEventKind.Chat,
            "Chat",
            longBody,
            null);
        var json = JsonConvert.SerializeObject(payload);
        var desc = JObject.Parse(json)["embeds"]![0]!["description"]!.ToString();
        Assert.True(desc.Length <= 901, $"Expected <= 901 with ellipsis, got {desc.Length}");
        Assert.EndsWith("…", desc, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWebhookPayload_PlainTextDescriptions_StripsMarkdown()
    {
        var o = BaseOptions();
        o.DiscordWebhookPlainTextDescriptions = true;
        var payload = AutomationService.BuildWebhookPayload(
            o,
            DiscordWebhookEventKind.ServerStart,
            "Go",
            "**Bold** and *italic* with `code`",
            null);
        var json = JsonConvert.SerializeObject(payload);
        var desc = JObject.Parse(json)["embeds"]![0]!["description"]!.ToString();
        Assert.DoesNotContain("*", desc, StringComparison.Ordinal);
        Assert.DoesNotContain("`", desc, StringComparison.Ordinal);
        Assert.Contains("Bold", desc, StringComparison.Ordinal);
        Assert.Contains("italic", desc, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWebhookPayload_PlainMode_UsesContentNotEmbeds()
    {
        var o = BaseOptions();
        o.DiscordWebhookUseEmbeds = false;
        var payload = AutomationService.BuildWebhookPayload(
            o,
            DiscordWebhookEventKind.LevelUp,
            "Level ping",
            "Details here",
            new DiscordWebhookExtras(
                new[] { new DiscordEmbedField("K", "V", true) },
                FooterText: "footer"));
        var json = JsonConvert.SerializeObject(payload);
        var j = JObject.Parse(json);
        Assert.Null(j["embeds"]);
        Assert.NotNull(j["content"]);
        var content = j["content"]!.ToString();
        Assert.Contains("Level ping", content, StringComparison.Ordinal);
        Assert.Contains("Details here", content, StringComparison.Ordinal);
        Assert.Contains("K", content, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWebhookPayload_Embed_IncludesAuthorAndThumbnailFromExtras()
    {
        var o = BaseOptions();
        var extras = new DiscordWebhookExtras(
            new[] { new DiscordEmbedField("A", "B", true) },
            FooterText: "My footer",
            AuthorName: "Author X",
            AuthorUrl: "https://example.com/a",
            ThumbnailUrl: "https://example.com/t.png");
        var payload = AutomationService.BuildWebhookPayload(
            o,
            DiscordWebhookEventKind.IniSaveFailed,
            "Fail",
            "oops",
            extras);
        var json = JsonConvert.SerializeObject(payload);
        var embed = JObject.Parse(json)["embeds"]![0]!;
        Assert.Equal("Author X", embed["author"]!["name"]!.ToString());
        Assert.Equal("https://example.com/a", embed["author"]!["url"]!.ToString());
        Assert.Equal("https://example.com/t.png", embed["thumbnail"]!["url"]!.ToString());
        Assert.Equal("My footer", embed["footer"]!["text"]!.ToString());
    }

    [Fact]
    public void BuildWebhookPayload_Embed_Color_IsInteger()
    {
        var o = BaseOptions();
        var payload = AutomationService.BuildWebhookPayload(
            o,
            DiscordWebhookEventKind.ServerStop,
            "x",
            "y",
            null);
        var color = JObject.Parse(JsonConvert.SerializeObject(payload))["embeds"]![0]!["color"];
        Assert.Equal(JTokenType.Integer, color?.Type);
    }

    [Fact]
    public void BuildWebhookPayload_Embed_FieldsOmittedWhenEmptyValuesFiltered()
    {
        var o = BaseOptions();
        var extras = new DiscordWebhookExtras(
            new[]
            {
                new DiscordEmbedField("Good", "value", true),
                new DiscordEmbedField("", "x", true),
                new DiscordEmbedField("Bad", "", true)
            });
        var payload = AutomationService.BuildWebhookPayload(o, DiscordWebhookEventKind.Chat, "c", "d", extras);
        var json = JsonConvert.SerializeObject(payload);
        var fields = JObject.Parse(json)["embeds"]![0]!["fields"] as JArray;
        Assert.NotNull(fields);
        Assert.Single(fields!);
        Assert.Equal("Good", fields[0]!["name"]!.ToString());
    }
}
