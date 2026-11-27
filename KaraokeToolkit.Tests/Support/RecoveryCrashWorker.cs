using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core;
using KaraokeToolkit.Core.Recipes;
using KaraokeToolkit.Core.Tagging;

namespace KaraokeToolkit.Tests.Support;

/// <summary>Runs disposable conversion/workflow children that pause at explicit recovery-test crash boundaries.</summary>
/// <remarks>Ready files signal the parent that the requested state is durable; the parent terminates the process
/// so normal disposal cannot clean it up. Uses generated fixtures, not user media.</remarks>
internal static class RecoveryCrashWorker
{
    // This runs in a disposable child process, never in the application. The parent test
    // terminates it without Dispose/finally so file handles and journals behave like a crash.
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--workflow-crash-worker")
        {
            string mode = args[1], root = args[2];
            var store = new RecoveryStore(args[3]);
            var pause = new PauseWorkflow(root, mode);
            if (mode is "tagging" or "tagging-item-only")
            {
                var service = new KaraokeTaggingService(store);
                var preview = await service.PreviewAsync([root], FilenameTagPattern.Default, false);
                preview = preview.Where(p => p.CanApply).Select(p => p with { Tags = p.Tags! with { Title = "Saved preview title" } }).ToArray();
                await service.ApplyAsync(preview, new(ClearComments: false), new(), pause,
                    continueBatchAfterRecovery: mode == "tagging");
            }
            else
            {
                var recipe = new RecipeDefinition { Name = "Crash continuation", Steps = mode == "moving"
                    ? [new() { OperationId = "FlattenOneLayer" }, new() { OperationId = "ValidateKaraoke" }]
                    : mode == "recipe-tagging"
                    ? [new() { OperationId = "TagKaraoke", Tagging = new(ClearComments: false), Options = new() { RecurseFolders = false } },
                       new() { OperationId = "AudioGToZip", Options = new() { DeleteOldFiles = false, AutoCreateCueText = false } }]
                    : [new() { OperationId = "ZipToAudioG", Options = new() { DeleteOldFiles = true, AutoCreateCueText = false } },
                       new() { OperationId = "AudioGToZip", Options = new() { DeleteOldFiles = true, AutoCreateCueText = false } }] };
                await new RecipeRunner(new RecipeStepExecutor(store), store).RunAsync(recipe,
                    mode is "moving" or "recipe-tagging" ? [root] : [Path.Combine(root, "song.zip")], pause);
            }
            return 0;
        }
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
        if (args.Length == 0 || args[0] != "--recovery-crash-worker")
            return await RunTestsAsync(args);
        if (args.Length != 3) return 2;
        string folder = args[1];
        await new ConversionService(new RecoveryStore(args[2])).RunAsync(
            new(OperationId.ZipToAudioG, folder,
                new()
                {
                    AutoCreateCueText = false,
                    DeleteOldFiles = false
                }),
            progress: new PauseBeforePublication(folder));
        return 0;
    }

    // Match xUnit's standard entry point after handling the project's worker commands.
    private static Task<int> RunTestsAsync(string[] args) =>
        args.Any(arg => arg is "--server" or "--internal-msbuild-node")
            ? Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions)
            : Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args);

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

    private sealed class PauseWorkflow(string folder, string mode) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value)
        {
            bool ready = mode switch
            {
                "tagging" or "tagging-item-only" => value.Completed == 1 && value.ActivityLabel == "Processing",
                "recipe-tagging" => value.Completed == 1 && value.ActivityLabel?.EndsWith("Processing", StringComparison.Ordinal) == true,
                "between" => value.Message == "Completed step 1/2.",
                "moving" => value.Message.Contains("planned items.", StringComparison.Ordinal),
                _ => value.Message.Contains("Verified outputs:", StringComparison.Ordinal)
            };
            if (!ready) return;
            File.WriteAllText(Path.Combine(folder, "workflow-crash-ready"), "ready");
            using var wait = new ManualResetEventSlim(); wait.Wait();
        }
    }
}
