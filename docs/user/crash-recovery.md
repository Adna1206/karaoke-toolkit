# Crash recovery

The app offers **Recover and resume**, **Later**, and **Abandon interrupted run…** when it finds an
interrupted run. When you start a file-based operation, the app checks the
source folders for recovery, including nested folders when recursion is enabled.
The status area shows “Checking for interrupted runs…” and an animated bar
while searching selected folders or checking the saved-run index at startup
or through **Recover…**. Nothing resumes automatically. Turn off **Check for
interrupted runs on startup** to skip the Desktop startup check. **Recover…**
and checks before file changes stay available.

During recovery the main window explicitly shows **Recovery mode** and prefixes the
operation title with **Recovery**. The saved action, conversion options, overwrite
policy and source list are used, even if current settings have since changed.
Completed, failed and skipped source groups are not attempted again. With batch
continuation enabled, the interrupted group and remaining groups continue.
Automatic CUE/TXT generation also has checkpoints.
New files added after the original scan are left for a subsequent run.
The footer bar animates while the app checks saved files and interrupted
publication before item processing resumes.

For standalone desktop conversions using **Ask each time**, conflicting outputs wait in
disposable staging until the batch review. An interruption before publication
leaves those source and destination files in place. Recovery can regenerate
the unpublished outputs and review the remaining conflicts again. Published
groups retain their existing receipts and are skipped on resume.

Recipes and tagging use the same startup/Recover prompt. A recipe checkpoint stores
its complete definition/settings snapshot, completed-step reports, next input scope
and current child conversion or move plans. Recovery offers one parent recipe,
resumes its interrupted work, then continues later steps; it does not depend on the
recipe still existing in the library. A tagging checkpoint stores the accepted
preview (including manual corrections) and run options, skips completed files and
continues the batch without asking for a new pattern.

