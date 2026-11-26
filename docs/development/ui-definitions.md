# Desktop UI guidelines

## Dialog design

Use modal dialogs for decisions that block the current task, important
acknowledgements, and explicit requests to open a tool or reference window.
Show routine feedback in the status bar or beside the relevant control.

- Start with a visible heading that names the task, decision, or outcome. Use
  `heading2` for short prompts and `heading1` for larger tool windows.
- Keep the body focused on what the user needs to decide or do. Summarize the
  immediate consequence and put long file lists in a bounded scrolling area.
  Use labelled sections, tabs, or field help for larger forms.
- Use a shared footer. Place dismissal to the left of the primary action
  and give each action a label that describes the result. Use a single Close
  or acknowledgement button when there is no decision.
- Enable confirmation when the required choice or input is ready. Focus the
  safe action when a confirmation opens and use the shared red
  `destructive` style for actions that discard, delete, or overwrite.

Design reference. [Povio, Dos and Don'ts of Product Design: Dialogs](https://povio.com/blog/product-design-dialogs)

## Shared presentation

Use App's shared `WindowSpacing` values for the 24-DIP horizontal and 14-DIP
vertical inset in dialogs and the main footer, and the 10-DIP gap between
footer buttons.
Use the shared `windowFooter` border style for the divider above bottom
actions. Add the `dialog` class when the footer sits inside an inset dialog
grid. Ordinary labels and controls inherit Fluent typography. Use the shared
heading, small-text, caption and code styles for their respective roles.
Use `Shared.StatusBar` for footer messages with an icon. Bind `Message` and
`Kind` to the current text and severity. The supported kinds are Info,
Warning, Failure and Success. Messages that exceed the available width
scroll horizontally, pausing at each end. New messages start at the beginning.
Put footer status and progress inside the footer divider. Place the progress
bar to the right of its message using the shared `StatusProgressGap` and
`StatusProgressWidth`.
Use one status bar per window. Publish new feedback into its current message
and severity instead of adding another bar.

Use a 16-DIP monochrome icon beside the label on main menu commands. Reuse
the same geometry at 22 DIP next to the corresponding window heading.
Use an icon badge for short message and confirmation dialogs, with warning
color for destructive confirmations. Menu categories and settings tabs use
text labels.

Open app dialogs through `ModalDialog.ShowAsync` so Escape dismisses them
after focused controls have had a chance to handle the key. Escape uses the
normal window close path, including any close guards.
Use `Shared.PromptDialog` for short app-owned prompts. Native pickers use the
platform's layout and a task-specific title.

## Operation and setting definitions

`KaraokeToolkit.App/Metadata` contains immutable presentation definitions. These
belong to App because they describe the user interface; Core owns execution,
format support and validation.

- `OperationDefinitions.cs`: operation IDs, names, short and full descriptions,
  categories, availability, input selection scope and routing information. The
  same definitions serve the normal sections and Favourites.
- `SettingsDefinitions.cs`: setting IDs, labels, concise notes, full hover help,
  categories and displayed choices. The Options layout binds to these definitions
  through `SettingsViewModel.Definitions`; it remains hand-authored XAML.

Desktop input definitions distinguish a batch of files and folders, a batch of
folders, a single disc, and a single quarantine manifest. Batch operations accept
one or more sources through the home screen. Ripping and restoration select their
single input after Start. Saved recipes use the first step's input rules.
Conversion pickers and the conversion planner share the accepted file extensions.
The home screen checks these extensions and path types before enabling Start.

Built-in definitions take Core's `OperationId` and derive their existing saved
string IDs through the shared catalogue. Presentation labels remain in App;
dynamic saved recipes keep `Recipe:<GUID>` IDs. See [operation contracts](operations.md)
for capabilities, general/conversion requests and explicit CLI exposure decisions.

`ActionChoice` adds per-user favourite state to a shared operation definition.
Favourites are saved separately by stable ID. The home category sidebar keeps
its selection for the current session. Each category has a shared definition
that supplies its display label. Operations and navigation use that same definition.
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
On Windows 11, Automatic tries Mica, then Acrylic, then Solid. Automatic uses
Solid elsewhere. Acrylic uses Dark with the System theme preference, and an
explicit Light choice overrides it. Other materials use the saved theme.
Explicit effects fall back to
Solid when unavailable; other platforms currently use Solid. All open and newly
opened windows use the applied preference, including dialogs. The actual OS
transparency level controls whether the background is transparent, with a themed
opaque fallback for suppressed effects. Reset stages System and Automatic.
Core validators remain authoritative; tests verify that displayed choices include
the defaults and are accepted by those validators. Input metadata controls the
source picker scope, not the detailed format validation performed by Core.

## Activity and preview tables

Use `Shared.TableView` for the activity log and operation preview tables. It
provides the frame, header styling and row dividers. Each view defines the
column layout and keeps its own list or tree behavior.

Follow the [activity table message standard](activity-messages.md). Activity
rows use structured `ConversionProgress` source and label fields.
Use `Processing` for per-item progress. The desktop formats paths relative
to the displayed source root where possible. Mark progress-only updates
with `ShowInActivity = false` to update the bars without filling the table
with repeated counts. Use `Skipped` and `Failed` with a reason for issues.

The Group by Track ID preview binds the apply button's enabled state and accent
class to `CanApply`, so Move and Copy use the same readiness cue.

## Adding operations and settings

To add an operation, define its metadata and implement its execution route, then
extend the relevant workflow tests. To change setting wording, edit its catalogue
entry; labels, notes and hover descriptions will use that entry everywhere they
are bound. New settings still need typed values, persistence and validation.

The Recipe Builder reuses these operation labels and setting definitions. Core's
`RecipeOperations` supplies execution and chaining constraints by the same stable
operation IDs. Register recipe support there only when an operation has a
non-interactive input/output contract; catalog tests check the builder's supported
IDs. See [recipes.md](../user/recipes.md) for settings snapshots and output flow.
