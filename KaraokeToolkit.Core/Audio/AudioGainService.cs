using System.Diagnostics;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Core.Audio;

internal sealed record AudioGainItem(string Source, string? SourceHash = null, string? Graphics = null,
    string? GraphicsHash = null, AudioGainMeasurement? Measurement = null, string? Error = null);

public sealed class AudioGainScan
{
    internal AudioGainScan(string[] inputs, AudioGainItem[] items) => (Inputs, Items) = (inputs, items);
    internal string[] Inputs { get; }
    internal AudioGainItem[] Items { get; }
}

public sealed class AudioGainPlan
{
    internal AudioGainPlan(string folder, OverwritePolicy policy, AudioGainOptions options, AudioKeyOptions? key,
        AudioGainItem[] items, AudioGainRow[] rows)
        => (Folder, Policy, Options, Key, Items, Rows) = (folder, policy, options, key, items, Array.AsReadOnly(rows));
    internal string Folder { get; }
    internal OverwritePolicy Policy { get; }
    internal AudioGainOptions Options { get; }
    internal AudioKeyOptions? Key { get; }
    internal AudioGainItem[] Items { get; }
    public IReadOnlyList<AudioGainRow> Rows { get; }
}

public sealed class AudioGainService(RecoveryStore? recovery = null)
{
    private static readonly string[] LooseAudioExtensions = [".mp3", ".m4a", ".aac", ".wav", ".flac", ".wma", ".ogg"];
    public static IReadOnlyList<string> Extensions { get; } = Array.AsReadOnly<string>([.. LooseAudioExtensions, ".zip"]);
    private readonly RecoveryStore recovery = recovery ?? new();
    private const RecoveryVerificationMode Verification = RecoveryVerificationMode.XxHash128;

