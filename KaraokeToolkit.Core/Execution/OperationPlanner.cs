using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Reporting;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Video;
using SharpCompress.Archives.Zip;

namespace KaraokeToolkit.Core.Execution;

/// <summary>Read-only scope/output planning. A plan is a snapshot, not an integrity or encoding guarantee.</summary>
public sealed class OperationPlanner
{
    /// <summary>Inspects operation scope, proposed destinations and conflicts without publishing changes.</summary>
    /// <remarks>Reads current inputs; it neither reserves paths nor guarantees that later execution will succeed.</remarks>
    public async Task<ExecutionResult> PlanAsync(OperationRequest request, CancellationToken token = default)
    {
        if (request.OperationId == OperationId.GroupByTrackId)
            return TrackGroupingExecution.Preview(request, token);
        var operation = OperationCatalog.Get(request.OperationId);
        if (!operation.CanRunUnattended) throw new NotSupportedException("This operation has no file plan.");
        if (request.Inputs.Count == 0) throw new ArgumentException("Select at least one input.");
        token.ThrowIfCancellationRequested();
        if (!string.IsNullOrEmpty(request.OutputFolder) && request.Inputs.Select(Path.GetFullPath).Any(p => Directory.Exists(p) && RunSession.Within(p, request.OutputFolder)))
            throw new ArgumentException("The output folder must be outside selected input folders.");
        if (request.OperationId == OperationId.QuarantineInvalid)
            return await new OperationExecutionService().QuarantineAsync(request.Inputs, request.Options.RecurseFolders, token: token, dryRun: true);
        var entries = new List<ReportEntry>();
        ConversionResult? moves = null;
        if (request.OperationId is OperationId.OrganizeFiles or OperationId.OrganizeFolders)
            moves = await new AlphabeticalOrganizer().PreviewAsync(request.Inputs,
                request.OperationId == OperationId.OrganizeFiles ? AlphabeticalOrganizeScope.Files : AlphabeticalOrganizeScope.Folders, token, request.IgnoreLeadingThe);
        else if (request.OperationId is OperationId.FlattenOneLayer or OperationId.FlattenAllLayers)
            moves = await new FlattenFolderLayer().PreviewAsync(request.Inputs,
                request.OperationId == OperationId.FlattenOneLayer ? FolderFlattenScope.OneLayer : FolderFlattenScope.AllLayers, token);
        else if (request.OperationId is OperationId.TagKaraoke or OperationId.PreviewTags or OperationId.ValidateKaraoke)
            return await new OperationExecutionService().ExecuteAsync(request with
            {
                OperationId = request.OperationId == OperationId.TagKaraoke ? OperationId.PreviewTags : request.OperationId
            }, token: token);
        else if (request.OperationId == OperationId.RestoreQuarantined)
            foreach (string path in request.Inputs)
                entries.Add(new(path, "Planned", "Restore quarantined group", new QuarantineService().PreviewRestore(path)));
        else
        {
            new ConversionRequest(request.OperationId, ".", request.Options) { OutputFolder = request.OutputFolder }.Validate();
            var plans = InputSelection.Plan(request.OperationId, request.Inputs, request.Options.RecurseFolders);
            if (!string.IsNullOrEmpty(request.OutputFolder) && request.Options.AutoCreateCueText && plans.Count > 1 &&
                request.OperationId is not (OperationId.ArchiveToZip or OperationId.RepackZip or OperationId.KaraokeToVideo))
                throw new ArgumentException("Multiple source folders sharing one output folder require --no-cue-text. Generate one listing afterward with create-cue-and-text-files.");
            foreach (var batch in plans)
            foreach (string source in batch.Sources)
            {
                token.ThrowIfCancellationRequested();
                if (request.OperationId == OperationId.CleanUpFilenames)
                {
                    var renamed = await FilenameCleanup.RunAsync(source, token, preview: true);
                    entries.Add(new(source, "Planned", renamed.Message, renamed.Outputs)); continue;
                }
                string destination = string.IsNullOrEmpty(request.OutputFolder) ? batch.Folder : request.OutputFolder;
                string stem = Path.Combine(destination, Path.GetFileNameWithoutExtension(source));
                string[] outputs = request.OperationId switch
                {
                    OperationId.AudioGToBin or OperationId.ZipToBin => [stem + ".bin"],
                    OperationId.AudioGToZip or OperationId.BinToZip or OperationId.ArchiveToZip => [stem + ".zip"],
                    OperationId.RepackZip => [request.Options.DeleteOldFiles ? source : stem + " (repacked).zip"],
                    OperationId.BinToAudioG => [stem + AudioEncoderService.Extension(request.Options.AudioEncoder), stem + ".cdg"],
                    OperationId.LosslessGToMp3G => [stem + ".mp3", string.IsNullOrEmpty(request.OutputFolder) ? source : stem + ".cdg"],
                    OperationId.ZipToAudioG => await ZipOutputsAsync(source, stem, token),
                    OperationId.KaraokeToVideo => [stem + VideoOptions.Extension(request.Options.VideoContainer)],
                    OperationId.CreateCueAndTextFiles => MetadataOutputs(source, request.Options),
                    _ => throw new NotSupportedException("No conversion plan is registered.")
                };
                string message = request.Options.DeleteOldFiles && request.OperationId != OperationId.KaraokeToVideo
                    ? "Publish verified outputs, then remove replaced source media." : "Publish outputs; preserve source media.";
                if (request.Options.AutoCreateCueText) message += " CUE/TXT may also be generated where applicable.";
                entries.Add(new(source, "Planned", message + " Encoding/content checks occur during execution.", outputs));
            }
        }
        if (moves != null) entries.AddRange(moves.Files.Select(f => new ReportEntry(f.Source, f.Status == ConversionStatus.Failed ? "Blocked" : f.Status == ConversionStatus.Skipped ? "Skipped" : "Planned", f.Message, f.Outputs)));
        var duplicates = entries.SelectMany(e => e.Outputs.Where(p => !InputSelection.Paths.Equals(p, e.Source)))
            .GroupBy(p => p, InputSelection.Paths).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(InputSelection.Paths);
        entries = entries.Select(e =>
        {
            bool exists = request.OperationId != OperationId.CleanUpFilenames && e.Outputs.Any(p => !InputSelection.Paths.Equals(p, e.Source) && (File.Exists(p) || Directory.Exists(p)));
            if (e.Outputs.Any(duplicates.Contains)) return e with { Status = "Blocked", Message = "Multiple inputs target the same output." };
            if (exists) return e with { Status = request.OverwritePolicy == OverwritePolicy.Fail ? "Blocked" : "Planned", Message = e.Message + " Existing outputs: " + request.OverwritePolicy + "." };
            return e;
        }).ToList();
        return new(new(operation.Identifier, DateTimeOffset.Now, false, entries) { ReportType = "Dry Run Report" }, entries.Count, entries.Any(e => e.Status == "Blocked"));
    }

