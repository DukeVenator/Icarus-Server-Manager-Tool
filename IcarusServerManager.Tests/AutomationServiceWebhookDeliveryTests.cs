using IcarusServerManager.Models;
using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class AutomationServiceWebhookDeliveryTests
{
    private readonly AutomationService _svc = new(Path.Combine(Path.GetTempPath(), "IcarusServerManagerTests", "profiles-unused"));

    [Fact]
    public async Task SendWebhookEventAsync_ReturnsImmediately_WhenMasterSwitchOff()
    {
        var o = new ManagerOptions
        {
            EnableDiscordWebhook = false,
            DiscordWebhookUrl = "https://example.invalid/webhook",
            DiscordWebhookNotifyPlayerJoin = true
        };
        await _svc.SendWebhookEventAsync(o, DiscordWebhookEventKind.PlayerJoin, "t", "d");
    }

    [Fact]
    public async Task SendWebhookEventAsync_ReturnsImmediately_WhenUrlEmpty()
    {
        var o = new ManagerOptions
        {
            EnableDiscordWebhook = true,
            DiscordWebhookUrl = "   ",
            DiscordWebhookNotifyPlayerJoin = true
        };
        await _svc.SendWebhookEventAsync(o, DiscordWebhookEventKind.PlayerJoin, "t", "d");
    }

    [Fact]
    public async Task SendWebhookEventAsync_ReturnsImmediately_WhenPerEventToggleOff()
    {
        var o = new ManagerOptions
        {
            EnableDiscordWebhook = true,
            DiscordWebhookUrl = "https://example.invalid/webhook",
            DiscordWebhookNotifyPlayerJoin = false
        };
        await _svc.SendWebhookEventAsync(o, DiscordWebhookEventKind.PlayerJoin, "t", "d");
    }

    [Fact]
    public async Task SendWebhookEventAsync_Heartbeat_NoOp_WhenIntervalZero()
    {
        var o = new ManagerOptions
        {
            EnableDiscordWebhook = true,
            DiscordWebhookUrl = "https://example.invalid/webhook",
            DiscordWebhookHeartbeatIntervalHours = 0
        };
        await _svc.SendWebhookEventAsync(o, DiscordWebhookEventKind.ManagerHeartbeat, "h", "b");
    }
}
