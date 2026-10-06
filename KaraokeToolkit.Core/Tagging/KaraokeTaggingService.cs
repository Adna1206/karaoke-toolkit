using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using SharpCompress.Archives.Zip;
using SharpCompress.Writers.Zip;

namespace KaraokeToolkit.Core.Tagging;

/// <summary>Previews filename tags, then updates staged copies without re-encoding audio or graphics.</summary>
public sealed class KaraokeTaggingService
{
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

    public Task<IReadOnlyList<TaggingPreview>> PreviewAsync(IEnumerable<string> inputs, string pattern, bool recurse, CancellationToken token = default)
        => Task.Run(async () =>
        {
            var template = new FilenameTagPattern(pattern);
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
            foreach (string path in files.Order(InputSelection.Paths))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    RunSession.CheckPlainPath(path);
                    string hash = FileTransaction.Hash(path);
                    string extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension is ".bin" or ".cdg") throw new NotSupportedException("BIN and CDG have no standard metadata fields. Tag their audio or a converted media file instead.");
                    var tags = template.Parse(Path.GetFileNameWithoutExtension(path));
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
                    if (hash != FileTransaction.Hash(path)) throw new IOException("Source changed while previewing.");
                    result.Add(new(path, tags, existing, null, hash));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                { result.Add(new(path, null, null, error.Message, null)); }
            }
            return (IReadOnlyList<TaggingPreview>)result;
        }, token);

    public Task<ConversionResult> ApplyAsync(IReadOnlyList<TaggingPreview> preview, TaggingOptions options,
        ConversionOptions processing, IProgress<ConversionProgress>? progress = null, CancellationToken token = default)
        => Task.Run(async () =>
        {
            var clock = Stopwatch.StartNew();
            var results = new List<ConversionFileResult>();
            foreach (var item in preview)
            {
                if (token.IsCancellationRequested) break;
                if (!item.CanApply)
                { results.Add(new(item.Path, ConversionStatus.Skipped, [], item.Error ?? "Not selected for tagging.")); continue; }
                try
                {
                    string folder = Path.GetDirectoryName(item.Path)!;
                    if (new RecoveryStore().Find(folder, false).Count > 0) throw new IOException("Recover the interrupted run before tagging this folder.");
                    using var transaction = new FileTransaction(folder);
                    DurableJson.Write(Path.Combine(transaction.Workspace, "tagging.json"), new { Version = 1 });
                    if (FileTransaction.Hash(item.Path) != item.SourceHash) throw new IOException("Source changed after preview. Refresh the preview before tagging.");
                    string staged = await transaction.CopyInputAsync(item.Path, token);
                    if (FileTransaction.Hash(staged) != item.SourceHash) throw new IOException("Source changed while staging.");
                    if (Path.GetExtension(staged).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                        await TagZipAsync(staged, item.Tags!, options, processing, token);
                    else await TagMediaAsync(staged, item.Tags!, options, token);
                    token.ThrowIfCancellationRequested();
                    var outputs = transaction.Commit([staged], [item.Path], OverwritePolicy.Replace, token);
                    results.Add(new(item.Path, ConversionStatus.Succeeded, outputs, "Tags updated in place; media was not re-encoded."));
                }
                catch (OperationCanceledException) { break; }
                catch (Exception error) { results.Add(new(item.Path, ConversionStatus.Failed, [], error.Message)); }
                progress?.Report(new(100, results.Count * 100 / Math.Max(1, preview.Count), preview.Count, results.Count,
                    "Tagging " + Path.GetFileName(item.Path), clock.Elapsed.ToString(@"hh\:mm\:ss")));
            }
            return new ConversionResult(results, token.IsCancellationRequested) { Found = preview.Count, Elapsed = clock.Elapsed };
        });

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
        using (var archive = ZipArchive.OpenArchive(path)) names = archive.Entries.Where(e => !e.IsDirectory).Select(e => e.Key!).ToArray();
        var pair = await KaraokeArchive.ExtractAsync(path, token);
        string graphicsHash = FileTransaction.Hash(pair.Graphics);
        await TagMediaAsync(pair.Audio, tags, options, token);
        string rebuilt = Path.Combine(folder, "tagged-" + Guid.NewGuid().ToString("N") + ".zip");
        await using (var output = File.Create(rebuilt))
        {
            using var writer = new ZipWriter(output, KaraokeZipWriter.CreateOptions(processing.ZipCompression, processing.ZipAlgorithm));
            foreach (string name in names)
            {
                string source = Path.GetExtension(name).Equals(".cdg", StringComparison.OrdinalIgnoreCase) ? pair.Graphics : pair.Audio;
                await using var input = File.OpenRead(source);
                using var entry = writer.WriteToStream(name, new ZipWriterEntryOptions());
                await input.CopyToAsync(entry, token);
            }
        }
        using (var archive = ZipArchive.OpenArchive(rebuilt))
        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
        {
            using var contents = entry.OpenEntryStream();
            string expected = Path.GetExtension(entry.Key!).Equals(".cdg", StringComparison.OrdinalIgnoreCase) ? graphicsHash : FileTransaction.Hash(pair.Audio);
            if (Convert.ToHexString(SHA256.HashData(contents)) != expected) throw new InvalidDataException("Tagged ZIP payload verification failed.");
        }
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
        var digests = new Dictionary<string, IncrementalHash>(StringComparer.Ordinal);
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
                    if (!digests.TryGetValue(fields[0], out var digest)) digests.Add(fields[0], digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256));
                    digest.AppendData(Encoding.UTF8.GetBytes(fields[1] + "," + fields[2] + "\n"));
                    lines++;
                }
                else if (output.Length < 1024 * 1024) output.AppendLine(line);
            }
            await process.WaitForExitAsync(token);
            string message = await error;
            if (process.ExitCode != 0) throw new InvalidDataException(tool + " could not process the container: " + message);
            if (hashOutput && lines == 0) throw new InvalidDataException("No readable media packets were found.");
            return hashOutput ? string.Join(";", digests.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => d.Key + ":" + Convert.ToHexString(d.Value.GetHashAndReset()))) : output.ToString();
        }
        finally
        {
            foreach (var digest in digests.Values) digest.Dispose();
            if (!process.HasExited) { try { process.Kill(true); } catch (InvalidOperationException) { } }
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }
}
