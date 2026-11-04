# Operation and setting definitions

`KaraokeToolkit.App/Metadata` contains immutable presentation definitions. These
belong to App because they describe the user interface; Core owns execution,
format support and validation.

- `OperationDefinitions.cs`: operation IDs, names, short and full descriptions,
  categories, availability, input selection scope and routing information. The
  same definitions serve the normal sections and Favourites.
- `SettingsDefinitions.cs`: setting IDs, labels, concise notes, full hover help,
  categories and displayed choices. The Options layout binds to these definitions
  through `OptionsViewModel.Definitions`; it remains hand-authored XAML.

Built-in definitions take Core's `OperationId` and derive their existing saved
string IDs through the shared catalogue. Presentation labels remain in App;
dynamic saved recipes keep `Recipe:<GUID>` IDs. See [operation contracts](operations.md)
for capabilities, general/conversion requests and explicit CLI exposure decisions.

`ActionChoice` adds per-user favourite state to a shared operation definition.
Favourites are saved separately by stable ID. The home category sidebar uses
session selection; old section expansion preferences are retained for compatibility.
Renaming a label does not change a saved favourite. Keep IDs stable when editing
definitions, and do not add user state to the catalogues.

The settings dialog remembers its last-used tab only for the lifetime of the main
window. Its normal size is saved in logical pixels across restarts, including when
the dialog is cancelled. This saves only presentation state and already-applied
preferences. On opening, the size and position are constrained to the owner's
screen working area, accounting for display scaling. Invalid saved dimensions
fall back to the default size; maximized/minimized dimensions are not remembered.

Defaults remain in the typed `ConversionOptions`, `AudioBitratePreferences` and
settings-store fallbacks. Presentation defaults and persistence live in
`KaraokeToolkit.App/Features/Settings`; processing defaults and persistence remain in Core.
The App store adds its XML entries inside Core's atomic save transaction, retaining
compatibility with existing settings files. The catalogues do not introduce
another copy of defaults.
Colour theme (System/Light/Dark) and window backdrop (Automatic/Solid/Mica/Acrylic)
are independent App preferences, saved together when Apply or OK is clicked.
Automatic prefers Mica, then Acrylic on Windows. Explicit effects fall back to
Solid when unavailable; other platforms currently use Solid. All open and newly
opened windows use the applied preference, including dialogs. The actual OS
transparency level controls whether the background is transparent, with a themed
opaque fallback for suppressed effects. Reset stages System and Automatic.
Core validators remain authoritative; tests verify that displayed choices include
the defaults and are accepted by those validators. Input metadata controls the
source picker scope, not the detailed format validation performed by Core.

To add an operation, define its metadata and implement its execution route, then
extend the relevant workflow tests. To change setting wording, edit its catalogue
entry; labels, notes and hover descriptions will use that entry everywhere they
are bound. New settings still need typed values, persistence and validation.

The Recipe Builder reuses these operation labels and setting definitions. Core's
`RecipeOperations` supplies execution and chaining constraints by the same stable
operation IDs. Register recipe support there only when an operation has a
non-interactive input/output contract; catalog tests check the builder's supported
IDs. See [recipes.md](recipes.md) for settings snapshots and output flow.
