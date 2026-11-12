using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core;

namespace KaraokeToolkit.Core.Tests.Support;

internal static class RecoveryCrashWorker
{
    // This runs in a disposable child process, never in the application. The parent test
    // terminates it without Dispose/finally so file handles and journals behave like a crash.
    public static async Task<int> Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--cdrdao-process-probe")
        {
            if (args[1] == "flood")
            {
                Console.Out.Write(new string('O', 180000));
                Console.Error.Write(new string('E', 180000));
                return 0;
            }
            if (args[1] == "wait")
            {
                File.WriteAllText(args[2], Environment.ProcessId.ToString());
                await Task.Delay(Timeout.Infinite);
            }
            Console.Out.WriteLine("stdout discovery");
            Console.Error.WriteLine("stderr discovery");
            return args[1] == "error" ? 3 : 0;
        }
        if (args.Length > 0 && args[0] == "--benchmark-audio")
            return await Benchmarks.AudioBenchmarks.RunAsync(args.Skip(1).ToArray());
        if (args.Length != 3 || args[0] != "--recovery-crash-worker")
            return 2;
        string folder = args[1];
        await new ConversionService(new RecoveryStore(args[2])).RunAsync(
            new(OperationCatalog.defAction.Zip_To_AudioG, folder,
                new()
                {
                    AutoCreateCueText = false,
                    DeleteOldFiles = false
                }),
            progress: new PauseBeforePublication(folder));
        return 0;
    }

    private sealed class PauseBeforePublication(string folder) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value)
        {
            if (!value.Message.StartsWith("Verified outputs:", StringComparison.Ordinal))
                return;
            File.WriteAllText(Path.Combine(folder, "crash-ready"), "ready");
            using var wait = new ManualResetEventSlim();
            wait.Wait();
        }
    }
}
