namespace IcarusServerManager.Services;

internal readonly record struct LogNotification(string Line, bool IsGameProcessOutput);

internal sealed class Logger
{
    private readonly string _logFolder;
    private readonly object _lock = new();
    public event Action<LogNotification>? OnLog;

    public Logger()
    {
        _logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(_logFolder);
    }

    /// <summary>For tests: write logs under a specific folder.</summary>
    public Logger(string logFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logFolder);
        _logFolder = logFolder;
        Directory.CreateDirectory(_logFolder);
    }

    public void Info(string message, bool isGameProcessOutput = false) => Write("INFO", message, isGameProcessOutput);

    public void Warn(string message) => Write("WARN", message, false);

    public void Error(string message, Exception? ex = null)
    {
        if (ex != null)
        {
            message = $"{message}{Environment.NewLine}{ex}";
        }

        Write("ERROR", message, false);
    }

    private void Write(string level, string message, bool isGameProcessOutput)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        lock (_lock)
        {
            var path = Path.Combine(_logFolder, $"manager-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(path, line + Environment.NewLine);
        }

        var gameFlag = isGameProcessOutput && string.Equals(level, "INFO", StringComparison.Ordinal);
        OnLog?.Invoke(new LogNotification(line, gameFlag));
    }
}
