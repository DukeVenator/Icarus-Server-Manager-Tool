using IcarusServerManager.Models;
using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ManagerOptionsSchemaMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "IcarusServerManagerTests", Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public ManagerOptionsSchemaMigrationTests()
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
    public void Load_MigratesSchemaV1_WhenWebhookWasEnabled()
    {
        File.WriteAllText(_path, """
            {
              "EnableDiscordWebhook": true,
              "DiscordWebhookUrl": "https://discord.com/api/webhooks/x/y",
              "OptionsSchemaVersion": 0
            }
            """);
        var svc = new ManagerOptionsService(_path);
        var o = svc.Load();
        Assert.Equal(8, o.OptionsSchemaVersion);
        Assert.True(o.DiscordWebhookNotifyServerRestart);
    }

    [Fact]
    public void Load_DoesNotForceRestartNotify_WhenWebhookWasOff()
    {
        File.WriteAllText(_path, """
            {
              "EnableDiscordWebhook": false,
              "OptionsSchemaVersion": 0
            }
            """);
        var svc = new ManagerOptionsService(_path);
        var o = svc.Load();
        Assert.Equal(8, o.OptionsSchemaVersion);
        Assert.False(o.DiscordWebhookNotifyServerRestart);
    }

    [Fact]
    public void Load_MigratesSchemaV2_ServerStopCopiesToUnexpectedExit()
    {
        File.WriteAllText(_path, """
            {
              "DiscordWebhookNotifyServerStop": true,
              "OptionsSchemaVersion": 2
            }
            """);
        var svc = new ManagerOptionsService(_path);
        var o = svc.Load();
        Assert.Equal(8, o.OptionsSchemaVersion);
        Assert.True(o.DiscordWebhookNotifyUnexpectedExit);
        Assert.True(o.DiscordWebhookNotifyServerStop);
    }

    [Fact]
    public void Load_MigratesSchemaV7_ClampsGracefulShutdownWait()
    {
        File.WriteAllText(_path, """
            {
              "OptionsSchemaVersion": 6,
              "GracefulShutdownWaitSeconds": 5
            }
            """);
        var svc = new ManagerOptionsService(_path);
        var o = svc.Load();
        Assert.Equal(8, o.OptionsSchemaVersion);
        Assert.True(o.GracefulShutdownTryCtrlC);
        Assert.Equal(120, o.GracefulShutdownWaitSeconds);
    }

    [Fact]
    public void Load_MigratesSchemaV8_SetsDiscordBehaviorDefaults()
    {
        File.WriteAllText(_path, """
            {
              "OptionsSchemaVersion": 7,
              "DiscordWebhookUseTitleEmojis": false
            }
            """);
        var svc = new ManagerOptionsService(_path);
        var o = svc.Load();
        Assert.Equal(8, o.OptionsSchemaVersion);
        Assert.True(o.DiscordWebhookUseTitleEmojis);
        Assert.True(o.DiscordWebhookShowEmbedAuthor);
        Assert.True(o.DiscordWebhookShowEmbedTimestamp);
        Assert.Equal(3500, o.DiscordWebhookDescriptionMaxChars);
    }
}
