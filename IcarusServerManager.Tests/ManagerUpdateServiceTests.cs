using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ManagerUpdateServiceTests
{
    [Theory]
    [InlineData("manager-v2.0.2", 2, 0, 2)]
    [InlineData("manager-v2.0.0", 2, 0, 0)]
    [InlineData("manager-v2.0.1", 2, 0, 1)]
    [InlineData("v1.0.8", 1, 0, 8)]
    public void TryParseTagVersion_parses_manager_and_legacy_tags(string tag, int major, int minor, int build)
    {
        Assert.True(ManagerUpdateService.TryParseTagVersion(tag, out var v));
        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(build, v.Build);
    }

    [Fact]
    public void TryParseTagVersion_rejects_editor_tags()
    {
        Assert.False(ManagerUpdateService.TryParseTagVersion("editor-v1.0.1", out _));
    }
}
