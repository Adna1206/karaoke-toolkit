using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OperationIdValue = KaraokeToolkit.Core.Execution.OperationId;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recipes;
using KaraokeToolkit.Core.Tagging;

namespace KaraokeToolkit.Core.Recovery;

public enum RecoveryKind { Conversion, Recipe, Tagging, TrackGrouping, FileMoves }

internal sealed record WorkflowData
{
    public int Version { get; set; } = 2;
    public bool SaveResumableRun { get; set; } = true;
    public bool SupportsItemRecovery { get; set; }
    public bool SkipUnavailableMoveGroups { get; set; }
    public int? ActiveMoveGroup { get; set; }
    // Journals written before this field was added contain SHA-256 fingerprints.
    public RecoveryVerificationMode VerificationMode { get; set; } = RecoveryVerificationMode.Sha256;
    // Journals without this field used a batch-wide fingerprint pass.
    public FileCheckTiming CheckTiming { get; set; } = FileCheckTiming.BeforeBatch;
    public RecoveryKind Kind { get; set; }
    public string[] Roots { get; set; } = [];
    public string[] Inputs { get; set; } = [];
    public string[] CurrentInputs { get; set; } = [];
    public RecipeDefinition? Recipe { get; set; }
    public List<RecipeStepResult> Steps { get; set; } = [];
    public bool StepStarted { get; set; }
    public Dictionary<string, string> Expected { get; set; } = new(InputSelection.Paths);
    public List<WorkflowConversion>? Conversions { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OperationIdValue? OperationId { get; set; }
    public bool CopyGrouping { get; set; }
    public List<WorkflowMoveGroup>? Moves { get; set; }
    public TaggingPreview[] TagFiles { get; set; } = [];
    public bool TagStepStarted { get; set; }
    public TaggingOptions TagOptions { get; set; } = new();
    public ConversionOptions Processing { get; set; } = new();
    public Dictionary<string, ConversionFileResult> TagResults { get; set; } = new(InputSelection.Paths);
    public string? TagCurrent { get; set; }
    public string? Workspace { get; set; }
    public bool Finished { get; set; }
}

internal sealed class WorkflowConversion
{
    public required string Folder { get; set; }
    public required string[] Sources { get; set; }
    public string? Journal { get; set; }
    public ConversionResult? Result { get; set; }
}

internal sealed record WorkflowMove(string Source, string? Target, string Hash, bool Directory = false);
internal sealed class WorkflowMoveGroup
{
    public WorkflowMove[] Items { get; set; } = [];
    public bool CopyPrepared { get; set; }
    public string? SkipReason { get; set; }
    public List<ConversionFileResult>? Results { get; set; }
}

/// <summary>A durable recipe/tagging checkpoint. Transaction receipts are saved before backups
/// disappear; child conversions and frozen move plans close the gap inside an interrupted step.</summary>
internal sealed class WorkflowSession : IDisposable
{
    internal static readonly AsyncLocal<WorkflowSession?> Current = new();
    internal WorkflowData Data { get; private set; }
    internal string Journal { get; }
    internal RecoveryStore Store { get; }
    private readonly FileStream lease;
    private readonly WorkflowSession? previous;

