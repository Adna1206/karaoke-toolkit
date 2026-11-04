using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using Action = KaraokeToolkit.Core.Execution.OperationId;
namespace KaraokeToolkit.Tests.Support;

internal static class FileWorkflowFixture
{
    internal static ConversionOptions Options => new() { DeleteOldFiles = false, AutoCreateCueText = false };
    internal static SelectionConversionService Service() => new(new RecoveryStore(TestMediaFixture.NewFolder()));
    internal static string ZipFolder()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.ZipToBin, "keep");
        return folder;
    }

}
