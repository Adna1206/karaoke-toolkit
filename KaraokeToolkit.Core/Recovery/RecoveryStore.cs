using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Conversion;
using System.Text.Json;

namespace KaraokeToolkit.Core.Recovery;

public sealed record RecoveryJob(string JournalPath, string Folder, string Action, int Completed, int Total, string? Problem = null)
{
    public RecoveryKind Kind { get; init; }
}

/// <summary>Finds interrupted runs without scanning a drive. The folder journal remains authoritative.</summary>
public sealed class RecoveryStore
{
    internal string IndexDirectory
    {
        get;
    }
    public RecoveryStore(string? indexDirectory = null) => IndexDirectory = indexDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KaraokeToolkit", "Recovery");

    public IReadOnlyList<RecoveryJob> Find(string? folder = null, bool recurse = true)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (folder == null)
        {
            if (Directory.Exists(IndexDirectory))
                foreach (string index in Directory.GetFiles(IndexDirectory, "*.json"))
                    try
                    {
                        paths.Add(JsonSerializer.Deserialize<string>(File.ReadAllText(index))!);
                    }
                    catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        else if (Directory.Exists(folder))
            foreach (string directory in ConversionService.EnumerateFolders(Path.GetFullPath(folder), recurse))
                foreach (string run in Directory.GetDirectories(directory, ".karaoke-job-run-*"))
                    paths.Add(Path.Combine(run, "run.json"));
        // Workflow indexes also cover selections spanning several source folders.
        if (Directory.Exists(IndexDirectory))
            foreach (string index in Directory.GetFiles(IndexDirectory, "*.json"))
                try
                {
                    string? path = JsonSerializer.Deserialize<string>(File.ReadAllText(index));
                    if (path != null && Path.GetFileName(path) == "workflow.json") paths.Add(path);
                }
                catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        if (folder != null && Directory.Exists(folder))
            foreach (string directory in ConversionService.EnumerateFolders(Path.GetFullPath(folder), recurse))
            foreach (string workflow in Directory.GetDirectories(directory, ".karaoke-job-workflow-*")) paths.Add(Path.Combine(workflow, "workflow.json"));
        var jobs = new List<RecoveryJob>();
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;
            string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            if (Path.GetFileName(path) == "workflow.json")
            {
                if (!Path.GetFileName(directory).StartsWith(".karaoke-job-workflow-", StringComparison.Ordinal)) continue;
                FileStream workflowLease;
                try { RunSession.CheckPlainPath(directory); workflowLease = RunSession.Lease(directory); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                using (workflowLease)
                {
                    try
                    {
                        var workflow = WorkflowSession.Read(path);
                        if (folder != null && !workflow.Roots.Any(root => WorkflowSession.IsWithin(root, Path.GetFullPath(folder)) || WorkflowSession.IsWithin(Path.GetFullPath(folder), root))) continue;
                        if (!workflow.Finished)
                            jobs.Add(new(path, workflow.Roots[0], workflow.Kind == RecoveryKind.Recipe ? "Recipe — " + workflow.Recipe!.Name : "Tag Karaoke Files",
                                workflow.Kind == RecoveryKind.Recipe ? workflow.Steps.Count : workflow.TagResults.Count,
                                workflow.Kind == RecoveryKind.Recipe ? workflow.Recipe!.Steps.Count : workflow.TagFiles.Length) { Kind = workflow.Kind });
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException or NullReferenceException)
                    { jobs.Add(new(path, Path.GetDirectoryName(directory)!, "Workflow recovery needs attention", 0, 0, "The workflow journal could not be read safely. Keep the recovery files for inspection. " + e.Message) { Kind = RecoveryKind.Recipe }); }
                }
                continue;
            }
            if (Path.GetFileName(path) != "run.json" || !Path.GetFileName(directory).StartsWith(".karaoke-job-run-", StringComparison.Ordinal))
                continue;
            FileStream lease;
            try
            {
                RunSession.CheckPlainPath(directory);
                lease = RunSession.Lease(directory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            using (lease)
            {
                try
                {
                    var data = RunSession.Read(path);
                    // A recipe owns continuation of its child runs; offer one prompt for the workflow.
                    if (data.OwnerWorkflow != null && File.Exists(data.OwnerWorkflow))
                    {
                        var owner = WorkflowSession.Read(data.OwnerWorkflow);
                        if (!owner.Finished && owner.Conversions?.Any(c => InputSelection.Paths.Equals(c.Journal, path)) == true)
                        {
                            // Revisit discovery still finds the parent through its child when
                            // the startup index was lost and the selected root is a later folder.
                            try
                            {
                                using var ownerLease = RunSession.Lease(Path.GetDirectoryName(data.OwnerWorkflow)!);
                                jobs.Add(new(data.OwnerWorkflow, owner.Roots[0], "Recipe — " + owner.Recipe!.Name, owner.Steps.Count, owner.Recipe.Steps.Count) { Kind = RecoveryKind.Recipe });
                            }
                            catch (IOException) { /* The parent is active; do not offer it or its child. */ }
                            continue;
                        }
                    }
                    if (!data.Finished)
                        jobs.Add(new(path, data.Request.Folder, data.Request.Action.ToString(), data.Results.Count, data.Sources.Length));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException or NullReferenceException)
                {
                    jobs.Add(new(path, Path.GetDirectoryName(directory)!, "Recovery needs attention", 0, 0,
                        "The journal could not be read safely. Keep the recovery files for inspection. " + e.Message));
                }
            }
        }
        return jobs.DistinctBy(j => j.JournalPath, InputSelection.Paths).ToArray();
    }

    internal string IndexPath(string journal) => Path.Combine(IndexDirectory, Path.GetFileName(Path.GetDirectoryName(journal)) + ".json");
    internal void Register(string journal)
    {
        Directory.CreateDirectory(IndexDirectory);
        DurableJson.Write(IndexPath(journal), journal);
    }
}

internal static class DurableJson
{
    // Human-readable checkpoints must not depend on enum member ordering.
    internal static System.Text.Json.JsonSerializerOptions Settings { get; } = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: false) }
    };

    internal static void Write<T>(string path, T value)
    {
        string temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, Settings);
            stream.Flush(flushToDisk: true);
        }
        // Windows readers/scanners can briefly deny replacement. Keep the old journal
        // intact and retry only the atomic rename; never fall back to an in-place write.
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(temporary, path, overwrite: true); return; }
            catch (Exception error) when (attempt < 4 && OperatingSystem.IsWindows() &&
                (error is UnauthorizedAccessException && (error.HResult & 0xffff) == 5 ||
                 error is IOException && (error.HResult & 0xffff) is 32 or 33))
            { Thread.Sleep(25 * (attempt + 1)); }
        }
    }
}

