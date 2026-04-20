using System.Diagnostics;

namespace ManagerUpdater;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var map = ParseArgs(args);
            if (!map.TryGetValue("source", out var sourceDir) ||
                !map.TryGetValue("target", out var targetDir) ||
                !map.TryGetValue("exe", out var exeName))
            {
                return 2;
            }

            if (map.TryGetValue("pid", out var pidRaw) && int.TryParse(pidRaw, out var pid))
            {
                WaitForProcessExit(pid, TimeSpan.FromMinutes(2));
            }

            if (!Directory.Exists(sourceDir))
            {
                return 3;
            }

            Directory.CreateDirectory(targetDir);
            CopyDirectory(sourceDir, targetDir);

            var exePath = Path.Combine(targetDir, exeName);
            if (File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo(exePath) { WorkingDirectory = targetDir, UseShellExecute = true });
                return 0;
            }

            return 4;
        }
        catch
        {
            return 1;
        }
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = args[i][2..];
            var value = i + 1 < args.Length ? args[i + 1] : string.Empty;
            if (!value.StartsWith("--", StringComparison.Ordinal))
            {
                map[key] = value;
                i++;
            }
        }

        return map;
    }

    private static void WaitForProcessExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch
        {
            // best-effort
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(targetDir, rel));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("ManagerUpdater", StringComparison.OrdinalIgnoreCase))
            {
                // Cannot replace running updater binaries while this process is active.
                continue;
            }

            var rel = Path.GetRelativePath(sourceDir, file);
            var dst = Path.Combine(targetDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

            const int retries = 20;
            for (var i = 0; i < retries; i++)
            {
                try
                {
                    File.Copy(file, dst, overwrite: true);
                    break;
                }
                catch when (i < retries - 1)
                {
                    Thread.Sleep(250);
                }
            }
        }
    }
}
