using IcarusProspectEditor.Services;
using Xunit;

namespace IcarusServerManager.Tests;

public sealed class ProspectEditorMetadataDefaultsTests
{
    [Fact]
    public void ClaimedCharacterSlot_IsZero_ForDirtyBaselineAlignment()
    {
        Assert.Equal(0, ProspectEditorMetadataDefaults.ClaimedCharacterSlot);
    }
}
