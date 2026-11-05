# App structure

App groups related screens and supporting code by feature. Namespaces match
directories, and views share their feature namespace with their view models.

| Folder | Responsibility |
| --- | --- |
| Features/Main | Main window, operation selection, favourites, progress and workflow coordination |
| Features/Settings | Options window and view model, UI preferences, persistence and window layout |
| Features/Settings/Integrations | CLI examples/terminal launching and MCP executable/client configuration; explicit clipboard actions |
| Features/About | About window, application credits and bundled licence notice presentation |
| Features/Reports | Session report window and its view model |
| Features/DiscRipping | Disc ripping window and its view model |
| Features/Recipes | Recipe library, builder and per-step settings editor |
| Features/Tagging | Filename-pattern configuration, editable tag preview and tag submission |
| Metadata | Shared operation and setting definitions |
| Services | Explorer integration, folder launching and the disabled update-check scaffold |
| Shared | View model base, view locator, window titles and modal dialog presentation |
| Assets | Logos, icons and other bundled visuals |

Startup files, global styling, assembly configuration and the application
manifest stay at the project root. Code used by one feature belongs inside that
feature; code used by several features belongs in an appropriate shared folder.
Core continues to own processing and remains independent of App and Avalonia.

`Shared/ModalDialog` wraps modal windows and native file/folder pickers. It adds
a translucent smoke layer to the blocked owner's Avalonia overlay layer and
removes it in `finally`, including failed or cancelled opens. The layer tracks
the owner's size and works independently of colour theme or desktop backdrop.
Nested dialogs dim their immediate owner; overlapping dialogs on one owner
share one layer until the last closes. Actual input blocking remains the dialog
or native picker's responsibility. Templates without an overlay layer retain
normal modality without dimming.

The main window uses a conventional File / Tools / Help menu, compact branding,
a category sidebar and one operation list. Favourite operations share the same
instances as the normal categories. Selecting a category selects an operation
within it; external input routing and recovery keep navigation synchronized.
Add Folders, Add Files and drop actions accumulate sources without replacing the
chosen operation. Clear resets the selection. Source-free operations display
their own instructions. Status, reports and Start/Cancel remain in the footer.
The category is session state; old expander preferences remain stored for
compatibility but are no longer used by the home layout.

Tools also opens the Recipe Builder. Core's `Recipes` folder owns the portable
workflow definitions, chaining constraints and execution; App coordinates runs
through the existing main progress/report screen. See [recipes.md](recipes.md).
The builder edits saved recipes only. Its library populates the home Recipes
category and Favourites; input controls follow the selected recipe's first step.

The view locator resolves a feature's `*ViewModel` to its `*Window` in the same
namespace. Window creation and operation execution use explicit routes. Test folders
follow the corresponding Core responsibility or App feature; shared fixtures and
opt-in benchmarks have their own folders in `KaraokeToolkit.Tests`.
