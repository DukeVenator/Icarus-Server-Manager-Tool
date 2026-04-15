namespace IcarusServerManager.Models;

internal sealed class DedicatedServerSettingsModel
{
    public string SessionName { get; set; } = string.Empty;
    public string JoinPassword { get; set; } = string.Empty;
    public int MaxPlayers { get; set; } = 8;
    public double ShutdownIfNotJoinedFor { get; set; } = 600;
    public double ShutdownIfEmptyFor { get; set; } = 600;
    public string AdminPassword { get; set; } = string.Empty;
    public string LoadProspect { get; set; } = string.Empty;
    public string CreateProspect { get; set; } = string.Empty;
    public bool ResumeProspect { get; set; } = true;
    public string LastProspectName { get; set; } = string.Empty;
    public bool AllowNonAdminsToLaunchProspects { get; set; } = true;
    public bool AllowNonAdminsToDeleteProspects { get; set; } = false;
    public bool FiberFoliageRespawn { get; set; }
    public bool LargeStonesRespawn { get; set; }
    public double GameSaveFrequency { get; set; } = 10;
    public bool SaveGameOnExit { get; set; } = true;
    public string SteamServerName { get; set; } = string.Empty;
}
