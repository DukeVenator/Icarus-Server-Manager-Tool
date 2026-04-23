using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ProspectExpireTimeUiTests
{
    [Fact]
    public void ToPersistedSeconds_never_expires_is_negative_one()
    {
        var anyDate = new DateTime(2030, 6, 15, 12, 0, 0, DateTimeKind.Local);
        Assert.Equal(-1, ProspectExpireTimeUi.ToPersistedSeconds(true, anyDate));
    }

    [Fact]
    public void ToPersistedSeconds_with_date_matches_unix_round_trip()
    {
        var local = new DateTime(2026, 1, 15, 14, 30, 0, DateTimeKind.Local);
        var expected = new DateTimeOffset(local).ToUnixTimeSeconds();
        Assert.Equal(expected, ProspectExpireTimeUi.ToPersistedSeconds(false, local));
    }
}
