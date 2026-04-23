using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace IcarusProspectEditor.Services;

internal static class AppLogService
{
    private static readonly object Sync = new();
    private static readonly Queue<string> RecentActions = new();
    private const int MaxRecentActions = 100;
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IcarusProspectEditor",
        "logs");
    private static bool _sessionStartLogged;

    public static string GetLogDirectoryPath() => LogDir;

    public static void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            UserAction("Opened log folder from UI.");
            Process.Start(new ProcessStartInfo
            {
                FileName = LogDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Error("Failed to open log folder.", ex);
        }
    }

    public static void UserAction(string message) => Write("USER", message);

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Debug(string message) => Write("DEBUG", message);

    public static void Error(string message, Exception ex) => Write("ERROR", $"{message}{Environment.NewLine}{ex}");

    public static void LogSessionStart()
    {
        lock (Sync)
        {
            if (_sessionStartLogged)
            {
                return;
            }

            _sessionStartLogged = true;
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var processPath = Environment.ProcessPath ?? "unknown";
        var runtime = RuntimeInformation.FrameworkDescription;
        var os = RuntimeInformation.OSDescription;
        Info($"Session started: version={version}; runtime={runtime}; os={os}; process={processPath}");
    }

    public static string DumpRecentActions()
    {
        lock (Sync)
        {
            if (RecentActions.Count == 0)
            {
                return "Recent actions: <none>";
            }

            var sb = new StringBuilder();
            sb.AppendLine("Recent actions (oldest to newest):");
            foreach (var line in RecentActions)
            {
                sb.AppendLine(line);
            }

            return sb.ToString().TrimEnd();
        }
    }

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var file = Path.Combine(LogDir, $"editor-{DateTime.Now:yyyyMMdd}.log");
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var line = $"[{timestamp}] [{level}] {message}";
            lock (Sync)
            {
                RecentActions.Enqueue($"[{timestamp}] [{level}] {message}");
                while (RecentActions.Count > MaxRecentActions)
                {
                    RecentActions.Dequeue();
                }

                File.AppendAllText(file, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never break editor flow.
        }
    }
}
