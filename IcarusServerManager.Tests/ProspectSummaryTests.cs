using IcarusServerManager.Models;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ProspectSummaryTests
{
    [Fact]
    public void OnlineMemberCount_CountsPlayingMembersOnly()
    {
        var s = new ProspectSummary
        {
            BaseName = "P",
            FullPath = @"C:\data\p.json",
            Members = new[]
            {
                new ProspectMemberInfo("a", "A", "1", true, 0, null),
                new ProspectMemberInfo("b", "B", "2", false, 0, null)
            }
        };
        Assert.Equal(1, s.OnlineMemberCount);
    }

    [Fact]
    public void ToString_IncludesBasicsAndOnlineCount()
    {
        var s = new ProspectSummary
        {
            BaseName = "MyProspect",
            FullPath = @"C:\x.json",
            ProspectDtKey = "Map1",
            Difficulty = "Normal",
            ProspectState = "Active",
            Members = new[] { new ProspectMemberInfo("a", "A", "1", true, 0, null) }
        };
        var t = s.ToString();
        Assert.Contains("MyProspect", t, StringComparison.Ordinal);
        Assert.Contains("Map1", t, StringComparison.Ordinal);
        Assert.Contains("online 1", t, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDetailsText_IncludesFileMetadataAndMembers()
    {
        var when = new DateTime(2026, 4, 15, 12, 0, 0);
        var s = new ProspectSummary
        {
            BaseName = "P",
            FullPath = @"C:\srv\prospects\file.json",
            ProspectId = "id-1",
            FileSizeBytes = 2048,
            LastWriteTimeLocal = when,
            Members = new[] { new ProspectMemberInfo("acc", "Char", "99", true, 10, "ok") }
        };
        var text = s.BuildDetailsText();
        Assert.Contains("file.json", text, StringComparison.Ordinal);
        Assert.Contains("2 KB", text, StringComparison.Ordinal);
        Assert.Contains("id-1", text, StringComparison.Ordinal);
        Assert.Contains("Char", text, StringComparison.Ordinal);
        Assert.Contains("●", text, StringComparison.Ordinal);
    }
}
