# Group by Track ID

In **File & Folder Tools**, select files or folders and open **Group by Track
ID**. Choose whether to scan folders for files, with optional recursion, or
group each selected folder as a whole. Whole folders keep their contents and
names. Files keep their names and matching audio and graphics are grouped together.
The preview animates its progress bar while scanning and preparing the preview.
It shows item counts during grouping and item destination checks.

- **Number ranges** uses a configurable range size and origin. With 50 starting
  at 1, `ABC33601` and `ABC-33650` go in `ABC-33601 to ABC-33650` when the
  range separator is set to **to**. `ABC-33600` goes in the preceding range.
- **Trailing digits** can name the same interval with `x`, `X`, `#`, or the
  range start followed by `s`. Optional automatic parents add one masked digit
  at each level. You can limit parent depth and skip single-child parents.
- Whole-folder mode understands names such as `ABC-31xxx` and
  `ABC-31000 to ABC-31999`. The full interval must fit inside the destination
  group. Range-start labels such as `ABC-31000s` are ambiguous as inputs.
  Distinct prefixes remain independent.

The desktop fills in the destination when the selection resolves to one folder.
For files and whole-folder items, it uses their containing folder. For a folder
being scanned, it uses the selected folder itself. You can change it before
applying. Items already at their planned path are skipped.

Review the live destination tree before applying. When there are skipped items
or conflicts, drag the divider above their panel to resize it. The Move or Copy
button uses the accent colour once the plan is ready. Conflicts block
the batch. The desktop dialog defaults to Move, which requires a same-volume
destination. CLI and MCP default to Copy.
Interrupted publication can be resumed from the saved plan using Recover.
The same operation is available through the [CLI](cli.md) and [MCP](mcp.md).
