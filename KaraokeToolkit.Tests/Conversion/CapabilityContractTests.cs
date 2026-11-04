using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Conversion;
using Action = KaraokeToolkit.Core.Execution.OperationId;

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
    [InlineData(Action.DiscRip, OperationSupport.HardwareUnverified)]
    [InlineData(Action.RenameFilesFromInternet, OperationSupport.Deferred)]
    public void ExternalOperationsAreNotReportedAsVerifiedLocalConversions(Action action, OperationSupport expected)
        => Assert.Equal(expected, OperationCapabilities.Describe(action));
}
