# Recipe Builder

Recipe Builder is experimental. Review each step's settings before running;
completed changes cannot be undone as a whole recipe, and interrupted recipes
do not automatically resume.

Open **Tools → Recipe Builder (Experimental)** to create a named sequence of built-in operations.
Add steps, reorder them, and use **Step Settings** to configure each step. New
steps copy current app preferences; their snapshots are saved with the recipe.
Displayed step numbers update after reordering or removal. Reordering immediately
rechecks format compatibility; invalid sequences cannot be saved, exported or run.
Saving and launching also validate the complete recipe again.
Editing a recipe never changes general app settings. The dedicated lossless-to-MP3
step uses the saved MP3 bitrate when first added.

**Save** keeps the recipe in the local library beside the app settings, under
`Recipes`. **Import** and **Export** exchange versioned JSON files. An import gets
a new local ID, so saving it does not overwrite the recipe it came from. Input
paths are chosen on the home screen and are not embedded in exported recipes.

## Running saved recipes

After closing the builder, saved recipes populate **Recipes (Experimental)** in
the home Tools/category list. The category appears only when recipes exist.
Select a recipe to see its step count and workflow summary, choose inputs, then
click **Start**. Recipes can be starred and launched from Favourites too. Saved
recipes are loaded on startup and refreshed when the builder closes, including
renames, deletions and changes to the first step. The builder is an editor; it
does not choose inputs or launch runs.

Input controls follow the first step: folder-only operations disable **Add Files**,
and file pickers filter to the supported input extensions. Selection, drag/drop,
typed-path readiness checks and Core execution check those same constraints.
Organize Files can accept arbitrary files. Selecting a recipe never replaces its
saved settings with the current general app settings.

Select a saved recipe and choose **Delete Recipe** to remove it from the library.
A confirmation names the recipe. Confirming also clears its editor, including
unsaved edits; karaoke files and separately exported copies are untouched.

## Execution

Conversions pass only their successful primary outputs to the next step, excluding
CUE/TXT side files and retained originals. Lossless-to-MP3 also passes its retained
CDG companion. Validation passes the same inputs onward after all findings are
valid. Folder maintenance and CUE/TXT creation preserve their selected folder
scope; organizing explicitly selected files passes their new locations onward.
Outputs are written beside their inputs, using each existing engine's naming rules.

The complete recipe is checked before execution for unknown operations, invalid
settings and known format mismatches. Folder-wide filename cleanup and CUE/TXT
creation require folders, so they can precede conversions but cannot consume a
conversion's individual file outputs. Disc ripping, restoration and unavailable
online operations are not recipe steps. Detailed file/runtime checks remain with
the engines: an advanced recipe is not a dry run or a guarantee of useful output.

A failure stops subsequent steps, including validation findings needing attention.
Skipped conversions are not presented as new outputs. If a step has no inputs or
finds no matching items, the recipe stops with a warning. Cancellation stops further
work; completed steps remain committed. Existing overwrite prompts, staging,
output verification and per-conversion recovery are reused. Runs appear in the
main progress screen with a numbered step log and a **Recipe Report** available
through **View Last Run Report**.

An interrupted conversion must be recovered before a recipe starts in its folder.
Recovery can recover that individual conversion step; automatic continuation of
the remaining recipe is not implemented. Recipes do not provide whole-workflow
rollback. Keeping lossless originals beside an MP3 with the same basename can
make a later Audio+G operation ambiguous; the engine reports this normally.

## Code

- Core `Recipes`: portable definitions, format constraints, JSON library storage,
  orchestration and adapters to existing operation engines. No GUI dependency.
- App `Features/Recipes`: builder and step configuration. Presentation names and
  setting descriptions reuse the existing metadata; appearance and Explorer
  integration are not recipe settings.
- Tests `Recipes` and `App/Recipes`: orchestration, import/export, settings snapshots,
  known incompatibilities, cancellation, failure and real engine workflows.

Custom PowerShell/CMD steps and whole-recipe crash continuation are recorded in
the feature backlog. This first recipe format supports built-in operations only.
