using System.Diagnostics;

namespace IcarusProspectEditor.Services;

internal static class AppLogService
{
    private static readonly object Sync = new();
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IcarusProspectEditor",
        "logs");

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

    public static void Error(string message, Exception ex) => Write("ERROR", $"{message}{Environment.NewLine}{ex}");

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var file = Path.Combine(LogDir, $"editor-{DateTime.Now:yyyyMMdd}.log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
            lock (Sync)
            {
                File.AppendAllText(file, line);
            }
        }
        catch
        {
            // Logging must never break editor flow.
        }
    }
}
