using IcarusServerManager.Models;
using Newtonsoft.Json;

namespace IcarusServerManager.Services;

internal sealed class ManagerOptionsService
{
    private readonly string _path;

    public ManagerOptionsService()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IcarusServerManager");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "manager-options.json");
    }

    /// <summary>For tests: read/write options at an explicit path (parent directory is created).</summary>
    public ManagerOptionsService(string optionsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(optionsFilePath);
        var dir = Path.GetDirectoryName(optionsFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _path = optionsFilePath;
    }

    public ManagerOptions Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new ManagerOptions();
            }

            var json = File.ReadAllText(_path);
            var options = JsonConvert.DeserializeObject<ManagerOptions>(json) ?? new ManagerOptions();
            ApplySchemaMigrations(options);
            return options;
        }
        catch
        {
            return new ManagerOptions();
        }
    }

    public void Save(ManagerOptions options)
    {
        var json = JsonConvert.SerializeObject(options, Formatting.Indented);
        File.WriteAllText(_path, json);
    }

    private static void ApplySchemaMigrations(ManagerOptions o)
    {
        if (o.OptionsSchemaVersion < 2)
        {
            o.DiscordWebhookNotifyServerRestart = o.EnableDiscordWebhook;
            o.DiscordWebhookChatThrottleSeconds = Math.Clamp(o.DiscordWebhookChatThrottleSeconds, 0, 120);
            o.OptionsSchemaVersion = 2;
        }

        if (o.OptionsSchemaVersion < 3)
        {
            o.DiscordWebhookNotifyUnexpectedExit = o.DiscordWebhookNotifyServerStop;
            o.DiscordWebhookGameplayThrottleSeconds = Math.Clamp(o.DiscordWebhookGameplayThrottleSeconds, 0, 120);
            o.DiscordWebhookHeartbeatIntervalHours = Math.Clamp(o.DiscordWebhookHeartbeatIntervalHours, 0, 168);
            o.OptionsSchemaVersion = 3;
        }

        if (o.OptionsSchemaVersion < 5)
        {
            ConsoleLogFilter.ApplyPreset(o, "Balanced");
            o.OptionsSchemaVersion = 5;
        }

        if (o.OptionsSchemaVersion < 6)
        {
            o.PauseIntervalRestartWhenEmpty = false;
            o.IntervalRestartUseEmptyIdleTimer = false;
            o.OptionsSchemaVersion = 6;
        }
    }
}
