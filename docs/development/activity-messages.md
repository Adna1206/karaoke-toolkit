# Activity table messages

Use these labels in the desktop activity table. For item rows, supply
`ConversionProgress.ActivityLabel`, `ActivitySource` and optional
`ActivityDetail`. The desktop formats paths relative to the displayed source
folder when possible, using forward slashes.

| Event | Message or format |
| --- | --- |
| Start work on an item | `Processing: <relative path>`, or `Processing: <track ID>` for disc capture |
| Create a CUE or TXT listing | `Writing Cue File: <path>` or `Writing Text File: <path>` |
| Skip an item | `Skipped: <path> — <reason>` |
| Fail an item | `Failed: <path> — <reason>` |
| Publish an item with a warning | `Warning: <path> — <reason>` |
| Build a grouping plan for an unattended run | `Checking grouping plan…` |
| Prepare hashes and recovery | `Preparing recovery for grouped items…` or `Preparing recovery for file moves…` |
| Recipe activity | Prefix the same message with the step number and name |
| Finish, cancel or stop a run | Show one operation summary with totals or the reason it stopped |

Use `ShowInActivity = false` for progress-only percentages and repeated batch
counts. Routine successes go in the totals and report. Use fictional catalogue
prefixes in examples, such as `Processing: Disc 1/ABC-12345.zip`.

The elapsed time below the activity table updates once a second during a run.
Each activity row keeps the elapsed time when the desktop records the event.
The count beside elapsed time shows items found during discovery or preparation,
then shows completed items out of the batch total. It follows the operation's
progress units, including failures and skips. Remaining Time and Throughput use
the same recent completion rate. It needs at least three completed items and
three seconds to start, then samples again at most every three seconds when
more items finish. Remaining Time uses the same hours, minutes, and seconds
format as Elapsed Time. Both rate-based values show a dash during discovery,
preparation, overwrite review, and when the rate is too early or stale.
Recipes estimate the current step. Footer-only updates stay out of the activity
table.