    public async Task<AudioGainScan> ScanAsync(IReadOnlyList<string> inputs, bool recurse,
        IProgress<ConversionProgress>? progress = null, CancellationToken token = default)
    {
        if (inputs.Count == 0) throw new ArgumentException("Select at least one audio file, karaoke ZIP or folder.");
        string[] selection = inputs.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).Distinct(InputSelection.Paths).ToArray();
        var sources = new HashSet<string>(InputSelection.Paths);
        foreach (string path in selection)
        {
            token.ThrowIfCancellationRequested();
            CheckScope(path);
            if (Directory.Exists(path))
            {
                RunSession.CheckPlainPath(path);
                foreach (string folder in ConversionService.EnumerateFolders(path, recurse))
                {
                    token.ThrowIfCancellationRequested();
                    sources.UnionWith(Directory.EnumerateFiles(folder).Where(IsSupportedInput));
                }
            }
            else sources.Add(path);
        }
        var results = new List<AudioGainItem>();
        var clock = Stopwatch.StartNew();
        Report(progress, clock, sources.Count, 0, "Measuring audio…");
        foreach (string source in sources.Order(InputSelection.Paths))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!IsSupportedInput(source)) throw new NotSupportedException("Unsupported audio file extension.");
                string hash = await HashAsync(source, token);
                string? graphics = IsZip(source) ? null : FindGraphics(source);
                string? graphicsHash = graphics == null ? null : await HashAsync(graphics, token);
                var measurement = await MeasureSourceAsync(source, token);
                if (hash != await HashAsync(source, token)) throw new IOException("Audio changed while measuring.");
                results.Add(new(source, hash, graphics, graphicsHash, measurement));
            }
            catch (Exception error) when (IsItemError(error))
            {
                results.Add(new(source, Error: error.Message));
            }
            Report(progress, clock, sources.Count, results.Count, $"Measured {results.Count} of {sources.Count} files.");
        }
        return new(selection, results.ToArray());
    }

    private static string? FindGraphics(string source)
    {
        string[] companions = Directory.GetFiles(Path.GetDirectoryName(source)!).Where(path =>
            Path.GetExtension(path).Equals(".cdg", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileNameWithoutExtension(path).Equals(Path.GetFileNameWithoutExtension(source), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (companions.Length > 1) throw new InvalidDataException("Multiple matching CDG files.");
        return companions.SingleOrDefault();
    }

    private static async Task<AudioGainMeasurement> MeasureSourceAsync(string source, CancellationToken token)
    {
        var scratch = IsZip(source) ? Directory.CreateTempSubdirectory("KaraokeToolkitAudioGain-") : null;
        try
        {
            string audio = scratch == null ? source : (await AudioGainArchive.ExtractAudioAsync(source, scratch.FullName, token)).Path;
            await AudioGainMedia.GetEncodingAsync(audio, token);
            return await AudioGainMedia.MeasureAsync(audio, token);
        }
        finally { scratch?.Delete(recursive: true); }
    }

    public AudioGainPlan Plan(AudioGainScan scan, AudioGainOptions options, string outputFolder, OverwritePolicy policy)
    {
        options.Validate();
        return BuildPlan(scan, options, null, outputFolder, policy);
    }

    public AudioGainPlan PlanKey(AudioGainScan scan, AudioKeyOptions options, string outputFolder, OverwritePolicy policy)
    {
        options.Validate();
        return BuildPlan(scan, new(), options, outputFolder, policy);
    }

    private static AudioGainPlan BuildPlan(AudioGainScan scan, AudioGainOptions options, AudioKeyOptions? key,
        string outputFolder, OverwritePolicy policy)
    {
        if (policy is not (OverwritePolicy.Fail or OverwritePolicy.Skip or OverwritePolicy.Replace))
            throw new ArgumentException("Choose Fail, Skip or Replace for existing output copies.");
        if (string.IsNullOrWhiteSpace(outputFolder) || !Path.IsPathFullyQualified(outputFolder) || !Directory.Exists(outputFolder))
            throw new ArgumentException("Choose an existing absolute output folder.");
        string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputFolder));
        CheckScope(folder);
        RunSession.CheckPlainPath(folder);
        if (scan.Inputs.Any(p => Directory.Exists(p) && RunSession.Within(p, folder)) ||
            scan.Items.Any(item => InputSelection.Paths.Equals(Path.GetDirectoryName(item.Source), folder)))
            throw new ArgumentException("Choose an output folder outside the selected source folders.");

        var rows = scan.Items.Select(item => BuildRow(item, options, key, folder)).ToArray();
        var duplicates = rows.Where(CanApply).SelectMany(Outputs).GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            if (!CanApply(row)) continue;
            if (Outputs(row).Any(duplicates.Contains))
                rows[i] = row with { Status = AudioGainStatus.Blocked, Message = "Multiple inputs target the same output." };
            else if (Outputs(row).Any(Directory.Exists))
                rows[i] = row with { Status = AudioGainStatus.Blocked, Message = "An output path is an existing folder." };
            else if (policy != OverwritePolicy.Replace && Outputs(row).Any(File.Exists))
                rows[i] = row with { Status = policy == OverwritePolicy.Skip ? AudioGainStatus.Skipped : AudioGainStatus.Blocked,
                    Message = "An output file already exists." };
            else if (policy == OverwritePolicy.Replace && Outputs(row).Any(File.Exists))
                rows[i] = row with { Message = row.Message + " Existing output will be replaced." };
        }
        return new(folder, policy, options, key, scan.Items, rows);
    }

    internal static bool CanApply(AudioGainRow row) => row.Status is AudioGainStatus.Ready or AudioGainStatus.Limited;
    internal static string[] Outputs(AudioGainRow row) => row.Output == null ? [] :
        row.GraphicsOutput == null ? [row.Output] : [row.Output, row.GraphicsOutput];

    private static AudioGainRow BuildRow(AudioGainItem item, AudioGainOptions options, AudioKeyOptions? key, string folder)
    {
        if (item.Measurement is not { } measured)
            return new(item.Source, null, null, null, null, null, null, null, AudioGainStatus.Blocked, item.Error!);
        string audio = Path.Combine(folder, Path.GetFileName(item.Source));
        string? graphics = item.Graphics == null ? null : Path.Combine(folder, Path.GetFileName(item.Graphics));
        var row = new AudioGainRow(item.Source, audio, graphics, measured.Lufs, measured.PeakDbtp,
            null, null, null, AudioGainStatus.Skipped, "Audio has no measurable level for this mode.");
        if (key != null)
        {
            row = row with { Semitones = key.Semitones, SourceDuration = TimeSpan.FromMicroseconds(measured.DurationUs),
                CurrentLufs = null, CurrentPeakDbtp = null };
            if (key.Semitones == 0 || measured.PeakDbtp == null)
                return row with { Message = key.Semitones == 0 ? "Choose a non-zero key change." : "Audio has no measurable signal." };
            if (measured.Samples <= 0)
                return row with { Status = AudioGainStatus.Blocked, Message = "Could not measure the decoded audio length." };
            return row with { Status = AudioGainStatus.Ready, Message = "Ready." };
        }
        if (measured.PeakDbtp is not double peak || options.Mode == AudioGainMode.Loudness && measured.Lufs == null) return row;
        double requested = options.Mode switch
        {
            AudioGainMode.Loudness => options.TargetLufs - measured.Lufs!.Value,
            AudioGainMode.Peak => options.PeakCeilingDbtp - peak,
            _ => options.OffsetDb
        };
        double allowed = options.PeakCeilingDbtp - peak;
        if (options.Mode == AudioGainMode.Offset && requested > allowed + 0.00001)
            return row with { Message = "Requested adjustment exceeds the true-peak ceiling." };
        bool limited = options.Mode == AudioGainMode.Loudness && requested > allowed + 0.00001;
        double gain = limited ? allowed : requested;
        return row with
        {
            GainDb = gain, EstimatedLufs = measured.Lufs + gain, EstimatedPeakDbtp = peak + gain,
            Status = limited ? AudioGainStatus.Limited : AudioGainStatus.Ready,
            Message = limited ? "Limited by true-peak ceiling." : "Ready."
        };
    }

    public Task<ConversionResult> ApplyAsync(AudioGainPlan plan, IProgress<ConversionProgress>? progress = null,
        CancellationToken token = default) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        string[] roots = plan.Items.Where((_, i) => CanApply(plan.Rows[i])).Select(i => Path.GetDirectoryName(i.Source)!)
            .Prepend(plan.Folder).Distinct(InputSelection.Paths).ToArray();
        if (!plan.Rows.Any(CanApply))
            return new ConversionResult(plan.Rows.Select(Rejected).ToArray(), false) { Found = plan.Rows.Count };
        WorkflowMoves.CheckApprovedRecovery(recovery, roots, token);
        using var session = new WorkflowSession(recovery, new WorkflowData
        {
            Kind = RecoveryKind.AudioGain, OperationId = plan.Key == null ? OperationId.NormalizeAudioGain : OperationId.ChangeAudioKey,
            Roots = roots, VerificationMode = Verification, GainItems = plan.Items, GainRows = plan.Rows.ToArray(),
            GainOptions = plan.Options, KeyOptions = plan.Key, GainPolicy = plan.Policy
        });
        session.Enter();
        return await ApplySavedAsync(session, progress, token);
    }, token);

    public Task<ConversionResult> ResumeAsync(RecoveryJob job, IProgress<ConversionProgress>? progress = null,
        CancellationToken token = default) => Task.Run(async () =>
    {
        using var session = new WorkflowSession(recovery, job);
        if (session.Data.Kind != RecoveryKind.AudioGain) throw new IOException("This recovery job is not audio gain normalization.");
        session.Enter();
        session.RecoverTransaction();
        return await ApplySavedAsync(session, progress, token);
    }, token);

    private static async Task<ConversionResult> ApplySavedAsync(WorkflowSession session, IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        var data = session.Data;
        var clock = Stopwatch.StartNew();
        int total = data.GainRows!.Length;
        Report(progress, clock, total, data.GainResults.Count, data.KeyOptions == null ? "Applying audio gain…" : "Changing audio key…");
        for (int i = 0; i < total; i++)
        {
            var row = data.GainRows[i];
            if (data.GainResults.ContainsKey(row.Source)) continue;
            if (token.IsCancellationRequested) break;
            ConversionFileResult result;
            if (!CanApply(row)) result = Rejected(row);
            else
            {
                data.GainCurrent = row.Source;
                session.Save();
                Report(progress, clock, total, data.GainResults.Count, "Processing: " + row.Source, row.Source, "Processing");
                try
                {
                    result = await PublishAsync(session, i, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested && !session.HasPendingPublication) { break; }
                catch (OutputSkippedException) when (!session.HasPendingPublication)
                { result = new(row.Source, ConversionStatus.Skipped, [], "Existing output retained."); }
                catch (Exception error) when (IsItemError(error) && !session.HasPendingPublication)
                { result = new(row.Source, ConversionStatus.Failed, [], error.Message); }
                data.Workspace = null;
                data.GainCurrent = null;
            }
            data.GainResults[row.Source] = result;
            session.Save();
            if (result.Status == ConversionStatus.Succeeded)
                Report(progress, clock, total, data.GainResults.Count, $"Processed {data.GainResults.Count} of {total} files.");
            else Report(progress, clock, total, data.GainResults.Count, result.Message, row.Source, result.Status.ToString());
        }
        session.Finish();
        return new(data.GainRows.Where(r => data.GainResults.ContainsKey(r.Source)).Select(r => data.GainResults[r.Source]).ToArray(),
            token.IsCancellationRequested) { Found = total, Elapsed = clock.Elapsed };
    }

    private static async Task<ConversionFileResult> PublishAsync(WorkflowSession session, int index, CancellationToken token)
    {
        var data = session.Data;
        var item = data.GainItems![index];
        var row = data.GainRows![index];
        string folder = data.Roots[0];
        using var transaction = new FileTransaction(folder, Verification);
        string[] targets = Outputs(row);
        foreach (string output in targets)
            RunSession.CheckPlainPath(output);
        transaction.ObserveTargets(targets);
        RunSession.CheckPlainPath(item.Source);
        string staged = await transaction.CopyInputAsync(item.Source, token, item.SourceHash);
        string? graphics = null;
        if (item.Graphics != null)
        {
            RunSession.CheckPlainPath(item.Graphics);
            graphics = await transaction.CopyInputAsync(item.Graphics, token, item.GraphicsHash);
        }
        if (row.Semitones != null || Math.Abs(row.GainDb!.Value) > 0.00001)
        {
            var member = IsZip(staged) ? await AudioGainArchive.ExtractAudioAsync(staged, transaction.Workspace, token) : null;
            string audio = member?.Path ?? staged;
            string encoded = Path.Combine(transaction.Workspace, Guid.NewGuid().ToString("N") + Path.GetExtension(audio));
            if (row.Semitones is int semitones)
            {
                await AudioGainMedia.EncodeAsync(audio, encoded, 0, token, semitones, item.Measurement!.Samples);
                var actual = await AudioGainMedia.MeasureAsync(encoded, token);
                CheckDuration(actual, item.Measurement);
                if (actual.PeakDbtp == null) throw new InvalidDataException("The key change produced no measurable audio.");
            }
            else
            {
                var verified = await EncodeWithPeakCheckAsync(audio, encoded, row, data.GainOptions!, item.Measurement!, token);
                if (verified != row)
                {
                    row = verified;
                    data.GainRows[index] = row;
                    session.Save();
                }
            }
            if (member == null) File.Move(encoded, staged, overwrite: true);
            else
            {
                string rebuilt = Path.Combine(transaction.Workspace, Guid.NewGuid().ToString("N") + ".zip");
                await ArchiveZipConverter.ConvertAsync(staged, rebuilt, new(), token, repackZip: true,
                    verification: Verification, replacement: member with { Path = encoded });
                File.Move(rebuilt, staged, overwrite: true);
            }
        }
        var outputs = transaction.Commit(graphics == null ? [staged] : [staged, graphics], [], data.GainPolicy, token);
        return new(row.Source, ConversionStatus.Succeeded, outputs, GainMessage(row));
    }

    private static async Task<AudioGainRow> EncodeWithPeakCheckAsync(string input, string output, AudioGainRow row,
        AudioGainOptions options, AudioGainMeasurement source, CancellationToken token)
    {
        double gain = row.GainDb!.Value, ceiling = options.PeakCeilingDbtp;
        for (int attempt = 0; ; attempt++)
        {
            await AudioGainMedia.EncodeAsync(input, output, gain, token);
            var measured = await AudioGainMedia.MeasureAsync(output, token);
            CheckDuration(measured, source);
            // Allow for rounding in FFmpeg's reported true peak, while keeping full scale an absolute ceiling.
            if (measured.PeakDbtp is not double peak || peak <= Math.Min(ceiling + 0.05, 0))
                return gain == row.GainDb ? row : row with
                {
                    GainDb = gain, EstimatedLufs = measured.Lufs, EstimatedPeakDbtp = measured.PeakDbtp,
                    Status = AudioGainStatus.Limited, Message = "Reduced gain to keep the encoded output within the true-peak ceiling."
                };
            if (options.Mode == AudioGainMode.Offset || attempt == 2)
                throw new InvalidDataException("Encoded output exceeds the true-peak ceiling. Try a lower gain or target.");
            gain -= peak - ceiling + 0.1;
            File.Delete(output);
        }
    }

    private static void CheckDuration(AudioGainMeasurement actual, AudioGainMeasurement source)
    {
        if (Math.Abs(actual.DurationUs - source.DurationUs) > 80000)
            throw new InvalidDataException("Encoded audio duration differs from the source by more than 80 ms.");
    }

    internal static string GainMessage(AudioGainRow row)
    {
        if (row.Semitones is int semitones) return FormattableString.Invariant($"Key shift {semitones:+0;-0;0} semitones.");
        return FormattableString.Invariant(
            $"Gain {row.GainDb:+0.00;-0.00;0.00} dB. {(row.Status == AudioGainStatus.Limited ? row.Message : "Audio verified and published.")}");
    }
    private static ConversionFileResult Rejected(AudioGainRow row) =>
        new(row.Source, row.Status == AudioGainStatus.Blocked ? ConversionStatus.Failed : ConversionStatus.Skipped, [], row.Message);
    internal static bool IsLooseAudio(string path) => LooseAudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    internal static bool IsZip(string path) => Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase);
    private static bool IsSupportedInput(string path) => IsLooseAudio(path) || IsZip(path);
    private static void CheckScope(string path)
    {
        if (path.Split(Path.DirectorySeparatorChar).Any(InputSelection.IsExcludedFolder))
            throw new IOException("Recovery and Damaged folders cannot be used for audio gain: " + path);
    }
    private static bool IsItemError(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException
        or NotSupportedException or TimeoutException or ArgumentException or System.ComponentModel.Win32Exception or SharpCompress.Common.ArchiveException;
    private static Task<string> HashAsync(string path, CancellationToken token)
    {
        if (RunSession.CheckPlainPath(path) is not { } attributes || (attributes & FileAttributes.Directory) != 0)
            throw new IOException("Audio or companion file is missing: " + path);
        return RecoveryFingerprint.HashFileAsync(path, Verification, token);
    }

    private static void Report(IProgress<ConversionProgress>? progress, Stopwatch clock, int total, int completed,
        string message, string? source = null, string? label = null) =>
        progress?.Report(new(0, total == 0 ? 100 : completed * 100 / total, total, completed,
            message, clock.Elapsed.ToString(@"hh\:mm\:ss"))
            { ActivitySource = source, ActivityLabel = label, ActivityDetail = source == null ? null : message, ShowInActivity = source != null });
}