internal sealed class RunData
{
    public string? OwnerWorkflow { get; set; }
    public int Version { get; set; } = 2;
    public required ConversionRequest Request
    {
        get; set;
    }
    public OverwritePolicy Policy
    {
        get; set;
    }
    public bool ValidateBeforeConversion
    {
        get; set;
    }
    public string[] Sources { get; set; } = [];
    public Dictionary<string, ConversionFileResult> Results { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ConversionFileResult> MetadataResults { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Current
    {
        get; set;
    }
    public bool IsMetadata
    {
        get; set;
    }
    public string? Workspace
    {
        get; set;
    }
    public bool Finished
    {
        get; set;
    }
}

/// <summary>A durable batch checkpoint. A transaction records success before its backups are removed.</summary>
internal sealed class RunSession : IDisposable
{
    internal static readonly AsyncLocal<RunSession?> Current = new();
    internal RunData Data
    {
        get;
    }
    internal string Journal
    {
        get;
    }
    private readonly FileStream lease;
    private readonly FileStream rootLease;
    private readonly RecoveryStore store;
    internal static FileStream Lease(string directory) => new(Path.Combine(directory, "run.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    internal RunSession(RecoveryStore store, ConversionRequest request, OverwritePolicy policy, string[] sources, bool validateBeforeConversion = false)
    {
        this.store = store;
        rootLease = FileTransaction.AcquireLease(Path.Combine(Path.GetFullPath(request.Folder), ".karaoke-run-lease"));
        try
        {
            foreach (string existing in Directory.GetDirectories(request.Folder, ".karaoke-job-run-*"))
                if (File.Exists(Path.Combine(existing, "run.json")) && !Read(Path.Combine(existing, "run.json")).Finished)
                    throw new IOException("Recovery required. Resume the interrupted run before starting another in this folder.");
        }
        catch { rootLease.Dispose(); throw; }
        string directory = Path.Combine(Path.GetFullPath(request.Folder), ".karaoke-job-run-" + Guid.NewGuid().ToString("N"));
        Journal = Path.Combine(directory, "run.json");
        try
        {
            Directory.CreateDirectory(directory);
            lease = Lease(directory);
        }
        catch { rootLease.Dispose(); throw; }
        Data = new()
        {
            OwnerWorkflow = WorkflowSession.Current.Value?.Journal,
            Request = request with
            {
                Folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Folder))
            },
            Policy = policy,
            Sources = sources,
            ValidateBeforeConversion = validateBeforeConversion
        };
        try
        {
            Save();
            store.Register(Journal);
        }
        catch { lease.Dispose(); rootLease.Dispose(); throw; }
    }

    internal RunSession(RecoveryStore store, string journal)
    {
        this.store = store;
        Journal = Path.GetFullPath(journal);
        var data = Read(Journal);
        rootLease = FileTransaction.AcquireLease(Path.Combine(data.Request.Folder, ".karaoke-run-lease"));
        try
        {
            lease = Lease(Path.GetDirectoryName(Journal)!);
        }
        catch { rootLease.Dispose(); throw; }
        try
        {
            Data = Read(Journal);
        }
        catch { lease.Dispose(); rootLease.Dispose(); throw; }
    }

    internal static RunData Read(string journal)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(journal))!;
        if (Path.GetFileName(journal) != "run.json" || !Path.GetFileName(directory).StartsWith(".karaoke-job-run-", StringComparison.Ordinal))
            throw new IOException("Invalid recovery journal location.");
        CheckPlainPath(directory);
        CheckPlainPath(journal);
        var data = JsonSerializer.Deserialize<RunData>(File.ReadAllText(journal), DurableJson.Settings) ?? throw new IOException("Empty recovery journal.");
        string root = Path.GetDirectoryName(directory)!;
        if (data.Version != 2 || data.Request == null || data.Sources == null || data.Results == null || data.Metadata == null || data.MetadataResults == null ||
            !Enum.IsDefined(data.Policy) || !string.Equals(root, Path.GetFullPath(data.Request.Folder), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Unsupported or misplaced recovery journal.");
        foreach (string source in data.Sources.Concat(data.Results.Keys).Concat(data.Metadata).Concat(data.Current == null ? [] : new[] { data.Current }))
            if (!Within(root, source))
                throw new IOException("Recovery source is outside its root folder.");
        if (data.Current != null && !(data.IsMetadata ? data.Sources.Any(s => string.Equals(Path.GetDirectoryName(s), data.Current, StringComparison.OrdinalIgnoreCase)) : data.Sources.Contains(data.Current, StringComparer.OrdinalIgnoreCase)))
            throw new IOException("Invalid current recovery source.");
        if (data.Workspace != null)
        {
            string name = Path.GetFileName(data.Workspace);
            string? expectedFolder = data.Current == null ? null : Directory.Exists(data.Current) ? data.Current : Path.GetDirectoryName(data.Current);
            if (!Within(root, data.Workspace) || !name.StartsWith(".karaoke-job-", StringComparison.Ordinal) ||
                !Guid.TryParseExact(name[13..], "N", out _) ||
                !string.Equals(Path.GetDirectoryName(data.Workspace), expectedFolder, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Invalid recovery workspace.");
        }
        if (data.Results.Any(pair => pair.Value == null || !data.Sources.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)))
            throw new IOException("Invalid completed source in recovery journal.");
        data.Request.Validate();
        return data;
    }

    internal static bool Within(string root, string path) => Path.IsPathFullyQualified(path) &&
        (string.Equals(Path.GetFullPath(path), root, StringComparison.OrdinalIgnoreCase) ||
        Path.GetFullPath(path).StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    internal static void CheckPlainPath(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Recovery cannot follow linked files or folders: " + current);
    }

    internal void Save()
    {
        DurableJson.Write(Journal, Data);
        WorkflowSession.Current.Value?.RecordChild(Journal, Data);
    }
    internal void ThrowIfUnresolved()
    {
        if (Data.Workspace != null && File.Exists(Path.Combine(Data.Workspace, "recovery.json")))
            throw new IOException("Recovery required. Publication could not be completed safely. Files retained at " + Data.Workspace);
    }
    internal void RecoverPending()
    {
        if (Data.Workspace == null || !Directory.Exists(Data.Workspace))
            return;
        bool recorded = Data.Current != null && (Data.IsMetadata ? Data.Metadata.Contains(Data.Current) : Data.Results.ContainsKey(Data.Current));
        if (!recorded)
        {
            var outputs = FileTransactionRecovery.Recover(Data.Workspace);
            if (outputs != null)
                Committed(outputs);
        }
        // Only disposable staged files and checkpointed backups remain. Refuse links anywhere.
        CheckTree(Data.Workspace);
        Directory.Delete(Data.Workspace, true);
        Data.Workspace = null;
        Save();
    }
    private static void CheckTree(string directory)
    {
        CheckPlainPath(directory);
        foreach (string file in Directory.GetFiles(directory))
            CheckPlainPath(file);
        foreach (string child in Directory.GetDirectories(directory))
            CheckTree(child);
    }
    internal void Begin(string source, bool metadata = false)
    {
        ThrowIfUnresolved();
        Data.Current = source;
        Data.IsMetadata = metadata;
        Data.Workspace = null;
        Save();
    }
    internal void Attach(string workspace)
    {
        Data.Workspace = workspace;
        Save();
    }
    internal void Complete(ConversionFileResult result)
    {
        if (Data.IsMetadata)
        {
            Data.Metadata.Add(Data.Current!);
            Data.MetadataResults[Data.Current!] = result;
        }
        else
            Data.Results[Data.Current!] = result;
        Save();
    }
    internal void Committed(IReadOnlyList<string> outputs) => Complete(new(Data.Current!, ConversionStatus.Succeeded, outputs, "Outputs verified and published."));
    internal void Finish()
    {
        Data.Finished = true;
        Save();
        File.Delete(store.IndexPath(Journal));
    }
    public void Dispose()
    {
        lease.Dispose();
        rootLease.Dispose();
        if (Data.Finished)
            try
            {
                Directory.Delete(Path.GetDirectoryName(Journal)!, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
