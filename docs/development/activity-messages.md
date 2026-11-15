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
| Grouping plan check | `Checking grouping plan…` |
| Prepare hashes and recovery | `Preparing recovery for grouped items…` or `Preparing recovery for file moves…` |
| Recipe activity | Prefix the same message with the step number and name |
| Finish, cancel or stop a run | Show one operation summary with totals or the reason it stopped |

Use `ShowInActivity = false` for progress-only percentages and repeated batch
counts. Routine successes go in the totals and report. Use fictional catalogue
prefixes in examples, such as `Processing: Disc 1/ABC-12345.zip`.
