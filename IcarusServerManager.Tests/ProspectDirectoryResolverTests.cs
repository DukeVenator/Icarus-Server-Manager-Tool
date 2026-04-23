using IcarusServerManager.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ProspectDirectoryResolverTests
{
    private readonly ServerSettingsIniService _ini = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolve_returns_null_when_install_root_missing(string? root)
    {
        Assert.Null(ProspectDirectoryResolver.TryResolveProspectsDirectory(root, "", "", _ini));
    }

    [Fact]
    public void TryResolve_trims_install_root_and_matches_service()
    {
        const string root = @"  D:\IcarusRoot  ";
        var expected = _ini.ResolveProspectsDirectory(@"D:\IcarusRoot", "u", "s");
        var actual = ProspectDirectoryResolver.TryResolveProspectsDirectory(root, "u", "s", _ini);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TryResolve_default_saved_matches_direct_service_call()
    {
        const string root = @"E:\game";
        var viaHelper = ProspectDirectoryResolver.TryResolveProspectsDirectory(root, "", "", _ini);
        var direct = _ini.ResolveProspectsDirectory(root, "", "");
        Assert.Equal(direct, viaHelper);
    }
}