[Normalize Audio Gain](audio-gain.md) and [Change Audio Key](audio-gain.md#change-audio-key)
save their measured plans under the output folder. Recovery checks the staged
publication and resumes the remaining copies using the saved adjustments and
source fingerprints. Audio and any matching CDG file publish as a group.
The source files are kept.

If saving a completed recipe step fails, the previous checkpoint and its
operation records remain available through **Recover**. Once the filesystem
problem is resolved, recovery uses those records to recognize work already
published. If the saved step ended in failure, cancellation or an empty result,
recovery honors that stop before starting another step.

**Later** leaves files and journals untouched and does not start a fresh operation over
the interrupted run. A normal Cancel still discards unpublished work and ends the run;
this feature recovers unexpected termination and retained publication failures, rather
than introducing pause/resume for ordinary cancellation.

Track grouping saves the source and destination paths and content hashes for
each planned group in a workflow journal under the destination root. Copy
stages and verifies a full group on the destination volume before publishing.
Move uses a same-volume rename without replacement. Resume verifies a partial
group and finishes publication using the saved paths. It does not rescan the
input selection. Desktop, CLI and MCP can resume these jobs. Plan revalidation
happens before the first item is published. The default captures source
fingerprints as each group begins. Standalone flattening and alphabetical
organization also save their move plans for recovery. Rename by Filename Pattern saves its approved
same-folder paths and can finish a partially renamed audio/CDG pair. Recipe
steps save their plan in the recipe journal.

The desktop footer shows an animated bar during “Preparing recovery.” With
content verification and **Before the batch starts**, preparing the move plan reads
every source file, including audio and graphics sidecars. It then saves the plan to disk.
Preparation time depends on total bytes and storage speed. The number of
tracks alone does not predict the wait.

Flattening records a failure and continues if it cannot inspect or remove a
child folder during empty-folder cleanup. The folder stays in place and the
failure appears in the run report. Move or verification failures that may
leave a partially published group still stop the run for recovery.

**Abandon interrupted run…** asks for confirmation, then removes the selected
run from recovery prompts. The app keeps its journal and recovery files for
manual inspection. File changes already made stay in place. A pending file
transaction can still block writes to protect retained originals and backups.

## Recovery settings

**Continue batch after recovery** defaults to on for new standalone conversions,
tagging and file moves. When off, **Recover interrupted item** reconciles any
pending publication and leaves the rest of the batch for a new run. A partial
file move or grouping copy can finish using the saved group checkpoint. If the
app cannot verify the interrupted state safely, it keeps the journal for manual
review. The setting is saved with the run. Recipes still resume as a workflow.

**Recovery verification** applies to new standalone media conversions, Group by
Track ID, Group by Alphabet, Flatten and Rename by Filename Pattern runs. Choose
**xxHash128** (default), **SHA-256**,
**Metadata only**, or **No verification**. A saved verification choice takes
precedence over the default. Metadata checks compare file size
and last-write time. Directory metadata checks also compare the paths in
the tree. No verification keeps path and transaction checks. Change the shared
settings in Desktop, [CLI](cli.md#commands-and-settings), or [MCP](mcp.md#requests-and-settings).
The shared IDs are `recovery.batch-resume` and `recovery.check-on-startup`.
CLI and MCP can set the Desktop startup preference through the shared profile.
Headless recovery discovery remains an explicit command or tool call.

**When to start checking for file changes** sets when standalone media
conversions, file moves, renames and grouping copies capture their source
fingerprints.

- **Before the batch starts** is more robust because it can
  detect edits to files while they wait.
- **When each item starts** is the default and starts faster with content verification. It uses the
  source as it exists when its group begins. Grouping copies and conversion
  staging can hash the source bytes during the copy.

Both options validate the plan and check each result at the selected verification
level. Conversion batches that check up front capture each input group,
including its matching audio sidecar, across the whole selection before
converting anything. Each staged input is compared to that saved baseline.
Conversions use the selected verification mode for input checks, staging,
and publication in both timing modes. Archive structure, CRCs, and media
format checks still apply where required.
Conversion verifies the staged file, then checks the original again before
publishing any output or removing any source.

Recovery uses the timing saved with the run. Older move journals without a timing
field use **Before the batch starts**. Older conversion journals without a
baseline use **When each item starts**. The shared setting ID is
`recovery.check-timing` with values `BeforeBatch` and `BeforeItem`.

Metadata-only and unverified interrupted moves require manual review. Older
move journals created with batch resume disabled also require manual review
because they lack an interrupted group checkpoint. Successful runs clean up their
records. An interrupted conversion publication without content fingerprints
also requires manual review. A conversion interrupted between publications can
resume according to its saved batch continuation setting. An older journal
without a verification mode uses SHA-256.

Tagging, recipe-owned conversions and quarantine use their existing SHA-256
checks. Standalone validation records SHA-256 fingerprints for quarantine.

## Batch failure handling

Continue processing when an item failure is isolated and the affected item
is in a known safe state. Collect the failures in the final run report and
show a red completion status if any item failed. Stop and retain recovery
data if a group may be partially published or its state cannot be verified.

Conversions, tagging and disc ripping generally continue after isolated item
failures. Approved grouping and rename previews skip an affected group if
its source is unavailable or its destination is occupied before publication.
Flattening continues after isolated empty-folder cleanup failures. A recipe can
finish the current step's batch, and a failed step prevents later steps from
starting.

## File handling

- Interrupted staging: originals have not moved; discard disposable staging and restart
  that group.
- Interrupted publication: verify all staged outputs, published outputs and numbered
  backups against the journal's SHA256 hashes, then finish publishing the group.
- Interrupted rollback: finish restoring the originals, then retry that group.
- Publication already checkpointed: retain its outputs and finish removing staging.

Changed, missing or unexpected files block recovery. The error is shown and recovery
files are retained. Do not delete `.karaoke-job-*` folders while recovery is pending.
Damaged run journals are shown as needing inspection and cannot be resumed from the
dialog. Old journals from before this feature lack the necessary hashes and require
manual inspection; they are never automatically applied. A folder with a retained
transaction journal is protected against a fresh transaction.

### Workspace cleanup

Resolved transactions and completed run/workflow checkpoints use bounded cleanup
retries. Cleanup clears the read-only attribute only inside the disposable workspace,
deletes its journal last, and reports persistent failures with the remaining path.
Pending transaction cleanup keeps its owner checkpoint resumable. Recovery accepts
partially removed backups after committed publication while still verifying outputs.
If removing the final directory fails, cleanup attempts to restore its journal.

Deletion, attributes and retries use portable .NET APIs. Windows cloud placeholders
are recognized through an OS-guarded metadata query; symbolic links, junctions and
unknown reparse providers are refused. There is no external utility dependency.
Linux/macOS runtime cleanup has not been verified.

Startup discovery does not sweep historical orphan transaction folders without
owner journals. A missing journal alone does not establish that backups are safe to
delete. Persistent cleanup failure for an already finished run/workflow is reported,
but its metadata folder is not presented as an unfinished job.

## Storage and guarantees

The source root contains `.karaoke-job-run-<id>/run.json`, plus a lease file. Each
publication uses `.karaoke-job-<id>/recovery.json` and a `backups` subdirectory.
These directories are excluded from recursive media scanning. A small index under
`%LOCALAPPDATA%\KaraokeToolkitData\Recovery` on Windows lets startup find runs without scanning drives.
Folder discovery also works if that index is missing. Keep the source folder at its
original path until recovery is complete; unavailable drives must be reconnected.

Recipe/tagging checkpoints live in `.karaoke-job-workflow-<id>/workflow.json` in
the first source root. Their index also covers other selected roots in a multi-folder
launch. Child conversion journals remain underneath their own source folders but
are owned by the recipe, so startup presents the whole recipe once. The source roots
and pending outputs are validated before resuming. Folder-moving recipe steps retain
their original item groups, target paths and hashes; completed moves are recorded
before progress is reported. A crash between a rename and its receipt is reconciled
from the verified source/target state, without rebuilding a plan from the altered tree.

Journals use flushed temporary files and replacement; staged outputs are flushed before
originals are moved. Windows checkpoint replacement briefly retries access/sharing
conflicts while preserving the old journal. Persistent failures still stop the write.
A completed-group checkpoint is saved before backups are deleted.
Process leases prevent recovery of a still-running run and concurrent commits in a
folder. Recovery rejects linked paths and paths outside the recorded root.

This is process-crash recovery, not a guarantee of atomic multi-file updates during
hardware failure or sudden power loss. Filesystem/storage durability and unrelated
applications changing files concurrently remain outside the application's control.

## Automated verification

`WorkflowRecoveryTests` kills actual recipe/tagging child processes between steps,
inside conversion, during flattening and after the first tagged file. It also checks
startup dispatch, Later, saved manual tags, deleted recipe-library entries, altered
outputs, multi-root discovery without the parent index, explicit cancellation, a
rename that happened before its durable receipt, and retention/resumption of a
half-moved pair blocked by a collision. Checkpoint tests exercise temporary and
persistent Windows reader locks.

`RecoveryTests` covers every backup/output move boundary, repeated recovery, interrupted
rollback, changed files, unsafe paths, checkpoint/cleanup interruption, staging,
automatic metadata, saved plans, missing startup indexes, damaged journals, active
leases and the viewmodel's Later/recovery-mode behaviour. A disposable test process also
runs a real ZIP extraction, is killed before publication without cleanup, and is
successfully resumed. Fixtures are generated; user karaoke media is not modified.

The crash worker is an entry point in the existing test project, not a new project or
application feature. Publication boundary tests reconstruct exact interrupted on-disk
states; they do not claim to simulate physical power loss.
