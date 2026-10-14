using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.App.Shared;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Reporting;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Core.Validation;
using KaraokeToolkit.Core.FileOperations;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaraokeToolkit.Core;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;
namespace KaraokeToolkit.App.Features.Main;

public sealed record ActivityEntry(string Time, string Message);
public partial class MainViewModel : ViewModelBase
{
    private CancellationTokenSource? cancellation;
    private readonly ApplicationSettingsStore store;
    private bool sectionsReady;
    public IReadOnlyList<OperationSectionViewModel> OperationSections { get; }
    public IReadOnlyList<OperationSectionViewModel> VisibleSections => OperationSections.Where(section => section.IsVisible).ToArray();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(VisibleOperations)), NotifyPropertyChangedFor(nameof(SelectedSectionTitle))]
    public partial OperationSectionViewModel? SelectedSection { get; set; }
    public IReadOnlyList<ActionChoice> VisibleOperations => SelectedSection?.Operations ?? [];
    public string SelectedSectionTitle => SelectedSection?.Header ?? "Choose an operation";
    public bool NeedsSource => SelectedOperation?.Definition.Inputs != OperationInputKind.None;
    public bool CanClearInputs => CanEdit && !string.IsNullOrWhiteSpace(FolderPath);
    public string SourceInstruction => SelectedOperation?.IsRip == true
        ? "Click Start to choose a drive, tracks and output folder."
        : "Click Start to choose the Damaged batch manifest to restore.";
    public IReadOnlyList<ActionChoice> FavouriteOperations => Operations.Concat(RecipeChoices).Where(operation => operation.Available && operation.IsFavourite &&
        (!operation.IsRip || Options.ExperimentalDiscRipping)).ToArray();
    private readonly RecoveryStore recovery;
    private string[] selectedInputs = [];
    private bool settingInputs;
    private IReadOnlyList<ConversionRetry> retryRequests = [];
    [ObservableProperty] public partial string InputSummary { get; set; } = "Drop files or folders here, or use Add Folders / Add Files.";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasReport)), NotifyPropertyChangedFor(nameof(CanViewReport))]
    public partial CompletionReport? LastReport
    {
        get; set;
    }
    public IReadOnlyList<ValidationFinding> ValidationFindings { get; private set; } = [];
    public bool CanViewReport => HasReport && !IsBusy;
    public bool HasReport => LastReport != null;
    public bool CanRetry => !IsBusy && retryRequests.Count > 0;
    public IReadOnlyList<string> SelectedInputs => selectedInputs;
    public string[] CurrentInputs => selectedInputs.Length > 0 ? selectedInputs : [FolderPath];
    public bool CanValidate => !IsBusy && !string.IsNullOrWhiteSpace(FolderPath);

    public void SetInputs(IEnumerable<string> paths, bool suggestOperation = true)
    {
        if (IsBusy)
            return;
        var normalized = SelectedOperation?.IsTagging == true ? KaraokeToolkit.Core.Tagging.KaraokeTaggingService.NormalizeInputs(paths)
            : SelectedOperation?.Recipe is { } recipe
            ? KaraokeToolkit.Core.Recipes.RecipeOperations.NormalizeInputs(recipe, paths)
            : SelectedOperation is { IsOrganizer: true } or { IsFlatten: true }
            ? paths.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray()
            : InputSelection.Normalize(paths);
        if (normalized.Length == 0 || normalized.Any(p => !File.Exists(p) && !Directory.Exists(p)))
            throw new ArgumentException("Select existing files or folders.");
        if (SelectedOperation?.OrganizeMode == AlphabeticalOrganizeScope.Folders && normalized.Any(File.Exists))
            throw new ArgumentException("Organize Folders Alphabetically requires folder selections.");
        if (SelectedOperation?.IsFlatten == true && normalized.Any(File.Exists))
            throw new ArgumentException("Folder flattening requires folder selections.");
        if (SelectedOperation?.IsRecipe == true && SelectedOperation.Definition.Inputs == OperationInputKind.Folders && normalized.Any(File.Exists))
            throw new ArgumentException("This recipe starts with an operation requiring folder selections.");
        settingInputs = true;
        try
        {
            FolderPath = normalized[0];
            selectedInputs = normalized;
        }
        finally { settingInputs = false; }
        InputSummary = normalized.Length == 1 ? "Selected: " + normalized[0] : normalized.Length + " selected inputs: " + string.Join("; ", normalized.Select(Path.GetFileName));
        var extensions = normalized.Where(File.Exists).Select(p => Path.GetExtension(p).ToLowerInvariant()).Distinct().ToArray();
        if (suggestOperation && SelectedOperation is not { IsOrganizer: true } and not { IsFlatten: true } and not { IsRecipe: true } and not { IsTagging: true } && normalized.All(File.Exists))
        {
            if (extensions.All(e => e == ".zip") && SelectedOperation?.Action != Action.Repack_Zip)
                SelectedOperation = Operations.First(x => x.Action == Action.Zip_To_AudioG);
            else if (extensions.All(e => e == ".bin"))
                SelectedOperation = Operations.First(x => x.Action == Action.Bin_To_AudioG);
            else if (extensions.All(e => e is not (".zip" or ".bin")))
                SelectedOperation = Operations.First(x => x.Action == Action.AudioG_To_Zip);
        }
        ValidateCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanClearInputs));
        ClearInputsCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
    }
    public void AddInputs(IEnumerable<string> paths)
        // The home workflow chooses an operation first; adding inputs must not replace that choice.
        => SetInputs(string.IsNullOrWhiteSpace(FolderPath) ? paths : CurrentInputs.Concat(paths), suggestOperation: false);

    [RelayCommand(CanExecute = nameof(CanClearInputs))]
    private void ClearInputs()
    {
        selectedInputs = [];
        FolderPath = "";
        InputSummary = "Drop files or folders here, or use Add Folders / Add Files.";
    }

    partial void OnSelectedSectionChanged(OperationSectionViewModel? value)
    {
        if (value != null && !value.Operations.Contains(SelectedOperation))
            SelectedOperation = value.Operations.FirstOrDefault();
    }

    partial void OnSelectedOperationChanged(ActionChoice? value)
    {
        // Keep favourite navigation when it contains the selection; external input routing
        // and recovery can otherwise select an operation from a different category.
        if (value != null && SelectedSection?.Operations.Contains(value) != true)
            SelectedSection = OperationSections.FirstOrDefault(section => section.Section == value.Section && section.IsVisible);
        OnPropertyChanged(nameof(NeedsSource));
        OnPropertyChanged(nameof(SourceInstruction));
        OnPropertyChanged(nameof(SourceLabel));
    }

    private void RefreshNavigation()
    {
        OnPropertyChanged(nameof(VisibleSections));
        OnPropertyChanged(nameof(VisibleOperations));
        if (SelectedSection?.IsVisible != true)
            SelectedSection = OperationSections.FirstOrDefault(section => section.IsVisible && section.Section == SelectedOperation?.Section)
                ?? VisibleSections.FirstOrDefault();
        else if (!SelectedSection.Operations.Contains(SelectedOperation))
            SelectedOperation = SelectedSection.Operations.FirstOrDefault();
    }
    partial void OnFolderPathChanged(string value)
    {
        if (!settingInputs)
        {
            selectedInputs = [];
            InputSummary = string.IsNullOrWhiteSpace(value) ? "Drop files or folders here, or use Add Folders / Add Files." : "Typed input: " + value;
        }
        ValidateCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanClearInputs));
        ClearInputsCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsBusyChanged(bool value)
    {
        if (value) StatusOutcome = RunStatus.Neutral;
        ValidateCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanViewReport));
        OnPropertyChanged(nameof(CanChooseSource));
        OnPropertyChanged(nameof(CanChooseFiles));
        OnPropertyChanged(nameof(CanClearInputs));
        ClearInputsCommand.NotifyCanExecuteChanged();
    }

    public Func<RecoveryJob, Task<bool>>? ConfirmRecoveryAsync
    {
        get; set;
    }
    [ObservableProperty]
    public partial bool RecoveryAvailable
    {
        get; set;
    }
    [ObservableProperty]
    public partial bool IsRecoveryMode
    {
        get; set;
    }
    [ObservableProperty] public partial string RecoveryMessage { get; set; } = "";
    private string lastMessage = "";
    public OverwritePolicy OverwritePolicy { get; private set; } = OverwritePolicy.Ask;
    public ApplicationTheme Theme { get; private set; } = ApplicationTheme.System;
    public ApplicationBackdrop Backdrop { get; private set; } = ApplicationBackdrop.Automatic;
    public event Action<ApplicationBackdrop>? BackdropChanged;
    public bool ValidateBeforeConversion
    {
        get; private set;
    }
    public bool IgnoreLeadingTheForFileOrganization { get; private set; }
    public event Action<ApplicationTheme>? ThemeChanged;
    public Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? ResolveConflictAsync
    {
        get; set;
    }
    public ConversionOptions Options { get; private set; } = new();
    public AudioBitratePreferences AudioBitrates { get; private set; } = new();
    public SettingsWindowSize OptionsWindowSize { get; private set; } = new();
    public int OptionsTabIndex { get; private set; }
    public IReadOnlyList<ActionChoice> Operations { get; } = OperationDefinitions.All.Select(definition => new ActionChoice(definition)).ToArray();
    public ObservableCollection<ActivityEntry> Activity { get; } = new();
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string FolderPath { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ActionDescription)), NotifyPropertyChangedFor(nameof(CanChooseSource)), NotifyPropertyChangedFor(nameof(CanChooseFiles)), NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial ActionChoice? SelectedOperation
    {
        get; set;
    }
    public string ActionDescription => SelectedOperation?.Description ?? "Choose an action to continue.";
    public string SourceLabel => SelectedOperation?.Definition.Inputs == OperationInputKind.Folders ? "Source folders" : "Source files or folders";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEdit)), NotifyCanExecuteChangedFor(nameof(StartCommand)), NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsBusy
    {
        get; set;
    }
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool CancelRequested
    {
        get; set;
    }
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool ShowProgress
    {
        get; set;
    }
    [ObservableProperty]
    public partial int Progress
    {
        get; set;
    }
    [ObservableProperty]
    public partial int TotalProgress
    {
        get; set;
    }
    [ObservableProperty]
    public partial int FoundFiles
    {
        get; set;
    }
    [ObservableProperty] public partial string Elapsed { get; set; } = "00:00:00";
    [ObservableProperty] public partial string Status { get; set; } = "Choose an operation and its inputs, then click Start.";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusSucceeded)), NotifyPropertyChangedFor(nameof(StatusWarning)), NotifyPropertyChangedFor(nameof(StatusFailed))]
    public partial RunStatus StatusOutcome { get; set; }
    public bool StatusSucceeded => StatusOutcome == RunStatus.Success;
    public bool StatusWarning => StatusOutcome == RunStatus.Warning;
    public bool StatusFailed => StatusOutcome == RunStatus.Failure;

    partial void OnShowProgressChanged(bool value)
    {
        if (!value)
        {
            StatusOutcome = RunStatus.Neutral;
            Status = "Choose an operation and its inputs, then click Start.";
        }
    }
    [ObservableProperty] public partial string RunTitle { get; set; } = "Current Progress";
    public bool CanEdit => !IsBusy;
    public Func<Task<ConversionResult?>>? RipDiscAsync { get; set; }
    public Func<Task>? RestoreQuarantinedFilesAsync { get; set; }
    public bool CanChooseSource => CanEdit && SelectedOperation?.Definition.Inputs != OperationInputKind.None;
    public bool CanChooseFiles => CanChooseSource && SelectedOperation?.Definition.Inputs != OperationInputKind.Folders;
    public bool CanStart => !IsBusy && !ShowProgress && SelectedOperation?.Available == true &&
        (SelectedOperation.IsRip ? Options.ExperimentalDiscRipping :
            SelectedOperation.IsRestore || !string.IsNullOrWhiteSpace(FolderPath) &&
            (SelectedOperation.Definition.Inputs != OperationInputKind.Folders || CurrentInputs.All(Directory.Exists)) &&
            (SelectedOperation.Recipe == null || KaraokeToolkit.Core.Recipes.RecipeOperations.CanAcceptInputs(SelectedOperation.Recipe, CurrentInputs)));
    public bool CanCancel => IsBusy && !CancelRequested;
    public MainViewModel() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KaraokeToolkit", "options.xmls")) { }
    public MainViewModel(string settingsPath)
    {
        RecipeDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!, "Recipes");
        store = new(settingsPath, persistRenameBins: true);
        OperationSections = OperationDefinitions.Sections.Select(group => new OperationSectionViewModel(this, group.Section, group.Header,
            Operations.Where(operation => operation.Available && operation.Section == group.Section).ToArray())).ToArray();
        recovery = new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!, "Recovery"));
        SelectedOperation = Operations.Single(operation => operation.Action == Action.AudioG_To_Zip);
        try
        {
            Options = store.Load();
            AudioBitrates = store.LoadAudioBitrates(Options);
            OptionsWindowSize = store.LoadSettingsWindowSize();
            OverwritePolicy = store.LoadOverwritePolicy();
            Theme = store.LoadTheme();
            Backdrop = store.LoadBackdrop();
            ValidateBeforeConversion = store.LoadValidateBeforeConversion();
            IgnoreLeadingTheForFileOrganization = store.LoadIgnoreLeadingTheForFileOrganization();
            var favouriteIds = store.LoadFavouriteOperations().ToHashSet(StringComparer.Ordinal);
            foreach (var operation in Operations) operation.IsFavourite = operation.Available && favouriteIds.Contains(operation.Id);
            var sections = store.LoadOperationSections();
            foreach (var group in OperationSections)
                group.IsExpanded = group.Section == SelectedOperation?.Section ||
                    !sections.TryGetValue(group.Section.ToString(), out bool expanded) || expanded;
        }
        catch (Exception e) { Status = "Could not load options: " + e.Message; }
        foreach (var operation in Operations)
            operation.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName != nameof(ActionChoice.IsFavourite)) return;
                OnPropertyChanged(nameof(FavouriteOperations));
                RefreshNavigation();
                SaveOperationSections();
            };
        sectionsReady = true;
        ReloadRecipes();
    }
    internal void SaveOperationSections()
    {
        if (!sectionsReady) return;
        try
        {
            store.Save(Options, OverwritePolicy, Theme, ValidateBeforeConversion, IgnoreLeadingTheForFileOrganization, AudioBitrates,
                OperationSections.ToDictionary(group => group.Section.ToString(), group => group.IsExpanded),
                Operations.Concat(RecipeChoices).Where(operation => operation.Available && operation.IsFavourite).Select(operation => operation.Id).ToArray());
        }
        catch (Exception error) { Status = "Could not save operation sections: " + error.Message; }
    }
    public void RememberOptionsWindow(SettingsWindowSize size, int tabIndex)
    {
        // The tab is session-only. Save the window size using applied settings, never pending dialog edits.
        OptionsTabIndex = Math.Max(0, tabIndex);
        if (!size.IsValid || size == OptionsWindowSize) return;
        OptionsWindowSize = size;
        try { store.Save(Options, OverwritePolicy, Theme, ValidateBeforeConversion, IgnoreLeadingTheForFileOrganization,
            AudioBitrates, settingsWindowSize: size); }
        catch (Exception error) { Status = "Could not save settings window size: " + error.Message; }
    }
    public void SaveOptions(ConversionOptions options) => SaveOptions(options, OverwritePolicy);
    public void SaveOptions(ConversionOptions options, OverwritePolicy policy) => SaveOptions(options, policy, Theme);
    public void SaveOptions(ConversionOptions options, OverwritePolicy policy, ApplicationTheme theme)
        => SaveOptions(options, policy, theme, ValidateBeforeConversion);
    public void SaveOptions(ConversionOptions options, OverwritePolicy policy, ApplicationTheme theme, bool validateBeforeConversion)
        => SaveOptions(options, policy, theme, validateBeforeConversion, IgnoreLeadingTheForFileOrganization);
    public void SaveOptions(ConversionOptions options, OverwritePolicy policy, ApplicationTheme theme, bool validateBeforeConversion, bool ignoreLeadingTheForFileOrganization)
        => SaveOptions(options, policy, theme, validateBeforeConversion, ignoreLeadingTheForFileOrganization,
            AudioBitrates.With(options.AudioEncoder, options.AudioBitrate));
    public void SaveOptions(ConversionOptions options, OverwritePolicy policy, ApplicationTheme theme, bool validateBeforeConversion,
        bool ignoreLeadingTheForFileOrganization, AudioBitratePreferences audioBitrates)
        => SaveOptions(options, policy, theme, validateBeforeConversion, ignoreLeadingTheForFileOrganization, audioBitrates, Backdrop);

    public void SaveOptions(ConversionOptions options, OverwritePolicy policy, ApplicationTheme theme, bool validateBeforeConversion,
        bool ignoreLeadingTheForFileOrganization, AudioBitratePreferences audioBitrates, ApplicationBackdrop backdrop)
    {
        store.Save(options, policy, theme, validateBeforeConversion, ignoreLeadingTheForFileOrganization, audioBitrates, backdrop: backdrop);
        Options = options;
        OverwritePolicy = policy;
        Theme = theme;
        Backdrop = backdrop;
        ValidateBeforeConversion = validateBeforeConversion;
        IgnoreLeadingTheForFileOrganization = ignoreLeadingTheForFileOrganization;
        AudioBitrates = audioBitrates;
        ThemeChanged?.Invoke(theme);
        BackdropChanged?.Invoke(backdrop);
        OnPropertyChanged(nameof(Options));
        OnPropertyChanged(nameof(FavouriteOperations));
        if (!options.ExperimentalDiscRipping && SelectedOperation?.IsRip == true) SelectedOperation = Operations.Single(operation => operation.Action == Action.AudioG_To_Zip);
        RefreshNavigation();
        StartCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (!CanStart)
            return;
        if (SelectedOperation?.Recipe is { } recipe)
        {
            await RunRecipeAsync(recipe, CurrentInputs);
            return;
        }
        if (SelectedOperation?.IsRip == true)
        {
            IsBusy = true;
            try
            {
                if (RipDiscAsync == null) { StatusOutcome = RunStatus.Failure; Status = "The disc ripping dialog is unavailable."; return; }
                var result = await RipDiscAsync();
                if (result != null)
                {
                    LastReport = CompletionReport.FromConversion("Experimental CD+G ripping", result);
                    StatusOutcome = RunStatusSummary.From(result);
                    Status = $"Ripping: {result.Succeeded} succeeded, {result.Skipped} skipped, {result.Failed} failed.";
                }
            }
            catch (Exception error) { StatusOutcome = RunStatus.Failure; Status = error.Message; }
            finally { IsBusy = false; }
            return;
        }
        if (SelectedOperation?.IsRestore == true)
        {
            if (RestoreQuarantinedFilesAsync != null)
                await RestoreQuarantinedFilesAsync();
            else
            {
                StatusOutcome = RunStatus.Failure;
                Status = "The restore picker is not available.";
            }
            return;
        }
        if (SelectedOperation?.IsTagging == true)
        {
            foreach (string input in CurrentInputs)
                if (await CheckForRecoveryAsync(Directory.Exists(input) ? input : Path.GetDirectoryName(Path.GetFullPath(input))!)) return;
            await RunTaggingAsync();
            return;
        }
        if (SelectedOperation?.IsValidation == true)
        {
            await ValidateAsync();
            return;
        }
        if (SelectedOperation is { IsOrganizer: true } or { IsFlatten: true })
        {
            await RunFileOperationAsync();
            return;
        }
        try
        {
            foreach (string input in CurrentInputs)
            {
                string folder = Directory.Exists(input) ? input : Path.GetDirectoryName(Path.GetFullPath(input))!;
                if (await CheckForRecoveryAsync(folder))
                    return;
            }
            await ExecuteAsync(null);
        }
        catch (Exception error) { StatusOutcome = RunStatus.Failure; Status = "Could not start: " + error.Message; }
    }
    [RelayCommand]
    private async Task CheckRecoveryAsync() => await CheckForRecoveryAsync();

    public async Task<bool> CheckForRecoveryAsync(string? folder = null)
    {
        if (IsBusy)
            return true;
        IsBusy = true;
        RecoveryJob? resume = null;
        try
        {
            var jobs = await Task.Run(() => recovery.Find(folder, Options.RecurseFolders));
            RecoveryAvailable = jobs.Count > 0;
            RecoveryMessage = RecoveryAvailable ? $"Recovery available: {jobs.Count} interrupted run(s)." : "";
            if (!RecoveryAvailable)
                return false;
            Status = RecoveryMessage;
            if (ConfirmRecoveryAsync != null && await ConfirmRecoveryAsync(jobs[0]))
                resume = jobs[0];
        }
        catch (Exception error) { Status = "Could not inspect recovery files: " + error.Message; return true; }
        finally { IsBusy = false; }
        if (resume != null)
        try
        {
            if (resume.Kind == RecoveryKind.Recipe) await ResumeRecipeAsync(resume);
            else if (resume.Kind == RecoveryKind.Tagging) await RunTaggingAsync(resume);
            else await ExecuteAsync(resume);
        }
        catch (Exception error) { StatusOutcome = RunStatus.Failure; Status = "Recovery stopped: " + error.Message; }
        return true; // Later never starts a fresh run over an interrupted one.
    }

    private async Task ExecuteAsync(RecoveryJob? recoveryJob, IReadOnlyList<ConversionRetry>? retries = null)
    {
        if (recoveryJob != null)
        {
            FolderPath = recoveryJob.Folder;
            SelectedOperation = Operations.FirstOrDefault(x => x.Action.ToString() == recoveryJob.Action);
        }
        if (retries is { Count: > 0 })
            SelectedOperation = Operations.First(x => x.Action == retries[0].Action);
        if (SelectedOperation is not { Available: true, Action: { } action } selected)
            return;
        // The dedicated lossless-to-MP3 action uses MP3's saved bitrate even when
        // another encoder is selected for general BIN/disc output.
        var runOptions = action == Action.LosslessG_To_Mp3G
            ? Options with { AudioBitrate = AudioBitrates.Mp3 }
            : Options;
        using var source = new CancellationTokenSource();
        cancellation = source;
        IsBusy = true;
        CancelRequested = false;
        ShowProgress = true;
        IsRecoveryMode = recoveryJob != null;
        RecoveryAvailable = IsRecoveryMode;
        RecoveryMessage = IsRecoveryMode ? "Recovery mode — checking interrupted publication and resuming unfinished work." : "";
        RunTitle = (IsRecoveryMode ? "Recovery — " : "") + selected.Label;
        LastReport = null;
        ValidationFindings = [];
        retryRequests = [];
        Progress = TotalProgress = FoundFiles = 0;
        Elapsed = "00:00:00";
        Activity.Clear();
        lastMessage = "";
        Status = "Starting conversion…";
        try
        {
            // UI progress callbacks can arrive after the awaited task completes; ignore stale callbacks.
            bool accepting = true;
            var progressGate = new object();
            var reporter = new Progress<ConversionProgress>(report => { lock (progressGate) { if (accepting) Report(report); } });
            ConversionResult result;
            try
            {
                if (retries != null)
                {
                    var all = new List<ConversionFileResult>();
                    var nextRetry = new List<ConversionRetry>();
                    int total = 0;
                    TimeSpan elapsed = TimeSpan.Zero;
                    bool cancelled = false;
                    foreach (var retry in retries)
                    {
                        if (source.IsCancellationRequested)
                        {
                            cancelled = true;
                            break;
                        }
                        var attempt = await new SelectionConversionService(recovery).RunAsync(retry, reporter, source.Token, ResolveConflictAsync);
                        all.AddRange(attempt.Files);
                        nextRetry.AddRange(attempt.RetryRequests);
                        total += attempt.Found;
                        elapsed += attempt.Elapsed;
                        if (attempt.Cancelled)
                        {
                            cancelled = true;
                            break;
                        }
                    }
                    result = new(all, cancelled)
                    {
                        Found = total,
                        Elapsed = elapsed,
                        RetryRequests = nextRetry
                    };
                }
                else if (recoveryJob != null)
                    result = await new ConversionService(recovery).ResumeAsync(recoveryJob, reporter, source.Token, ResolveConflictAsync);
                else if (selectedInputs.Length > 0 || File.Exists(FolderPath))
                    result = await new SelectionConversionService(recovery).RunAsync(new(action, CurrentInputs, runOptions, OverwritePolicy)
                    {
                        ValidateBeforeConversion = ValidateBeforeConversion
                    }, reporter, source.Token, ResolveConflictAsync);
                else
                    result = await new ConversionService(recovery).RunAsync(new(action, FolderPath, runOptions), OverwritePolicy, reporter, source.Token, ResolveConflictAsync, ValidateBeforeConversion);
            }
            finally { lock (progressGate) { accepting = false; } }
            retryRequests = result.RetryRequests;
            ValidationFindings = [];
            LastReport = CompletionReport.FromConversion((retries != null ? "Retry failed: " : "") + RunTitle, result);
            OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanViewReport));
        OnPropertyChanged(nameof(CanChooseSource));
            FoundFiles = result.Found;
            Elapsed = result.Elapsed.ToString(@"hh\:mm\:ss");
            if (!result.Cancelled)
            {
                Progress = 100;
                TotalProgress = 100;
            }
            Status = $"{(result.Cancelled ? "Cancelled" : "Processing finished")}: {result.Succeeded} succeeded, {result.Skipped} skipped, {result.Failed} failed.";
            StatusOutcome = RunStatusSummary.From(result);
            foreach (var file in result.Files)
                Activity.Add(new(Elapsed, $"{file.Status}: {file.Source} — {file.Message}"));
            Activity.Add(new(Elapsed, Status));
        }
        catch (OperationCanceledException) { Status = "Cancelled. Unpublished changes were discarded."; Activity.Add(new(Elapsed, Status)); }
        catch (Exception e)
        {
            StatusOutcome = RunStatus.Failure;
            Status = e.Message;
            LastReport = new(RunTitle, DateTimeOffset.Now, false, [new ReportEntry(FolderPath, "Failed", e.Message, [])]);
            Activity.Add(new(Elapsed, Status));
        }
        finally
        {
            cancellation = null;
            IsBusy = false;
            IsRecoveryMode = false;
            try
            {
                RecoveryAvailable = recovery.Find().Count > 0;
                RecoveryMessage = RecoveryAvailable ? "An interrupted run remains. Use Recover to review it." : "";
            }
            catch { /* The operation error remains visible in Status. */ }
        }
    }
    public async Task RetryFailedAsync()
    {
        if (CanRetry)
            await ExecuteAsync(null, retryRequests.ToArray());
    }

    private async Task RunFileOperationAsync()
    {
        using var source = new CancellationTokenSource();
        cancellation = source;
        IsBusy = true;
        CancelRequested = false;
        ShowProgress = true;
        RunTitle = SelectedOperation?.Label ?? "File Operation";
        Activity.Clear();
        LastReport = null;
        retryRequests = [];
        ValidationFindings = [];
        Progress = TotalProgress = FoundFiles = 0;
        Elapsed = "00:00:00";
        Status = "Processing selected folders…";
        bool accepting = true;
        var progressGate = new object();
        try
        {
            // Avoid moving an interrupted conversion's recovery data or its input folders.
            foreach (string input in CurrentInputs)
            {
                string folder = Directory.Exists(input) ? input : Path.GetDirectoryName(Path.GetFullPath(input))!;
                if (recovery.Find(folder, true).Count > 0)
                    throw new IOException("Recover the interrupted conversion before moving files: " + folder);
            }
            var reporter = new Progress<ConversionProgress>(p => { lock (progressGate) { if (accepting) Report(p); } });
            var result = SelectedOperation!.IsFlatten
                ? await new FlattenFolderLayer().RunAsync(CurrentInputs, reporter, source.Token, SelectedOperation.FlattenScope!.Value)
                : await new AlphabeticalOrganizer().RunAsync(CurrentInputs, SelectedOperation.OrganizeMode!.Value, reporter, source.Token,
                    IgnoreLeadingTheForFileOrganization);
            lock (progressGate) { accepting = false; }
            LastReport = CompletionReport.FromConversion(RunTitle, result) with { ReportType = "File Organization Report" };
            FoundFiles = result.Found;
            Elapsed = result.Elapsed.ToString(@"hh\:mm\:ss");
            if (!result.Cancelled) Progress = TotalProgress = 100;
            Status = $"{(result.Cancelled ? "Cancelled" : "File operation finished")}: {result.Succeeded} completed, {result.Skipped} skipped, {result.Failed} failed.";
            StatusOutcome = RunStatusSummary.From(result);
            foreach (var item in result.Files)
                Activity.Add(new(Elapsed, $"{item.Status}: {item.Source} — {item.Message}"));
            Activity.Add(new(Elapsed, Status));
        }
        catch (OperationCanceledException) { Status = "File operation cancelled."; }
        catch (Exception error)
        {
            Status = "File operation stopped: " + error.Message;
            StatusOutcome = RunStatus.Failure;
            LastReport = new(RunTitle, DateTimeOffset.Now, false, [new ReportEntry(FolderPath, "Failed", error.Message, [])]);
        }
        finally { lock (progressGate) { accepting = false; } cancellation = null; IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanValidate))]
    private async Task ValidateAsync()
    {
        using var source = new CancellationTokenSource();
        cancellation = source;
        IsBusy = true;
        CancelRequested = false;
        ShowProgress = true;
        RunTitle = "Validate Karaoke Files";
        Activity.Clear();
        lastMessage = "";
        LastReport = null;
        ValidationFindings = [];
        retryRequests = [];
        Progress = TotalProgress = FoundFiles = 0;
        Status = "Scanning files without changing them...";
        bool accepting = true;
        var progressGate = new object();
        try
        {
            var reporter = new Progress<ConversionProgress>(p => { lock (progressGate) { if (accepting) Report(p); } });
            var result = await new ValidationService().ScanAsync(CurrentInputs, Options.RecurseFolders, reporter, source.Token);
            lock (progressGate) { accepting = false; }
            ValidationFindings = result.Findings;
            retryRequests = [];
            LastReport = CompletionReport.FromValidation(result);
            FoundFiles = result.Findings.Count;
            Elapsed = result.Elapsed.ToString(@"hh\:mm\:ss");
            if (!result.Cancelled)
                Progress = TotalProgress = 100;
            int issues = result.Findings.Count(f => f.Status != ValidationStatus.Valid);
            StatusOutcome = result.Cancelled ? RunStatus.Neutral : issues > 0 || FoundFiles == 0 ? RunStatus.Warning : RunStatus.Success;
            Status = $"Validation {(result.Cancelled ? "cancelled" : "finished")}: {FoundFiles - issues} valid, {issues} need attention. Use View Last Run Report to review.";
            Activity.Add(new(Elapsed, Status));
        }
        catch (OperationCanceledException) { Status = "Validation cancelled. No files were changed."; }
        catch (Exception error)
        {
            Status = "Validation failed: " + error.Message;
            StatusOutcome = RunStatus.Failure;
            LastReport = new(RunTitle, DateTimeOffset.Now, false, [new ReportEntry(FolderPath, "Unreadable", error.Message, [])]);
        }
        finally { lock (progressGate) { accepting = false; } cancellation = null; IsBusy = false; }
    }

    private void Report(ConversionProgress report)
    {
        Progress = report.FilePercent;
        TotalProgress = report.TotalPercent;
        FoundFiles = report.Found;
        Elapsed = report.Elapsed;
        Status = report.Message;
        if (lastMessage != Status)
        {
            Activity.Add(new(Elapsed, Status));
            lastMessage = Status;
        }
    }
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        CancelRequested = true;
        cancellation?.Cancel();
        Status = "Cancellation requested. Finishing any publication already in progress.";
    }
}





