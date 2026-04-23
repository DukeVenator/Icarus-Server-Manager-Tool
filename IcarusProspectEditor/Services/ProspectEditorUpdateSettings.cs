using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IcarusProspectEditor.Services;

internal sealed class ProspectEditorUpdateSettings
{
    public bool UpdateCheckEnabled { get; set; } = true;
    public int UpdateCheckIntervalHours { get; set; } = 24;
    public bool UpdateIncludePrerelease { get; set; }
    public bool UpdatePromptBeforeDownload { get; set; } = true;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IcarusProspectEditor",
        "prospect-editor-update.json");

    public static ProspectEditorUpdateSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new ProspectEditorUpdateSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            var o = JObject.Parse(json);
            return new ProspectEditorUpdateSettings
            {
                UpdateCheckEnabled = o.Value<bool?>("UpdateCheckEnabled") ?? true,
                UpdateCheckIntervalHours = Math.Clamp(o.Value<int?>("UpdateCheckIntervalHours") ?? 24, 1, 168),
                UpdateIncludePrerelease = o.Value<bool?>("UpdateIncludePrerelease") ?? false,
                UpdatePromptBeforeDownload = o.Value<bool?>("UpdatePromptBeforeDownload") ?? true
            };
        }
        catch
        {
            return new ProspectEditorUpdateSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var o = new JObject
            {
                ["UpdateCheckEnabled"] = UpdateCheckEnabled,
                ["UpdateCheckIntervalHours"] = UpdateCheckIntervalHours,
                ["UpdateIncludePrerelease"] = UpdateIncludePrerelease,
                ["UpdatePromptBeforeDownload"] = UpdatePromptBeforeDownload
            };
            File.WriteAllText(SettingsPath, o.ToString(Formatting.Indented));
        }
        catch
        {
            // best-effort
        }
    }
}
