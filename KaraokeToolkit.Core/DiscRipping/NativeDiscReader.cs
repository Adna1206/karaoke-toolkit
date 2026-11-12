using System.Diagnostics;
using System.Text.RegularExpressions;
namespace KaraokeToolkit.Core.DiscRipping;

/// <summary>Cross-platform, out-of-process cdrdao reader. Each ripping batch owns
/// one full-disc image; only verified selected tracks reach the publishing pipeline.</summary>
public sealed class NativeDiscReader : IDiscReader, IDiscBatchReader
{
    private readonly Func<ProcessStartInfo, CancellationToken, Task<string>> run;
    private readonly Func<string> executable;
    private readonly string temporaryRoot;
    private Batch? batch;
    public NativeDiscReader(string? executablePath = null) : this(CdrdaoProcess.RunAsync,
        () => CdrdaoProcess.Resolve(AppContext.BaseDirectory, string.IsNullOrWhiteSpace(executablePath)
            ? Environment.GetEnvironmentVariable("KARAOKETOOLKIT_CDRDAO") : executablePath), Path.GetTempPath()) { }
    internal NativeDiscReader(Func<ProcessStartInfo, CancellationToken, Task<string>> run, Func<string> executable, string temporaryRoot)
    { this.run = run; this.executable = executable; this.temporaryRoot = temporaryRoot; }

    private async Task<string> Execute(string directory, CancellationToken token, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable()) { WorkingDirectory = directory };
        start.Environment["LC_ALL"] = "C";
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return await run(start, token);
    }
    public async Task<IReadOnlyList<OpticalDisc>> ScanAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        await using var scratch = new Batch(this, null);
        string output = await Execute(scratch.Directory, timeout.Token, "scanbus");
        var discs = new List<OpticalDisc>();
        var devices = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in output.Split('\n'))
        {
            // cdrdao prints discovery to stderr; its address may be a SCSI triple or device path.
            var match = line.TrimStart().StartsWith("IOService:", StringComparison.Ordinal)
                ? Regex.Match(line.Trim(), @"^(IOService:.+)\s+:\s+(.+,.+,.+)$")
                : Regex.Match(line.Trim(), @"^((?:(?:ATA|ATAPI):)?[0-9]+,[0-9]+,[0-9]+|/dev/[^\s]+)\s+(?::\s*)?(.+,.+,.+)$");
            if (!match.Success || !devices.Add(match.Groups[1].Value)) continue;
            string device = match.Groups[1].Value, name = match.Groups[2].Value.Trim();
            try
            {
                await Execute(scratch.Directory, timeout.Token, "read-toc", "--device", device, "--read-subchan", "rw_raw", "--datafile", "disc.bin", "disc.toc");
                var tracks = CdrdaoToc.Parse(await File.ReadAllTextAsync(Path.Combine(scratch.Directory, "disc.toc"), timeout.Token));
                discs.Add(new(discs.Count, name, true, CdrdaoToc.Identity(tracks), tracks.Select(t => t.AsDiscTrack()).ToArray()) { Device = device });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (error is IOException or InvalidOperationException or NotSupportedException)
            { discs.Add(new(discs.Count, name + " — " + error.Message, false, "", []) { Device = device }); }
            finally { File.Delete(Path.Combine(scratch.Directory, "disc.toc")); }
        }
        return discs;
    }
    IAsyncDisposable IDiscBatchReader.BeginBatch(OpticalDisc disc)
    {
        if (batch != null) throw new InvalidOperationException("A disc read is already active.");
        return batch = new Batch(this, disc);
    }
    public async Task ReadTrackAsync(OpticalDisc disc, DiscTrack track, string stagedBin, IProgress<int>? progress, CancellationToken token)
    {
        if (!disc.SupportsCdg || string.IsNullOrWhiteSpace(disc.Device) || disc.Device.StartsWith('-') || disc.Device.Any(char.IsControl))
            throw new NotSupportedException("Refresh and select a cdrdao-compatible audio/CD+G disc first.");
        if (!disc.Tracks.Contains(track)) throw new IOException("Selected track is unavailable.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        await using var local = batch == null ? new Batch(this, disc) : null;
        var current = batch ?? local!;
        if (current.Disc != disc) throw new IOException("Disc changed. Refresh and select tracks again.");
        if (current.Tracks == null)
        {
            progress?.Report(0);
            File.Delete(Path.Combine(current.Directory, "disc.bin"));
            File.Delete(Path.Combine(current.Directory, "disc.toc"));
            await Execute(current.Directory, timeout.Token, "read-cd", "--device", disc.Device, "--read-subchan", "rw_raw", "--datafile", "disc.bin", "disc.toc");
            var tracks = CdrdaoToc.Parse(await File.ReadAllTextAsync(Path.Combine(current.Directory, "disc.toc"), timeout.Token));
            if (CdrdaoToc.Identity(tracks) != disc.Identity) throw new IOException("Disc changed. Refresh and select tracks again.");
            long length = new FileInfo(Path.Combine(current.Directory, "disc.bin")).Length;
            if (length != checked(tracks[^1].Offset + tracks[^1].Sectors * 2448)) throw new InvalidDataException("Incomplete cdrdao disc image.");
            current.Tracks = tracks;
        }
        var selected = current.Tracks.Single(t => t.Number == track.Number);
        if (selected.AsDiscTrack() != track) throw new IOException("Disc track length changed.");
        await CdrdaoToc.ExtractAsync(Path.Combine(current.Directory, "disc.bin"), selected, stagedBin, progress, timeout.Token);
    }
    private sealed class Batch : IAsyncDisposable
    {
        private readonly NativeDiscReader owner;
        internal OpticalDisc? Disc { get; }
        internal string Directory { get; }
        internal CdrdaoToc.Track[]? Tracks { get; set; }
        internal Batch(NativeDiscReader owner, OpticalDisc? disc)
        {
            this.owner = owner; Disc = disc;
            Directory = Path.Combine(owner.temporaryRoot, "karaoke-cdrdao-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
        }
        public ValueTask DisposeAsync()
        {
            if (owner.batch == this) owner.batch = null;
            try { System.IO.Directory.Delete(Directory, true); }
            catch (IOException) { /* Preserve an abandoned image if cleanup is temporarily blocked. */ }
            catch (UnauthorizedAccessException) { }
            return ValueTask.CompletedTask;
        }
    }
}
