using System.Diagnostics;
using System.IO.Compression;
using IcarusProspectEditor.Services;

namespace IcarusProspectEditor;

internal sealed partial class MainForm
{
    private const string EditorMainExeName = "IcarusProspectEditor.exe";

    private void SetupEditorUpdateFlow()
    {
        _editorUpdateTimer.Tick += (_, _) => _ = TickEditorUpdateCheckAsync();
        _editorUpdateTimer.Start();
        Load += (_, _) => _ = CheckForEditorUpdateAsync(userInitiated: false);
    }

    private async Task TickEditorUpdateCheckAsync()
    {
        if (!_updateSettings.UpdateCheckEnabled)
        {
            return;
        }

        if (_lastEditorUpdateCheckUtc != DateTime.MinValue &&
            (DateTime.UtcNow - _lastEditorUpdateCheckUtc).TotalHours < _updateSettings.UpdateCheckIntervalHours)
        {
            return;
        }

        await CheckForEditorUpdateAsync(userInitiated: false).ConfigureAwait(true);
    }

    private async Task CheckForEditorUpdateAsync(bool userInitiated)
    {
        if (_editorUpdateCheckInProgress)
        {
            return;
        }

        _editorUpdateCheckInProgress = true;
        _lastEditorUpdateCheckUtc = DateTime.UtcNow;
        try
        {
            AppLogService.UserAction(userInitiated ? "Update check (manual)" : "Update check (scheduled/startup)");
            var rel = await _editorUpdateService
                .GetLatestEditorReleaseAsync(_updateSettings.UpdateIncludePrerelease, CancellationToken.None)
                .ConfigureAwait(true);
            if (rel == null)
            {
                if (userInitiated)
                {
                    MessageBox.Show(this, "Could not find an editor release (look for tags like editor-v1.0.0 and a matching zip asset).", "Prospect Editor updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                AppLogService.Info("Editor update: no matching GitHub release.");
                return;
            }

            var current = GetCurrentEditorVersion();
            if (!ProspectEditorUpdateService.TryParseEditorTagVersion(rel.TagName, out var latest))
            {
                if (userInitiated)
                {
                    MessageBox.Show(this, $"Latest release tag is {rel.TagName}.", "Prospect Editor updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return;
            }

            if (latest <= current)
            {
                if (userInitiated)
                {
                    MessageBox.Show(this, $"You're already on the latest version ({current}).", "Prospect Editor updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return;
            }

            if (_editorUpdatePromptShownThisRun && !userInitiated)
            {
                return;
            }

            _editorUpdatePromptShownThisRun = true;
            var ask = _updateSettings.UpdatePromptBeforeDownload || userInitiated;
            if (ask)
            {
                var prompt = MessageBox.Show(
                    this,
                    $"A newer Prospect Editor release is available.\n\nCurrent: {current}\nLatest: {rel.TagName}\n\nDownload, install, and restart now?",
                    "Prospect Editor update available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1);
                if (prompt != DialogResult.Yes)
                {
                    AppLogService.UserAction("Update download declined by user.");
                    return;
                }
            }

            await DownloadAndInstallEditorUpdateAsync(rel).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppLogService.Error("Editor update check failed.", ex);
            if (userInitiated)
            {
                MessageBox.Show(this, $"Update check failed:\n{ex.Message}", "Prospect Editor updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _editorUpdateCheckInProgress = false;
        }
    }

    private static Version GetCurrentEditorVersion()
    {
        var v = typeof(MainForm).Assembly.GetName().Version;
        if (v == null)
        {
            return new Version(0, 0, 0, 0);
        }

        return new Version(v.Major, v.Minor, Math.Max(0, v.Build), 0);
    }

    private async Task DownloadAndInstallEditorUpdateAsync(ProspectEditorReleaseInfo release)
    {
        var updaterExe = Path.Combine(AppContext.BaseDirectory, "ManagerUpdater.exe");
        if (!File.Exists(updaterExe))
        {
            MessageBox.Show(
                this,
                "ManagerUpdater.exe was not found next to the editor executable. Re-download the full release zip or rebuild with the updater copied to output.",
                "Prospect Editor updates",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _status.Text = "Downloading editor update...";
        AppLogService.UserAction($"Starting update download: {release.AssetName} ({release.TagName})");

        var tempRoot = Path.Combine(Path.GetTempPath(), "IcarusProspectEditor-Update", Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(tempRoot, release.AssetName);
        var extractDir = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(extractDir);

        await _editorUpdateService.DownloadAssetAsync(release.DownloadUrl, zipPath, CancellationToken.None).ConfigureAwait(true);
        ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

        var extractedExe = Directory.GetFiles(extractDir, EditorMainExeName, SearchOption.AllDirectories).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(extractedExe))
        {
            throw new InvalidOperationException("Downloaded release zip does not contain IcarusProspectEditor.exe.");
        }

        var extractedRoot = Path.GetDirectoryName(extractedExe)!;
        var currentExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, EditorMainExeName);
        var currentDir = Path.GetDirectoryName(currentExe) ?? AppContext.BaseDirectory;
        var currentProcId = Environment.ProcessId;

        var args = new[]
        {
            "--source", extractedRoot,
            "--target", currentDir,
            "--pid", currentProcId.ToString(),
            "--exe", EditorMainExeName
        };

        Process.Start(new ProcessStartInfo
        {
            FileName = updaterExe,
            WorkingDirectory = currentDir,
            UseShellExecute = false,
            Arguments = string.Join(" ", args.Select(QuoteEditorUpdateArg))
        });

        AppLogService.Info($"Launching ManagerUpdater.exe for {release.TagName} and closing editor.");
        Close();
    }

    private static string QuoteEditorUpdateArg(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "\"\"";
        }

        return s.Contains(' ') ? $"\"{s}\"" : s;
    }

    private void ShowEditorUpdateSettings()
    {
        AppLogService.UserAction("Opened editor update settings.");
        using var dlg = new ProspectEditorUpdateSettingsForm(_updateSettings);
        dlg.ApplyTheme(_isDarkTheme);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _updateSettings = dlg.Settings;
            _updateSettings.Save();
            AppLogService.UserAction("Editor update settings saved.");
        }
    }
}
