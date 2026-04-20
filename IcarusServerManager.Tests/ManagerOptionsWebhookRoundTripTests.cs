using IcarusServerManager.Models;
using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ManagerOptionsWebhookRoundTripTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "IcarusServerManagerTests", Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public ManagerOptionsWebhookRoundTripTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "manager-options.json");
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
    public void SaveLoad_RoundTripsExtendedWebhookOptions()
    {
        var svc = new ManagerOptionsService(_path);
        var original = new ManagerOptions
        {
            EnableDiscordWebhook = true,
            DiscordWebhookUrl = "https://discord.com/api/webhooks/test/token",
            DiscordWebhookNotifyRestartWarning = true,
            DiscordWebhookNotifyScheduledUpdate = true,
            DiscordWebhookNotifySteamCmd = false,
            DiscordWebhookNotifyIniSaveFailed = true,
            DiscordWebhookNotifyLevelUp = true,
            DiscordWebhookGameplayThrottleSeconds = 7,
            DiscordWebhookHeartbeatIntervalHours = 12,
            OptionsSchemaVersion = 3
        };
        svc.Save(original);

        var loaded = svc.Load();
        Assert.True(loaded.DiscordWebhookNotifyRestartWarning);
        Assert.True(loaded.DiscordWebhookNotifyScheduledUpdate);
        Assert.False(loaded.DiscordWebhookNotifySteamCmd);
        Assert.True(loaded.DiscordWebhookNotifyIniSaveFailed);
        Assert.True(loaded.DiscordWebhookNotifyLevelUp);
        Assert.Equal(7, loaded.DiscordWebhookGameplayThrottleSeconds);
        Assert.Equal(12, loaded.DiscordWebhookHeartbeatIntervalHours);
        Assert.Equal(8, loaded.OptionsSchemaVersion);
    }
}
