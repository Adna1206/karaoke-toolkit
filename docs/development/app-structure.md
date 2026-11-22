# App structure

App groups related screens and supporting code by feature. Namespaces match
directories, and views share their feature namespace with their view models.

| Folder | Responsibility |
| --- | --- |
| Features/Main | Main window, operation selection, favourites, progress and workflow coordination |
| Features/Settings | Settings window and view model, UI preferences, persistence and window layout |
| Features/Settings/Integrations | Windows Explorer registration, CLI examples and terminal launching, and MCP executable and client configuration |
| Features/About | About window, application credits and bundled licence notice presentation |
| Features/Reports | Session report window and its view model |
| Features/DiscRipping | Disc ripping window and its view model |
| Features/Recipes | Recipe library, builder and per-step settings editor |
| Features/Tagging | Filename-pattern configuration, editable tag preview and tag submission |
| Metadata | Shared operation and setting definitions |
| Services | Explorer integration, folder launching and Velopack update checks against GitHub Releases |
| Shared | View model base, view locator, window titles and modal dialog presentation |
| Assets | Logos, icons and other bundled visuals |

Startup files, global styling, assembly configuration and the application
manifest stay at the project root. Code used by one feature belongs inside that
feature; code used by several features belongs in an appropriate shared folder.
Core continues to own processing and remains independent of App and Avalonia.

Keep each app-owned dialog in its own named file. Standalone XAML dialogs use
the feature's `*Window.axaml` and code-behind pair. Put short, code-built
prompts in the owning feature's `Dialogs/` folder as `*Dialog.cs` files. Keep
the prompt's wording and controls there, and call it from the view that owns
the workflow. Reuse `Shared.PromptDialog` for their common layout. Each
prompt supplies its own footer buttons. Use `ConfirmAsync` for Cancel and
confirm, or `Create` when a prompt needs a different set of actions.
Configure native pickers at the call site through `Shared.ModalDialog`.

Use `Shared.BusyNavigation` for Close and Back from a window with active work.
It checks the owning view model's `IsBusy`, prompts asynchronously, and
waits for the operation to settle before leaving. Supply the existing
cancellation action where the workflow supports it.

`Shared/Typography.axaml` defines the `heading1`, `heading2`, and `heading3`
classes for page, section, and subsection headings. It also defines the size
for small text and the font family and size for Windows captions. Content
inherits the app's Fluent/Inter typography.

`Shared/ModalDialog` wraps modal windows and native file/folder pickers. It adds
a translucent smoke layer to the blocked owner's Avalonia overlay layer and
removes it in `finally`, including failed or cancelled opens. The layer tracks
the owner's size and works independently of colour theme or desktop backdrop.
Nested dialogs dim their immediate owner; overlapping dialogs on one owner
share one layer until the last closes. Actual input blocking remains the dialog
or native picker's responsibility. Templates without an overlay layer retain
normal modality without dimming.

OperationPickerView owns the category sidebar, operation list, descriptions
and arrow-key navigation. MainWindow supplies the source panel and owns the
file and folder picker dialogs.

The main window uses a conventional File / Tools / Help menu, compact branding,
a category sidebar and one operation list. Favourite operations share the same
instances as the normal categories. Selecting a category selects an operation
within it; external input routing and recovery keep navigation synchronized.
On Windows, the main window extends its client area into the title bar and
places the menu beside the app icon and name. The Tools pane starts below the
title bar. Avalonia supplies the caption buttons and the remaining space supports
dragging. Other platforms keep the standard menu row and header.
On Windows, secondary windows and dialogs share the transparent caption theme
and inset their content below the Avalonia-drawn title bar.
Add Folders, Add Files and drop actions accumulate sources without replacing the
chosen operation. Clear resets the selection. Source-free operations display
their own instructions. Status, reports and Start/Cancel remain in the footer.
The category is session state; old expander preferences remain stored for
compatibility but are no longer used by the home layout.

Tools also opens the Recipe Builder. Core's `Recipes` folder owns the portable
workflow definitions, chaining constraints and execution; App coordinates runs
through the existing main progress/report screen. See [recipes.md](../user/recipes.md).
The builder edits saved recipes only. Its library populates the home Recipes
category and Favourites; input controls follow the selected recipe's first step.

`OperationProgressView` hosts the active run title, source, activity table and progress indicators. `MainWindow` controls when it is visible.

`ActivityLogView` owns the live table layout and attaches auto-scroll while loaded. Its entries come from `MainViewModel.Activity`. Completed reports use `ResultsWindow`.

`ProcessingRun` owns cancellation and UI-dispatched progress for one processing
run. Conversion/retry/recovery, validation, file operations, tagging and recipes
use the same begin/end setup in `MainViewModel`. Progress is stopped before successful
results are presented and always disposed before the UI becomes idle, so queued callbacks
cannot overwrite a completed report or a subsequent run. Outcome text, reports,
retry scopes and recovery discovery remain specific to each workflow. Tagging's
configuration dialog, disc ripping and quarantine restoration retain their
separate modal busy-state handling.

The view locator resolves a feature's `*ViewModel` to its `*Window` in the same
namespace. Window creation and operation execution use explicit routes. Test folders
follow the corresponding Core responsibility or App feature; shared fixtures and
opt-in benchmarks have their own folders in `KaraokeToolkit.Tests`.
