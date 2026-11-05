# Crash recovery

The app offers **Recover and resume** or **Later** when it starts and finds an
interrupted run. Selecting its source folder (or pressing Start with that folder
typed in) also checks for recovery, including nested folders when recursion is enabled.
The **Recover…** button returns to pending recovery. Nothing resumes automatically.

During recovery the main window explicitly shows **Recovery mode** and prefixes the
operation title with **Recovery**. The saved action, conversion options, overwrite
policy and source list are used, even if current settings have since changed.
Completed, failed and skipped source groups are not attempted again. The interrupted
group and remaining groups continue; automatic CUE/TXT generation also has checkpoints.
New files added after the original scan are left for a subsequent run.

Recipes and tagging use the same startup/Recover prompt. A recipe checkpoint stores
its complete definition/settings snapshot, completed-step reports, next input scope
and current child conversion or move plans. Recovery offers one parent recipe,
resumes its interrupted work, then continues later steps; it does not depend on the
recipe still existing in the library. A tagging checkpoint stores the accepted
preview (including manual corrections) and run options, skips completed files and
continues the batch without asking for a new pattern.

**Later** leaves files and journals untouched and does not start a fresh operation over
the interrupted run. A normal Cancel still discards unpublished work and ends the run;
this feature recovers unexpected termination and retained publication failures, rather
than introducing pause/resume for ordinary cancellation.

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
`%LOCALAPPDATA%\KaraokeToolkit\Recovery` lets startup find runs without scanning drives.
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
