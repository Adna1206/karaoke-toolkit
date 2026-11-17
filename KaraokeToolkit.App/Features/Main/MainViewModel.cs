using KaraokeToolkit.Core.Execution;
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
using Action = KaraokeToolkit.Core.Execution.OperationId;
namespace KaraokeToolkit.App.Features.Main;

public sealed record ActivityEntry(string Time, string Message);
/// <summary>Coordinates main-screen selection, preferences, commands and presentation of processing results.</summary>
/// <remarks>Core services own file processing and recovery guarantees. ProcessingRun owns each run's
/// cancellation and progress lifetime; this viewmodel owns the corresponding busy state and reports.</remarks>
public partial class MainViewModel : ViewModelBase
{
    private ProcessingRun? activeRun;
    private readonly ApplicationSettingsStore store;
    private bool sectionsReady;
    public IReadOnlyList<OperationSectionViewModel> OperationSections { get; }
    public IReadOnlyList<OperationSectionViewModel> VisibleSections => OperationSections.Where(section => section.IsVisible).ToArray();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(VisibleOperations)), NotifyPropertyChangedFor(nameof(SelectedSectionTitle))]
    public partial OperationSectionViewModel? SelectedSection { get; set; }
    public IReadOnlyList<ActionChoice> VisibleOperations => SelectedSection?.Operations ?? [];
    public string SelectedSectionTitle => SelectedSection?.Header ?? "Choose an operation";
    public bool NeedsSource => SelectedOperation?.Definition.AllowsMultipleSources == true;
    public bool CanClearInputs => CanEdit && !string.IsNullOrWhiteSpace(FolderPath);
    public string SourceInstruction => SelectedOperation?.Definition.Inputs switch
    {
        OperationInputKind.SingleDisc => "Click Start to choose a drive, tracks and output folder.",
        OperationInputKind.SingleManifest => "Click Start to choose a damaged batch manifest to restore.",
        _ => ""
    };
    public IReadOnlyList<ActionChoice> FavouriteOperations => Operations.Concat(RecipeChoices).Where(operation => operation.Available && operation.IsFavourite &&
        (!operation.IsRip || Options.ExperimentalDiscRipping)).ToArray();
    private readonly RecoveryStore recovery;
    private string[] selectedInputs = [];
    private bool settingInputs;
    private IReadOnlyList<ConversionRetry> retryRequests = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SourceSummary))]
    public partial string InputSummary { get; set; } = "Drop files or folders here, or use Add Folders / Add Files.";
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
    internal RecoveryVerificationMode RecoveryVerification { get; private set; }
    internal bool SaveResumableRun { get; private set; } = true;
    internal bool CheckForUpdatesOnStartup { get; private set; }
    public bool RememberInputFolder { get; private set; }
    private string? lastInputFolder;
    internal string? InputPickerStartFolder => RememberInputFolder && Path.IsPathFullyQualified(lastInputFolder ?? "") &&
        Directory.Exists(lastInputFolder) ? lastInputFolder : null;
    public bool CanValidate => !IsBusy && !string.IsNullOrWhiteSpace(FolderPath);

    public void SetInputs(IEnumerable<string> paths, bool suggestOperation = true)
    {
        if (IsBusy)
            return;
        var normalized = SelectedOperation?.IsTagging == true ? KaraokeToolkit.Core.Tagging.KaraokeTaggingService.NormalizeInputs(paths)
            : SelectedOperation?.Recipe is { } recipe
            ? KaraokeToolkit.Core.Recipes.RecipeOperations.NormalizeInputs(recipe, paths)
            : SelectedOperation is { IsOrganizer: true } or { IsFlatten: true } or { IsTrackGrouping: true }
            ? paths.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray()
            : InputSelection.Normalize(paths);
        if (normalized.Length == 0 || normalized.Any(p => !File.Exists(p) && !Directory.Exists(p)))
            throw new ArgumentException("Select existing files or folders.");
        if ((!suggestOperation || SelectedOperation?.Definition.Inputs == OperationInputKind.Folders) &&
            GetSourceError(normalized) is { } error)
            throw new ArgumentException(error);
        settingInputs = true;
        try
        {
            FolderPath = normalized[0];
            selectedInputs = normalized;
        }
        finally { settingInputs = false; }
        InputSummary = normalized.Length == 1 ? "Selected: " + normalized[0] : normalized.Length + " selected inputs: " + string.Join("; ", normalized.Select(Path.GetFileName));
        var extensions = normalized.Where(File.Exists).Select(p => Path.GetExtension(p).ToLowerInvariant()).Distinct().ToArray();
        if (suggestOperation && SelectedOperation is not { IsOrganizer: true } and not { IsFlatten: true } and not { IsRecipe: true } and not { IsTagging: true } and not { IsTrackGrouping: true } && normalized.All(File.Exists))
        {
            if (extensions.All(e => e == ".zip") && SelectedOperation?.Action != Action.RepackZip)
                SelectedOperation = Operations.First(x => x.Action == Action.ZipToAudioG);
            else if (extensions.All(e => e == ".bin"))
                SelectedOperation = Operations.First(x => x.Action == Action.BinToAudioG);
            else if (normalized.All(KaraokeToolkit.Core.Archives.ArchiveZipConverter.IsInput))
                SelectedOperation = Operations.First(x => x.Action == Action.ArchiveToZip);
            else if (extensions.All(e => e is not (".zip" or ".bin")))
                SelectedOperation = Operations.First(x => x.Action == Action.AudioGToZip);
        }
        ValidateCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanClearInputs));
        ClearInputsCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
    }
    public void AddInputs(IEnumerable<string> paths)
        // The home workflow chooses an operation first; adding inputs must not replace that choice.
        => SetInputs(string.IsNullOrWhiteSpace(FolderPath) ? paths : CurrentInputs.Concat(paths), suggestOperation: false);

    internal void AddPickedInputs(string[] paths)
    {
        if (IsBusy || paths.Length == 0) return;
        AddInputs(paths);
        if (!RememberInputFolder) return;

        string first = Path.GetFullPath(paths[0]);
        string folder = Directory.Exists(first) ? first : Path.GetDirectoryName(first)!;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(folder, lastInputFolder, comparison)) return;

        try
        {
            store.Save(Options, lastInputFolder: folder);
            lastInputFolder = folder;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        { Status = "Could not remember the input folder: " + error.Message; }
    }

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
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FooterStatusMessage)), NotifyPropertyChangedFor(nameof(FooterStatusKind))]
    public partial bool ShowRecoveryCheckProgress { get; private set; }
    private string lastMessage = "";
    private string? activityRoot;
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
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ActionDescription)), NotifyPropertyChangedFor(nameof(CanChooseSource)), NotifyPropertyChangedFor(nameof(CanChooseFiles)), NotifyPropertyChangedFor(nameof(SourceSummary)), NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial ActionChoice? SelectedOperation
    {
        get; set;
    }
    public string ActionDescription => SelectedOperation?.Description ?? "Choose an action to continue.";
    public string SourceLabel => SelectedOperation?.Definition.Inputs == OperationInputKind.Folders ? "Source folders" : "Source files or folders";
    public string SourceSummary => string.IsNullOrWhiteSpace(FolderPath)
        ? SelectedOperation?.Definition.Inputs == OperationInputKind.Folders
            ? "Select one or more folders, or type a folder path."
            : "Select one or more files or folders, or type a path."
        : GetSourceError(CurrentInputs) ?? InputSummary;

    private string? GetSourceError(IEnumerable<string> inputs)
    {
        if (SelectedOperation is not { Definition.AllowsMultipleSources: true } operation)
            return null;
        var extensions = operation.SourceFileExtensions;
        foreach (string path in inputs)
        {
            if (Directory.Exists(path)) continue;
            if (extensions?.Count == 0)
                return "This operation requires folders. Clear the selection or enter an existing folder path.";
            if (!File.Exists(path))
                return "Choose an existing file or folder.";
            if (extensions != null && !extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                return $"{Path.GetFileName(path)} is unsupported for this operation. Select {string.Join(", ", extensions)} files or folders.";
        }
        return null;
    }
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
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FooterStatusMessage))]
    public partial string Status { get; set; } = "Choose an operation and its inputs, then click Start.";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FooterStatusKind))]
    public partial RunStatus StatusOutcome { get; set; }
    public string FooterStatusMessage => ShowRecoveryCheckProgress ? "Checking for interrupted runs…" : Status;
    public StatusKind FooterStatusKind => ShowRecoveryCheckProgress ? StatusKind.Info : StatusOutcome switch
    {
        RunStatus.Success => StatusKind.Success,
        RunStatus.Warning => StatusKind.Warning,
        RunStatus.Failure => StatusKind.Failure,
        _ => StatusKind.Info
    };

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
    public bool CanChooseSource => CanEdit && NeedsSource;
    public bool CanChooseFiles => CanChooseSource && SelectedOperation?.Definition.Inputs == OperationInputKind.FilesAndFolders;
    public bool CanStart => !IsBusy && !ShowProgress && SelectedOperation?.Available == true &&
        (SelectedOperation.IsRip ? Options.ExperimentalDiscRipping :
            SelectedOperation.IsRestore || !string.IsNullOrWhiteSpace(FolderPath) &&
            GetSourceError(CurrentInputs) == null &&
            (SelectedOperation.Recipe == null || KaraokeToolkit.Core.Recipes.RecipeOperations.CanAcceptInputs(SelectedOperation.Recipe, CurrentInputs)));
    public bool CanCancel => IsBusy && !CancelRequested;
    public MainViewModel() : this(UserDataPaths.DesktopSettingsPath(), new RecoveryStore()) { }
    public MainViewModel(string settingsPath) : this(settingsPath, null) { }

    private MainViewModel(string settingsPath, RecoveryStore? recoveryStore)
    {
        RecipeDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!, "Recipes");
        store = new(settingsPath, persistRenameBins: true);
        OperationSections = OperationDefinitions.Sections.Select(group => new OperationSectionViewModel(this, group.Section, group.Header,
            Operations.Where(operation => operation.Available && operation.Section == group.Section).ToArray())).ToArray();
        recovery = recoveryStore ?? new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!, "Recovery"));
        SelectedOperation = Operations.Single(operation => operation.Action == Action.AudioGToZip);
        try
        {
            Options = store.Load();
            (RecoveryVerification, SaveResumableRun) = store.LoadRecoveryPreferences();
            CheckForUpdatesOnStartup = store.LoadCheckForUpdatesOnStartup();
            (RememberInputFolder, lastInputFolder) = store.LoadInputFolderPreference();
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
        => SaveOptions(options, policy, theme, validateBeforeConversion, ignoreLeadingTheForFileOrganization, audioBitrates, backdrop, RememberInputFolder);

    internal void SaveOptions(ConversionOptions options, OverwritePolicy policy, ApplicationTheme theme, bool validateBeforeConversion,
        bool ignoreLeadingTheForFileOrganization, AudioBitratePreferences audioBitrates, ApplicationBackdrop backdrop, bool rememberInputFolder, RecoveryVerificationMode? recoveryVerification = null, bool? saveResumableRun = null, bool? checkForUpdatesOnStartup = null)
    {
        store.Save(options, policy, theme, validateBeforeConversion, ignoreLeadingTheForFileOrganization, audioBitrates,
            backdrop: backdrop, rememberInputFolder: rememberInputFolder, recoveryVerification: recoveryVerification, saveResumableRun: saveResumableRun, checkForUpdatesOnStartup: checkForUpdatesOnStartup);
        RecoveryVerification = recoveryVerification ?? RecoveryVerification;
        SaveResumableRun = saveResumableRun ?? SaveResumableRun;
        CheckForUpdatesOnStartup = checkForUpdatesOnStartup ?? CheckForUpdatesOnStartup;
        RememberInputFolder = rememberInputFolder;
        if (!rememberInputFolder) lastInputFolder = null;
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
        if (!options.ExperimentalDiscRipping && SelectedOperation?.IsRip == true) SelectedOperation = Operations.Single(operation => operation.Action == Action.AudioGToZip);
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
        if (SelectedOperation?.IsTrackGrouping == true)
        {
            await RunTrackGroupingAsync();
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
            ShowRecoveryCheckProgress = folder != null;
            var jobs = await Task.Run(() => recovery.Find(folder, Options.RecurseFolders));
            ShowRecoveryCheckProgress = false;
            RecoveryAvailable = jobs.Count > 0;
            RecoveryMessage = RecoveryAvailable ? $"Recovery available: {jobs.Count} interrupted run(s)." : "";
            if (!RecoveryAvailable)
                return false;
            StatusOutcome = RunStatus.Warning;
            Status = RecoveryMessage;
            if (ConfirmRecoveryAsync != null && await ConfirmRecoveryAsync(jobs[0]))
                resume = jobs[0];
        }
        catch (Exception error) { StatusOutcome = RunStatus.Failure; Status = "Could not inspect recovery files: " + error.Message; return true; }
        finally
        {
            ShowRecoveryCheckProgress = false;
            IsBusy = false;
        }
        if (resume != null)
        try
        {
            if (resume.Kind == RecoveryKind.Recipe) await ResumeRecipeAsync(resume);
            else if (resume.Kind == RecoveryKind.Tagging) await RunTaggingAsync(resume);
            else if (resume.Kind == RecoveryKind.FileMoves) await RunFileOperationAsync(resume);
            else if (resume.Kind == RecoveryKind.TrackGrouping) await RunTrackGroupingAsync(resume);
            else await ExecuteAsync(resume);
        }
        catch (Exception error) { StatusOutcome = RunStatus.Failure; Status = "Recovery stopped: " + error.Message; }
        return true; // Later never starts a fresh run over an interrupted one.
    }

    /// <summary>Starts a processing lifetime and resets the shared busy, progress and report presentation.</summary>
    private ProcessingRun BeginRun(string title, string status)
    {
        if (activeRun != null) throw new InvalidOperationException("A processing run is already active.");
        var run = new ProcessingRun(Report);
        activeRun = run;
        try
        {
            IsBusy = true;
            CancelRequested = false;
            ShowProgress = true;
            RunTitle = title;
            Activity.Clear();
            lastMessage = "";
            activityRoot = string.IsNullOrWhiteSpace(FolderPath) ? null
                : Directory.Exists(FolderPath) ? Path.GetFullPath(FolderPath)
                : Path.GetDirectoryName(Path.GetFullPath(FolderPath));
            LastReport = null;
            ValidationFindings = [];
            retryRequests = [];
            Progress = TotalProgress = FoundFiles = 0;
            Elapsed = "00:00:00";
            Status = status;
            return run;
        }
        catch
        {
            EndRun(run);
            throw;
        }
    }

    /// <summary>Disposes progress/cancellation before returning the active run's screen state to idle.</summary>
    private void EndRun(ProcessingRun? run)
    {
        run?.Dispose();
        activeRun = null;
        IsBusy = false;
        IsRecoveryMode = false;
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
        var runOptions = action == Action.LosslessGToMp3G
            ? Options with { AudioBitrate = AudioBitrates.Mp3 }
            : Options;
        var run = BeginRun((recoveryJob != null ? "Recovery — " : "") + selected.Label, "Starting conversion…");
        IsRecoveryMode = recoveryJob != null;
        RecoveryAvailable = IsRecoveryMode;
        RecoveryMessage = IsRecoveryMode ? "Recovery mode — checking interrupted publication and resuming unfinished work." : "";
        try
        {
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
                        if (run.Token.IsCancellationRequested)
                        {
                            cancelled = true;
                            break;
                        }
                        var attempt = await new SelectionConversionService(recovery).RunAsync(retry, run.Progress, run.Token, ResolveConflictAsync);
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
                    result = await new ConversionService(recovery).ResumeAsync(recoveryJob, run.Progress, run.Token, ResolveConflictAsync);
                else if (selectedInputs.Length > 0 || File.Exists(FolderPath))
                    result = await new SelectionConversionService(recovery).RunAsync(new(action, CurrentInputs, runOptions, OverwritePolicy)
                    {
                        ValidateBeforeConversion = ValidateBeforeConversion
                    }, run.Progress, run.Token, ResolveConflictAsync);
                else
                    result = await new ConversionService(recovery).RunAsync(new(action, FolderPath, runOptions), OverwritePolicy, run.Progress, run.Token, ResolveConflictAsync, ValidateBeforeConversion);
            }
            finally { run.StopProgress(); }
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
            AppendActivityIssues(result.Files);
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
            EndRun(run);
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

    private async Task RunFileOperationAsync(RecoveryJob? job = null)
    {
        var run = BeginRun(job == null ? SelectedOperation?.Label ?? "File Operation" : "Recovery — " + job.Action,
            "Preparing file moves…");
        IsRecoveryMode = job != null;
        if (job != null)
        {
            try
            {
                var resumed = await new OperationExecutionService(recovery).ResumeAsync(job.JournalPath, run.Progress, run.Token);
                run.StopProgress();
                LastReport = resumed.Report;
                FoundFiles = resumed.Found;
                int succeeded = resumed.Report.Entries.Count(entry => entry.Status == "Succeeded");
                int failed = resumed.Report.Entries.Count(entry => entry.Status is "Failed" or "Blocked");
                int skipped = resumed.Report.Entries.Count(entry => entry.Status == "Skipped");
                StatusOutcome = RunStatusSummary.From(resumed.Report.Cancelled, succeeded, failed, skipped);
                if (!resumed.Report.Cancelled) Progress = TotalProgress = 100;
                Status = $"{(resumed.Report.Cancelled ? "File move recovery cancelled" : "File move recovery finished")}: {succeeded} completed, {skipped} skipped, {failed} failed.";
                Activity.Add(new(Elapsed, Status));
            }
            catch (Exception error)
            {
                Status = "File move recovery stopped: " + error.Message;
                StatusOutcome = RunStatus.Failure;
            }
            finally { EndRun(run); RecoveryAvailable = recovery.Find().Count > 0; }
            return;
        }
        try
        {
            // Avoid moving an interrupted conversion's recovery data or its input folders.
            foreach (string input in CurrentInputs)
            {
                string folder = Directory.Exists(input) ? input : Path.GetDirectoryName(Path.GetFullPath(input))!;
                if (recovery.Find(folder, true).Count > 0)
                    throw new IOException("Recover the interrupted conversion before moving files: " + folder);
            }
            var result = SelectedOperation!.IsFlatten
                ? await new FlattenFolderLayer().RunAsync(CurrentInputs, run.Progress, run.Token, SelectedOperation.FlattenScope!.Value, RecoveryVerification, SaveResumableRun)
                : await new AlphabeticalOrganizer().RunAsync(CurrentInputs, SelectedOperation.OrganizeMode!.Value, run.Progress, run.Token,
                    IgnoreLeadingTheForFileOrganization, RecoveryVerification, SaveResumableRun);
            run.StopProgress();
            LastReport = CompletionReport.FromConversion(RunTitle, result) with { ReportType = "File Organization Report" };
            FoundFiles = result.Found;
            Elapsed = result.Elapsed.ToString(@"hh\:mm\:ss");
            if (!result.Cancelled) Progress = TotalProgress = 100;
            Status = $"{(result.Cancelled ? "Cancelled" : "File operation finished")}: {result.Succeeded} completed, {result.Skipped} skipped, {result.Failed} failed.";
            StatusOutcome = RunStatusSummary.From(result);
            AppendActivityIssues(result.Files);
            Activity.Add(new(Elapsed, Status));
        }
        catch (OperationCanceledException) { Status = "File operation cancelled."; }
        catch (Exception error)
        {
            Status = "File operation stopped: " + error.Message;
            StatusOutcome = RunStatus.Failure;
            LastReport = new(RunTitle, DateTimeOffset.Now, false, [new ReportEntry(FolderPath, "Failed", error.Message, [])]);
        }
        finally
        {
            EndRun(run);
            RecoveryAvailable = recovery.Find().Count > 0;
        }
    }

    [RelayCommand(CanExecute = nameof(CanValidate))]
    private async Task ValidateAsync()
    {
        var run = BeginRun("Validate Karaoke Files", "Scanning files without changing them...");
        try
        {
            var result = await new ValidationService().ScanAsync(CurrentInputs, Options.RecurseFolders, run.Progress, run.Token);
            run.StopProgress();
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
        finally { EndRun(run); }
    }

    private void Report(ConversionProgress report)
    {
        Progress = report.FilePercent;
        TotalProgress = report.TotalPercent;
        FoundFiles = report.Found;
        Elapsed = report.Elapsed;
        if (!report.ShowInActivity || IsRoutineSkip(report.ActivityLabel, report.ActivityDetail)) return;
        Status = report is { ActivityLabel: { } label, ActivitySource: { } source }
            ? FormatActivityMessage(label, source, report.ActivityDetail) : report.Message;
        if (lastMessage != Status)
        {
            Activity.Add(new(Elapsed, Status));
            lastMessage = Status;
        }
    }

    private static bool IsRoutineSkip(string? label, string? detail)
        => label == nameof(ConversionStatus.Skipped) && detail == "Names already clean.";

    private string FormatActivityMessage(string label, string source, string? detail)
    {
        string path = RelativeActivityPath(source, activityRoot);
        string? reason = detail?.Split(['\r', '\n'], 2)[0].Trim();
        return string.IsNullOrWhiteSpace(reason) ? $"{label}: {path}" : $"{label}: {path} — {reason}";
    }

    internal static string RelativeActivityPath(string source, string? root)
    {
        if (root == null || !Path.IsPathFullyQualified(source)) return source;
        string relative = Path.GetRelativePath(root, source);
        if (relative == ".")
        {
            string folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
            return folder.Length == 0 ? root : folder;
        }
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return source;
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    // Completion can suppress progress callbacks still waiting on the UI dispatcher.
    private void AppendActivityIssues(IEnumerable<ConversionFileResult> files, string prefix = "")
    {
        var logged = Activity.Select(entry => entry.Message).ToHashSet(StringComparer.Ordinal);
        foreach (var file in files)
        {
            string label = file.Status.ToString();
            if (file.Status is not (ConversionStatus.Skipped or ConversionStatus.Failed) || IsRoutineSkip(label, file.Message))
                continue;
            string message = FormatActivityMessage(prefix + label, file.Source, file.Message);
            if (logged.Add(message))
                Activity.Add(new(Elapsed, message));
        }
    }
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        CancelRequested = true;
        activeRun?.Cancel();
        Status = "Cancellation requested. Finishing any publication already in progress.";
    }
}





