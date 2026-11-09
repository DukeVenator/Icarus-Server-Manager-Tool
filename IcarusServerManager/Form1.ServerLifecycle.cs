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
    private void StartProcess(bool isRestartOperation = false)
    {
        if (serverStarted)
        {
            return;
        }

        RefreshInstallerState();

        if (_startInProgress)
        {
            logger.Warn("Start ignored: a start is already in progress.");
            UpdateStatus("Start ignored — already starting");
            return;
        }

        if (_isClosing)
        {
            logger.Warn("Start ignored: the manager is closing.");
            return;
        }

        if (_installInProgress || _installerExitUnconfirmed)
        {
            logger.Warn("Start ignored: an install/update is in progress.");
            UpdateStatus("Start blocked — install in progress");
            return;
        }

        if (!isRestartOperation && _restartGuardDepth > 0)
        {
            logger.Warn("Start ignored: another stop or restart owns the lifecycle.");
            UpdateStatus("Start ignored — stop/restart in progress");
            return;
        }

        // A restart's own launch is nested inside the outer restart operation and is allowed. Any
        // other overlap (double start, start during stop/force-kill) is refused instead of raced.
        if (_lifecycleDepth > 0 && !(isRestartOperation && _restartGuardDepth > 0))
        {
            logger.Warn("Start ignored: another lifecycle operation is in progress.");
            UpdateStatus("Start blocked — another lifecycle operation is in progress");
            return;
        }

        using var lifecycle = EnterLifecycle();
        _startInProgress = true;
        try
        {
            if (!ReleaseExitedProcessOrBlockStart(out var blockReason))
            {
                logger.Error(blockReason);
                UpdateStatus("Start blocked — previous process exit unconfirmed");
                MessageBox.Show(this, blockReason, "Server not ready to start", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveManagerOptionsFromUi(false);
            SaveUiToIni(false);
            logger.Info("Starting game server...");
            var exe = GetExpectedServerExecutablePath();
            if (!File.Exists(exe))
            {
                logger.Error($"Executable not found: {exe}");
                UpdateStatus("Start failed — game server executable not found");
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

            var proc = new Process { StartInfo = procStartInfo, EnableRaisingEvents = true };
            proc.OutputDataReceived += HandleExeOutput;
            proc.ErrorDataReceived += HandleExeOutput;
            proc.Exited += (_, _) => OnServerExited();

            try
            {
                proc.Start();
            }
            catch (Exception ex)
            {
                logger.Error("Game server process failed to start.", ex);
                TryDisposeUnstartedProcess(proc);
                UpdateStatus("Start failed — see log");
                return;
            }

            // Own the handle only after a real process exists; a handle that never started must not
            // block a later start (its exit state is unreadable, not confirmed).
            serverProc = proc;
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            playerTracker.Clear();
            startTime = DateTime.Now;
            serverStarted = true;
            _serverReadyAnnounced = 0;
            crashDetected = false;
            _serverExitUnconfirmed = false;
            _lastStartUtc = DateTime.UtcNow;
            UpdateStatus("Starting...");
            startServerButton.BackColor = Color.Green;
            ChangeStartButton("Stop Server");
            forceKillServerButton.Enabled = true;
            ApplyTheme();
            logger.Info("Game server started.");
        }
        catch (Exception ex)
        {
            logger.Error("Starting the game server failed.", ex);
            if (!serverStarted)
            {
                UpdateStatus("Start failed — see log");
                ChangeStartButton("Start Server");
            }
        }
        finally
        {
            _startInProgress = false;
        }
    }

    /// <summary>Disposes a handle whose Start() threw; no process exists, so exit need not be confirmed.</summary>
    private void TryDisposeUnstartedProcess(Process proc)
    {
        try
        {
            proc.Dispose();
        }
        catch (Exception ex)
        {
            logger.Warn($"Dispose of an unstarted process handle failed: {ex.Message}");
        }
    }

    /// <summary>
    /// A new process may only be launched once the previous one is known to have exited. When exit
    /// is unconfirmed the handle is retained so the manager cannot forget a server it still owns.
    /// </summary>
    private bool ReleaseExitedProcessOrBlockStart(out string reason)
    {
        reason = string.Empty;
        var proc = serverProc;
        if (proc is null)
        {
            _serverExitUnconfirmed = false;
            return true;
        }

        if (SafeProcessHasExited(proc))
        {
            ReleaseServerProcess(proc);
            return true;
        }

        _serverExitUnconfirmed = true;
        reason = "The previous server process is still running (or its exit could not be confirmed). " +
                 "Stop it or use Force kill before starting a replacement.";
        return false;
    }

    /// <summary>
    /// Drops and disposes the tracked process handle only after the caller has confirmed exit.
    /// Never call this for an unconfirmed process: the manager would forget a server it still owns.
    /// </summary>
    private void ReleaseServerProcess(Process proc)
    {
        lock (_serverProcDisposeLock)
        {
            if (!ReferenceEquals(serverProc, proc))
            {
                return;
            }

            serverProc = null;
        }

        try
        {
            proc.Dispose();
        }
        catch (Exception ex)
        {
            logger.Warn($"Process dispose failed: {ex.Message}");
        }

        _serverExitUnconfirmed = false;
    }

    /// <summary>Single place that renders the "no server process" state so every exit path matches.</summary>
    private void ApplyIdleLifecycleUi()
    {
        UpdateStatus("Idle");
        startServerButton.BackColor = Color.Maroon;
        ChangeStartButton("Start Server");
        forceKillServerButton.Enabled = false;
        ApplyTheme();
    }

    private IDisposable EnterLifecycle()
    {
        _lifecycleDepth++;
        return new LifecycleScope(this);
    }

    private sealed class LifecycleScope : IDisposable
    {
        private readonly Form1 _form;
        private bool _disposed;

        public LifecycleScope(Form1 form)
        {
            _form = form;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _form._lifecycleDepth = Math.Max(0, _form._lifecycleDepth - 1);
        }
    }

    private void OnServerExited()
    {
        var wasRunning = serverStarted;
        serverStarted = false;
        _serverExitUnconfirmed = false;
        _serverReadyAnnounced = 0;
        playerTracker.Clear();

        void applyExitUi()
        {
            if (IsDisposed || Disposing || serverStarted)
            {
                // A replacement already owns the UI; do not overwrite its state with Idle.
                return;
            }

            try
            {
                ApplyIdleLifecycleUi();
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

        if (wasRunning && !restartInProgress && !_isClosing)
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

        if (!isRestartOperation && _restartGuardDepth > 0)
        {
            logger.Warn("Stop ignored: another stop or restart is already in progress.");
            UpdateStatus("Stop ignored — another server operation is in progress");
            return;
        }

        if (_installInProgress && !isRestartOperation)
        {
            logger.Warn("Stop ignored: an install/update is in progress.");
            UpdateStatus("Stop ignored — install in progress");
            return;
        }

        using var lifecycle = EnterLifecycle();

        // Take a lease rather than assigning the flag. A stop nested inside a restart releases only
        // its own lease, so the outer restart guard stays set until the restart operation finishes.
        AcquireRestartGuard();
        var proc = serverProc;
        try
        {
            logger.Info("Stopping server...");
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

            if (!SafeProcessHasExited(proc))
            {
                // Ownership is retained: never report Idle while the process may still be alive.
                _serverExitUnconfirmed = true;
                logger.Error("Server process did not exit; keeping process ownership and reporting a failed stop.");
                UpdateStatus("Stop failed — process still running");
                SetStartButtonForRunningProcess();
                MessageBox.Show(
                    this,
                    "The server process did not exit. The manager still owns the process; use Force kill or wait for it to exit before starting a replacement.",
                    "Stop did not complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            ReleaseServerProcess(proc);
            serverStarted = false;
            _serverReadyAnnounced = 0;
            playerTracker.Clear();
            _lastStopUtc = DateTime.UtcNow;
            ApplyIdleLifecycleUi();
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
            if (proc is not null && SafeProcessHasExited(proc))
            {
                ReleaseServerProcess(proc);
                serverStarted = false;
                _serverReadyAnnounced = 0;
                playerTracker.Clear();
                _lastStopUtc = DateTime.UtcNow;
                ApplyIdleLifecycleUi();
            }
            else
            {
                _serverExitUnconfirmed = true;
                UpdateStatus("Stop failed — see log");
                SetStartButtonForRunningProcess();
            }
        }
        finally
        {
            ReleaseRestartGuard();
        }
    }

    private void SetStartButtonForRunningProcess()
    {
        serverStarted = true;
        startServerButton.BackColor = Color.Green;
        ChangeStartButton("Stop Server");
        forceKillServerButton.Enabled = true;
        ApplyTheme();
    }

    /// <summary>
    /// Reads exit state without ever converting a read failure into a false "exited". A handle the
    /// manager cannot read (disposed, access denied, never started) is treated as still owned, so a
    /// replacement launch is refused instead of orphaning a live server.
    /// </summary>
    private bool SafeProcessHasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            logger.Warn($"Process exit state could not be read ({ex.GetType().Name}); treating the process as still running.");
            return false;
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
        if (proc == null)
        {
            UpdateStatus("Force kill ignored — no server process is tracked");
            return;
        }

        if (_installInProgress)
        {
            logger.Warn("Force kill ignored: an install/update is in progress.");
            UpdateStatus("Force kill blocked — install in progress");
            return;
        }

        if (_lifecycleDepth > 0)
        {
            logger.Warn("Force kill ignored: another lifecycle operation is in progress.");
            UpdateStatus("Force kill blocked — another server operation is in progress");
            return;
        }

        if (SafeProcessHasExited(proc))
        {
            // The exit event may have raced the click; release the now-confirmed handle.
            ReleaseServerProcess(proc);
            serverStarted = false;
            _serverReadyAnnounced = 0;
            playerTracker.Clear();
            _lastStopUtc = DateTime.UtcNow;
            ApplyIdleLifecycleUi();
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

        // The modal dialog pumps messages: the process may have exited or another operation may
        // have started while it was open, so re-validate before taking the lifecycle lease.
        proc = serverProc;
        if (proc is null || _lifecycleDepth > 0 || restartInProgress)
        {
            logger.Warn("Force kill ignored: the server state changed while the confirmation dialog was open.");
            UpdateStatus("Force kill ignored — server state changed");
            return;
        }

        if (SafeProcessHasExited(proc))
        {
            ReleaseServerProcess(proc);
            serverStarted = false;
            _serverReadyAnnounced = 0;
            playerTracker.Clear();
            _lastStopUtc = DateTime.UtcNow;
            ApplyIdleLifecycleUi();
            return;
        }

        using var lifecycle = EnterLifecycle();
        AcquireRestartGuard();
        try
        {
            logger.Warn("Force killing server process (user requested).");
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                logger.Error("Force kill could not terminate the process.", ex);
                _serverExitUnconfirmed = !SafeProcessHasExited(proc);
                UpdateStatus("Force kill failed — see log");
                SetStartButtonForRunningProcess();
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

            if (!SafeProcessHasExited(proc))
            {
                _serverExitUnconfirmed = true;
                logger.Error("Force kill did not terminate the process; ownership retained.");
                UpdateStatus("Force kill failed — process still running");
                SetStartButtonForRunningProcess();
                MessageBox.Show(
                    this,
                    "The server process is still running after Force kill. The manager keeps ownership of it; check the process manually.",
                    "Force kill did not complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            ReleaseServerProcess(proc);
            serverStarted = false;
            _serverReadyAnnounced = 0;
            playerTracker.Clear();
            _lastStopUtc = DateTime.UtcNow;
            ApplyIdleLifecycleUi();
            PostDiscordWebhook(
                DiscordWebhookEventKind.ServerStop,
                "Hard stop",
                "*The server process was killed immediately* — saves may not have flushed.",
                DiscordWebhookEmbedFactory.BuildLifecycleExtras(managerOptions, currentServerSettings, DateTime.Now));
        }
        catch (Exception ex)
        {
            logger.Error("Force kill failed unexpectedly.", ex);
            if (proc is not null && SafeProcessHasExited(proc))
            {
                ReleaseServerProcess(proc);
                serverStarted = false;
                _serverReadyAnnounced = 0;
                playerTracker.Clear();
                _lastStopUtc = DateTime.UtcNow;
                ApplyIdleLifecycleUi();
            }
            else
            {
                _serverExitUnconfirmed = true;
                UpdateStatus("Force kill failed — see log");
                SetStartButtonForRunningProcess();
            }
        }
        finally
        {
            ReleaseRestartGuard();
        }
    }

    private async void forceKillServerButton_Click(object sender, EventArgs e)
    {
        try
        {
            await ForceKillServerAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.Error("Force kill failed unexpectedly.", ex);
        }
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
        if (_isClosing)
        {
            logger.Warn($"Restart ignored ({reason}): the manager is closing.");
            return;
        }

        if (restartInProgress)
        {
            logger.Warn($"Restart ignored ({reason}): a restart cycle is already in progress.");
            return;
        }

        RefreshInstallerState();

        if (_installInProgress || _installerExitUnconfirmed)
        {
            logger.Warn($"Restart ignored ({reason}): an install/update is in progress.");
            UpdateStatus("Restart ignored — install in progress");
            return;
        }

        if (_lifecycleDepth > 0)
        {
            logger.Warn($"Restart ignored ({reason}): another lifecycle operation is in progress.");
            UpdateStatus("Restart ignored — another server operation is in progress");
            return;
        }

        using var lifecycle = EnterLifecycle();

        // The restart operation owns the guard for the whole cycle (stop + delay + launch). The
        // nested stop takes its own lease and releases it without clearing this outer guard.
        AcquireRestartGuard();
        try
        {
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

            // The stop may have been a no-op because the process already exited on its own. Release
            // a confirmed-exit handle here and refuse the replacement only when exit is unconfirmed.
            if (!ReleaseExitedProcessOrBlockStart(out var blockReason))
            {
                logger.Error($"Restart ({reason}) cannot launch a replacement: {blockReason}");
                UpdateStatus("Restart failed — previous process still running");
                PostDiscordWebhook(
                    DiscordWebhookEventKind.ServerRestartFailed,
                    "Restart did not come back online",
                    "The previous server process did not exit, so a replacement was not launched.",
                    DiscordWebhookEmbedFactory.BuildOperationalExtras(managerOptions, currentServerSettings));
                return;
            }

            await Task.Delay(1500).ConfigureAwait(true);
            StartProcess(isRestartOperation: true);
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
        finally
        {
            ReleaseRestartGuard();
        }
    }

    private async void startServerButton_Click(object sender, EventArgs e)
    {
        try
        {
            if (_isClosing)
            {
                return;
            }

            if (serverStarted)
            {
                // A double-click would otherwise start and then immediately stop the server.
                if (DateTime.UtcNow - _lastStartUtc < LifecycleClickGuard)
                {
                    logger.Warn("Stop ignored: the server was just started.");
                    UpdateStatus("Stop ignored — server just started; click Stop again");
                    return;
                }

                await StopProcessAsync().ConfigureAwait(true);
            }
            else
            {
                if (DateTime.UtcNow - _lastStopUtc < LifecycleClickGuard)
                {
                    logger.Warn("Start ignored: the server was just stopped.");
                    UpdateStatus("Start ignored — server just stopped; click Start again");
                    return;
                }

                StartProcess();
            }
        }
        catch (Exception ex)
        {
            logger.Error("Start/stop click failed unexpectedly.", ex);
            UpdateStatus("Server action failed — see log");
        }
    }

    private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        RefreshInstallerState();

        if (_installInProgress || _installerExitUnconfirmed)
        {
            e.Cancel = true;
            logger.Warn("Close refused: an install/update is in progress.");
            UpdateStatus("Close blocked — install in progress");
            MessageBox.Show(
                this,
                "An install/update is still running (or a SteamCMD process it started is still alive). Wait for SteamCMD to exit before closing the manager.",
                "Install in progress",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (restartInProgress || _lifecycleDepth > 0)
        {
            e.Cancel = true;
            logger.Warn("Close refused: a lifecycle operation is in progress.");
            UpdateStatus("Close blocked — server operation in progress");
            MessageBox.Show(
                this,
                "A start, stop, or restart is still in progress. Wait for it to finish before closing the manager.",
                "Server operation in progress",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var proc = serverProc;
        if (proc is null)
        {
            _isClosing = true;
            DisposeAutomationServiceOnClose();
            return;
        }

        var alreadyExited = SafeProcessHasExited(proc);
        if (!serverStarted || alreadyExited)
        {
            if (alreadyExited)
            {
                ReleaseServerProcess(proc);
            }

            _isClosing = true;
            DisposeAutomationServiceOnClose();
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

        _isClosing = true;
        SynchronouslyShutdownServerForAppClose(notifyDiscord: interactiveClose);
        DisposeAutomationServiceOnClose();
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

        if (SafeProcessHasExited(proc))
        {
            ReleaseServerProcess(proc);
        }
        else
        {
            // Never dispose a handle whose exit is unconfirmed; the manager cannot track it after close.
            _serverExitUnconfirmed = true;
            logger.Error("Server process exit could not be confirmed while closing the manager; the handle is retained and not disposed.");
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
            if (proc == null)
            {
                return;
            }

            if (!SafeProcessHasExited(proc))
            {
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
            }

            if (SafeProcessHasExited(proc))
            {
                ReleaseServerProcess(proc);
            }
            else
            {
                _serverExitUnconfirmed = true;
                logger.Error("Final kill on FormClosed could not confirm exit; the process handle is retained and not disposed.");
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
