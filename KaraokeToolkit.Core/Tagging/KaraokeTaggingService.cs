using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Globalization;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;
using SharpCompress.Archives.Zip;
using SharpCompress.Writers.Zip;

namespace KaraokeToolkit.Core.Tagging;

/// <summary>Previews filename tags, then updates staged copies without re-encoding audio or graphics.</summary>
public sealed class KaraokeTaggingService(RecoveryStore? recovery = null)
{
    private readonly RecoveryStore recovery = recovery ?? new();
    public static readonly string[] MediaExtensions = [".mp3", ".ogg", ".opus", ".wav", ".flac", ".ape", ".wma", ".aif", ".aiff", ".m4a", ".wv", ".mp4", ".mkv", ".avi", ".webm"];
    public static readonly string[] InputExtensions = [.. MediaExtensions, ".zip", ".cdg", ".bin"];

    /// <summary>Finishes a previous interrupted tag publication before a fresh preview is made.
    /// Only tagging workspaces are handled; conversion journals retain their normal recovery UI.</summary>
    public Task<int> RecoverInterruptedPublicationsAsync(IEnumerable<string> inputs, bool recurse, CancellationToken token = default)
        => Task.Run(() =>
        {
            int recovered = 0;
            var folders = new HashSet<string>(InputSelection.Paths);
            foreach (string path in inputs.Select(Path.GetFullPath))
                foreach (string folder in ConversionService.EnumerateFolders(Directory.Exists(path) ? path : Path.GetDirectoryName(path)!, Directory.Exists(path) && recurse)) folders.Add(folder);
            foreach (string folder in folders)
            foreach (string workspace in Directory.GetDirectories(folder, ".karaoke-job-*"))
            {
                token.ThrowIfCancellationRequested();
                // Verify the absolute deletion target is an immediate workspace in the selected folder.
                if (!InputSelection.Paths.Equals(Path.GetDirectoryName(Path.GetFullPath(workspace)), Path.GetFullPath(folder)) ||
                    !File.Exists(Path.Combine(workspace, "tagging.json"))) continue;
                RunSession.CheckPlainPath(workspace);
                _ = FileTransactionRecovery.Recover(workspace); // Takes the folder lease; rejects live/changed files.
                Directory.Delete(workspace, true);
                recovered++;
            }
            return recovered;
        }, token);

