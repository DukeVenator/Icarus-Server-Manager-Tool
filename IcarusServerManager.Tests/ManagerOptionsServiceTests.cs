using IcarusServerManager.Models;
using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ManagerOptionsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "IcarusServerManagerTests", Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public ManagerOptionsServiceTests()
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
            // best-effort cleanup on temp
        }
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenFileMissing()
    {
        var svc = new ManagerOptionsService(_path);
        var o = svc.Load();
        Assert.Equal("Dark", o.Theme);
        Assert.True(o.IntervalRestartEnabled);
        Assert.Equal(9, o.OptionsSchemaVersion);
        Assert.True(File.Exists(_path), "First run should persist the created defaults.");
    }

    [Fact]
    public void Load_WhenFileMissing_ImportsLegacyPorts_AndPersistsThem()
    {
        var svc = new ManagerOptionsService(_path);

        var first = svc.Load(() => (21001, 27101));

        Assert.Equal(21001, first.LaunchGamePort);
        Assert.Equal(27101, first.LaunchQueryPort);
        Assert.Equal(9, first.OptionsSchemaVersion);
        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + ".bak"), "No backup is needed when the file did not exist yet.");

        // The first-run import was persisted, so a later load must not re-import newer legacy values.
        var second = new ManagerOptionsService(_path).Load(() => (17777, 27015));
        Assert.Equal(21001, second.LaunchGamePort);
        Assert.Equal(27101, second.LaunchQueryPort);
    }

    [Fact]
    public void Load_WhenFileMissing_IgnoresInvalidLegacyPorts()
    {
        var svc = new ManagerOptionsService(_path);

        var o = svc.Load(() => (0, 70000));

        Assert.Equal(17777, o.LaunchGamePort);
        Assert.Equal(27015, o.LaunchQueryPort);
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsValues()
    {
        var svc = new ManagerOptionsService(_path);
        var original = new ManagerOptions
        {
            Theme = "Dark",
            IntervalRestartMinutes = 90,
            UserDirOverride = @"D:\userdata",
            UpdateScheduleEnabled = true,
            UpdateScheduleTime = "03:15"
        };
        svc.Save(original);

        var loaded = svc.Load();
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal(90, loaded.IntervalRestartMinutes);
        Assert.Equal(@"D:\userdata", loaded.UserDirOverride);
        Assert.True(loaded.UpdateScheduleEnabled);
        Assert.Equal("03:15", loaded.UpdateScheduleTime);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsLaunchArguments()
    {
        var svc = new ManagerOptionsService(_path);
        var original = new ManagerOptions
        {
            LaunchGamePort = 20000,
            LaunchQueryPort = 27020,
            LaunchLogPath = @"C:\logs\icarus.log",
            OptionsSchemaVersion = 4
        };
        svc.Save(original);

        var loaded = svc.Load();
        Assert.Equal(20000, loaded.LaunchGamePort);
        Assert.Equal(27020, loaded.LaunchQueryPort);
        Assert.Equal(@"C:\logs\icarus.log", loaded.LaunchLogPath);
        Assert.Equal(9, loaded.OptionsSchemaVersion);
    }

    [Fact]
    public void Load_ReportsMalformedJson_AndLeavesFileUnchanged()
    {
        const string malformed = "{ not json";
        File.WriteAllText(_path, malformed);
        var svc = new ManagerOptionsService(_path);

        var ex = Assert.Throws<InvalidDataException>(() => svc.Load());
        Assert.Contains("left unchanged", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(malformed, File.ReadAllText(_path));
        Assert.False(File.Exists(_path + ".bak"), "A failed load must not publish defaults or a backup.");
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Save_CreatesBackupOfPreviousOptionsBeforeReplacing()
    {
        var svc = new ManagerOptionsService(_path);
        svc.Save(new ManagerOptions { Theme = "Dark", IntervalRestartMinutes = 45 });
        var originalJson = File.ReadAllText(_path);

        svc.Save(new ManagerOptions { Theme = "Light", IntervalRestartMinutes = 90 });

        Assert.Equal(originalJson, File.ReadAllText(_path + ".bak"));
        Assert.Equal("Light", svc.Load().Theme);
        Assert.Equal(90, svc.Load().IntervalRestartMinutes);
    }

    [Fact]
    public void Save_WhenBackupCannotBeCreated_DoesNotReplaceOriginalFile()
    {
        var svc = new ManagerOptionsService(_path);
        svc.Save(new ManagerOptions { Theme = "Dark", IntervalRestartMinutes = 45 });
        var originalJson = File.ReadAllText(_path);

        using (new FileStream(_path + ".bak", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var ex = Assert.Throws<IOException>(() => svc.Save(new ManagerOptions { Theme = "Light" }));
            Assert.Contains("backup", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(originalJson, File.ReadAllText(_path));
    }

    [Fact]
    public void Save_WhenBackupPathIsDirectory_DoesNotReplaceOriginalFile()
    {
        var svc = new ManagerOptionsService(_path);
        svc.Save(new ManagerOptions { Theme = "Dark" });
        var originalJson = File.ReadAllText(_path);
        Directory.CreateDirectory(_path + ".bak");

        Assert.Throws<IOException>(() => svc.Save(new ManagerOptions { Theme = "Light" }));

        Assert.Equal(originalJson, File.ReadAllText(_path));
    }

    [Fact]
    public void Load_MigratesLegacyPorts_BeforeSchemaFourGuard()
    {
        File.WriteAllText(_path, """
            {
              "OptionsSchemaVersion": 3,
              "LaunchGamePort": 17777,
              "LaunchQueryPort": 27015
            }
            """);
        var svc = new ManagerOptionsService(_path);

        var o = svc.Load(() => (20011, 27022));

        Assert.Equal(20011, o.LaunchGamePort);
        Assert.Equal(27022, o.LaunchQueryPort);
        Assert.Equal(9, o.OptionsSchemaVersion);

        // The migrated file was committed, so a second load keeps the custom ports.
        var again = new ManagerOptionsService(_path).Load(() => (17777, 27015));
        Assert.Equal(20011, again.LaunchGamePort);
        Assert.Equal(27022, again.LaunchQueryPort);
    }

    [Fact]
    public void Load_DoesNotApplyLegacyPorts_AfterSchemaFour()
    {
        File.WriteAllText(_path, """
            {
              "OptionsSchemaVersion": 4,
              "LaunchGamePort": 20100,
              "LaunchQueryPort": 27100
            }
            """);
        var svc = new ManagerOptionsService(_path);

        var o = svc.Load(() => (17777, 27015));

        Assert.Equal(20100, o.LaunchGamePort);
        Assert.Equal(27100, o.LaunchQueryPort);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsDiscordBehaviorOptions()
    {
        var svc = new ManagerOptionsService(_path);
        var original = new ManagerOptions
        {
            DiscordWebhookUseTitleEmojis = false,
            DiscordWebhookShowEmbedAuthor = false,
            DiscordWebhookShowEmbedTimestamp = false,
            DiscordWebhookShowPortsOnEmbeds = false,
            DiscordWebhookShowSessionOnEmbeds = false,
            DiscordWebhookIncludeProspectOnStart = false,
            DiscordWebhookHeartbeatShowPolicyLine = false,
            DiscordWebhookUseThemedLabels = false,
            DiscordWebhookPlainTextDescriptions = true,
            DiscordWebhookCustomFooter = "Custom footer line",
            DiscordWebhookDescriptionMaxChars = 1200
        };
        svc.Save(original);

        var loaded = svc.Load();
        Assert.False(loaded.DiscordWebhookUseTitleEmojis);
        Assert.False(loaded.DiscordWebhookShowEmbedAuthor);
        Assert.False(loaded.DiscordWebhookShowEmbedTimestamp);
        Assert.False(loaded.DiscordWebhookShowPortsOnEmbeds);
        Assert.False(loaded.DiscordWebhookShowSessionOnEmbeds);
        Assert.False(loaded.DiscordWebhookIncludeProspectOnStart);
        Assert.False(loaded.DiscordWebhookHeartbeatShowPolicyLine);
        Assert.False(loaded.DiscordWebhookUseThemedLabels);
        Assert.True(loaded.DiscordWebhookPlainTextDescriptions);
        Assert.Equal("Custom footer line", loaded.DiscordWebhookCustomFooter);
        Assert.Equal(1200, loaded.DiscordWebhookDescriptionMaxChars);
        Assert.Equal(9, loaded.OptionsSchemaVersion);
    }
}
