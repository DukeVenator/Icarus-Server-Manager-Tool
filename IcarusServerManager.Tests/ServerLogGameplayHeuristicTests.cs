using IcarusServerManager.Models;
using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ServerLogGameplayHeuristicTests
{
    [Fact]
    public void LooksLikeLevelUp_ReturnsFalse_WhenJoinLine()
    {
        var line = "Player \"Ada\" joined.";
        var pr = new ServerOutputPlayerTracker().ProcessLogLine(line);
        Assert.False(ServerLogGameplayHeuristic.LooksLikeLevelUp(line, pr));
    }

    [Fact]
    public void LooksLikeLevelUp_ReturnsTrue_ForLevelUpPhrase()
    {
        var line = "LogTemp: Player leveled up to 5";
        Assert.True(ServerLogGameplayHeuristic.LooksLikeLevelUp(line, PlayerLogLineResult.None));
    }

    [Fact]
    public void LooksLikePlayerDeath_ReturnsTrue_ForDiedPhrase()
    {
        var line = "Warning: Player character has died.";
        Assert.True(ServerLogGameplayHeuristic.LooksLikePlayerDeath(line, PlayerLogLineResult.None));
    }

    [Fact]
    public void LooksLikePlayerDeath_ReturnsFalse_ForLevelLine()
    {
        var line = "reached level 10";
        Assert.False(ServerLogGameplayHeuristic.LooksLikePlayerDeath(line, PlayerLogLineResult.None));
    }
}
