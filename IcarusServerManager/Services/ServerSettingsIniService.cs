using IcarusServerManager.Models;
using System.Globalization;
using System.Text;

namespace IcarusServerManager.Services;

internal sealed class ServerSettingsIniService
{
    private const string SectionName = "/Script/Icarus.DedicatedServerSettings";

    public string ResolveIniPath(string serverLocation, string? userDirOverride, string? savedDirSuffix)
    {
        if (!string.IsNullOrWhiteSpace(userDirOverride))
        {
            var baseSaved = Path.IsPathRooted(userDirOverride)
                ? userDirOverride
                : Path.GetFullPath(Path.Combine(serverLocation, userDirOverride));
            var savedFolder = string.IsNullOrWhiteSpace(savedDirSuffix) ? "Saved" : $"Saved_{savedDirSuffix}";
            return Path.Combine(baseSaved, savedFolder, "Config", "WindowsServer", "ServerSettings.ini");
        }

        var defaultPath = Path.Combine(serverLocation, "Icarus", "Saved", "Config", "WindowsServer", "ServerSettings.ini");
        return defaultPath;
    }

    /// <summary>
    /// Dedicated-server prospect saves live under Saved/.../PlayerData/DedicatedServer/Prospects (same Saved root rules as <see cref="ResolveIniPath"/>).
    /// </summary>
    public string ResolveProspectsDirectory(string serverLocation, string? userDirOverride, string? savedDirSuffix)
    {
        if (!string.IsNullOrWhiteSpace(userDirOverride))
        {
            var baseSaved = Path.IsPathRooted(userDirOverride)
                ? userDirOverride
                : Path.GetFullPath(Path.Combine(serverLocation, userDirOverride));
            var savedFolder = string.IsNullOrWhiteSpace(savedDirSuffix) ? "Saved" : $"Saved_{savedDirSuffix}";
            return Path.Combine(baseSaved, savedFolder, "PlayerData", "DedicatedServer", "Prospects");
        }

        return Path.Combine(serverLocation, "Icarus", "Saved", "PlayerData", "DedicatedServer", "Prospects");
    }

    public DedicatedServerSettingsModel Load(string path)
    {
        EnsureFile(path);

        var model = new DedicatedServerSettingsModel();
        var lines = File.ReadAllLines(path);
        var inSection = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                inSection = string.Equals(line.Trim('[', ']'), SectionName, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection || string.IsNullOrWhiteSpace(line) || line.StartsWith(';'))
            {
                continue;
            }

            var idx = line.IndexOf('=');
            if (idx < 0)
            {
                continue;
            }

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            MapValue(model, key, value);
        }