    public static string[] NormalizeInputs(IEnumerable<string> inputs)
    {
        var paths = inputs.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).Distinct(InputSelection.Paths).ToArray();
        if (paths.Length == 0) throw new ArgumentException("Select files or folders to tag.");
        foreach (string path in paths)
        {
            RunSession.CheckPlainPath(path);
            if (!Directory.Exists(path) && (!File.Exists(path) || !InputExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
                throw new ArgumentException("Select supported media, ZIP, CDG or BIN files: " + path);
            if (path.Split(Path.DirectorySeparatorChar).Any(InputSelection.IsExcludedFolder))
                throw new IOException("Recovery and Damaged folders cannot be tagged.");
        }
        return paths;
    }

    /// <summary>Parses filename-derived tags and reads input fingerprints without modifying media.</summary>
    /// <remarks>Returns applicability/errors per candidate. The preview is a snapshot, not a reservation;
    /// application must still verify that its inputs have not changed.</remarks>
    public Task<IReadOnlyList<TaggingPreview>> PreviewAsync(IEnumerable<string> inputs, string pattern, bool recurse, CancellationToken token = default)
        => PreviewAsync(inputs, pattern, recurse, token, null);

    public Task<IReadOnlyList<TaggingPreview>> PreviewAsync(IEnumerable<string> inputs, string pattern, bool recurse,
        CancellationToken token, Action<int, int>? report)
        => ReadPreviewAsync(inputs, pattern, recurse, token, report);

    public Task<IReadOnlyList<TaggingPreview>> ReadPreviewAsync(IEnumerable<string> inputs, bool recurse,
        CancellationToken token, Action<int, int>? report)
        => ReadPreviewAsync(inputs, null, recurse, token, report);

    private static Task<IReadOnlyList<TaggingPreview>> ReadPreviewAsync(IEnumerable<string> inputs, string? pattern, bool recurse,
        CancellationToken token, Action<int, int>? report)
        => Task.Run(async () =>
        {
            var template = pattern == null ? null : new FilenameTagPattern(pattern);
            var files = new HashSet<string>(InputSelection.Paths);
            foreach (string path in NormalizeInputs(inputs))
                if (Directory.Exists(path))
                {
                    foreach (string folder in ConversionService.EnumerateFolders(path, recurse))
                    foreach (string file in Directory.GetFiles(folder).Where(p => InputExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))) files.Add(file);
                }
                else files.Add(path);
            // Selecting the CDG selects its accompanying audio, but never causes duplicate writes.
            foreach (string cdg in files.Where(p => Path.GetExtension(p).Equals(".cdg", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                var audio = Directory.GetFiles(Path.GetDirectoryName(cdg)!).Where(p => MediaExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)
                    && Path.GetFileNameWithoutExtension(p).Equals(Path.GetFileNameWithoutExtension(cdg), StringComparison.OrdinalIgnoreCase)).ToArray();
                if (audio.Length == 1) { files.Remove(cdg); files.Add(audio[0]); }
            }
            var result = new List<TaggingPreview>();
            report?.Invoke(0, files.Count);
            foreach (string path in files.Order(InputSelection.Paths))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    RunSession.CheckPlainPath(path);
                    string hash = RecoveryFingerprint.Hash(path);
                    string extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension is ".bin" or ".cdg") throw new NotSupportedException("BIN and CDG have no standard metadata fields. Tag their audio or a converted media file instead.");
                    var tags = template?.Parse(Path.GetFileNameWithoutExtension(path));
                    KaraokeTags existing;
                    if (extension == ".zip")
                    {
                        string temporary = Path.Combine(Path.GetTempPath(), "KaraokeToolkitTagging", Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(temporary);
                        try
                        {
                            string copy = Path.Combine(temporary, Path.GetFileName(path));
                            File.Copy(path, copy);
                            var pair = await KaraokeArchive.ExtractAsync(copy, token);
                            existing = ReadTags(pair.Audio);
                        }
                        finally { Directory.Delete(temporary, true); }
                    }
                    else existing = ReadTags(path);
                    if (hash != RecoveryFingerprint.Hash(path)) throw new IOException("Source changed while previewing.");
                    result.Add(new(path, tags, existing, null, hash));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                { result.Add(new(path, null, null, error.Message, null)); }
                report?.Invoke(result.Count, files.Count);
            }
            return (IReadOnlyList<TaggingPreview>)result;
        }, token);

    public static IEnumerable<TaggingPreview> PlanPreview(IReadOnlyList<TaggingPreview> sources, string pattern)
    {
        var template = new FilenameTagPattern(pattern);
        foreach (var source in sources)
        {
            if (source.Error != null)
            {
                yield return source;
                continue;
            }
            TaggingPreview row;
            try { row = source with { Tags = template.Parse(Path.GetFileNameWithoutExtension(source.Path)) }; }
            catch (Exception error) when (error is FormatException or ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException)
            { row = source with { Error = error.Message }; }
            yield return row;
        }
    }

    /// <summary>Checkpoints the supplied preview and publishes verified tag updates from staged copies.</summary>
    /// <remarks>Checks saved fingerprints before writing and verifies media payload preservation.
    /// Completed files remain published if a later file fails or the run is cancelled.</remarks>
    public Task<ConversionResult> ApplyAsync(IReadOnlyList<TaggingPreview> preview, TaggingOptions options,
        ConversionOptions processing, IProgress<ConversionProgress>? progress = null, CancellationToken token = default,
        bool continueBatchAfterRecovery = true,
        RecoveryVerificationMode verificationMode = OperationCapabilities.DefaultFileWriteRecoveryVerification)
        => Task.Run(async () =>
        {
            if (!Enum.IsDefined(verificationMode)) throw new ArgumentOutOfRangeException(nameof(verificationMode));
            if (preview.Count == 0 || token.IsCancellationRequested) return new ConversionResult([], token.IsCancellationRequested);
            var roots = preview.Select(p => Path.GetDirectoryName(Path.GetFullPath(p.Path))!).Distinct(InputSelection.Paths).ToArray();
            string? workspaceRoot = roots.FirstOrDefault(WorkflowMoves.IsAvailableFolder);
            if (workspaceRoot == null)
                return new ConversionResult(preview.Select(item => new ConversionFileResult(item.Path,
                    item.CanApply ? ConversionStatus.Failed : ConversionStatus.Skipped, [], item.Error ?? "Source folder is unavailable.")).ToArray(),
                    false) { Found = preview.Count };
            WorkflowMoves.CheckApprovedRecovery(recovery, roots, token);
            using var session = new WorkflowSession(recovery, new WorkflowData
            {
                Kind = RecoveryKind.Tagging, Roots = roots.Where(root => !InputSelection.Paths.Equals(root, workspaceRoot)).Prepend(workspaceRoot).ToArray(),
                Inputs = preview.Select(p => p.Path).ToArray(),
                SaveResumableRun = continueBatchAfterRecovery, VerificationMode = verificationMode,
                TagFiles = preview.ToArray(), TagOptions = options, Processing = processing
            });
            return await ApplyCoreAsync(session, progress, token);
        });

