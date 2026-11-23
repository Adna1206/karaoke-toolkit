# Recipe Builder

Recipe Builder is experimental. Review each step's settings before running.
The [CLI](cli.md) can validate/run exported recipes and recover interrupted workflows;
its steps must use Fail, Skip or Replace rather than interactive Ask conflicts.
Completed changes cannot be undone as a whole recipe. After an interruption,
the recovery prompt offers to resume the saved workflow; it never resumes silently.

Open **Tools → Recipe Builder (Experimental)** to create a named sequence of built-in operations.
Type a name in the recipe field or use its arrow to open a saved recipe.
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

**Tag Karaoke Files** is available as a recipe step. Its Tagging tab saves the filename
pattern (default `%DISC%-%TRACK% - %ARTIST% - %TITLE%`), fill-empty-only preference,
and comment/other descriptive tag cleanup preferences. Processing controls recursion;
ZIP controls the compression used when updating audio inside archives. Tags update
files in place without re-encoding; DISC becomes Album and Track accepts zero.
There is no interactive preview dialog during a recipe. Pattern/media failures stop
later steps; BIN and orphan CDG files are reported as skipped. Tagging passes the
same input paths onward, retaining graphics companions for subsequent conversions.
Recovery freezes the generated tagging preview before writing and skips completed
files using the parent recipe checkpoint.

Conversions pass only their successful primary outputs to the next step, excluding
CUE/TXT side files and retained originals. Lossless-to-MP3 also passes its retained
CDG companion. Validation passes the same inputs onward after all findings are
valid. Folder maintenance and CUE/TXT creation preserve their selected folder
scope; organizing explicitly selected files passes their new locations onward.
Outputs are written beside their inputs, using each existing engine's naming rules.

The complete recipe is checked before execution for unknown operations, invalid
settings and known format mismatches. Filename cleanup, Add Track ID Separator,
Remove Track ID Separator, and CUE/TXT creation require folders. Put them before
conversions that pass individual file outputs to the next step. Disc ripping, restoration and unavailable
online operations are not recipe steps. Detailed file/runtime checks remain with
the engines: an advanced recipe is not a dry run or a guarantee of useful output.

A failure stops subsequent steps, including validation findings needing attention.
Skipped conversions are not presented as new outputs. If a step has no inputs or
finds no matching items, the recipe stops with a warning. Cancellation stops further
work and keeps completed steps committed. Desktop recipes using **Ask each time**
prompt as conflicts are encountered. Recipe conversions use staged publication,
output verification and per-conversion recovery. Runs appear in the
main progress screen with a numbered step log and a **Recipe Report** available
through **View Last Run Report**.

Interrupted work must be recovered before a fresh recipe starts in its folder.
Startup, folder selection and **Recover** offer the saved recipe as one workflow.
Recovery uses its own recipe/settings snapshot even if the library entry was edited
or deleted. Completed steps are not repeated; interrupted child conversions resume
their existing source plans before later steps run. Organisation and flattening
use frozen move plans, so a partial move is completed without flattening another layer.
Saved inputs/outputs and pending moves are hash-checked; changed files stop recovery
and retain the journal. **Later** does nothing, and explicit cancellation ends the run.
Recipes do not provide whole-workflow rollback. Keeping lossless originals beside an MP3 with the same basename can
make a later Audio+G operation ambiguous; the engine reports this normally.

## Code

- Core `Recipes`: portable definitions, format constraints, JSON library storage,
  orchestration and adapters to existing operation engines. No GUI dependency.
- App `Features/Recipes`: builder and step configuration. Presentation names and
  setting descriptions reuse the existing metadata; appearance and Explorer
  integration are not recipe settings.
- Tests `Recipes` and `App/Recipes`: orchestration, import/export, settings snapshots,
  known incompatibilities, cancellation, failure and real engine workflows.

Core's recipe operation registry declares allowed operations and their input/output
constraints. UI `CanUseInRecipes` reads that registry; imports and execution enforce
the same rule. Eligibility is a developer capability, not a user preference. Each
`RecipeStep` holds its own processing and operation-specific settings, passed to the
engine by `RecipeStepExecutor`. Older recipes default the added tagging fields.

Custom PowerShell/CMD steps remain in the feature backlog. This recipe format supports
built-in operations only.