        return model;
    }

    public void Save(string path, DedicatedServerSettingsModel model)
    {
        EnsureFile(path);
        var lines = File.ReadAllLines(path).ToList();
        var sectionStart = -1;
        var sectionEnd = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Equals($"[{SectionName}]", StringComparison.OrdinalIgnoreCase))
            {
                sectionStart = i;
                for (var j = i + 1; j < lines.Count; j++)
                {
                    if (lines[j].TrimStart().StartsWith("[", StringComparison.Ordinal))
                    {
                        sectionEnd = j;
                        break;
                    }
                }

                if (sectionEnd < 0)
                {
                    sectionEnd = lines.Count;
                }

                break;
            }
        }

        var newSection = BuildSection(model);
        if (sectionStart < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.Add(string.Empty);
            }

            lines.AddRange(newSection);
        }
        else
        {
            lines.RemoveRange(sectionStart, sectionEnd - sectionStart);
            lines.InsertRange(sectionStart, newSection);
        }

        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    public ValidationResult Validate(DedicatedServerSettingsModel model)
    {
        var result = new ValidationResult();
        if (model.MaxPlayers is < 1 or > 8)
        {
            result.Errors.Add("MaxPlayers must be between 1 and 8.");
        }

        if (double.IsNaN(model.ShutdownIfNotJoinedFor) || double.IsInfinity(model.ShutdownIfNotJoinedFor))
        {
            result.Errors.Add("ShutdownIfNotJoinedFor must be a valid number.");
        }

        if (double.IsNaN(model.ShutdownIfEmptyFor) || double.IsInfinity(model.ShutdownIfEmptyFor))
        {
            result.Errors.Add("ShutdownIfEmptyFor must be a valid number.");
        }

        if (string.IsNullOrWhiteSpace(model.AdminPassword))
        {
            result.Warnings.Add("AdminPassword is empty. Anyone using /AdminLogin can get admin rights.");
        }

        if (model.SteamServerName.Length > 64)
        {
            result.Errors.Add("SteamServerName must be 64 characters or fewer.");
        }

        return result;
    }

    public string BuildLaunchArguments(DedicatedServerSettingsModel model, int gamePort, int queryPort, string? logPath = null)
    {
        var args = new List<string>
        {
            "-nosteamclient",
            $"-Port={gamePort}",
            $"-QueryPort={queryPort}"
        };

        if (!string.IsNullOrWhiteSpace(model.SteamServerName))
        {
            args.Add($"-SteamServerName=\"{model.SteamServerName.Replace("\"", string.Empty, StringComparison.Ordinal)}\"");
        }

        if (!string.IsNullOrWhiteSpace(model.LoadProspect))
        {
            args.Add($"-LoadProspect=\"{model.LoadProspect.Replace("\"", string.Empty, StringComparison.Ordinal)}\"");
        }

        if (model.ResumeProspect)
        {
            args.Add("-ResumeProspect");
        }

        if (!string.IsNullOrWhiteSpace(model.CreateProspect))
        {
            args.Add($"-CreateProspect=\"{model.CreateProspect.Replace("\"", string.Empty, StringComparison.Ordinal)}\"");
        }

        if (!string.IsNullOrWhiteSpace(logPath))
        {
            args.Add($"-Log=\"{logPath.Replace("\"", string.Empty, StringComparison.Ordinal)}\"");
        }

        return string.Join(' ', args);
    }

    private static void EnsureFile(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        if (!File.Exists(path))
        {
            File.WriteAllLines(path, new[]
            {
                $"[{SectionName}]",
                "SteamServerName=",
                "SessionName=IcarusServer",
                "JoinPassword=",
                "MaxPlayers=8",
                "ShutdownIfNotJoinedFor=600.000000",
                "ShutdownIfEmptyFor=600.000000",
                "AdminPassword=",
                "LoadProspect=",
                "CreateProspect=",
                "ResumeProspect=True",
                "LastProspectName=",
                "AllowNonAdminsToLaunchProspects=True",
                "AllowNonAdminsToDeleteProspects=False",
                "FiberFoliageRespawn=False",
                "LargeStonesRespawn=False",
                "GameSaveFrequency=10.000000",
                "SaveGameOnExit=True"
            });
        }
    }

    private static List<string> BuildSection(DedicatedServerSettingsModel model)
    {
        return new List<string>
        {
            $"[{SectionName}]",
            $"SteamServerName={model.SteamServerName}",
            $"SessionName={model.SessionName}",
            $"JoinPassword={model.JoinPassword}",
            $"MaxPlayers={model.MaxPlayers}",
            $"ShutdownIfNotJoinedFor={model.ShutdownIfNotJoinedFor.ToString("0.000000", CultureInfo.InvariantCulture)}",
            $"ShutdownIfEmptyFor={model.ShutdownIfEmptyFor.ToString("0.000000", CultureInfo.InvariantCulture)}",
            $"AdminPassword={model.AdminPassword}",
            $"LoadProspect={model.LoadProspect}",
            $"CreateProspect={model.CreateProspect}",
            $"ResumeProspect={ToIniBool(model.ResumeProspect)}",
            $"LastProspectName={model.LastProspectName}",
            $"AllowNonAdminsToLaunchProspects={ToIniBool(model.AllowNonAdminsToLaunchProspects)}",
            $"AllowNonAdminsToDeleteProspects={ToIniBool(model.AllowNonAdminsToDeleteProspects)}",
            $"FiberFoliageRespawn={ToIniBool(model.FiberFoliageRespawn)}",
            $"LargeStonesRespawn={ToIniBool(model.LargeStonesRespawn)}",
            $"GameSaveFrequency={model.GameSaveFrequency.ToString("0.000000", CultureInfo.InvariantCulture)}",
            $"SaveGameOnExit={ToIniBool(model.SaveGameOnExit)}"
        };
    }

    private static string ToIniBool(bool value) => value ? "True" : "False";

    private static void MapValue(DedicatedServerSettingsModel model, string key, string value)
    {
        switch (key)
        {
            case "SteamServerName":
                model.SteamServerName = value;
                break;
            case "SessionName":
                model.SessionName = value;
                break;
            case "JoinPassword":
                model.JoinPassword = value;
                break;
            case "MaxPlayers":
                if (int.TryParse(value, out var maxPlayers))
                {
                    model.MaxPlayers = maxPlayers;
                }
                break;
            case "ShutdownIfNotJoinedFor":
                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var notJoined))
                {
                    model.ShutdownIfNotJoinedFor = notJoined;
                }
                break;
            case "ShutdownIfEmptyFor":
                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var emptyFor))
                {
                    model.ShutdownIfEmptyFor = emptyFor;
                }
                break;
            case "AdminPassword":
                model.AdminPassword = value;
                break;
            case "LoadProspect":
                model.LoadProspect = value;
                break;
            case "CreateProspect":
                model.CreateProspect = value;
                break;
            case "ResumeProspect":
                model.ResumeProspect = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                break;
            case "LastProspectName":
                model.LastProspectName = value;
                break;
            case "AllowNonAdminsToLaunchProspects":
                model.AllowNonAdminsToLaunchProspects = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                break;
            case "AllowNonAdminsToDeleteProspects":
                model.AllowNonAdminsToDeleteProspects = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                break;
            case "FiberFoliageRespawn":
                model.FiberFoliageRespawn = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                break;
            case "LargeStonesRespawn":
                model.LargeStonesRespawn = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                break;
            case "GameSaveFrequency":
                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var saveFreq))
                {
                    model.GameSaveFrequency = saveFreq;
                }
                break;
            case "SaveGameOnExit":
                model.SaveGameOnExit = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                break;
        }
    }
}
