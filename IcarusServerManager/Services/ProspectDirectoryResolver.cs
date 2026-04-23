namespace IcarusServerManager.Services;

/// <summary>
/// Shared prospect-folder resolution (install root + optional UserDir / Saved suffix), same rules as <see cref="ServerSettingsIniService.ResolveProspectsDirectory"/>.
/// </summary>
internal static class ProspectDirectoryResolver
{
    /// <summary>
    /// Returns null when the server install root is missing; otherwise the prospects directory path (may not exist on disk).
    /// </summary>
    public static string? TryResolveProspectsDirectory(
        string? serverInstallRoot,
        string userDirOverride,
        string savedDirSuffix,
        ServerSettingsIniService iniService) =>
        string.IsNullOrWhiteSpace(serverInstallRoot)
            ? null
            : iniService.ResolveProspectsDirectory(serverInstallRoot.Trim(), userDirOverride, savedDirSuffix);
}
