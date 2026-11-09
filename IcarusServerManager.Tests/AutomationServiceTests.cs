using System.Net;
using IcarusServerManager.Models;
using IcarusServerManager.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class AutomationServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "IcarusServerManagerTests", Guid.NewGuid().ToString("N"));

    public AutomationServiceTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }
        catch
        {
            // best-effort
        }
    }

    [Fact]
    public void SaveProfile_ThenLoadProfile_RoundTrips()
    {
        var svc = new AutomationService(_dir);
        var settings = new DedicatedServerSettingsModel
        {
            SteamServerName = "From Profile",
            MaxPlayers = 4,
            ResumeProspect = false
        };
        var options = new ManagerOptions { Theme = "Dark", IntervalRestartMinutes = 45 };
        svc.SaveProfile("alpha", settings, options);

        var tuple = svc.LoadProfile("alpha");
        Assert.NotNull(tuple);
        Assert.Equal("From Profile", tuple.Value.Settings.SteamServerName);
        Assert.Equal(4, tuple.Value.Settings.MaxPlayers);
        Assert.False(tuple.Value.Settings.ResumeProspect);
        Assert.Equal("Dark", tuple.Value.Options.Theme);
        Assert.Equal(45, tuple.Value.Options.IntervalRestartMinutes);
    }

    [Fact]
    public void LoadProfile_ReturnsNull_WhenMissing()
    {
        var svc = new AutomationService(_dir);
        Assert.Null(svc.LoadProfile("nope"));
    }

    [Fact]
    public void ExportBundle_ThenImportBundle_RoundTrips()
    {
        var svc = new AutomationService(_dir);
        var bundle = Path.Combine(_dir, "bundle.json");
        var settings = new DedicatedServerSettingsModel { SessionName = "S1", JoinPassword = "x" };
        var options = new ManagerOptions { AutoScrollConsole = false };
        svc.ExportBundle(bundle, settings, options);

        var loaded = svc.ImportBundle(bundle);
        Assert.NotNull(loaded);
        Assert.Equal("S1", loaded.Value.Settings.SessionName);
        Assert.Equal("x", loaded.Value.Settings.JoinPassword);
        Assert.False(loaded.Value.Options.AutoScrollConsole);
    }

    [Fact]
    public void GetProfiles_ReturnsSortedNames()
    {
        var svc = new AutomationService(_dir);
        svc.SaveProfile("zebra", new DedicatedServerSettingsModel(), new ManagerOptions());
        svc.SaveProfile("apple", new DedicatedServerSettingsModel(), new ManagerOptions());

        var names = svc.GetProfiles().ToList();
        Assert.Equal(new[] { "apple", "zebra" }, names);
    }

    [Fact]
    public void IsUpdateDue_ReturnsFalse_WhenScheduleDisabled()
    {
        var svc = new AutomationService(_dir);
        var o = new ManagerOptions { UpdateScheduleEnabled = false, UpdateScheduleTime = "04:00" };
        var now = new DateTime(2026, 4, 15, 4, 0, 30);
        Assert.False(svc.IsUpdateDue(o, now));
    }

    [Fact]
    public void IsUpdateDue_ReturnsTrue_WithinOneMinuteWindowAfterScheduledTime()
    {
        var svc = new AutomationService(_dir);
        var o = new ManagerOptions { UpdateScheduleEnabled = true, UpdateScheduleTime = "04:00" };
        var now = new DateTime(2026, 4, 15, 4, 0, 30);
        Assert.True(svc.IsUpdateDue(o, now));
    }

    [Fact]
    public void IsUpdateDue_ReturnsFalse_AfterWindow()
    {
        var svc = new AutomationService(_dir);
        var o = new ManagerOptions { UpdateScheduleEnabled = true, UpdateScheduleTime = "04:00" };
        var now = new DateTime(2026, 4, 15, 4, 1, 0);
        Assert.False(svc.IsUpdateDue(o, now));
    }

    [Fact]
    public void IsUpdateDue_ReturnsFalse_BeforeScheduledTime()
    {
        var svc = new AutomationService(_dir);
        var o = new ManagerOptions { UpdateScheduleEnabled = true, UpdateScheduleTime = "04:00" };
        var now = new DateTime(2026, 4, 15, 3, 59, 59);
        Assert.False(svc.IsUpdateDue(o, now));
    }

    [Fact]
    public void IsUpdateDue_ReturnsFalse_WhenTimeInvalid()
    {
        var svc = new AutomationService(_dir);
        var o = new ManagerOptions { UpdateScheduleEnabled = true, UpdateScheduleTime = "not-a-time" };
        var now = new DateTime(2026, 4, 15, 4, 0, 30);
        Assert.False(svc.IsUpdateDue(o, now));
    }

    [Fact]
    public void ImportBundle_ReturnsNull_WhenFileMissing()
    {
        var svc = new AutomationService(_dir);
        var path = Path.Combine(_dir, "missing-bundle.json");
        Assert.Null(svc.ImportBundle(path));
    }

    [Fact]
    public void SaveProfile_SanitizesInvalidFileNameCharacters()
    {
        var svc = new AutomationService(_dir);
        svc.SaveProfile("a/b|c", new DedicatedServerSettingsModel { SteamServerName = "X" }, new ManagerOptions());
        var loaded = svc.LoadProfile("a_b_c");
        Assert.NotNull(loaded);
        Assert.Equal("X", loaded.Value.Settings.SteamServerName);
    }

    [Fact]
    public async Task SendWebhookEventAsync_Success_UsesInjectedHandler_AndReturnsTrue()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var diagnostics = new List<string>();
        using var svc = new AutomationService(_dir, handler, diagnostics.Add);
        var options = WebhookOptions();

        var delivered = await svc.SendWebhookEventAsync(
            options,
            DiscordWebhookEventKind.PlayerJoin,
            "Player joined",
            "Alice");

        Assert.True(delivered);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(options.DiscordWebhookUrl, handler.LastRequestUri!.ToString());
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task SendWebhookEventAsync_NonSuccess_ReturnsFalse_AndReportsStatusCode()
    {
        using var handler = new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NotFound) { ReasonPhrase = "Not Found" });
        var diagnostics = new List<string>();
        using var svc = new AutomationService(_dir, handler, diagnostics.Add);

        var delivered = await svc.SendWebhookEventAsync(
            WebhookOptions(),
            DiscordWebhookEventKind.PlayerJoin,
            "Player joined");

        Assert.False(delivered);
        Assert.Equal(1, handler.CallCount);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("404", diagnostic, StringComparison.Ordinal);
        Assert.Contains("discord.com", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendWebhookEventAsync_Timeout_ReturnsFalse_AndReportsFailure()
    {
        using var handler = new StubHttpMessageHandler(
            _ => throw new TaskCanceledException("The request timed out."));
        var diagnostics = new List<string>();
        using var svc = new AutomationService(_dir, handler, diagnostics.Add);

        var delivered = await svc.SendWebhookEventAsync(
            WebhookOptions(),
            DiscordWebhookEventKind.PlayerJoin,
            "Player joined");

        Assert.False(delivered);
        Assert.Equal(1, handler.CallCount);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("failed", diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET_TOKEN", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendWebhookEventAsync_DisposesResponseAfterSend()
    {
        var content = new TrackingHttpContent();
        using var handler = new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        using var svc = new AutomationService(_dir, handler, _ => { });

        Assert.True(await svc.SendWebhookEventAsync(
            WebhookOptions(),
            DiscordWebhookEventKind.PlayerJoin,
            "Player joined"));
        Assert.True(content.Disposed, "The response (and its content) must be disposed by the sender.");
    }

    [Fact]
    public async Task SendWebhookEventAsync_TruncatesDescriptionToConfiguredLimitIncludingEllipsis()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var svc = new AutomationService(_dir, handler, _ => { });
        var options = WebhookOptions();
        options.DiscordWebhookDescriptionMaxChars = 900;
        var longDescription = new string('x', 5000);

        Assert.True(await svc.SendWebhookEventAsync(
            options,
            DiscordWebhookEventKind.PlayerJoin,
            "Player joined",
            longDescription));

        var json = JObject.Parse(handler.LastBody!);
        var description = json["embeds"]![0]!["description"]!.ToString();
        Assert.Equal(900, description.Length);
        Assert.EndsWith("…", description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendWebhookEventAsync_Diagnostics_DoNotLeakUrlPathTokenOrResponseBody()
    {
        const string token = "SUPER_SECRET_TOKEN";
        const string responseBody = "response body that contains " + token;
        using var handler = new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                ReasonPhrase = "Server Error",
                Content = new StringContent(responseBody)
            });
        var diagnostics = new List<string>();
        using var svc = new AutomationService(_dir, handler, diagnostics.Add);
        var options = WebhookOptions($"https://discord.com/api/webhooks/123456789/{token}");

        Assert.False(await svc.SendWebhookEventAsync(
            options,
            DiscordWebhookEventKind.PlayerJoin,
            "Player joined",
            "Details"));

        var allDiagnostics = string.Join("\n", diagnostics);
        Assert.Contains("500", allDiagnostics, StringComparison.Ordinal);
        Assert.Contains("discord.com", allDiagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain(token, allDiagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/webhooks", allDiagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("response body", allDiagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendWebhookEventAsync_WhenDisabled_DoesNotInvokeTransport()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var svc = new AutomationService(_dir, handler, _ => { });
        var options = WebhookOptions();
        options.EnableDiscordWebhook = false;

        Assert.False(await svc.SendWebhookEventAsync(
            options,
            DiscordWebhookEventKind.PlayerJoin,
            "Player joined"));
        Assert.Equal(0, handler.CallCount);
    }

    private static ManagerOptions WebhookOptions(
        string url = "https://discord.com/api/webhooks/123/SECRET_TOKEN") =>
        new()
        {
            EnableDiscordWebhook = true,
            DiscordWebhookUrl = url,
            DiscordWebhookNotifyPlayerJoin = true,
            DiscordWebhookUseEmbeds = true,
            DiscordWebhookDescriptionMaxChars = 3500
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public int CallCount { get; private set; }

        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            return _respond(request);
        }
    }

    private sealed class TrackingHttpContent : HttpContent
    {
        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
