using IcarusServerManager.Models;
using IcarusServerManager.Services;
using IcarusServerManager.UI;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace IcarusServerManager;

public partial class Form1 : Form
{
    private void SetupTimers()
    {
        policyTimer = new System.Windows.Forms.Timer { Interval = 10000 };
        metricsTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        updateTimer = new System.Windows.Forms.Timer { Interval = 30000 };
        policyTimer.Tick += (_, _) => EvaluatePolicies();
        metricsTimer.Tick += (_, _) => UpdateStats();
        updateTimer.Tick += (_, _) =>
        {
            CheckScheduledUpdate();
            _ = TickManagerUpdateCheckAsync();
        };
        heartbeatTimer.Tick += (_, _) => HeartbeatDiscordTick();
        policyTimer.Start();
        metricsTimer.Start();
        updateTimer.Start();
        heartbeatTimer.Start();
    }

    private void PrimeInstallPathWebhookState()
    {
        if (string.IsNullOrWhiteSpace(serverLocationBox.Text))
        {
            _installPathPreviouslyValid = false;
            return;
        }

        var exe = Path.Combine(serverLocationBox.Text.Trim(), "Icarus", "Binaries", "Win64", "IcarusServer-Win64-Shipping.exe");
        _installPathPreviouslyValid = File.Exists(exe);
    }

    private void HeartbeatDiscordTick()
    {
        if (managerOptions.DiscordWebhookHeartbeatIntervalHours <= 0)
        {
            return;
        }

        var hours = managerOptions.DiscordWebhookHeartbeatIntervalHours;
        var now = DateTime.UtcNow;
        if (_lastDiscordHeartbeatUtc == DateTime.MinValue)
        {
            _lastDiscordHeartbeatUtc = now;
            return;
        }

        if ((now - _lastDiscordHeartbeatUtc).TotalHours < hours)
        {
            return;
        }

        _lastDiscordHeartbeatUtc = now;
            PostDiscordWebhook(
                DiscordWebhookEventKind.ManagerHeartbeat,
                "Still here",
                "_Quiet pulse from the manager — game state is in the fields below._",
                DiscordWebhookEmbedFactory.BuildHeartbeatExtras(managerOptions, currentServerSettings, serverStarted, startTime, lastRestartReason));
    }

    private async void EvaluatePolicies()
    {
        var sample = metricsSampler.Sample(serverProc, startTime);
        var decision = restartPolicyService.Evaluate(
            managerOptions,
            serverStarted,
            startTime,
            DateTime.Now,
            crashed: crashDetected,
            memoryMb: sample.MemoryMb,
            maybeEmptyFromLogs: possibleServerEmpty);

        if (decision.ShouldWarn)
        {
            logger.Warn(decision.Reason);
            if (!_discordPolicyWarningActive)
            {
                _discordPolicyWarningActive = true;
                PostDiscordWebhook(
                    DiscordWebhookEventKind.RestartWarning,
                    "Heads-up",
                    DiscordWebhookEmbedFactory.TruncateDescription(decision.Reason, managerOptions),
                    DiscordWebhookEmbedFactory.BuildOperationalExtras(managerOptions, currentServerSettings));
            }
        }
        else
        {
            _discordPolicyWarningActive = false;
        }

        if (decision.ShouldRestart)
        {
            await RestartServerAsync(decision.Reason);
        }
    }

    private void UpdateStats()
    {
        var sample = metricsSampler.Sample(serverProc, startTime);
        metricsHistory.Add(sample);
        if (metricsHistory.Count > 1000)
        {
            metricsHistory.RemoveRange(0, 200);
        }

        var restartCountdown = startTime.AddMinutes(managerOptions.IntervalRestartMinutes) - DateTime.Now;
        var health = "Green";
        if (sample.MemoryMb > managerOptions.HighMemoryMbThreshold * 0.9)
        {
            health = "Yellow";
        }

        if (!serverStarted)
        {
            health = "Red";
        }

        SetStat("Uptime", sample.Uptime.ToString(@"dd\.hh\:mm\:ss"));
        SetStat("RestartCountdown", restartCountdown.TotalSeconds > 0 ? restartCountdown.ToString(@"hh\:mm\:ss") : "due");
        SetStat("LastRestartReason", lastRestartReason);
        SetStat("CrashCount", crashCount.ToString());
        SetStat("CpuPercent", $"{sample.CpuPercent:F1}%");
        SetStat("MemoryMb", $"{sample.MemoryMb:F0} MB");
        SetStat("Health", health);
        SetStat("RestartHistory", restartHistory.Count == 0 ? "-" : restartHistory[^1]);
        UpdateOnlinePlayersPanel();
        UpdateMetricsChart();
    }