    /// <summary>Resolves pending tag publication and continues the saved preview, retaining completed file results.</summary>
    public Task<ConversionResult> ResumeAsync(RecoveryJob job, IProgress<ConversionProgress>? progress = null, CancellationToken token = default)
        => Task.Run(async () =>
        {
            using var session = new WorkflowSession(recovery, job);
            if (session.Data.Kind != RecoveryKind.Tagging) throw new IOException("This recovery job is not tagging.");
            token.ThrowIfCancellationRequested();
            session.Enter(); session.RecoverTransaction();
            if (!session.Data.SaveResumableRun)
            {
                var results = session.Data.TagResults.Values.Concat(session.Data.TagFiles
                    .Where(item => !session.Data.TagResults.ContainsKey(item.Path))
                    .Select(item => new ConversionFileResult(item.Path, ConversionStatus.Skipped, [], "Left for a new run after recovery.")))
                    .ToArray();
                session.Finish();
                return new ConversionResult(results, false) { Found = session.Data.TagFiles.Length };
            }
            session.VerifyExpected();
            return await ApplyCoreAsync(session, progress, token);
        });

    /// <summary>Uses the recipe's checkpoint so tagging never creates a competing recovery prompt.
    /// The generated preview is frozen before the first write and reused on recovery.</summary>
    internal async Task<ConversionResult> ApplyRecipeStepAsync(IReadOnlyList<string> inputs, string pattern, TaggingOptions options,
        ConversionOptions processing, IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        var session = WorkflowSession.Current.Value;
        if (session == null)
            return await ApplyAsync(await PreviewAsync(inputs, pattern, processing.RecurseFolders, token), options, processing, progress, token);
        if (!session.Data.TagStepStarted)
        {
            session.Data.TagFiles = (await PreviewAsync(inputs, pattern, processing.RecurseFolders, token)).ToArray();
            session.Data.TagOptions = options; session.Data.Processing = processing;
            session.Data.TagStepStarted = true; session.Save();
        }
        return await ApplyCoreAsync(session, progress, token, finish: false);
    }

