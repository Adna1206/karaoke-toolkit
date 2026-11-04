using KaraokeToolkit.Core.Conversion;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;

namespace KaraokeToolkit.Tests.Conversion;

public class CapabilityContractTests
{
    [Fact]
    public void EveryCatalogActionHasAnExplicitCapability()
    {
        foreach (var action in Enum.GetValues<Action>()) Assert.True(Enum.IsDefined(OperationCapabilities.Describe(action)));
        Assert.Throws<ArgumentOutOfRangeException>(() => OperationCapabilities.Describe((Action)999));
    }

    [Theory]
    [InlineData(Action.CDG_To_Bin, OperationSupport.HardwareUnverified)]
    [InlineData(Action.CDG_To_Zip, OperationSupport.HardwareUnverified)]
    [InlineData(Action.CDG_To_AudioG, OperationSupport.HardwareUnverified)]
    [InlineData(Action.Rename_Files_From_Internet, OperationSupport.Deferred)]
    public void ExternalOperationsAreNotReportedAsVerifiedLocalConversions(Action action, OperationSupport expected)
        => Assert.Equal(expected, OperationCapabilities.Describe(action));
}