    private void UpdateOnlinePlayersPanel()
    {
        if (_onlinePlayersList == null)
        {
            return;
        }

        _onlinePlayersList.Items.Clear();
        var prospectsDir = TryResolveProspectsDirectorySilent();
        var lastName = GetString("LastProspectName", string.Empty).Trim();
        if (string.IsNullOrEmpty(prospectsDir) || string.IsNullOrEmpty(lastName))
        {
            _onlinePlayersList.Items.Add("(Set install location and Last Prospect Name.)");
            return;
        }

        var fullPath = Path.Combine(prospectsDir, lastName + ".json");
        if (!File.Exists(fullPath))
        {
            _onlinePlayersList.Items.Add($"(No file: {lastName}.json)");
            return;
        }

        var mtime = File.GetLastWriteTimeUtc(fullPath);
        var cacheKey = $"{fullPath}|{mtime.Ticks}";
        if (_prospectPlayersCacheKey != cacheKey || (DateTime.UtcNow - _prospectPlayersCacheUtc).TotalSeconds > 8)
        {
            try
            {
                _prospectPlayersCache = ProspectSummaryReader.Read(fullPath);
                _prospectPlayersCacheKey = cacheKey;
                _prospectPlayersCacheUtc = DateTime.UtcNow;
            }
            catch
            {
                _prospectPlayersCache = null;
            }
        }

        var summary = _prospectPlayersCache;
        if (summary == null)
        {
            _onlinePlayersList.Items.Add("(Could not read prospect header.)");
            return;
        }

        var fromJson = summary.Members.Where(m => m.IsCurrentlyPlaying)
            .Select(m => $"{m.CharacterName} ({m.AccountName})  {m.UserId}")
            .ToList();
        foreach (var line in fromJson)
        {
            _onlinePlayersList.Items.Add("Save: " + line);
        }

        foreach (var hint in playerTracker.HintNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (fromJson.Any(j => j.Contains(hint, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _onlinePlayersList.Items.Add("Output: " + hint);
        }

        if (_onlinePlayersList.Items.Count == 0)
        {
            _onlinePlayersList.Items.Add("(Nobody flagged IsCurrentlyPlaying; no join hints in log.)");
        }
    }

    private void UpdateMetricsChart()
    {
        if (metricsGraph == null)
        {
            return;
        }

        metricsGraph.Invalidate();
    }

    private void CheckScheduledUpdate()
    {
        if (!automationService.IsUpdateDue(managerOptions, DateTime.Now))
        {
            return;
        }

        if (TimeSpan.TryParse(managerOptions.UpdateScheduleTime, out var schedule))
        {
            var bucket = DateTime.Now.Date.Add(schedule);
            if (_scheduledUpdateWebhookBucketUtc != bucket)
            {
                _scheduledUpdateWebhookBucketUtc = bucket;
                PostDiscordWebhook(
                    DiscordWebhookEventKind.ScheduledUpdateWindow,
                    "Update window",
                    serverStarted
                        ? "*Scheduled time hit* — a restart will run if your policies allow it."
                        : "*Scheduled time hit* — the server was already idle.",
                    DiscordWebhookEmbedFactory.BuildOperationalExtras(managerOptions, currentServerSettings));
            }
        }

        logger.Info("Scheduled update window reached.");
        if (serverStarted)
        {
            _ = RestartServerAsync("Scheduled update window");
        }
    }

    private async Task TickManagerUpdateCheckAsync()
    {
        if (!managerOptions.ManagerUpdateCheckEnabled)
        {
            return;
        }

        if (_lastManagerUpdateCheckUtc != DateTime.MinValue &&
            (DateTime.UtcNow - _lastManagerUpdateCheckUtc).TotalHours < managerOptions.ManagerUpdateCheckIntervalHours)
        {
            return;
        }

        await CheckForManagerUpdateAsync(userInitiated: false).ConfigureAwait(true);
    }

    private async Task CheckForManagerUpdateAsync(bool userInitiated)
    {
        if (_managerUpdateCheckInProgress)
        {
            return;
        }

        _managerUpdateCheckInProgress = true;
        _lastManagerUpdateCheckUtc = DateTime.UtcNow;
        try
        {
            var rel = await managerUpdateService.GetLatestReleaseAsync(managerOptions.ManagerUpdateIncludePrerelease, CancellationToken.None)
                .ConfigureAwait(true);
            if (rel == null)
            {
                if (userInitiated)
                {
                    MessageBox.Show(this, "Could not fetch release information right now.", "Manager updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return;
            }

            var current = GetCurrentManagerVersion();
            if (!ManagerUpdateService.TryParseTagVersion(rel.TagName, out var rawLatest))
            {
                if (userInitiated)
                {
                    MessageBox.Show(this, $"Latest release tag is {rel.TagName}.", "Manager updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return;
            }

            // Same normalization as <see cref="GetCurrentManagerVersion"/> so tag vs assembly compares reliably.
            var latest = new Version(rawLatest.Major, rawLatest.Minor, Math.Max(0, rawLatest.Build), 0);

            if (latest <= current)
            {
                if (userInitiated)
                {
                    MessageBox.Show(
                        this,
                        $"You're already on the latest manager build.\n\nEmbedded version (this exe): v{current}\nGitHub tag: {rel.TagName}",
                        "Manager updates",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            if (_managerUpdatePromptShownThisRun && !userInitiated)
            {
                return;
            }

            _managerUpdatePromptShownThisRun = true;
            var ask = managerOptions.ManagerUpdatePromptBeforeDownload || userInitiated;
            if (ask)
            {
                var prompt = MessageBox.Show(
                    this,
                    $"A newer manager release is available.\n\nEmbedded version (this exe): v{current}\nLatest on GitHub: {rel.TagName}\n\nDownload, install, and restart now?",
                    "Manager update available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1);
                if (prompt != DialogResult.Yes)
                {
                    return;
                }
            }

            await DownloadAndInstallManagerUpdateAsync(rel).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.Warn($"Manager update check failed: {ex.Message}");
            if (userInitiated)
            {
                MessageBox.Show(this, $"Update check failed:\n{ex.Message}", "Manager updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _managerUpdateCheckInProgress = false;
        }
    }

    private static Version GetCurrentManagerVersion()
    {
        var v = typeof(Form1).Assembly.GetName().Version;
        if (v == null)
        {
            return new Version(0, 0, 0, 0);
        }

        return new Version(v.Major, v.Minor, Math.Max(0, v.Build), 0);
    }

    /// <summary>Keeps footer text aligned with <see cref="GetCurrentManagerVersion"/> (same source as update checks).</summary>
    private void ApplyManagerVersionFooter()
    {
        var v = GetCurrentManagerVersion();
        copyrightLabel.Text = $"v{v.Major}.{v.Minor}.{v.Build} | Icarus Server Manager";
    }

    private async Task DownloadAndInstallManagerUpdateAsync(ManagerReleaseInfo release)
    {
        var updaterExe = Path.Combine(AppContext.BaseDirectory, "ManagerUpdater.exe");
        if (!File.Exists(updaterExe))
        {
            MessageBox.Show(
                this,
                "ManagerUpdater.exe was not found next to the manager executable. Re-download the full package and try again.",
                "Manager updates",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        UpdateStatus("Downloading manager update...");
        logger.Info($"Downloading manager update asset: {release.AssetName}");

        var tempRoot = Path.Combine(Path.GetTempPath(), "IcarusServerManager-Update", Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(tempRoot, release.AssetName);
        var extractDir = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(extractDir);

        await managerUpdateService.DownloadAssetAsync(release.DownloadUrl, zipPath, CancellationToken.None).ConfigureAwait(true);
        ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

        var extractedExe = Directory.GetFiles(extractDir, ManagerMainExeName, SearchOption.AllDirectories).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(extractedExe))
        {
            throw new InvalidOperationException("Downloaded release zip does not contain IcarusServerManager.exe.");
        }

        var extractedRoot = Path.GetDirectoryName(extractedExe)!;
        var currentExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, ManagerMainExeName);
        var currentDir = Path.GetDirectoryName(currentExe) ?? AppContext.BaseDirectory;
        var currentProcId = Environment.ProcessId;

        var args = new[]
        {
            "--source", extractedRoot,
            "--target", currentDir,
            "--pid", currentProcId.ToString(),
            "--exe", ManagerMainExeName
        };

        Process.Start(new ProcessStartInfo
        {
            FileName = updaterExe,
            WorkingDirectory = currentDir,
            UseShellExecute = false,
            Arguments = string.Join(" ", args.Select(QuoteArg))
        });

        logger.Info($"Launching ManagerUpdater.exe for {release.TagName} and closing manager.");
        Close();
    }

    private static string QuoteArg(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "\"\"";
        }

        return s.Contains(' ') ? $"\"{s}\"" : s;
    }

    private void SetStat(string key, string value)
    {
        if (!statsLabels.TryGetValue(key, out var label))
        {
            return;
        }

        label.Text = $"{key}: {value}";
    }

    private void ApplyTheme()
    {
        themeManager.ApplyTheme(this, managerOptions.Theme);
        ApplyChartTheme();
        if (serverStarted)
        {
            startServerButton.BackColor = Color.Green;
            startServerButton.ForeColor = Color.White;
        }
        else
        {
            startServerButton.BackColor = Color.Maroon;
            startServerButton.ForeColor = Color.White;
        }

        var dark = managerOptions.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase);
        if (forceKillServerButton.Enabled)
        {
            forceKillServerButton.BackColor = dark ? Color.FromArgb(180, 90, 0) : Color.DarkOrange;
            forceKillServerButton.ForeColor = Color.White;
        }
        else
        {
            forceKillServerButton.BackColor = dark ? Color.FromArgb(53, 56, 66) : SystemColors.ControlDark;
            forceKillServerButton.ForeColor = dark ? Color.FromArgb(200, 200, 205) : Color.Black;
        }
    }

    private void ApplyChartTheme()
    {
        if (metricsGraph == null)
        {
            return;
        }

        var dark = managerOptions.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase);
        metricsGraph.BackColor = dark ? Color.FromArgb(45, 45, 48) : Color.White;
        metricsGraph.Invalidate();
    }

    private void RenderMetricsGraph(PaintEventArgs e)
    {
        if (metricsGraph == null)
        {
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var dark = managerOptions.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase);
        var gridColor = dark ? Color.FromArgb(80, 80, 80) : Color.Gainsboro;
        var textColor = dark ? Color.Gainsboro : Color.Black;
        var cpuColor = dark ? Color.DeepSkyBlue : Color.Blue;
        var memColor = dark ? Color.Orange : Color.DarkOrange;

        var rect = new Rectangle(50, 20, Math.Max(1, metricsGraph.Width - 80), Math.Max(1, metricsGraph.Height - 60));
        using var gridPen = new Pen(gridColor, 1);
        using var axisPen = new Pen(textColor, 1);
        using var font = new Font("Segoe UI", 8f);
        using var cpuPen = new Pen(cpuColor, 2);
        using var memPen = new Pen(memColor, 2);
        using var textBrush = new SolidBrush(textColor);

        for (var i = 0; i <= 5; i++)
        {
            var y = rect.Top + (i * rect.Height / 5f);
            g.DrawLine(gridPen, rect.Left, y, rect.Right, y);
        }

        g.DrawRectangle(axisPen, rect);
        g.DrawString("CPU % / Memory MB (last 120 samples)", font, textBrush, rect.Left, 2);
        g.DrawString("CPU", font, new SolidBrush(cpuColor), rect.Right - 90, 2);
        g.DrawString("MEM", font, new SolidBrush(memColor), rect.Right - 50, 2);

        var window = metricsHistory.TakeLast(120).ToList();
        if (window.Count < 2)
        {
            return;
        }

        var maxMem = Math.Max(100, window.Max(x => x.MemoryMb));
        var cpuPoints = new PointF[window.Count];
        var memPoints = new PointF[window.Count];
        for (var i = 0; i < window.Count; i++)
        {
            var x = rect.Left + (i / (float)(window.Count - 1)) * rect.Width;
            var cpuY = rect.Bottom - (float)(Math.Clamp(window[i].CpuPercent, 0, 100) / 100d) * rect.Height;
            var memY = rect.Bottom - (float)(window[i].MemoryMb / maxMem) * rect.Height;
            cpuPoints[i] = new PointF(x, cpuY);
            memPoints[i] = new PointF(x, memY);
        }

        g.DrawLines(cpuPen, cpuPoints);
        g.DrawLines(memPen, memPoints);
    }

    private void RunSetupWizardIfNeeded()
    {
        if (!string.IsNullOrWhiteSpace(serverLocationBox.Text.Trim()))
        {
            return;
        }

        var r = MessageBox.Show(
            this,
            "No dedicated server install folder is set yet.\n\nOpen the setup wizard to choose the folder (the directory that will contain Icarus\\Binaries\\Win64\\IcarusServer-Win64-Shipping.exe)?",
            "Welcome",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);
        if (r == DialogResult.Yes)
        {
            OpenSetupWizard();
        }
    }

    private void OpenSetupWizard()
    {
        using var wizard = new SetupWizardForm(serverLocationBox.Text);
        if (wizard.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(wizard.SelectedPath))
        {
            ApplyServerInstallPath(wizard.SelectedPath);
        }
    }

    private void ApplyServerInstallPath(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        folder = folder.Trim();
        Properties.Settings.Default.serverLocation = folder;
        Properties.Settings.Default.Save();
        serverLocationBox.Text = folder;
        PrimeInstallPathWebhookState();
        UpdateServerAvailability();
        LoadIniToUi();
    }

    private void ExportMetricsCsv()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "icarus-metrics.csv" };
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,CpuPercent,MemoryMb,UptimeSeconds");
        foreach (var row in metricsHistory)
        {
            sb.AppendLine($"{row.Timestamp:O},{row.CpuPercent:F2},{row.MemoryMb:F2},{row.Uptime.TotalSeconds:F0}");
        }

        File.WriteAllText(dialog.FileName, sb.ToString());
        logger.Info($"Exported metrics CSV: {dialog.FileName}");
    }

        private void selectLocationButton_Click(object sender, EventArgs e)
    {
        folderBrowser.Description = "Select the dedicated server install folder (game root).";
        if (folderBrowser.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        ApplyServerInstallPath(folderBrowser.SelectedPath);
    }

    private void UpdateServerAvailability()
    {
        var exe = Path.Combine(serverLocationBox.Text.Trim(), "Icarus", "Binaries", "Win64", "IcarusServer-Win64-Shipping.exe");
        var valid = File.Exists(exe);
        if (!valid && _installPathPreviouslyValid)
        {
            PostDiscordWebhook(
                DiscordWebhookEventKind.InstallPathIssue,
                "Game binary missing",
                DiscordWebhookEmbedFactory.TruncateDescription(exe, managerOptions),
                DiscordWebhookEmbedFactory.BuildOperationalExtras(managerOptions, currentServerSettings));
        }

        _installPathPreviouslyValid = valid;
        if (!valid)
        {
            logger.Warn("Icarus installation not found at selected location.");
            startServerButton.Enabled = false;
            UpdateStatus("Waiting for install");
        }
        else
        {
            logger.Info("Icarus installation found.");
            startServerButton.Enabled = true;
            UpdateStatus("Idle");
        }
    }

        private void installServerButton_Click(object sender, EventArgs e)
    {
        try
        {
            UpdateStatus("Installing...");
            var steamcmd = Path.Combine(Environment.CurrentDirectory, "steamcmd", "steamcmd.exe");
            var args = $"/b /w /high +login anonymous +force_install_dir \"{Properties.Settings.Default.serverLocation}\" +app_update 2089300 validate +quit";
            var procStartInfo = new ProcessStartInfo(steamcmd, args) { UseShellExecute = false };
            using var installer = new Process { StartInfo = procStartInfo };
            installer.Start();
            installer.WaitForExit();
            var exitCode = installer.ExitCode;
            PostDiscordWebhook(
                DiscordWebhookEventKind.SteamCmdFinished,
                exitCode == 0 ? "SteamCMD finished" : "SteamCMD reported failure",
                exitCode == 0 ? "*Install or update step completed.*" : $"*Exit code* `{exitCode}`",
                DiscordWebhookEmbedFactory.BuildOperationalExtras(managerOptions, currentServerSettings));
            installServerButton.Enabled = false;
            startServerButton.Enabled = true;
            UpdateStatus("Idle");
            Properties.Settings.Default.IsInit = false;
            Properties.Settings.Default.Save();
            logger.Info("Install/update completed.");
        }
        catch (Exception ex)
        {
            logger.Error("Install/update failed.", ex);
            PostDiscordWebhook(
                DiscordWebhookEventKind.SteamCmdFinished,
                "SteamCMD install/update failed",
                DiscordWebhookEmbedFactory.TruncateDescription(ex.ToString(), managerOptions),
                DiscordWebhookEmbedFactory.BuildDiagnosticExtras(managerOptions, currentServerSettings));
            MessageBox.Show("Install/update failed. See logs for details.");
        }
        }

}
