using KaraokeToolkit.Core.Settings;

namespace KaraokeToolkit.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try { return await new CliApplication(profile: SharedSettingsStore.ForCurrentUser()).RunAsync(args, Console.Out, Console.Error, cancellation.Token); }
        finally { Console.CancelKeyPress -= handler; }
    }
}
