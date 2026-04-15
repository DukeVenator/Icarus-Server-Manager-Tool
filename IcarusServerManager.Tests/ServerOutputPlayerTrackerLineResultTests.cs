using IcarusServerManager.Models;
using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ServerOutputPlayerTrackerLineResultTests
{
    [Fact]
    public void ProcessLogLine_ReturnsJoined_WhenDoubleQuotedName()
    {
        var t = new ServerOutputPlayerTracker();
        var r = t.ProcessLogLine("Player \"Ada\" joined the session.");
        Assert.Equal(PlayerLogHintKind.Joined, r.Kind);
        Assert.Equal("Ada", r.PlayerName);
        Assert.Contains("Ada", t.HintNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProcessLogLine_ReturnsJoined_WhenSingleQuotedName()
    {
        var t = new ServerOutputPlayerTracker();
        var r = t.ProcessLogLine("login: 'BobSmith' connected");
        Assert.Equal(PlayerLogHintKind.Joined, r.Kind);
        Assert.Equal("BobSmith", r.PlayerName);
    }

    [Fact]
    public void ProcessLogLine_ReturnsLeft_WhenQuotedName()
    {
        var t = new ServerOutputPlayerTracker();
        t.ProcessLogLine("Player \"Ada\" joined.");
        var r = t.ProcessLogLine("Player \"Ada\" left the game.");
        Assert.Equal(PlayerLogHintKind.Left, r.Kind);
        Assert.Equal("Ada", r.PlayerName);
        Assert.DoesNotContain("Ada", t.HintNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProcessLogLine_ReturnsNone_WhenJoinWithoutExtractableName()
    {
        var t = new ServerOutputPlayerTracker();
        var r = t.ProcessLogLine("someone joined but no quotes here");
        Assert.Equal(PlayerLogHintKind.None, r.Kind);
        Assert.Null(r.PlayerName);
        Assert.Empty(t.HintNames);
    }

    [Fact]
    public void Clear_RemovesAllHints()
    {
        var t = new ServerOutputPlayerTracker();
        t.ProcessLogLine("join 'ValidNameHere'");
        t.Clear();
        Assert.Empty(t.HintNames);
    }

    [Fact]
    public void ProcessLogLine_Icarus_AddConnectedPlayer_ParsesPlayerNamePipe()
    {
        var t = new ServerOutputPlayerTracker();
        var line =
            "LogConnectedPlayers: Display: AddConnectedPlayer - UserId: 76561198057119793 | PlayerName: Duke Venator Mythis";
        var r = t.ProcessLogLine(line);
        Assert.Equal(PlayerLogHintKind.Joined, r.Kind);
        Assert.Equal("Duke Venator Mythis", r.PlayerName);
        Assert.Contains("Duke Venator Mythis", t.HintNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProcessLogLine_Icarus_AddConnected_WithManagerPrefix_StillJoins()
    {
        var t = new ServerOutputPlayerTracker();
        var line =
            "[2026-04-15 15:43:31] [INFO] LogConnectedPlayers: Display: AddConnectedPlayer - UserId: 1 | PlayerName: Ada";
        var r = t.ProcessLogLine(line);
        Assert.Equal(PlayerLogHintKind.Joined, r.Kind);
        Assert.Equal("Ada", r.PlayerName);
    }

    [Fact]
    public void ProcessLogLine_Icarus_Finalise_DoesNotDuplicateJoin_WhenAddAlreadySeen()
    {
        var t = new ServerOutputPlayerTracker();
        t.ProcessLogLine("LogConnectedPlayers: Display: AddConnectedPlayer - UserId: 1 | PlayerName: Bob");
        var r = t.ProcessLogLine(
            "LogConnectedPlayers: Display: FinaliseConnectedPlayerInitialisation - PlayerName: Bob");
        Assert.Equal(PlayerLogHintKind.None, r.Kind);
        Assert.Single(t.HintNames);
    }

    [Fact]
    public void ProcessLogLine_Icarus_RemoveConnectedPlayer_Leaves()
    {
        var t = new ServerOutputPlayerTracker();
        t.ProcessLogLine("LogConnectedPlayers: Display: AddConnectedPlayer - UserId: 1 | PlayerName: Ada");
        var r = t.ProcessLogLine("LogConnectedPlayers: Display: RemoveConnectedPlayer - UserId: 1 | PlayerName: Ada");
        Assert.Equal(PlayerLogHintKind.Left, r.Kind);
        Assert.Equal("Ada", r.PlayerName);
        Assert.Empty(t.HintNames);
    }
}