    private async Task<ConversionResult> ApplyCoreAsync(WorkflowSession session, IProgress<ConversionProgress>? progress, CancellationToken token, bool finish = true)
    {
            session.Enter();
            var preview = session.Data.TagFiles;
            var options = session.Data.TagOptions;
            var processing = session.Data.Processing;
            var clock = Stopwatch.StartNew();
            var results = session.Data.TagResults.Values.ToList();
            foreach (var item in preview)
            {
                if (session.Data.TagResults.ContainsKey(item.Path)) continue;
                if (token.IsCancellationRequested) break;
                session.Data.TagCurrent = item.Path; session.Data.Workspace = null; session.Save();
                if (!item.CanApply)
                {
                    // An unattended recipe must report a pattern/media error as a failure.
                    // BIN and orphan graphics have no tag fields and remain explicitly skipped.
                    bool recipeError = session.Data.TagStepStarted && Path.GetExtension(item.Path).ToLowerInvariant() is not (".bin" or ".cdg");
                    var skipped = new ConversionFileResult(item.Path, recipeError ? ConversionStatus.Failed : ConversionStatus.Skipped, [], item.Error ?? "Not selected for tagging.");
                    results.Add(skipped); session.Data.TagResults[item.Path] = skipped; session.Save(); continue;
                }
                try
                {
                    string folder = Path.GetDirectoryName(item.Path)!;
                    if (RunSession.CheckPlainPath(item.Path) is not { } attributes || (attributes & FileAttributes.Directory) != 0)
                        throw new IOException("Tag source is missing or no longer a file: " + item.Path);
                    if (recovery.Find(folder, false).Count > 0) throw new IOException("Recover the interrupted run before tagging this folder.");
                    using var transaction = new FileTransaction(folder, session.Data.VerificationMode);
                    DurableJson.Write(Path.Combine(transaction.Workspace, "tagging.json"), new { Version = 1 });
                    if (RecoveryFingerprint.Hash(item.Path) != item.SourceHash) throw new IOException("Source changed after preview. Refresh the preview before tagging.");
                    string staged = await transaction.CopyInputAsync(item.Path, token);
                    if (RecoveryFingerprint.Hash(staged) != item.SourceHash) throw new IOException("Source changed while staging.");
                    if (Path.GetExtension(staged).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                        await TagZipAsync(staged, item.Tags!, options, processing, token);
                    else await TagMediaAsync(staged, item.Tags!, options, token);
                    token.ThrowIfCancellationRequested();
                    var outputs = transaction.Commit([staged], [item.Path], OverwritePolicy.Replace, token);
                    results.Add(new(item.Path, ConversionStatus.Succeeded, outputs, "Tags updated in place; media was not re-encoded."));
                }
                catch (OperationCanceledException) { break; }
                catch (Exception error)
                {
                    if (session.HasPendingPublication) throw new IOException("Tagging publication needs recovery; journal and originals retained.", error);
                    results.Add(new(item.Path, ConversionStatus.Failed, [], error.Message));
                }
                session.Data.TagResults[item.Path] = results[^1]; session.Save();
                progress?.Report(new(100, results.Count * 100 / Math.Max(1, preview.Length), preview.Length, results.Count,
                    "Processing: " + item.Path, clock.Elapsed.ToString(@"hh\:mm\:ss"))
                    { ActivitySource = item.Path, ActivityLabel = "Processing" });
            }
            if (finish) session.Finish();
            return new ConversionResult(results, token.IsCancellationRequested) { Found = preview.Length, Elapsed = clock.Elapsed };
    }

    internal static KaraokeTags ReadTags(string path)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is ".mkv" or ".webm")
            return ReadMatroskaTagsAsync(path).GetAwaiter().GetResult();
        using var media = TagLib.File.Create(path);
        if (media.PossiblyCorrupt) throw new InvalidDataException("Media container is damaged: " + string.Join("; ", media.CorruptionReasons));
        return new(media.Tag.Album, media.Tag.Track == 0 ? null : media.Tag.Track, media.Tag.FirstPerformer, media.Tag.Title);
    }

    private static async Task<KaraokeTags> ReadMatroskaTagsAsync(string path)
    {
        string output = await RunToolAsync("ffprobe", ["-v", "error", "-show_entries", "format_tags", "-of", "json", path], Path.GetDirectoryName(path)!, default);
        using var document = JsonDocument.Parse(output);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("tags", out var tags))
            foreach (var field in tags.EnumerateObject()) values[field.Name] = field.Value.GetString() ?? "";
        string? Value(string name) => values.TryGetValue(name, out string? value) && value.Length > 0 ? value : null;
        uint? track = uint.TryParse(Value("track")?.Split('/')[0], NumberStyles.None, CultureInfo.InvariantCulture, out uint number) ? number : null;
        return new(Value("album"), track, Value("artist"), Value("title"));
    }

    private static KaraokeTags Merge(KaraokeTags desired, KaraokeTags current, bool fill)
        => new(desired.Album == null || fill && !string.IsNullOrEmpty(current.Album) ? current.Album : desired.Album,
            desired.Track == null || fill && current.Track != null ? current.Track : desired.Track,
            desired.Artist == null || fill && !string.IsNullOrEmpty(current.Artist) ? current.Artist : desired.Artist,
            desired.Title == null || fill && !string.IsNullOrEmpty(current.Title) ? current.Title : desired.Title);

    private static async Task TagMediaAsync(string path, KaraokeTags tags, TaggingOptions options, CancellationToken token)
    {
        var expected = Merge(tags, ReadTags(path), options.FillEmptyOnly);
        // Compare compressed packet payloads, rather than whole-file hashes that change with tags.
        string before = await PacketHashAsync(path, token);
        if (Path.GetExtension(path).Equals(".mkv", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".webm", StringComparison.OrdinalIgnoreCase))
        {
            // The released TagLibSharp reads Matroska but cannot save it. Stream-copy with FFmpeg.
            string output = Path.Combine(Path.GetDirectoryName(path)!, "tagged-" + Guid.NewGuid().ToString("N") + Path.GetExtension(path));
            var arguments = new List<string> { "-nostdin", "-v", "error", "-i", path, "-map", "0", "-c", "copy" };
            void Metadata(string name, string? value) { if (value != null) arguments.AddRange(["-metadata", name + "=" + value]); }
            if (options.ClearOtherDescriptiveTags)
            {
                // Remove descriptive fields explicitly; keep language, chapters, attachments
                // and technical stream metadata instead of indiscriminately dropping all tags.
                foreach (string name in new[] { "genre", "date", "year", "lyrics", "composer", "conductor", "copyright", "grouping",
                    "album_artist", "albumartist", "tracktotal", "totaltracks", "disc", "disctotal", "totaldiscs", "bpm", "title-sort", "album-sort", "artist-sort",
                    "titlesort", "albumsort", "artistsort", "albumartistsort", "composersort" })
                { Metadata(name, ""); arguments.AddRange(["-metadata:s", name + "="]); }
            }
            Metadata("album", expected.Album); Metadata("track", expected.Track?.ToString(CultureInfo.InvariantCulture)); Metadata("artist", expected.Artist); Metadata("title", expected.Title);
            if (options.ClearComments) { Metadata("comment", ""); arguments.AddRange(["-metadata:s", "comment="]); }
            arguments.Add(output);
            await RunToolAsync("ffmpeg", arguments, Path.GetDirectoryName(path)!, token);
            File.Move(output, path, true);
        }
        else
        {
            using var media = TagLib.File.Create(path);
            if (!media.Writeable) throw new NotSupportedException("This media container does not support tag writing.");
            var tag = media.Tag;
            // Do not call Clear(): encoder delay, artwork and technical frames must survive.
            if (options.ClearOtherDescriptiveTags)
            {
                tag.Genres = []; tag.Year = 0; tag.Lyrics = null; tag.Composers = []; tag.Conductor = null;
                tag.Copyright = null; tag.Grouping = null; tag.AlbumArtists = []; tag.TrackCount = 0;
                tag.Disc = 0; tag.DiscCount = 0; tag.BeatsPerMinute = 0;
                tag.TitleSort = null; tag.AlbumSort = null; tag.PerformersSort = []; tag.AlbumArtistsSort = []; tag.ComposersSort = [];
            }
            if (options.ClearComments) tag.Comment = null;
            tag.Album = expected.Album; tag.Track = expected.Track ?? 0;
            if (tags.Artist != null && (!options.FillEmptyOnly || string.IsNullOrEmpty(tag.FirstPerformer)))
                tag.Performers = [tags.Artist];
            tag.Title = expected.Title;
            media.Save();
        }
        var actual = ReadTags(path);
        // TagLib uses zero for an unset track; several containers omit the field when
        // assigned zero. Accept that representation without confusing zero with a failed write.
        if (actual with { Track = actual.Track ?? 0 } != expected with { Track = expected.Track ?? 0 })
            throw new NotSupportedException("This container could not retain the requested Album, Track, Artist and Title tags. Original kept.");
        if (before != await PacketHashAsync(path, token)) throw new InvalidDataException("Tagging changed the media packet payloads. Original kept.");
    }

    private static async Task TagZipAsync(string path, KaraokeTags tags, TaggingOptions options, ConversionOptions processing, CancellationToken token)
    {
        string folder = Path.GetDirectoryName(path)!;
        // Keep names and paths from the original archive; the external ZIP basename supplies the tags.
        string[] names;
        await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true))
        await using (var archive = await ZipArchive.OpenAsyncArchive(input, cancellationToken: token))
            names = await archive.EntriesAsync.Where(e => !e.IsDirectory).Select(e => e.Key!).ToArrayAsync(token);
        var pair = await KaraokeArchive.ExtractAsync(path, token);
        string graphicsHash = await RecoveryFingerprint.HashFileAsync(pair.Graphics, RecoveryVerificationMode.Sha256, token);
        await TagMediaAsync(pair.Audio, tags, options, token);
        string rebuilt = Path.Combine(folder, "tagged-" + Guid.NewGuid().ToString("N") + ".zip");
        await using (var output = new FileStream(rebuilt, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
        {
            await using var writer = await ZipWriter.OpenAsyncWriter(output,
                KaraokeZipWriter.CreateOptions(processing.ZipCompression, processing.ZipAlgorithm), token);
            foreach (string name in names)
            {
                string source = Path.GetExtension(name).Equals(".cdg", StringComparison.OrdinalIgnoreCase) ? pair.Graphics : pair.Audio;
                await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                await writer.WriteAsync(name, input, modificationTime: null, cancellationToken: token);
            }
        }
        string audioHash = await RecoveryFingerprint.HashFileAsync(pair.Audio, RecoveryVerificationMode.Sha256, token);
        await using (var input = new FileStream(rebuilt, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true))
        await using (var archive = await ZipArchive.OpenAsyncArchive(input, cancellationToken: token))
        await foreach (var entry in archive.EntriesAsync.Where(e => !e.IsDirectory).WithCancellation(token))
        {
            await using var contents = await entry.OpenEntryStreamAsync(token);
            string expected = Path.GetExtension(entry.Key!).Equals(".cdg", StringComparison.OrdinalIgnoreCase) ? graphicsHash : audioHash;
            if (await RecoveryFingerprint.HashStreamAsync(contents, RecoveryVerificationMode.Sha256, token) != expected)
                throw new InvalidDataException("Tagged ZIP payload verification failed.");
        }
        token.ThrowIfCancellationRequested();
        File.Move(rebuilt, path, true);
    }

    private static async Task<string> PacketHashAsync(string path, CancellationToken token)
        => await RunToolAsync("ffprobe", ["-v", "error", "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=stream_index,size,data_hash", "-of", "csv=p=0", path], Path.GetDirectoryName(path)!, token, hashOutput: true);

    private static async Task<string> RunToolAsync(string tool, IEnumerable<string> arguments, string folder, CancellationToken token, bool hashOutput = false)
    {
        string binaries = FFmpegRuntime.Options(folder).BinaryFolder;
        var start = new ProcessStartInfo(Path.Combine(binaries, tool + (OperatingSystem.IsWindows() ? ".exe" : "")))
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = folder };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start " + tool);
        using var registration = token.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        var error = process.StandardError.ReadToEndAsync(token);
        // Packet lists can be huge. Hash them as a stream rather than retaining them in memory.
        var digests = new Dictionary<string, RecoveryFingerprint.ContentHash>(StringComparer.Ordinal);
        var output = new StringBuilder();
        int lines = 0;
        try
        {
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
            {
                if (hashOutput)
                {
                    // ffprobe also emits packet side-data metadata (e.g. Vorbis comments).
                    // Only stream index, size and compressed data hash are media payload.
                    string[] fields = line.Split(',');
                    if (fields.Length < 3 || !fields[2].StartsWith("SHA256:", StringComparison.Ordinal)) continue;
                    if (!digests.TryGetValue(fields[0], out var digest))
                        digests.Add(fields[0], digest = RecoveryFingerprint.CreateContentHash(RecoveryVerificationMode.Sha256));
                    digest.Append(Encoding.UTF8.GetBytes(fields[1] + "," + fields[2] + "\n"));
                    lines++;
                }
                else if (output.Length < 1024 * 1024) output.AppendLine(line);
            }
            await process.WaitForExitAsync(token);
            string message = await error;
            if (process.ExitCode != 0) throw new InvalidDataException(tool + " could not process the container: " + message);
            if (hashOutput && lines == 0) throw new InvalidDataException("No readable media packets were found.");
            return hashOutput ? string.Join(";", digests.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => d.Key + ":" + d.Value.GetHex())) : output.ToString();
        }
        finally
        {
            foreach (var digest in digests.Values) digest.Dispose();
            if (!process.HasExited) { try { process.Kill(true); } catch (InvalidOperationException) { } }
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }
}
