using IcarusServerManager.Models;
using Newtonsoft.Json;

namespace IcarusServerManager.Services;

internal sealed class ManagerOptionsService
{
    private const int CurrentSchemaVersion = 9;
    private readonly string _path;

    public ManagerOptionsService()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IcarusServerManager");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "manager-options.json");
    }

    /// <summary>For tests: read/write options at an explicit path (parent directory is created).</summary>
    public ManagerOptionsService(string optionsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(optionsFilePath);
        var dir = Path.GetDirectoryName(optionsFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _path = optionsFilePath;
    }

    public string OptionsPath => _path;

    /// <summary>
    /// Loads options and runs the ordered migration pipeline.
    ///
    /// Ordering matters: the legacy port import (schema 4) must run before the later version
    /// guards, because those guards used to advance the stored version first and make the import
    /// unreachable — silently replacing custom ports with defaults. Migrated options are written
    /// back only after the whole pipeline succeeds, and malformed files are reported instead of
    /// being replaced by defaults.
    /// </summary>
    /// <param name="legacyPortSource">
    /// Supplies ports from the previous per-user application settings file. Applied only while the
    /// options file predates schema 4, and on first run before the freshly created defaults are
    /// published.
    /// </param>
    public ManagerOptions Load(Func<(int GamePort, int QueryPort)>? legacyPortSource = null)
    {
        if (!File.Exists(_path))
        {
            // First run: current-schema defaults are already in memory, so the schema chain does not
            // need to replay. The legacy port import still happens before the defaults are published,
            // which is the first-run equivalent of the schema-4 step in the ordered pipeline below.
            var defaults = new ManagerOptions { OptionsSchemaVersion = CurrentSchemaVersion };
            ApplyLegacyPortImport(defaults, legacyPortSource);
            try
            {
                WriteFile(defaults);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                throw new InvalidDataException(
                    $"Manager options defaults could not be created at '{_path}'.\n{ex.Message}",
                    ex);
            }

            return defaults;
        }

        ManagerOptions options;
        try
        {
            var json = File.ReadAllText(_path);
            options = JsonConvert.DeserializeObject<ManagerOptions>(json)
                      ?? throw new InvalidDataException("The options file contained no JSON object.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"Could not read manager options from '{_path}'. The file was left unchanged.\n{ex.Message}",
                ex);
        }

        var migrated = ApplySchemaMigrations(options, legacyPortSource);
        if (migrated)
        {
            try
            {
                WriteFile(options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                throw new InvalidDataException(
                    $"Manager options were migrated in memory but could not be saved to '{_path}'. The original file is unchanged.\n{ex.Message}",
                    ex);
            }
        }

        return options;
    }

    public void Save(ManagerOptions options) => WriteFile(options);

    private void WriteFile(ManagerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var json = JsonConvert.SerializeObject(options, Formatting.Indented);

        // A backup of the previous options file must exist before that file is replaced. A backup
        // failure is surfaced instead of being swallowed into a false success.
        if (File.Exists(_path))
        {
            CreateBackup();
        }

        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, json);
            ValidateStagedFile(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                    // best-effort cleanup
                }
            }
        }
    }

    private void CreateBackup()
    {
        var backupPath = _path + ".bak";
        try
        {
            File.Copy(_path, backupPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Could not create the manager options backup at '{backupPath}'. The existing options file was not replaced.",
                ex);
        }

        if (!File.Exists(backupPath))
        {
            throw new IOException(
                $"The manager options backup at '{backupPath}' was not created. The existing options file was not replaced.");
        }
    }

    /// <summary>
    /// Reads the staged file back and verifies it is byte-for-byte the JSON that was just written.
    /// The move over the live options file only happens after this check succeeds.
    /// </summary>
    private static void ValidateStagedFile(string tempPath, string expectedJson)
    {
        string staged;
        try
        {
            staged = File.ReadAllText(tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"The staged manager options file could not be read back for validation. The existing options file was not replaced.\n{ex.Message}",
                ex);
        }

        if (!string.Equals(staged, expectedJson, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The staged manager options file failed validation. The existing options file was not replaced.");
        }

        try
        {
            if (JsonConvert.DeserializeObject<ManagerOptions>(staged) is null)
            {
                throw new InvalidDataException(
                    "The staged manager options file contained no JSON object. The existing options file was not replaced.");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "The staged manager options file failed validation. The existing options file was not replaced.",
                ex);
        }
    }

    /// <summary>
    /// Copies valid legacy ports from the previous per-user settings file. Invalid or absent values
    /// leave the manager defaults in place.
    /// </summary>
    private static void ApplyLegacyPortImport(
        ManagerOptions o,
        Func<(int GamePort, int QueryPort)>? legacyPortSource)
    {
        if (legacyPortSource is null)
        {
            return;
        }

        var (gamePort, queryPort) = legacyPortSource();
        if (gamePort is >= 1 and <= 65535)
        {
            o.LaunchGamePort = gamePort;
        }

        if (queryPort is >= 1 and <= 65535)
        {
            o.LaunchQueryPort = queryPort;
        }
    }

    private static bool ApplySchemaMigrations(ManagerOptions o, Func<(int GamePort, int QueryPort)>? legacyPortSource)
    {
        var changed = false;

        if (o.OptionsSchemaVersion < 2)
        {
            o.DiscordWebhookNotifyServerRestart = o.EnableDiscordWebhook;
            o.DiscordWebhookChatThrottleSeconds = Math.Clamp(o.DiscordWebhookChatThrottleSeconds, 0, 120);
            o.OptionsSchemaVersion = 2;
            changed = true;
        }

        if (o.OptionsSchemaVersion < 3)
        {
            o.DiscordWebhookNotifyUnexpectedExit = o.DiscordWebhookNotifyServerStop;
            o.DiscordWebhookGameplayThrottleSeconds = Math.Clamp(o.DiscordWebhookGameplayThrottleSeconds, 0, 120);
            o.DiscordWebhookHeartbeatIntervalHours = Math.Clamp(o.DiscordWebhookHeartbeatIntervalHours, 0, 168);
            o.OptionsSchemaVersion = 3;
            changed = true;
        }

        if (o.OptionsSchemaVersion < 4)
        {
            // Ordered legacy import: only reachable while the file still predates schema 4. The
            // version is advanced after the import so later guards cannot make it unreachable.
            ApplyLegacyPortImport(o, legacyPortSource);
            o.OptionsSchemaVersion = 4;
            changed = true;
        }

        if (o.OptionsSchemaVersion < 5)
        {
            ConsoleLogFilter.ApplyPreset(o, "Balanced");
            o.OptionsSchemaVersion = 5;
            changed = true;
        }

        if (o.OptionsSchemaVersion < 6)
        {
            o.PauseIntervalRestartWhenEmpty = false;
            o.IntervalRestartUseEmptyIdleTimer = false;
            o.OptionsSchemaVersion = 6;
            changed = true;
        }

        if (o.OptionsSchemaVersion < 7)
        {
            o.GracefulShutdownTryCtrlC = true;
            if (o.GracefulShutdownWaitSeconds is < 10 or > 900)
            {
                o.GracefulShutdownWaitSeconds = 120;
            }

            o.OptionsSchemaVersion = 7;
            changed = true;
        }

        if (o.OptionsSchemaVersion < 8)
        {
            o.DiscordWebhookUseTitleEmojis = true;
            o.DiscordWebhookShowEmbedAuthor = true;
            o.DiscordWebhookShowEmbedTimestamp = true;
            o.DiscordWebhookShowPortsOnEmbeds = true;
            o.DiscordWebhookShowSessionOnEmbeds = true;
            o.DiscordWebhookIncludeProspectOnStart = true;
            o.DiscordWebhookHeartbeatShowPolicyLine = true;
            o.DiscordWebhookUseThemedLabels = true;
            o.DiscordWebhookPlainTextDescriptions = false;
            o.DiscordWebhookCustomFooter ??= string.Empty;
            if (o.DiscordWebhookDescriptionMaxChars is < 800 or > 4096)
            {
                o.DiscordWebhookDescriptionMaxChars = 3500;
            }

            o.OptionsSchemaVersion = 8;
            changed = true;
        }

        if (o.OptionsSchemaVersion < 9)
        {
            o.ManagerUpdateCheckEnabled = true;
            if (o.ManagerUpdateCheckIntervalHours is < 1 or > 168)
            {
                o.ManagerUpdateCheckIntervalHours = 6;
            }

            o.ManagerUpdateIncludePrerelease = false;
            o.ManagerUpdatePromptBeforeDownload = true;
            o.OptionsSchemaVersion = 9;
            changed = true;
        }

        return changed;
    }
}
