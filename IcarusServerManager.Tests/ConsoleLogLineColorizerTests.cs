using System.Drawing;
using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ConsoleLogLineColorizerTests
{
    [Theory]
    [InlineData("[2026-04-15 14:00:00] [ERROR] Failed.", true)]
    [InlineData("[2026-04-15 14:00:00] [WARN] Heads up.", true)]
    [InlineData("[2026-04-15 14:00:00] [INFO] LogStreaming: Error: Missing file.", true)]
    [InlineData("[2026-04-15 14:00:00] [INFO] LogInit: Warning: Unregistered cvar.", true)]
    [InlineData("[2026-04-15 14:00:00] [INFO] LogNet: Display: Mounted.", true)]
    [InlineData("[2026-04-15 14:00:00] [INFO] Game server started.", true)]
    public void ResolveLineColor_Returns_Value_For_Classified_Lines(string line, bool dark)
    {
        var c = ConsoleLogLineColorizer.ResolveLineColor(line, dark);
        Assert.NotNull(c);
        Assert.NotEqual(Color.Empty, c.Value);
    }

    [Fact]
    public void Error_Is_Redder_Than_Warning_Dark_Theme()
    {
        var err = ConsoleLogLineColorizer.ResolveLineColor("[t] [ERROR] x", true)!.Value;
        var warn = ConsoleLogLineColorizer.ResolveLineColor("[t] [WARN] x", true)!.Value;
        Assert.True(err.R < warn.R || err.G < warn.G);
    }

    [Fact]
    public void Important_Phrase_Before_Display_Is_Accent_Not_Muted()
    {
        var line = "[2026-04-15 14:18:19] [INFO] LogGameMode: Display: Match State Changed from EnteringMap to WaitingToStart";
        var c = ConsoleLogLineColorizer.ResolveLineColor(line, true)!.Value;
        // Accent (blue-ish), not low-priority gray
        Assert.True(c.B > 160 && c.R < 200);
    }

    [Fact]
    public void Plain_Display_Line_Is_Muted()
    {
        var line = "[2026-04-15 14:18:12] [INFO] LogPakFile: Display: Mounting pak file foo.pak.";
        var c = ConsoleLogLineColorizer.ResolveLineColor(line, true)!.Value;
        Assert.True(c.R < 200 && c.G < 200);
    }

    [Fact]
    public void Icarus_StateRecorder_BeginRecording_Is_Muted_Even_When_Display()
    {
        var line =
            "[2026-04-15 15:43:09] [INFO] LogIcarusStateRecorderComponent: Display: BeginRecording - OwningActor: BP_EnzymeGeyser10_2 | Recorder: EnzymeGeyserRecorderComponent_0";
        var c = ConsoleLogLineColorizer.ResolveLineColor(line, true)!.Value;
        Assert.True(c.R < 200 && c.G < 200);
    }
}