    private static async Task<string[]> ZipOutputsAsync(string source, string stem, CancellationToken token)
    {
        await using var archive = await ZipArchive.OpenAsyncArchive(source, cancellationToken: token);
        var audio = await archive.EntriesAsync.Where(e => !e.IsDirectory &&
            KaraokeArchive.AudioExtensions.Contains(Path.GetExtension(e.Key!), StringComparer.OrdinalIgnoreCase)).ToArrayAsync(token);
        if (audio.Length != 1) throw new InvalidDataException("Expected one audio member in the ZIP.");
        return [stem + Path.GetExtension(audio[0].Key!).ToLowerInvariant(), stem + ".cdg"];
    }

    private static string[] MetadataOutputs(string folder, ConversionOptions options)
    {
        var extensions = Directory.GetFiles(folder).Select(p => Path.GetExtension(p).ToLowerInvariant()).ToArray();
        if (!extensions.Any(e => e is ".bin" or ".cdg" or ".zip")) return [];
        string name = MetadataFileNames.FolderName(folder);
        string text = Path.Combine(folder, MetadataFileNames.Resolve(options.TrackListingName, name, ".txt"));
        return extensions.Contains(".bin")
            ? [Path.Combine(folder, MetadataFileNames.Resolve(options.CueSheetName, name, ".cue")), text] : [text];
    }
}