    internal WorkflowSession(RecoveryStore store, WorkflowData data)
    {
        Store = store;
        Data = data;
        if (!data.SaveResumableRun && data.Kind is RecoveryKind.FileMoves or RecoveryKind.TrackGrouping)
            data.SupportsItemRecovery = true;
        string directory = Path.Combine(data.Roots[0], ".karaoke-job-workflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Journal = Path.Combine(directory, "workflow.json");
        lease = RunSession.Lease(directory);
        previous = Current.Value;
        try { Save(); store.Register(Journal); }
        catch { lease.Dispose(); throw; }
    }

    internal WorkflowSession(RecoveryStore store, RecoveryJob job)
    {
        Store = store;
        Journal = Path.GetFullPath(job.JournalPath);
        Data = Read(Journal);
        lease = RunSession.Lease(Path.GetDirectoryName(Journal)!);
        previous = Current.Value;
        try
        {
            if (RecoveryStore.IsAbandoned(Journal)) throw new IOException("This interrupted workflow was abandoned.");
            Data = Read(Journal);
            if (Data.Finished) throw new IOException("This workflow has already finished.");
        }
        catch { lease.Dispose(); throw; }
    }

    internal static string[] RootsFor(IEnumerable<string> inputs) => inputs.Select(p => Directory.Exists(p) ? Path.GetFullPath(p) : Path.GetDirectoryName(Path.GetFullPath(p))!)
        .Distinct(InputSelection.Paths).ToArray();

    internal static WorkflowData Read(string journal)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(journal))!;
        if (Path.GetFileName(journal) != "workflow.json" || !Path.GetFileName(directory).StartsWith(".karaoke-job-workflow-", StringComparison.Ordinal))
            throw new IOException("Invalid workflow journal location.");
        RunSession.CheckPlainPath(journal);
        var data = JsonSerializer.Deserialize<WorkflowData>(File.ReadAllText(journal), DurableJson.Settings) ?? throw new IOException("Empty workflow journal.");
        if (data.Version != 2 || !Enum.IsDefined(data.VerificationMode) || !Enum.IsDefined(data.CheckTiming) ||
            data.Kind is not (RecoveryKind.Recipe or RecoveryKind.Tagging or RecoveryKind.TrackGrouping or RecoveryKind.FileMoves) || data.Roots is not { Length: > 0 } ||
            data.Inputs == null || data.CurrentInputs == null || data.Steps == null || data.Expected == null || data.TagFiles == null || data.TagResults == null ||
            !InputSelection.Paths.Equals(Path.GetDirectoryName(directory), data.Roots[0])) throw new IOException("Invalid workflow checkpoint.");
        if (data.SkipUnavailableMoveGroups && !(data.Kind == RecoveryKind.TrackGrouping || data.Kind == RecoveryKind.FileMoves &&
            data.OperationId is OperationIdValue.OrganizeFiles or OperationIdValue.OrganizeFolders or OperationIdValue.RenameByPattern))
            throw new IOException("Only approved file plans can skip unavailable move groups.");
        foreach (string root in data.Roots)
        {
            if (!Path.IsPathFullyQualified(root) || !InputSelection.Paths.Equals(root, Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))))
                throw new IOException("Invalid workflow root.");
            if (!data.SkipUnavailableMoveGroups || InputSelection.Paths.Equals(root, data.Roots[0]))
                RunSession.CheckPlainPath(root);
        }
        void Check(string path, bool requirePlainPath = true)
        {
            if (!Path.IsPathFullyQualified(path) || !data.Roots.Any(root => IsWithin(root, path))) throw new IOException("Workflow path is outside the saved roots.");
            if (requirePlainPath) RunSession.CheckPlainPath(path);
        }
        foreach (string path in data.Inputs.Concat(data.CurrentInputs).Concat(data.Expected.Keys).Concat(data.TagFiles.Select(f => f.Path)).Concat(data.TagResults.Keys)) Check(path);
        foreach (var step in data.Steps)
        foreach (var file in step.Result.Files)
            foreach (string output in file.Outputs) Check(output);
        foreach (var plan in data.Conversions ?? [])
        {
            Check(plan.Folder); foreach (string source in plan.Sources) Check(source);
            if (plan.Journal != null && (Path.GetFileName(plan.Journal) != "run.json" ||
                !InputSelection.Paths.Equals(Path.GetDirectoryName(Path.GetDirectoryName(plan.Journal)), plan.Folder))) throw new IOException("Invalid child conversion journal.");
            foreach (var file in plan.Result?.Files ?? []) foreach (string output in file.Outputs) Check(output);
        }
        var moveGroups = data.Moves ?? [];
        for (int index = 0; index < moveGroups.Count; index++)
        {
            var group = moveGroups[index];
            bool requirePlainPath = !data.SkipUnavailableMoveGroups || data.ActiveMoveGroup == index ||
                group.Results?.Any(result => result.Status == ConversionStatus.Succeeded) == true;
            foreach (var move in group.Items)
            {
                if (group.SkipReason != null && move.Target == null) continue;
                Check(move.Source, requirePlainPath);
                if (move.Target != null) Check(move.Target, requirePlainPath);
            }
        }
        if (data.Expected.Values.Any(hash => hash != "directory" && !RecoveryFingerprint.IsValid(hash, data.VerificationMode))) throw new IOException("Invalid workflow fingerprint.");
        if (data.TagFiles.Select(f => f.Path).Distinct(InputSelection.Paths).Count() != data.TagFiles.Length ||
            data.TagResults.Keys.Any(p => !data.TagFiles.Any(f => InputSelection.Paths.Equals(f.Path, p))) ||
            data.TagCurrent != null && !data.TagFiles.Any(f => InputSelection.Paths.Equals(f.Path, data.TagCurrent))) throw new IOException("Invalid tagging checkpoint.");
        if (data.Workspace != null)
        {
            Check(data.Workspace);
            string name = Path.GetFileName(data.Workspace);
            if (!name.StartsWith(".karaoke-job-", StringComparison.Ordinal) || !Guid.TryParseExact(name[13..], "N", out _)) throw new IOException("Invalid workflow transaction workspace.");
        }
        bool perItem = data.CheckTiming == FileCheckTiming.BeforeItem;
        if (perItem && (data.Kind is not (RecoveryKind.TrackGrouping or RecoveryKind.FileMoves) || data.Moves == null))
            throw new IOException("Item check timing is only valid for standalone move plans.");
        bool ValidFingerprint(WorkflowMoveGroup group, WorkflowMove move) =>
            RecoveryFingerprint.IsValid(move.Hash, data.VerificationMode) ||
            data.SkipUnavailableMoveGroups && move.Hash == "" && !group.CopyPrepared &&
            group.Results?.All(result => result.Status != ConversionStatus.Succeeded) == true ||
            perItem && move.Hash == "" && !group.CopyPrepared &&
            group.Results?.Any(result => result.Status == ConversionStatus.Succeeded) != true &&
            group.Items.All(item => item.Target == null || item.Hash == "");
        if (data.Kind == RecoveryKind.TrackGrouping && (data.OperationId != OperationIdValue.GroupByTrackId || data.Moves is not { Count: > 0 } ||
            data.Moves.Any(g => g.Items.Length == 0 || g.Items.Any(m =>
                m.Target == null || !IsWithin(data.Roots[0], m.Target) || InputSelection.Paths.Equals(m.Source, m.Target) ||
                !ValidFingerprint(g, m)))))
            throw new IOException("Invalid track grouping checkpoint.");
        if (data.ActiveMoveGroup is int active &&
            (!(perItem || data.SkipUnavailableMoveGroups || data.SupportsItemRecovery && !data.SaveResumableRun) || data.Kind is not (RecoveryKind.FileMoves or RecoveryKind.TrackGrouping) ||
             data.Moves == null || active < 0 || active >= data.Moves.Count || data.Moves[active].Results != null ||
             data.Moves.Take(active).Any(group => group.Results == null)))
            throw new IOException("Invalid interrupted move group checkpoint.");
        if ((perItem || data.SupportsItemRecovery || data.SkipUnavailableMoveGroups) && data.CopyGrouping &&
            data.Moves!.Where((group, index) => index != data.ActiveMoveGroup).Any(group => group.CopyPrepared && group.Results == null))
            throw new IOException("A prepared copy has no matching interrupted group checkpoint.");
        if (data.Kind == RecoveryKind.FileMoves && (data.OperationId is not (OperationIdValue.OrganizeFiles or
            OperationIdValue.OrganizeFolders or OperationIdValue.FlattenOneLayer or OperationIdValue.FlattenAllLayers or OperationIdValue.RenameByPattern) ||
            data.CopyGrouping || data.Moves is not { Count: > 0 } || data.Moves.Any(g => g.Items.Length == 0 || g.Items.Any(m =>
                m.Target != null ? !ValidFingerprint(g, m) ||
                    (data.OperationId == OperationIdValue.RenameByPattern ? StringComparer.Ordinal : InputSelection.Paths).Equals(m.Source, m.Target)
                : g.SkipReason == null && (!m.Directory || m.Hash != "empty" || data.Roots.Contains(m.Source, InputSelection.Paths))))))
            throw new IOException("Invalid standalone file move checkpoint.");
        if (data.OperationId == OperationIdValue.RenameByPattern &&
            (data.Kind != RecoveryKind.FileMoves || data.Moves!.Any(group =>
                group.SkipReason != null || group.Items.Any(move => move.Directory || move.Target == null ||
                    !InputSelection.Paths.Equals(Path.GetDirectoryName(group.Items[0].Source), Path.GetDirectoryName(move.Source)) ||
                    !InputSelection.Paths.Equals(Path.GetDirectoryName(move.Source), Path.GetDirectoryName(move.Target))))))
            throw new IOException("Invalid filename rename checkpoint.");
        if (data.Kind == RecoveryKind.Recipe)
        {
            RecipeOperations.Validate(data.Recipe!);
            if (data.Steps.Count > data.Recipe!.Steps.Count || data.Steps.Where((s, i) => s.Number != i + 1 || s.OperationId != data.Recipe.Steps[i].OperationId).Any())
                throw new IOException("Invalid completed recipe steps.");
        }
        data.Expected = new(data.Expected, InputSelection.Paths);
        data.TagResults = new(data.TagResults, InputSelection.Paths);
        return data;
    }

    internal static bool IsWithin(string root, string path) => InputSelection.Paths.Equals(root, Path.GetFullPath(path)) ||
        Path.GetFullPath(path).StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal void Save() => DurableJson.Write(Journal, Data);
    internal void Enter() => Current.Value = this;
    internal void StartStep()
    {
        Data.StepStarted = true;
        Data.Expected = Capture(Data.CurrentInputs, Data.VerificationMode);
        Data.Conversions = null; Data.Moves = null;
        Data.TagStepStarted = false; Data.TagFiles = []; Data.TagResults.Clear(); Data.TagCurrent = null;
        Save();
    }
    internal void CompleteStep(RecipeStepResult result, string[] next)
    {
        var checkpoint = Data with
        {
            Steps = [.. Data.Steps, result],
            CurrentInputs = next,
            StepStarted = false,
            Conversions = null,
            Moves = null,
            Workspace = null,
            TagStepStarted = false,
            TagFiles = [],
            TagResults = new(InputSelection.Paths),
            TagCurrent = null,
            Expected = Capture(next, Data.VerificationMode)
        };
        DurableJson.Write(Journal, checkpoint);
        Data = checkpoint;
    }
    internal void VerifyExpected()
    {
        foreach (var item in Data.Expected)
        {
            var attributes = RunSession.CheckPlainPath(item.Key);
            bool isDirectory = attributes is { } found && (found & FileAttributes.Directory) != 0;
            if (item.Value == "directory" ? !isDirectory : attributes == null || isDirectory ||
                RecoveryFingerprint.Hash(item.Key, Data.VerificationMode) != item.Value)
            {
                bool pendingMove = Data.Moves?.Where(g => g.Results == null).SelectMany(g => g.Items).Any(m =>
                    m.Target == null ? InputSelection.Paths.Equals(m.Source, item.Key) && item.Value == "directory" && !Directory.Exists(m.Source)
                        : IsWithin(m.Source, item.Key) && !WorkflowMoves.Exists(m.Source) && WorkflowMoves.Exists(m.Target) && WorkflowMoves.Hash(m.Target, m.Directory, Data.VerificationMode) == m.Hash) == true;
                if (!pendingMove) throw new IOException("Saved workflow input/output changed or disappeared: " + item.Key);
            }
        }
    }
    internal static Dictionary<string, string> Capture(IEnumerable<string> paths, RecoveryVerificationMode mode = RecoveryVerificationMode.Sha256)
    {
        var result = new Dictionary<string, string>(InputSelection.Paths);
        foreach (string path in paths)
        {
            var attributes = RunSession.CheckPlainPath(path);
            if (attributes == null) throw new IOException("Workflow input/output is missing: " + path);
            if ((attributes.Value & FileAttributes.Directory) == 0) result[path] = RecoveryFingerprint.Hash(path, mode);
            else
                foreach (string folder in ConversionService.EnumerateFolders(path, true))
                {
                    result[folder] = "directory";
                    foreach (string file in Directory.GetFiles(folder)) { RunSession.CheckPlainPath(file); result[file] = RecoveryFingerprint.Hash(file, mode); }
                }
        }
        return result;
    }
    internal void Attach(string workspace) { Data.Workspace = workspace; Save(); }
    /// <summary>Durably records published content and removed paths before transaction backups can be cleaned.</summary>
    internal void Published(IReadOnlyList<string> outputs, IReadOnlyList<string> removed)
    {
        foreach (string path in removed) Data.Expected.Remove(path);
        foreach (string path in outputs) Data.Expected[path] = RecoveryFingerprint.Hash(path, Data.VerificationMode);
        if ((Data.Kind == RecoveryKind.Tagging || Data.TagStepStarted) && Data.TagCurrent != null)
            Data.TagResults[Data.TagCurrent] = new(Data.TagCurrent, ConversionStatus.Succeeded, outputs, "Tags verified and published.");
        Save();
    }
    /// <summary>Resolves the attached transaction and records its publication before deleting disposable files.</summary>
    /// <remarks>Uses the workflow's saved state; failures leave its workspace available for a later recovery attempt.</remarks>
    internal void RecoverTransaction()
    {
        if (Data.Workspace == null || !Directory.Exists(Data.Workspace)) return;
        string path = Path.Combine(Data.Workspace, "recovery.json");
        var journal = File.Exists(path) ? JsonSerializer.Deserialize<TransactionJournal>(File.ReadAllText(path)) : null;
        var outputs = FileTransactionRecovery.Recover(Data.Workspace);
        if (outputs != null)
            Published(outputs, journal!.Backups.Select(b => Path.Combine(Path.GetDirectoryName(Data.Workspace)!, b.Name)).ToArray());
        RecoveryWorkspaceCleanup.Delete(Data.Workspace);
        Data.Workspace = null; Save();
    }
    internal void RecordChild(string journal, RunData child)
    {
        if (Data.Kind != RecoveryKind.Recipe || Data.Conversions == null) return;
        if (!InputSelection.Paths.Equals(child.OwnerWorkflow, Journal)) throw new IOException("Child conversion belongs to a different workflow.");
        var plan = Data.Conversions.Single(p => InputSelection.Paths.Equals(p.Folder, child.Request.Folder));
        plan.Journal = journal;
        if (child.Finished)
            plan.Result = new(child.Results.Values.Concat(child.MetadataResults.Values.Where(r => r.Status is ConversionStatus.Failed or ConversionStatus.Skipped)).ToArray(), false) { Found = child.Sources.Length };
        Save();
    }
    internal bool HasPendingPublication => Data.Workspace != null && Directory.Exists(Data.Workspace);
    internal void BeginMoveGroup(int index)
    {
        if ((!Data.SupportsItemRecovery && !Data.SkipUnavailableMoveGroups && Data.CheckTiming != FileCheckTiming.BeforeItem) ||
            Data.ActiveMoveGroup == index) return;
        Data.ActiveMoveGroup = index;
        Save();
    }

    internal void Finish(bool interruptedMoveOnly = false)
    {
        // A cleared active checkpoint proves item-only recovery has settled. Later groups
        // were never started and their source paths may have changed independently.
        bool partialMove = !interruptedMoveOnly && !Data.CopyGrouping && !Data.SkipUnavailableMoveGroups &&
            Data.Moves?.Any(group => group.Results == null && group.SkipReason == null &&
            group.Items.Any(move => !WorkflowMoves.MovePathExists(move, move.Source, Data.OperationId == OperationIdValue.RenameByPattern))) == true;
        if (HasPendingPublication || Data.ActiveMoveGroup != null || partialMove ||
            Data.Conversions?.Any(p => p.Journal != null && File.Exists(p.Journal) && !RunSession.Read(p.Journal).Finished) == true)
            throw new IOException("An interrupted publication remains. Use Recover to resume this workflow.");
        Data.Finished = true; Save(); DurableJson.Delete(Store.IndexPath(Journal));
    }
    public void Dispose()
    {
        if (Current.Value == this) Current.Value = previous;
        lease.Dispose();
        if (Data.Finished) RecoveryWorkspaceCleanup.Delete(Path.GetDirectoryName(Journal)!, "workflow.json");
    }

    internal static string TreeHash(string directory, RecoveryVerificationMode mode = RecoveryVerificationMode.Sha256,
        Action<string>? visitDirectory = null, Func<string, string>? fingerprintFile = null, Action<string>? checkEntry = null)
    {

        using var digest = mode == RecoveryVerificationMode.None ? null : RecoveryFingerprint.CreateContentHash(
            mode == RecoveryVerificationMode.MetadataOnly ? RecoveryVerificationMode.XxHash128 : mode);
        void Visit(string folder)
        {
            RunSession.CheckPlainPath(folder);
            visitDirectory?.Invoke(folder);
            foreach (string entry in Directory.GetFileSystemEntries(folder).Order(InputSelection.Paths))
            {
                checkEntry?.Invoke(entry);
                RunSession.CheckPlainPath(entry);
                digest?.Append(Encoding.UTF8.GetBytes(Path.GetRelativePath(directory, entry) + "\n"));
                if (Directory.Exists(entry)) Visit(entry);
                else
                {
                    string fingerprint = fingerprintFile?.Invoke(entry) ?? RecoveryFingerprint.Hash(entry, mode);
                    digest?.Append(Encoding.UTF8.GetBytes(fingerprint));
                }
            }
        }
        Visit(directory);
        return digest?.GetHex() ?? "unverified";
    }
}
