using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;

namespace KaraokeToolkit.Core.Recovery;

internal static class AudioGainRecovery
{
    internal static void Validate(WorkflowData data)
    {
        bool key = data.OperationId == OperationId.ChangeAudioKey;
        if (data.OperationId is not (OperationId.NormalizeAudioGain or OperationId.ChangeAudioKey) || key != (data.KeyOptions != null) ||
            data.GainItems is not { Length: > 0 } items || data.GainRows is not { } rows || rows.Length != items.Length ||
            data.GainOptions == null || data.GainResults == null || data.VerificationMode != RecoveryVerificationMode.XxHash128 ||
            data.GainPolicy is not (OverwritePolicy.Fail or OverwritePolicy.Skip or OverwritePolicy.Replace))
            throw new IOException("Invalid audio gain checkpoint.");
        data.GainOptions.Validate();
        data.KeyOptions?.Validate();
        var sources = new HashSet<string>(InputSelection.Paths);
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string root = data.Roots[0];
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            var item = items[i];
            if (!Path.IsPathFullyQualified(item.Source) || !InputSelection.Paths.Equals(row.Source, item.Source) || !sources.Add(item.Source))
                throw new IOException("Invalid saved audio gain source.");
            if (!AudioGainService.CanApply(row)) continue;
            string? folder = Path.GetDirectoryName(item.Source);
            if (folder == null || InputSelection.Paths.Equals(folder, root) || !data.Roots.Contains(folder, InputSelection.Paths) ||
                !AudioGainService.Extensions.Contains(Path.GetExtension(item.Source), StringComparer.OrdinalIgnoreCase) ||
                !RecoveryFingerprint.IsValid(item.SourceHash ?? "", data.VerificationMode) || item.Measurement is not { DurationUs: > 0 } ||
                (key ? item.Measurement.Samples <= 0 || row.Semitones is null or 0 || row.Semitones != data.KeyOptions!.Semitones ||
                    row.GainDb != null :
                    row.Semitones != null || row.GainDb is not double gain || !double.IsFinite(gain)) ||
                !InputSelection.Paths.Equals(row.Output, Path.Combine(root, Path.GetFileName(item.Source))))
                throw new IOException("Invalid saved audio gain output.");
            if (item.Graphics == null ? row.GraphicsOutput != null : AudioGainService.IsZip(item.Source) ||
                !InputSelection.Paths.Equals(Path.GetDirectoryName(item.Graphics), folder) ||
                !Path.GetExtension(item.Graphics).Equals(".cdg", StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileNameWithoutExtension(item.Source).Equals(Path.GetFileNameWithoutExtension(item.Graphics), StringComparison.OrdinalIgnoreCase) ||
                !RecoveryFingerprint.IsValid(item.GraphicsHash ?? "", data.VerificationMode) ||
                !InputSelection.Paths.Equals(row.GraphicsOutput, Path.Combine(root, Path.GetFileName(item.Graphics))))
                throw new IOException("Invalid saved audio gain companion.");
            if (AudioGainService.Outputs(row).Any(output => !destinations.Add(output)))
                throw new IOException("Duplicate saved audio gain output.");
        }
        if (data.GainResults.Any(pair => !sources.Contains(pair.Key) || !InputSelection.Paths.Equals(pair.Key, pair.Value.Source) ||
                pair.Value.Outputs.Any(path => !destinations.Contains(path))) ||
            data.GainCurrent != null && !rows.Any(row => AudioGainService.CanApply(row) && InputSelection.Paths.Equals(row.Source, data.GainCurrent)) ||
            data.Workspace != null && (data.GainCurrent == null || !InputSelection.Paths.Equals(Path.GetDirectoryName(data.Workspace), root)))
            throw new IOException("Invalid audio gain publication checkpoint.");
        data.GainResults = new(data.GainResults, InputSelection.Paths);
    }

    internal static void ValidateTransaction(WorkflowData data, TransactionJournal journal)
    {
        var row = data.GainRows!.Single(r => InputSelection.Paths.Equals(r.Source, data.GainCurrent));
        var names = AudioGainService.Outputs(row).Select(Path.GetFileName).ToArray();
        if (journal.Outputs == null || !journal.Outputs.Select(o => o?.Name).SequenceEqual(names, InputSelection.Paths) ||
            journal.Backups == null || journal.Backups.Any(b => b == null || !names.Contains(b.Name, InputSelection.Paths)) ||
            data.GainPolicy != OverwritePolicy.Replace && journal.Backups.Length != 0)
            throw new IOException("Interrupted publication differs from the saved audio gain plan.");
    }
}
