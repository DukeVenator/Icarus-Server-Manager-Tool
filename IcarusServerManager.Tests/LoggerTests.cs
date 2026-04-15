using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class LoggerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "IcarusServerManagerTests", Guid.NewGuid().ToString("N"));

    public LoggerTests()
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
    public void Info_InvokesOnLog_WithLevel()
    {
        string? captured = null;
        var logger = new Logger(_dir);
        logger.OnLog += n => captured = n.Line;
        logger.Info("hello");

        Assert.NotNull(captured);
        Assert.Contains("[INFO]", captured, StringComparison.Ordinal);
        Assert.Contains("hello", captured, StringComparison.Ordinal);
    }

    [Fact]
    public void Info_WithGameFlag_SetsIsGameProcessOutput()
    {
        LogNotification? captured = null;
        var logger = new Logger(_dir);
        logger.OnLog += n => captured = n;
        logger.Info("ue line", isGameProcessOutput: true);

        Assert.NotNull(captured);
        Assert.True(captured.Value.IsGameProcessOutput);
        Assert.Contains("ue line", captured.Value.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void Info_WithoutGameFlag_IsNotGameProcessOutput()
    {
        LogNotification? captured = null;
        var logger = new Logger(_dir);
        logger.OnLog += n => captured = n;
        logger.Info("mgr");

        Assert.NotNull(captured);
        Assert.False(captured.Value.IsGameProcessOutput);
    }
}
