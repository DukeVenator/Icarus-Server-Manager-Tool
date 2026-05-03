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
    private void StartProcess()
    {
        if (serverStarted)
        {
            return;
        }

        SaveManagerOptionsFromUi(false);
        SaveUiToIni(false);
        logger.Info("Starting game server...");
        var exe = Path.Combine(serverLocationBox.Text, "Icarus", "Binaries", "Win64", "IcarusServer-Win64-Shipping.exe");
        if (!File.Exists(exe))
        {
            logger.Error($"Executable not found: {exe}");
            MessageBox.Show("Game server executable was not found.");
            return;
        }

        var gamePort = GetInt("LaunchGamePort", managerOptions.LaunchGamePort);
        var queryPort = GetInt("LaunchQueryPort", managerOptions.LaunchQueryPort);
        var logPath = GetString("LaunchLogPath", managerOptions.LaunchLogPath);
        var args = iniService.BuildLaunchArguments(currentServerSettings, gamePort, queryPort, logPath);
        logger.Info($"Launch arguments: {args}");
        var procStartInfo = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        serverProc = new Process { StartInfo = procStartInfo, EnableRaisingEvents = true };
        serverProc.OutputDataReceived += HandleExeOutput;
        serverProc.ErrorDataReceived += HandleExeOutput;
        serverProc.Exited += (_, _) => OnServerExited();

        serverProc.Start();
        serverProc.BeginOutputReadLine();
        serverProc.BeginErrorReadLine();
        playerTracker.Clear();
        startTime = DateTime.Now;
        serverStarted = true;
        _serverReadyAnnounced = 0;
        restartInProgress = false;
        crashDetected = false;
        restartPolicyService.ResetCrashAttempts();
        UpdateStatus("Starting...");
        startServerButton.BackColor = Color.Green;
        ChangeStartButton("Stop Server");
        forceKillServerButton.Enabled = true;
        ApplyTheme();
        logger.Info("Game server started.");
    }

    private void OnServerExited()
    {
        var wasRunning = serverStarted;
        serverStarted = false;
        _serverReadyAnnounced = 0;
        playerTracker.Clear();

        void applyExitUi()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            try
            {
                UpdateStatus("Idle");
                ChangeStartButton("Start Server");
                startServerButton.BackColor = Color.Maroon;
                forceKillServerButton.Enabled = false;
                ApplyTheme();
            }
            catch (ObjectDisposedException)
            {
                // Form or child controls disposed while applying exit UI.
            }
        }

        try
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke(applyExitUi);
            }
            else
            {
                applyExitUi();
            }
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            // Handle destroyed; cannot marshal to UI thread.
            return;
        }

        if (wasRunning && !restartInProgress)
        {
            crashDetected = true;
            crashCount++;
            logger.Warn("Server process exited unexpectedly.");
            PostDiscordWebhook(
                DiscordWebhookEventKind.UnexpectedExit,
                "Server dropped offline",
                "*The dedicated server exited on its own.* Check the host and logs when you can.",
                DiscordWebhookEmbedFactory.BuildLifecycleExtras(managerOptions, currentServerSettings, DateTime.Now));
        }
    }

    private async Task StopProcessAsync(bool isRestartOperation = false)
    {
        if (!serverStarted || serverProc == null)
        {
            return;
        }

        var proc = serverProc;
        try
        {
            logger.Info("Stopping server...");
            restartInProgress = true;
            var waitSeconds = Math.Clamp(managerOptions.GracefulShutdownWaitSeconds, 10, 900);
            var exitedGracefully = await RequestGracefulShutdownAsync(
                    proc,
                    TimeSpan.FromSeconds(waitSeconds),
                    managerOptions.GracefulShutdownTryCtrlC)
                .ConfigureAwait(true);
            if (!exitedGracefully && !SafeProcessHasExited(proc))
            {
                logger.Warn("Server did not exit gracefully in time; forcing termination.");
                try
                {
                    proc.Kill(true);
                    proc.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    logger.Warn($"Force termination step failed: {ex.Message}");
                }
            }

            lock (_serverProcDisposeLock)
            {
                if (ReferenceEquals(serverProc, proc))
                {
                    serverProc = null;
                    try
                    {
                        proc.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"Process dispose failed: {ex.Message}");
                    }
                }
            }

            serverStarted = false;
            _serverReadyAnnounced = 0;
            playerTracker.Clear();
            UpdateStatus("Idle");
            startServerButton.BackColor = Color.Maroon;
            ChangeStartButton("Start Server");
            forceKillServerButton.Enabled = false;
            ApplyTheme();
            logger.Info("Game server stopped.");
            if (!isRestartOperation)
            {
                PostDiscordWebhook(
                    DiscordWebhookEventKind.ServerStop,
                    "Session offline",
                    "*The manager stopped the dedicated server.*",
                    DiscordWebhookEmbedFactory.BuildLifecycleExtras(managerOptions, currentServerSettings, DateTime.Now));
            }
        }
        catch (Exception ex)
        {
            logger.Error("Failed to stop server.", ex);
        }
        finally
        {
            restartInProgress = false;
        }
    }

    private static bool SafeProcessHasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            // Includes ObjectDisposedException on some runtimes where it derives from InvalidOperationException.
            return true;
        }
    }

    private async Task<bool> RequestGracefulShutdownAsync(Process process, TimeSpan totalTimeout, bool tryCtrlC)
    {
        try
        {
            if (SafeProcessHasExited(process))
            {
                return true;
            }

            var deadline = DateTime.UtcNow + totalTimeout;

            if (tryCtrlC && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    if (WindowsConsoleShutdown.TrySendCtrlC(process.Id))
                    {
                        logger.Info("Sent Ctrl+C to the server process (graceful shutdown).");
                    }
                    else
                    {
                        logger.Warn("Ctrl+C could not be delivered to the server process; stdin fallback may be used.");
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn($"Ctrl+C delivery threw: {ex.Message}");
                }
            }
            else if (!tryCtrlC)
            {
                logger.Info("Ctrl+C skipped by manager option; using stdin quit/exit when available.");
            }

            // After Ctrl+C (when enabled), allow time to flush saves before stdin. When Ctrl+C is disabled, go to stdin immediately.
            DateTime firstPhaseEnd;
            if (tryCtrlC)
            {
                firstPhaseEnd = DateTime.UtcNow + TimeSpan.FromSeconds(45);
                if (firstPhaseEnd > deadline)
                {
                    firstPhaseEnd = deadline;
                }
            }
            else
            {
                firstPhaseEnd = DateTime.UtcNow;
            }

            while (DateTime.UtcNow < firstPhaseEnd)
            {
                if (SafeProcessHasExited(process))
                {
                    return true;
                }

                await Task.Delay(250).ConfigureAwait(true);
            }

            if (!SafeProcessHasExited(process) && process.StartInfo.RedirectStandardInput)
            {
                try
                {
                    await process.StandardInput.WriteLineAsync("quit").ConfigureAwait(true);
                    await process.StandardInput.WriteLineAsync("exit").ConfigureAwait(true);
                    await process.StandardInput.FlushAsync().ConfigureAwait(true);
                    logger.Info("Sent quit/exit on stdin (secondary shutdown path).");
                }
                catch (Exception ex)
                {
                    logger.Warn($"Stdin shutdown commands failed: {ex.Message}");
                }
            }

            while (DateTime.UtcNow < deadline)
            {
                if (SafeProcessHasExited(process))
                {
                    return true;
                }

                await Task.Delay(250).ConfigureAwait(true);
            }

            return SafeProcessHasExited(process);
        }
        catch (Exception ex)
        {
            logger.Warn($"Graceful shutdown request failed; will force stop. {ex.Message}");
            return false;
        }
    }

    private async Task ForceKillServerAsync()
    {
        var proc = serverProc;
        if (proc == null || SafeProcessHasExited(proc))
        {
            return;
        }

        var confirm = MessageBox.Show(
            "Force kill ends the server immediately. Unsaved progress may be lost.\n\nContinue?",
            "Force kill server",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        restartInProgress = true;
        try
        {
            logger.Warn("Force killing server process (user requested).");
            if (!SafeProcessHasExited(proc))
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    logger.Error("Force kill could not terminate the process.", ex);
                    return;
                }

                await Task.Run(() =>
                {
                    try
                    {
                        proc.WaitForExit(8000);
                    }
                    catch
                    {
                        // best-effort
                    }
                }).ConfigureAwait(true);
            }

            lock (_serverProcDisposeLock)
            {
                if (ReferenceEquals(serverProc, proc))
                {
                    serverProc = null;
                    try
                    {
                        proc.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"Process dispose after force kill failed: {ex.Message}");
                    }
                }
            }

            serverStarted = false;
            playerTracker.Clear();
            UpdateStatus("Idle");
            startServerButton.BackColor = Color.Maroon;
            ChangeStartButton("Start Server");
            forceKillServerButton.Enabled = false;
            ApplyTheme();
            PostDiscordWebhook(
                DiscordWebhookEventKind.ServerStop,
                "Hard stop",
                "*The server process was killed immediately* — saves may not have flushed.",
                DiscordWebhookEmbedFactory.BuildLifecycleExtras(managerOptions, currentServerSettings, DateTime.Now));
        }
        finally
        {
            restartInProgress = false;
        }
    }

    private async void forceKillServerButton_Click(object sender, EventArgs e)
    {
        await ForceKillServerAsync().ConfigureAwait(true);
    }

    private void PostDiscordWebhook(
        DiscordWebhookEventKind kind,
        string title,
        string? description = null,
        DiscordWebhookExtras? extras = null)
    {
        if (!managerOptions.EnableDiscordWebhook)
        {
            return;
        }

        var snapshot = managerOptions;
        var extrasSnapshot = extras;
        _ = Task.Run(async () =>
        {
            try
            {
                await automationService.SendWebhookEventAsync(snapshot, kind, title, description, extrasSnapshot)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Service already suppresses errors; extra guard for Task.Run.
            }
        });
    }

    private async Task RestartServerAsync(string reason)
    {
        if (restartInProgress)
        {
            return;
        }

        restartInProgress = true;
        lastRestartReason = reason;
        restartHistory.Add($"{DateTime.Now:HH:mm:ss} - {reason}");
        logger.Warn($"Restart requested: {reason}");
        await automationService.SendWebhookEventAsync(
            managerOptions,
            DiscordWebhookEventKind.ServerRestart,
            "Restart cycle",
            DiscordWebhookEmbedFactory.TruncateDescription(reason, managerOptions),
            DiscordWebhookEmbedFactory.BuildOperationalExtras(managerOptions, currentServerSettings)).ConfigureAwait(true);
        await StopProcessAsync(isRestartOperation: true).ConfigureAwait(true);
        await Task.Delay(1500).ConfigureAwait(true);
        StartProcess();
        lastRestartAt = DateTime.Now;
        if (!serverStarted)
        {
            PostDiscordWebhook(
                DiscordWebhookEventKind.ServerRestartFailed,
                "Restart did not come back online",
                DiscordWebhookEmbedFactory.TruncateDescription(reason, managerOptions),
                DiscordWebhookEmbedFactory.BuildOperationalExtras(managerOptions, currentServerSettings));
        }
    }
        private async void startServerButton_Click(object sender, EventArgs e)
        {
            if (serverStarted)
            {
                await StopProcessAsync().ConfigureAwait(true);
            }
            else
            {
                StartProcess();
            }
        }

    private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
    {
        var proc = serverProc;
        if (!serverStarted || proc == null || SafeProcessHasExited(proc))
        {
            return;
        }

        var interactiveClose = e.CloseReason is CloseReason.UserClosing
            or CloseReason.ApplicationExitCall
            or CloseReason.MdiFormClosing
            or CloseReason.FormOwnerClosing;

        if (interactiveClose)
        {
            var confirm = MessageBox.Show(
                "The dedicated server is still running. Closing the manager will stop the server immediately (not a graceful shutdown). Unsaved progress may be lost.\n\nClose the manager anyway?",
                "Server is running",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        SynchronouslyShutdownServerForAppClose(notifyDiscord: interactiveClose);
    }

    /// <summary>
    /// Terminates the game process without graceful shutdown. Used when exiting the app; avoids blocking on <see cref="StopProcessAsync"/>.
    /// </summary>
    private void SynchronouslyShutdownServerForAppClose(bool notifyDiscord)
    {
        var proc = serverProc;
        if (proc == null)
        {
            return;
        }

        try
        {
            if (!SafeProcessHasExited(proc))
            {
                logger.Warn("Terminating server process because the manager is closing.");
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    logger.Error("Could not kill server process on manager exit.", ex);
                }

                try
                {
                    proc.WaitForExit(8000);
                }
                catch
                {
                    // best-effort
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error("Shutdown cleanup failed while closing manager.", ex);
        }

        lock (_serverProcDisposeLock)
        {
            if (ReferenceEquals(serverProc, proc))
            {
                serverProc = null;
                try
                {
                    proc.Dispose();
                }
                catch (Exception ex)
                {
                    logger.Warn($"Process dispose on manager exit failed: {ex.Message}");
                }
            }
        }

        serverStarted = false;
        _serverReadyAnnounced = 0;
        playerTracker.Clear();

        if (notifyDiscord)
        {
            PostDiscordWebhook(
                DiscordWebhookEventKind.ServerStop,
                "Session offline",
                "*The manager closed and the server was stopped immediately.*",
                DiscordWebhookEmbedFactory.BuildLifecycleExtras(managerOptions, currentServerSettings, DateTime.Now));
        }
    }

    private void KillProcess(object? o, EventArgs e)
    {
        try
        {
            var proc = serverProc;
            if (proc == null || SafeProcessHasExited(proc))
            {
                return;
            }

            logger.Warn("Server process still running after close; applying best-effort kill.");
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                logger.Warn($"Final kill on FormClosed failed: {ex.Message}");
            }

            try
            {
                proc.WaitForExit(3000);
            }
            catch
            {
                // best-effort
            }

            lock (_serverProcDisposeLock)
            {
                if (ReferenceEquals(serverProc, proc))
                {
                    serverProc = null;
                    try
                    {
                        proc.Dispose();
                    }
                    catch
                    {
                        // best-effort
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error("FormClosed process cleanup failed.", ex);
        }
    }

        private void HandleExeOutput(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data))
        {
            return;
        }

        var line = e.Data;
        if (line.Contains(ReadyForPlayersLogMarker, StringComparison.OrdinalIgnoreCase) &&
            Interlocked.Exchange(ref _serverReadyAnnounced, 1) == 0)
        {
            logger.Info("Detected readiness marker in server output. Server is ready for players.");
            UpdateStatus("Running (Ready for players)");
            PostDiscordWebhook(
                DiscordWebhookEventKind.ServerStart,
                "Drop zone is live",
                "**Players can join** — the server reported readiness in its log.",
                DiscordWebhookEmbedFactory.BuildLaunchExtras(managerOptions, currentServerSettings, TryReadProspectSummaryForDiscord()));
        }

        var playerLine = playerTracker.ProcessLogLine(line);
        switch (playerLine.Kind)
        {
            case PlayerLogHintKind.Joined when !string.IsNullOrWhiteSpace(playerLine.PlayerName):
                PostDiscordWebhook(
                    DiscordWebhookEventKind.PlayerJoin,
                    "Crew inbound",
                    $"**{playerLine.PlayerName}** *is on approach (log hint).*",
                    DiscordWebhookEmbedFactory.BuildPlayerEventExtras(managerOptions, currentServerSettings, playerLine.PlayerName));
                break;
            case PlayerLogHintKind.Left when !string.IsNullOrWhiteSpace(playerLine.PlayerName):
                PostDiscordWebhook(
                    DiscordWebhookEventKind.PlayerLeave,
                    "Crew outbound",
                    $"**{playerLine.PlayerName}** *signed off (log hint).*",
                    DiscordWebhookEmbedFactory.BuildPlayerEventExtras(managerOptions, currentServerSettings, playerLine.PlayerName));
                break;
        }

        if (ServerLogChatHeuristic.LooksLikeChatLine(line, playerLine))
        {
            PostDiscordWebhook(
                DiscordWebhookEventKind.Chat,
                "Comms intercept",
                DiscordWebhookEmbedFactory.TruncateDescription(line, managerOptions, 2000),
                DiscordWebhookEmbedFactory.BuildMinimalServerTag(managerOptions, currentServerSettings.SteamServerName));
        }
        else if (ServerLogGameplayHeuristic.LooksLikeLevelUp(line, playerLine))
        {
            PostDiscordWebhook(
                DiscordWebhookEventKind.LevelUp,
                "Progress ping",
                DiscordWebhookEmbedFactory.TruncateDescription(line, managerOptions, 2000),
                DiscordWebhookEmbedFactory.BuildMinimalServerTag(managerOptions, currentServerSettings.SteamServerName));
        }
        else if (ServerLogGameplayHeuristic.LooksLikePlayerDeath(line, playerLine))
        {
            PostDiscordWebhook(
                DiscordWebhookEventKind.PlayerDeath,
                "Hazard ping",
                DiscordWebhookEmbedFactory.TruncateDescription(line, managerOptions, 2000),
                DiscordWebhookEmbedFactory.BuildMinimalServerTag(managerOptions, currentServerSettings.SteamServerName));
        }

        if (playerLine.Kind == PlayerLogHintKind.Joined)
        {
            possibleServerEmpty = false;
        }
        else if (playerLine.Kind == PlayerLogHintKind.Left)
        {
            possibleServerEmpty = true;
        }

        logger.Info(line, isGameProcessOutput: true);
        }

        private void UpdateStatus(string text)
        {
            try
            {
                if (IsDisposed || Disposing || serverStatusBox.IsDisposed)
                {
                    return;
                }

                void apply()
                {
                    if (IsDisposed || Disposing || serverStatusBox.IsDisposed)
                    {
                        return;
                    }

                    serverStatusBox.Text = text;
                }

                if (!IsHandleCreated)
                {
                    return;
                }

                if (InvokeRequired)
                {
                    Invoke(apply);
                }
                else
                {
                    apply();
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
                // No valid window handle.
            }
        }

        private void ChangeStartButton(string text)
        {
            try
            {
                if (IsDisposed || Disposing || startServerButton.IsDisposed)
                {
                    return;
                }

                void apply()
                {
                    if (IsDisposed || Disposing || startServerButton.IsDisposed)
                    {
                        return;
                    }

                    startServerButton.Text = text;
                }

                if (!IsHandleCreated)
                {
                    return;
                }

                if (InvokeRequired)
                {
                    Invoke(apply);
                }
                else
                {
                    apply();
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

}
