using IcarusServerManager.Models;
using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ConsoleLogFilterTests
{
    [Fact]
    public void Classify_ManagerLine_DetectsError()
    {
        var line = "[2026-01-01 12:00:00] [ERROR] boom";
        Assert.Equal(ConsoleLogLineKind.ManagerError, ConsoleLogFilter.Classify(line, false));
    }

    [Fact]
    public void Classify_GamePayload_Display_IsVerbose()
    {
        var line = "[2026-01-01 12:00:00] [INFO] LogTemp: Display: hello";
        Assert.Equal(ConsoleLogLineKind.GameVerbose, ConsoleLogFilter.Classify(line, true));
    }

    [Fact]
    public void ApplyPreset_Minimal_DisablesManagerInfoAndGeneralGame()
    {
        var o = new ManagerOptions();
        ConsoleLogFilter.ApplyPreset(o, "Minimal");
        Assert.False(o.ConsoleShowManagerInfo);
        Assert.False(o.ConsoleShowGameGeneral);
        Assert.True(o.ConsoleShowManagerError);
        Assert.Equal("Minimal", o.ConsoleLogPreset);
    }

    [Fact]
    public void ShouldDisplay_RespectsOptions()
    {
        var o = new ManagerOptions { ConsoleShowManagerInfo = false };
        Assert.False(ConsoleLogFilter.ShouldDisplay(ConsoleLogLineKind.ManagerInfo, o));
        Assert.True(ConsoleLogFilter.ShouldDisplay(ConsoleLogLineKind.ManagerError, o));
    }
}
