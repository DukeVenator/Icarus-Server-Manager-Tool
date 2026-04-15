namespace IcarusServerManager.Models;

/// <summary>
/// One entry from <c>ProspectInfo.AssociatedMembers</c> in a dedicated-server prospect JSON.
/// </summary>
internal sealed record ProspectMemberInfo(
    string AccountName,
    string CharacterName,
    string UserId,
    bool IsCurrentlyPlaying,
    long Experience,
    string? Status);
