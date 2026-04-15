using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ProspectSummaryReaderTests
{
    [Fact]
    public void Read_Extracts_ProspectInfo_Strings_From_Header()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ism-sum-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "HuntingResort.json");
        var json = """
            {
            	"ProspectInfo": {
            		"ProspectID": "HuntingResort",
            		"ProspectDTKey": "Outpost006_Olympus",
            		"ProspectState": "Active",
            		"Difficulty": "Medium"
            	},
            	"ProspectBlob": "huge"
            }
            """;
        File.WriteAllText(path, json);

        try
        {
            var s = ProspectSummaryReader.Read(path);
            Assert.Equal("HuntingResort", s.BaseName);
            Assert.Equal("HuntingResort", s.ProspectId);
            Assert.Equal("Outpost006_Olympus", s.ProspectDtKey);
            Assert.Equal("Active", s.ProspectState);
            Assert.Equal("Medium", s.Difficulty);
            Assert.Contains("Outpost006_Olympus", s.BuildDetailsText(), StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Read_Extracts_AssociatedMembers_AndScalars()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ism-mem-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Resort.json");
        var json = """
            {
            	"ProspectInfo": {
            		"ProspectID": "Resort",
            		"ProspectDTKey": "OpenWorld_Elysium",
            		"ProspectState": "Active",
            		"LobbyName": "",
            		"Difficulty": "Medium",
            		"ElapsedTime": 18,
            		"AssociatedMembers": [
            			{
            				"AccountName": "DUKEVENATOR",
            				"CharacterName": "DUKEVENATOR",
            				"UserID": "76561198057119793",
            				"ChrSlot": 0,
            				"Experience": 5995235,
            				"Status": "Prospect_Conifer",
            				"Settled": false,
            				"IsCurrentlyPlaying": true
            			}
            		],
            		"Cost": 0,
            		"Reward": 0,
            		"Insurance": false,
            		"NoRespawns": false
            	},
            	"ProspectBlob": "huge"
            }
            """;
        File.WriteAllText(path, json);

        try
        {
            var s = ProspectSummaryReader.Read(path);
            var m = Assert.Single(s.Members);
            Assert.Equal("DUKEVENATOR", m.AccountName);
            Assert.True(m.IsCurrentlyPlaying);
            Assert.Equal(18, s.ElapsedGameMinutes);
            Assert.Equal(0, s.Cost);
            Assert.False(s.Insurance!.Value);
            Assert.Equal(1, s.OnlineMemberCount);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
            }
        }
    }
}
