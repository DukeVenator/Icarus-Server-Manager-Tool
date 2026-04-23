using Newtonsoft.Json.Linq;

namespace IcarusProspectEditor.Services;

internal static class ThemePreferenceService
{
    private static readonly string OptionsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IcarusServerManager",
        "manager-options.json");

    public static string LoadTheme()
    {
        try
        {
            if (!File.Exists(OptionsPath))
            {
                return "Dark";
            }

            var json = File.ReadAllText(OptionsPath);
            var obj = JObject.Parse(json);
            var value = (string?)obj["Theme"];
            return string.Equals(value, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
        }
        catch
        {
            return "Dark";
        }
    }

    public static void SaveTheme(string theme)
    {
        var normalized = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
        Directory.CreateDirectory(Path.GetDirectoryName(OptionsPath)!);

        JObject obj;
        if (File.Exists(OptionsPath))
        {
            var json = File.ReadAllText(OptionsPath);
            obj = JObject.Parse(json);
        }
        else
        {
            obj = new JObject();
        }

        obj["Theme"] = normalized;
        if (obj["OptionsSchemaVersion"] is null)
        {
            obj["OptionsSchemaVersion"] = 9;
        }

        File.WriteAllText(OptionsPath, obj.ToString());
    }
}
