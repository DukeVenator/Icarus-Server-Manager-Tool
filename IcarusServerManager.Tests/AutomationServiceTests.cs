using IcarusServerManager.Models;
using IcarusServerManager.Services;
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
}
