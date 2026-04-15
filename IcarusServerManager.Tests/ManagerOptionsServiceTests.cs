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
        Assert.Equal("Light", o.Theme);
        Assert.True(o.IntervalRestartEnabled);
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
        Assert.Equal(5, loaded.OptionsSchemaVersion);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenJsonInvalid()
    {
        File.WriteAllText(_path, "{ not json");
        var svc = new ManagerOptionsService(_path);
        var o = svc.Load();
        Assert.Equal("Light", o.Theme);
    }
}
