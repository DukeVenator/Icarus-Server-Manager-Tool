using IcarusServerManager.Models;
using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ServerLogChatHeuristicTests
{
    [Fact]
    public void LooksLikeChatLine_ReturnsFalse_WhenPlayerJoinDetected()
    {
        var line = "Player \"Ada\" joined the session.";
        var pr = new ServerOutputPlayerTracker().ProcessLogLine(line);
        Assert.False(ServerLogChatHeuristic.LooksLikeChatLine(line, pr));
    }

    [Fact]
    public void LooksLikeChatLine_ReturnsTrue_ForLogChatStyle()
    {
        var line = "LogChat: GlobalChat Hello everyone";
        Assert.True(ServerLogChatHeuristic.LooksLikeChatLine(line, PlayerLogLineResult.None));
    }

    [Fact]
    public void LooksLikeChatLine_ReturnsTrue_ForSayWithQuotes()
    {
        var line = "SomeCategory: Player say \"hello\" in channel";
        Assert.True(ServerLogChatHeuristic.LooksLikeChatLine(line, PlayerLogLineResult.None));
    }

    [Fact]
    public void LooksLikeChatLine_ReturnsFalse_ForIcarusAddConnectedPlayer()
    {
        var line =
            "LogConnectedPlayers: Display: AddConnectedPlayer - UserId: 1 | PlayerName: Duke Venator Mythis";
        Assert.False(ServerLogChatHeuristic.LooksLikeChatLine(line, PlayerLogLineResult.None));
    }
}
