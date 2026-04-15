using IcarusServerManager.Models;
using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ServerSettingsIniServiceTests : IDisposable
{
    private readonly ServerSettingsIniService _service = new();
    private readonly string? _tempDir;

    public ServerSettingsIniServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "IcarusServerManagerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (_tempDir != null && Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // best-effort
            }
        }
    }

    [Fact]
    public void ResolveIniPath_UsesDefaultPath_WhenNoOverride()
    {
        var path = _service.ResolveIniPath(@"F:\Games\IcarusServer", null, null);
        Assert.Equal(
            Path.Combine(@"F:\Games\IcarusServer", "Icarus", "Saved", "Config", "WindowsServer", "ServerSettings.ini"),
            path);
    }

    [Fact]
    public void ResolveProspectsDirectory_UsesDefaultPath_WhenNoOverride()
    {
        var path = _service.ResolveProspectsDirectory(@"F:\Games\IcarusServer", null, null);
        Assert.Equal(
            Path.Combine(@"F:\Games\IcarusServer", "Icarus", "Saved", "PlayerData", "DedicatedServer", "Prospects"),
            path);
    }

    [Fact]
    public void ResolveProspectsDirectory_UsesSavedSuffix_WhenOverride()
    {
        var path = _service.ResolveProspectsDirectory(@"C:\srv", "userdata", "staging");
        Assert.Equal(
            Path.Combine(@"C:\srv", "userdata", "Saved_staging", "PlayerData", "DedicatedServer", "Prospects"),
            path);
    }

    [Fact]
    public void Validate_ReturnsError_WhenMaxPlayersOutOfRange()
    {
        var model = new DedicatedServerSettingsModel { MaxPlayers = 12 };
        var result = _service.Validate(model);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MaxPlayers", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ReturnsWarning_WhenAdminPasswordEmpty()
    {
        var model = new DedicatedServerSettingsModel { AdminPassword = "" };
        var result = _service.Validate(model);
        Assert.Contains(result.Warnings, w => w.Contains("AdminPassword", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ReturnsError_WhenShutdownIfNotJoinedForIsNaN()
    {
        var model = new DedicatedServerSettingsModel { ShutdownIfNotJoinedFor = double.NaN };
        var result = _service.Validate(model);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("ShutdownIfNotJoinedFor", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ReturnsError_WhenShutdownIfEmptyForIsInfinity()
    {
        var model = new DedicatedServerSettingsModel { ShutdownIfEmptyFor = double.PositiveInfinity };
        var result = _service.Validate(model);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("ShutdownIfEmptyFor", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildLaunchArguments_IncludesConfiguredFlags()
    {
        var model = new DedicatedServerSettingsModel
        {
            SteamServerName = "Test Server",
            LoadProspect = "ProspectOne",
            CreateProspect = "Tier1_Forest_Recon_0 2 false Smoke",
            ResumeProspect = true
        };

        var args = _service.BuildLaunchArguments(model, 17777, 27015);
        Assert.Contains("-Port=17777", args, StringComparison.Ordinal);
        Assert.Contains("-QueryPort=27015", args, StringComparison.Ordinal);
        Assert.Contains("-SteamServerName=\"Test Server\"", args, StringComparison.Ordinal);
        Assert.Contains("-LoadProspect=\"ProspectOne\"", args, StringComparison.Ordinal);
        Assert.Contains("-ResumeProspect", args, StringComparison.Ordinal);
        Assert.Contains("-CreateProspect=\"Tier1_Forest_Recon_0 2 false Smoke\"", args, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveIniPath_UsesRootedUserDir_WithoutServerLocationPrefix()
    {
        var rooted = Path.Combine(_tempDir!, "userdata");
        Directory.CreateDirectory(rooted);
        var path = _service.ResolveIniPath(@"C:\ignored", rooted, null);
        Assert.Equal(
            Path.Combine(rooted, "Saved", "Config", "WindowsServer", "ServerSettings.ini"),
            path);
    }

    [Fact]
    public void Validate_ReturnsError_WhenSteamServerNameTooLong()
    {
        var model = new DedicatedServerSettingsModel { SteamServerName = new string('x', 65) };
        var result = _service.Validate(model);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SteamServerName", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildLaunchArguments_StripsEmbeddedQuotes_InSteamServerName()
    {
        var model = new DedicatedServerSettingsModel { SteamServerName = "Say \"Hi\"" };
        var args = _service.BuildLaunchArguments(model, 1, 2);
        Assert.DoesNotContain("\\\"", args);
        Assert.Contains("-SteamServerName=\"Say Hi\"", args, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLaunchArguments_IncludesLogPath_WhenProvided()
    {
        var model = new DedicatedServerSettingsModel();
        var args = _service.BuildLaunchArguments(model, 17777, 27015, @"C:\logs\game.log");
        Assert.Contains("-Log=\"C:\\logs\\game.log\"", args, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadSave_RoundTripsDedicatedSection()
    {
        var iniPath = Path.Combine(_tempDir!, "ServerSettings.ini");
        var written = new DedicatedServerSettingsModel
        {
            SteamServerName = "RT Server",
            SessionName = "Lobby1",
            MaxPlayers = 3,
            ResumeProspect = false,
            JoinPassword = "secret",
            GameSaveFrequency = 12.5
        };
        _service.Save(iniPath, written);
        var loaded = _service.Load(iniPath);
        Assert.Equal("RT Server", loaded.SteamServerName);
        Assert.Equal("Lobby1", loaded.SessionName);
        Assert.Equal(3, loaded.MaxPlayers);
        Assert.False(loaded.ResumeProspect);
        Assert.Equal("secret", loaded.JoinPassword);
        Assert.Equal(12.5, loaded.GameSaveFrequency);
    }
}
